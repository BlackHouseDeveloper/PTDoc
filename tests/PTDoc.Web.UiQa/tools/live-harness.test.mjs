import assert from 'node:assert/strict';
import { mkdtemp, readFile, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  allocateUniqueLoopbackPorts,
  createDisposableDatabase,
  removeDisposableDirectory,
  sanitizeLog,
  startProcess,
  stopProcess
} from './live-harness.mjs';

test('loopback port allocation returns unique candidates', async () => {
  const ports = await allocateUniqueLoopbackPorts(3);
  assert.equal(new Set(ports).size, 3);
  assert.ok(ports.every(port => Number.isInteger(port) && port > 0));
});

test('missing source leaves a valid destination path for SQLite to create', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ptdoc-live-empty-db-test-'));
  const destination = await createDisposableDatabase(path.join(root, 'missing.db'), path.join(root, 'run'));
  await assert.rejects(() => stat(destination), error => error.code === 'ENOENT');
  await removeDisposableDirectory(root);
});

test('disposable database copy is isolated and cleanup removes the run directory', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ptdoc-live-harness-test-'));
  const source = path.join(root, 'source.db');
  const disposableDirectory = path.join(root, 'run');
  await writeFile(source, 'source-database');

  const copy = await createDisposableDatabase(source, disposableDirectory);
  await writeFile(copy, 'mutated-copy');

  assert.equal(await readFile(source, 'utf8'), 'source-database');
  assert.equal(await readFile(copy, 'utf8'), 'mutated-copy');
  await removeDisposableDirectory(disposableDirectory);
  await assert.rejects(() => stat(copy), error => error.code === 'ENOENT');
  await removeDisposableDirectory(root);
});

test('an unopenable database with a WAL is never copied inconsistently', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ptdoc-live-wal-test-'));
  const source = path.join(root, 'source.db');
  await writeFile(source, 'not-a-sqlite-database');
  await writeFile(`${source}-wal`, 'active-wal');
  await assert.rejects(
    () => createDisposableDatabase(source, path.join(root, 'run')),
    /consistent live SQLite backup could not be created while a WAL is present/);
  await removeDisposableDirectory(root);
});

test('process teardown stops a child and writes only sanitized logs', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ptdoc-live-process-test-'));
  const logPath = path.join(root, 'child.log');
  const child = startProcess(process.execPath, [
    '-e',
    "console.log('Bearer secret-token user@example.test (555) 123-4567'); setInterval(() => {}, 1000)"
  ], { cwd: root, env: process.env, logPath });

  await new Promise(resolve => setTimeout(resolve, 100));
  await stopProcess(child);
  await child.writeSanitizedLog();
  const log = await readFile(logPath, 'utf8');

  assert.match(log, /Bearer \[REDACTED\]/);
  assert.match(log, /\[REDACTED_EMAIL\]/);
  assert.match(log, /\[REDACTED_PHONE\]/);
  assert.doesNotMatch(log, /secret-token|user@example|555/);
  await removeDisposableDirectory(root);
});

test('log sanitizer redacts query and JSON secret fields', () => {
  const sanitized = sanitizeLog('GET /path?token=abc&code=123 {"destination":"person@example.test","otp":"5678"}');
  assert.doesNotMatch(sanitized, /abc|123|person@example|5678/);
  assert.match(sanitized, /\[REDACTED\]/);
});
