import assert from 'node:assert/strict';
import http from 'node:http';
import test from 'node:test';
import { startFaultProxy } from './fault-proxy.mjs';

test('fault proxy consumes exact one-shot rules without logging secrets', async t => {
  let upstreamRequests = 0;
  const upstream = http.createServer((request, response) => {
    upstreamRequests += 1;
    request.resume();
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ ok: true }));
  });
  await listen(upstream);
  t.after(() => close(upstream));

  const logs = [];
  const address = upstream.address();
  const proxy = await startFaultProxy({
    upstream: `http://127.0.0.1:${address.port}`,
    nonce: 'test-nonce',
    logger: event => logs.push(JSON.stringify(event))
  });
  t.after(() => proxy.close());

  const unauthorized = await fetch(proxy.controlUrl, { method: 'GET' });
  assert.equal(unauthorized.status, 403);

  const rejected = await control(proxy, 'POST', {
    method: 'POST',
    path: '/api/v1/not-allowlisted',
    status: 503
  });
  assert.equal(rejected.status, 400);

  const configured = await control(proxy, 'POST', {
    method: 'PATCH',
    path: '/api/v1/appointments/00000000-0000-0000-0000-000000000001/appointment-type',
    status: 503,
    occurrences: 1
  });
  assert.equal(configured.status, 201);

  const headers = { authorization: 'Bearer secret-value', 'x-test-secret': 'body-secret' };
  const first = await fetch(`${proxy.origin}/api/v1/appointments/00000000-0000-0000-0000-000000000001/appointment-type`, { method: 'PATCH', headers, body: '{}' });
  const second = await fetch(`${proxy.origin}/api/v1/appointments/00000000-0000-0000-0000-000000000001/appointment-type`, { method: 'PATCH', headers, body: '{}' });

  assert.equal(first.status, 503);
  assert.equal(second.status, 200);
  assert.equal(upstreamRequests, 1);
  assert.equal(logs.length, 1);
  assert.doesNotMatch(logs[0], /secret-value|body-secret/);
});

test('fault proxy delay rules are isolated by exact method and path', async t => {
  const upstream = http.createServer((request, response) => {
    request.resume();
    response.writeHead(204).end();
  });
  await listen(upstream);
  t.after(() => close(upstream));
  const address = upstream.address();
  const proxy = await startFaultProxy({ upstream: `http://127.0.0.1:${address.port}`, nonce: 'delay-nonce' });
  t.after(() => proxy.close());

  await control(proxy, 'POST', {
    method: 'GET',
    path: '/api/v1/dashboard/snapshot',
    delayMs: 75,
    occurrences: 1
  });

  const unrelatedStart = Date.now();
  await fetch(`${proxy.origin}/api/v1/dashboard/alerts`);
  const unrelatedElapsed = Date.now() - unrelatedStart;
  const delayedStart = Date.now();
  await fetch(`${proxy.origin}/api/v1/dashboard/snapshot`);
  const delayedElapsed = Date.now() - delayedStart;

  assert.ok(delayedElapsed >= 60, `expected a visible delay, observed ${delayedElapsed}ms`);
  assert.ok(unrelatedElapsed < delayedElapsed);
  const state = await control(proxy, 'GET');
  assert.deepEqual((await state.json()).rules, []);
});

async function control(proxy, method, body) {
  return fetch(proxy.controlUrl, {
    method,
    headers: {
      'x-ptdoc-live-control': proxy.nonce,
      ...(body ? { 'content-type': 'application/json' } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });
}

function listen(server) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
}

function close(server) {
  return new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
}

