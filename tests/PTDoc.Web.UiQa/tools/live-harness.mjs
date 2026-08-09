import { execFile, spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { copyFile, mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

export async function allocateLoopbackPort() {
  const server = createServer();
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
  const address = server.address();
  const port = address && typeof address !== 'string' ? address.port : null;
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  if (!port) {
    throw new Error('Unable to allocate an isolated loopback port.');
  }
  return port;
}

export async function createDisposableDatabase(sourcePath, destinationDirectory) {
  await mkdir(destinationDirectory, { recursive: true });
  const destinationPath = path.join(destinationDirectory, 'ptdoc-live.db');
  if (await exists(sourcePath)) {
    try {
      const escapedDestination = destinationPath.replaceAll('"', '""');
      await execFileAsync('sqlite3', [sourcePath, `.backup "${escapedDestination}"`]);
    } catch (error) {
      if (await exists(`${sourcePath}-wal`)) {
        throw new Error(`A consistent live SQLite backup could not be created while a WAL is present: ${error.message}`);
      }
      await copyFile(sourcePath, destinationPath);
    }
  }
  return destinationPath;
}

export async function allocateUniqueLoopbackPorts(count) {
  const ports = new Set();
  while (ports.size < count) {
    ports.add(await allocateLoopbackPort());
  }
  return [...ports];
}

export async function removeDisposableDirectory(directory) {
  if (directory) {
    await rm(directory, { recursive: true, force: true });
  }
}

export function startProcess(command, args, { cwd, env, logPath }) {
  const child = spawn(command, args, {
    cwd,
    env,
    detached: false,
    stdio: ['ignore', 'pipe', 'pipe']
  });
  const chunks = [];
  child.stdout.on('data', chunk => chunks.push(Buffer.from(chunk)));
  child.stderr.on('data', chunk => chunks.push(Buffer.from(chunk)));
  child.completion = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('exit', (code, signal) => resolve({ code, signal }));
  });
  child.writeSanitizedLog = async () => {
    if (!logPath) return;
    const text = sanitizeLog(Buffer.concat(chunks).toString('utf8'));
    await mkdir(path.dirname(logPath), { recursive: true });
    await writeFile(logPath, text, 'utf8');
  };
  return child;
}

export async function stopProcess(child, timeoutMs = 5_000) {
  if (!child || child.exitCode !== null || child.signalCode !== null) {
    return;
  }
  child.kill('SIGTERM');
  const completed = await Promise.race([
    child.completion.then(() => true),
    new Promise(resolve => setTimeout(() => resolve(false), timeoutMs))
  ]);
  if (!completed && child.exitCode === null && child.signalCode === null) {
    child.kill('SIGKILL');
    await child.completion;
  }
}

export async function waitForHttp(url, { timeoutMs = 120_000, intervalMs = 500, process } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError = 'No response received.';
  while (Date.now() < deadline) {
    if (process && (process.exitCode !== null || process.signalCode !== null)) {
      throw new Error(`Process exited before ${url} became ready.`);
    }
    try {
      const response = await fetch(url, { redirect: 'manual' });
      if (response.ok || (response.status >= 300 && response.status < 400)) {
        return;
      }
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error.message;
    }
    await new Promise(resolve => setTimeout(resolve, intervalMs));
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError}`);
}

export function sanitizeLog(value) {
  return String(value)
    .replace(/Bearer\s+[A-Za-z0-9._~-]+/gi, 'Bearer [REDACTED]')
    .replace(/([?&](?:token|code|otp|pin|invite|access_token|refresh_token)=)[^&\s]+/gi, '$1[REDACTED]')
    .replace(/\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b/g, '[REDACTED_EMAIL]')
    .replace(/\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b/g, '[REDACTED_PHONE]')
    .replace(/("(?:pin|otp|token|inviteUrl|destination)"\s*:\s*")[^"]*(")/gi, '$1[REDACTED]$2');
}

export async function writeEvidenceIndex(artifactDirectory, runId) {
  const evidencePath = path.join(artifactDirectory, 'evidence.jsonl');
  const records = await readJsonLines(evidencePath);
  const lines = [
    '# Fixable Blocker Live Verification Evidence',
    '',
    `Run ID: \`${runId}\``,
    '',
    '| Checklist ID | Disposition | Role | Route | Expected | Observed | Evidence |',
    '| --- | --- | --- | --- | --- | --- | --- |'
  ];
  for (const record of records) {
    const ids = Array.isArray(record.checkIds) ? record.checkIds : [record.checkId];
    for (const id of ids.filter(Boolean)) {
      const evidence = [record.timestampUtc, record.viewport, record.theme, record.inputMethod, record.artifact]
        .filter(Boolean)
        .join(' · ');
      lines.push(`| ${escapeCell(id)} | ${escapeCell(record.disposition)} | ${escapeCell(record.role)} | ${escapeCell(record.route)} | ${escapeCell(record.expected)} | ${escapeCell(record.observed)} | ${escapeCell(evidence)} |`);
    }
  }
  await writeFile(path.join(artifactDirectory, 'EVIDENCE_INDEX.md'), `${lines.join('\n')}\n`, 'utf8');
}

async function readJsonLines(filePath) {
  try {
    return (await readFile(filePath, 'utf8'))
      .split(/\r?\n/)
      .filter(Boolean)
      .map(line => JSON.parse(line));
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }
}

async function exists(filePath) {
  try {
    await stat(filePath);
    return true;
  } catch (error) {
    if (error.code === 'ENOENT') return false;
    throw error;
  }
}

function escapeCell(value) {
  return String(value ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
}
