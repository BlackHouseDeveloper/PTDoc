import { createHash, randomUUID } from 'node:crypto';
import { copyFile, mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { startFaultProxy } from './fault-proxy.mjs';
import {
  allocateUniqueLoopbackPorts,
  createDisposableDatabase,
  removeDisposableDirectory,
  startProcess,
  stopProcess,
  waitForHttp,
  writeEvidenceIndex
} from './live-harness.mjs';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const qaRoot = path.resolve(currentDirectory, '..');
const repoRoot = path.resolve(qaRoot, '..', '..');
const sourceDatabase = path.resolve(repoRoot, process.env.PTDOC_LIVE_SOURCE_DB ?? 'src/PTDoc.Api/PTDoc.db');
const timestamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
const runId = process.env.PTDOC_UI_QA_LIVE_RUN_ID ?? `localhost-unblock-${timestamp}`;
const artifactDirectory = path.resolve(repoRoot, process.env.PTDOC_UI_QA_ARTIFACT_DIR ?? `output/playwright/branch-live-verification/${runId}`);
const temporaryDirectory = await mkdtemp(path.join(os.tmpdir(), 'ptdoc-branch-live-'));
const tempDatabaseDirectory = path.join(temporaryDirectory, 'database');
let apiProcess;
let webProcess;
let faultProxy;
let exitCode = 1;

try {
  requireCredentials();
  await mkdir(artifactDirectory, { recursive: true });
  await copyFile(
    path.join(repoRoot, 'docs/BRANCH_LIVE_VERIFICATION_CHECKLIST.md'),
    path.join(artifactDirectory, 'BRANCH_LIVE_VERIFICATION_RESULTS.md'));

  const databasePath = await createDisposableDatabase(sourceDatabase, tempDatabaseDirectory);
  const [apiPort, proxyPort, webPort] = await allocateUniqueLoopbackPorts(3);
  const apiOrigin = `http://127.0.0.1:${apiPort}`;
  const webOrigin = `http://127.0.0.1:${webPort}`;
  const nonDeliverablePublicOrigin = `https://${runId.toLowerCase().replace(/[^a-z0-9-]/g, '').slice(-50)}.example.test`;
  const sourceSha = readSourceSha();
  const sourceMetadata = readSourceMetadata();

  apiProcess = startProcess('dotnet', [
    'run', '--no-build', '--project', 'src/PTDoc.Api', '--urls', apiOrigin
  ], {
    cwd: repoRoot,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      PTDoc_DB_PATH: databasePath,
      PTDOC_DEVELOPER_MODE: 'true',
      PTDOC_SOURCE_SHA: sourceSha,
      PTDOC_RELEASE_ID: runId,
      'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics': 'Information',
      Communication__PublicBaseUrl: nonDeliverablePublicOrigin,
      IntakeInvite__PublicWebBaseUrl: nonDeliverablePublicOrigin
    },
    logPath: path.join(artifactDirectory, 'api.log')
  });
  await waitForHttp(`${apiOrigin}/health/live`, { process: apiProcess });

  faultProxy = await startFaultProxy({
    upstream: apiOrigin,
    port: proxyPort,
    nonce: randomUUID()
  });

  webProcess = startProcess('dotnet', [
    'run', '--no-build', '--project', 'src/PTDoc.Web', '--urls', webOrigin
  ], {
    cwd: repoRoot,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      PTDOC_DEVELOPER_MODE: 'true',
      PTDOC_SOURCE_SHA: sourceSha,
      PTDOC_RELEASE_ID: runId,
      ReverseProxy__Clusters__apiCluster__Destinations__api__Address: `${faultProxy.origin}/`,
      IntakeInvite__PublicWebBaseUrl: nonDeliverablePublicOrigin
    },
    logPath: path.join(artifactDirectory, 'web.log')
  });
  await waitForHttp(`${webOrigin}/health/live`, { process: webProcess });

  await writeFile(path.join(artifactDirectory, 'RUN_METADATA.json'), JSON.stringify({
    runId,
    sourceSha,
    branch: sourceMetadata.branch,
    workingTreeDirty: sourceMetadata.workingTreeDirty,
    workingTreeEntryCount: sourceMetadata.workingTreeEntryCount,
    uncommittedDiffHash: sourceMetadata.uncommittedDiffHash,
    environment: 'Development',
    database: 'Disposable SQLite copy',
    databaseProvider: 'SQLite',
    operatingSystem: `${process.platform} ${os.release()} ${process.arch}`,
    dotnetSdk: readCommand('dotnet', ['--version']),
    playwright: readCommand('npx', ['playwright', '--version']),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    webOrigin,
    apiOrigin: 'isolated-loopback',
    faultProxy: 'enabled-loopback',
    startedAtUtc: new Date().toISOString()
  }, null, 2));

  const playwright = startProcess('npx', [
    'playwright', 'test', 'tests/branch-live-unblock.spec.ts', '--workers=1', ...process.argv.slice(2)
  ], {
    cwd: qaRoot,
    env: {
      ...process.env,
      PTDOC_WEB_BASE_URL: webOrigin,
      PTDOC_UI_QA_USERNAME: process.env.PTDOC_UI_QA_ADMIN_USERNAME ?? process.env.PTDOC_UI_QA_USERNAME ?? 'testuser',
      PTDOC_UI_QA_ADMIN_USERNAME: process.env.PTDOC_UI_QA_ADMIN_USERNAME ?? process.env.PTDOC_UI_QA_USERNAME ?? 'testuser',
      PTDOC_UI_QA_PT_USERNAME: process.env.PTDOC_UI_QA_PT_USERNAME ?? 'amorgan',
      PTDOC_UI_QA_PTA_USERNAME: process.env.PTDOC_UI_QA_PTA_USERNAME ?? 'rlopez',
      PTDOC_UI_QA_LIVE_RUN_ID: runId,
      PTDOC_UI_QA_ARTIFACT_DIR: artifactDirectory,
      PTDOC_UI_QA_FAULT_PROXY_CONTROL_URL: faultProxy.controlUrl,
      PTDOC_UI_QA_FAULT_PROXY_NONCE: faultProxy.nonce,
      PTDOC_UI_QA_FIXTURE_PREFIX: runId.slice(-24)
    },
    logPath: path.join(artifactDirectory, 'playwright.log')
  });
  const result = await playwright.completion;
  await playwright.writeSanitizedLog();
  exitCode = result.code ?? 1;
  await writeEvidenceIndex(artifactDirectory, runId);
} catch (error) {
  await mkdir(artifactDirectory, { recursive: true });
  await writeFile(path.join(artifactDirectory, 'HARNESS_ERROR.txt'), `${error.message}\n`, 'utf8');
  console.error(`Branch live verification harness failed: ${error.message}`);
} finally {
  if (faultProxy) await faultProxy.close().catch(() => { });
  await stopProcess(webProcess).catch(() => { });
  await stopProcess(apiProcess).catch(() => { });
  await webProcess?.writeSanitizedLog?.().catch(() => { });
  await apiProcess?.writeSanitizedLog?.().catch(() => { });
  await removeDisposableDirectory(temporaryDirectory);
}

process.exitCode = exitCode;

function requireCredentials() {
  if (!process.env.PTDOC_UI_QA_PIN?.trim()) {
    throw new Error('PTDOC_UI_QA_PIN is required. The harness never reads or prints credentials from files.');
  }
}

function readSourceSha() {
  const result = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' });
  return result.status === 0 ? result.stdout.trim() : 'unknown';
}

function readSourceMetadata() {
  const branch = readCommand('git', ['branch', '--show-current'], repoRoot) || 'detached';
  const status = readCommand('git', ['status', '--porcelain=v1'], repoRoot);
  const diff = readCommand('git', ['diff', 'HEAD', '--binary'], repoRoot);
  return {
    branch,
    workingTreeDirty: status.length > 0,
    workingTreeEntryCount: status ? status.split(/\r?\n/).filter(Boolean).length : 0,
    uncommittedDiffHash: createHash('sha256').update(status).update('\0').update(diff).digest('hex')
  };
}

function readCommand(command, args, cwd = repoRoot) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', maxBuffer: 20 * 1024 * 1024 });
  return result.status === 0 ? result.stdout.trim() : 'unavailable';
}
