import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../../../src/PTDoc.Web/wwwroot/js/sessionActivity.js', import.meta.url), 'utf8');

function harness() {
  const listeners = new Map();
  const requests = [];
  const notices = [];
  let now = 0;
  let wallClock = 1_000_000;
  let respond = async () => ({ status: 204 });
  const document = {
    visibilityState: 'visible',
    addEventListener: (name, handler) => listeners.set(name, handler),
    removeEventListener: (name, handler) => {
      if (listeners.get(name) === handler) listeners.delete(name);
    }
  };
  const context = vm.createContext({
    document, AbortController, Date: { now: () => wallClock }, performance: { now: () => now },
    fetch: async (url, options) => { requests.push({ url, options }); return respond(); }
  });
  vm.runInContext(source.replace(/^export /gm, '') + '\nthis.monitor = { start, stop };', context);
  const receiver = { invokeMethodAsync: async (...args) => { notices.push(args); } };
  return {
    monitor: context.monitor, receiver, requests, notices, document, listeners,
    advance: ms => { now += ms; wallClock += ms; },
    adjustWallClock: ms => { wallClock += ms; },
    respond: fn => { respond = fn; },
    fire: (name = 'input', isTrusted = true) => listeners.get(name)?.({ isTrusted })
  };
}

test('only visible trusted user input renews; no timer or synthetic heartbeat', async () => {
  const h = harness();
  h.monitor.start('circuit-session', h.receiver);
  h.advance(20 * 60_000);
  assert.equal(h.requests.length, 0);
  await h.fire('input', false);
  h.document.visibilityState = 'hidden';
  await h.fire();
  assert.equal(h.requests.length, 0);
  h.document.visibilityState = 'visible';
  await h.fire();
  assert.equal(h.requests.length, 1);
  assert.equal(h.requests[0].url, '/auth/activity');
  assert.equal(h.requests[0].options.credentials, 'same-origin');
  assert.deepEqual(Object.keys(h.requests[0].options.headers), ['X-PTDoc-Web-Session']);
  assert.equal(h.requests[0].options.headers['X-PTDoc-Web-Session'], 'circuit-session');
});

test('rapid and in-flight input cannot multiply renewal requests', async () => {
  const h = harness();
  let finish;
  h.respond(() => new Promise(resolve => { finish = resolve; }));
  h.monitor.start('session', h.receiver);
  const pending = h.fire();
  h.advance(61_000);
  await h.fire('keydown');
  assert.equal(h.requests.length, 1);
  finish({ status: 204 });
  await pending;
  h.respond(async () => ({ status: 204 }));
  await h.fire('pointerdown');
  await h.fire('wheel');
  assert.equal(h.requests.length, 2);
});

for (const status of [401, 409]) {
  test(`${status} recovery retries after a disconnected callback`, async () => {
    const h = harness();
    h.respond(async () => ({ status }));
    let callbacks = 0;
    h.receiver.invokeMethodAsync = async (...args) => {
      callbacks++;
      if (callbacks === 1) throw new Error('Synthetic circuit disconnect');
      h.notices.push(args);
    };
    h.monitor.start('session', h.receiver);
    await h.fire();
    assert.equal(h.notices.length, 0);
    h.advance(61_000);
    await h.fire();
    assert.deepEqual(h.notices, [['SessionEnded', status === 409 ? 'SessionChanged' : 'SessionEnded']]);
    h.advance(61_000);
    await h.fire();
    assert.equal(h.requests.length, 2);
    assert.equal(callbacks, 2);
  });

  test(`${status} requests recovery once without navigation or draft mutation`, async () => {
    const h = harness();
    h.respond(async () => ({ status }));
    h.monitor.start('session', h.receiver);
    await h.fire();
    assert.deepEqual(h.notices, [['SessionEnded', status === 409 ? 'SessionChanged' : 'SessionEnded']]);
    h.advance(61_000);
    await h.fire();
    assert.equal(h.requests.length, 1);
  });
}

for (const failure of ['network', 'server']) {
  test(`${failure} failure leaves session recoverable on later input`, async () => {
    const h = harness();
    h.respond(async () => {
      if (failure === 'network') throw new Error('Synthetic disconnect');
      return { status: 503 };
    });
    h.monitor.start('session', h.receiver);
    await h.fire();
    assert.equal(h.notices.length, 0);
    h.advance(61_000);
    h.respond(async () => ({ status: 204 }));
    await h.fire();
    assert.equal(h.requests.length, 2);
  });
}

test('restart and disposal remove handlers and suppress an old in-flight result', async () => {
  const h = harness();
  let finish;
  h.respond(() => new Promise(resolve => { finish = resolve; }));
  h.monitor.start('old', h.receiver);
  const pending = h.fire();
  h.monitor.start('new', h.receiver);
  assert.equal(h.requests[0].options.signal.aborted, true);
  finish({ status: 401 });
  await pending;
  assert.equal(h.notices.length, 0);
  assert.equal(h.listeners.size, 5);
  h.monitor.stop();
  assert.equal(h.listeners.size, 0);
});


test('backward wall-clock corrections do not suppress renewal or bypass throttling', async () => {
  const h = harness();
  h.monitor.start('session', h.receiver);
  await h.fire(); // Monotonic clock starts at zero: first input must still renew.
  assert.equal(h.requests.length, 1);
  h.adjustWallClock(-60 * 60_000);
  h.advance(59_999);
  await h.fire();
  assert.equal(h.requests.length, 1);
  h.advance(1);
  await h.fire();
  assert.equal(h.requests.length, 2);
});
