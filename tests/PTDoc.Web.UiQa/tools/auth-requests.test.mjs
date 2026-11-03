import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../../../src/PTDoc.Web/wwwroot/js/auth.js', import.meta.url), 'utf8');

function client(fetch) {
  const context = vm.createContext({ window: {}, fetch });
  vm.runInContext(source, context);
  return context.window.ptdocAuth;
}

test('transport failure returns a retryable failure without leaking the error', async () => {
  const auth = client(async () => { throw new TypeError('sensitive transport details'); });
  const result = await auth.postStepUp('/auth/example', { code: '123456' });
  assert.equal(result.ok, false);
  assert.equal(result.status, 0);
  assert.equal(JSON.stringify(result.body), '{}');
});

test('HTTP failures and malformed response bodies remain failures', async () => {
  const auth = client(async () => ({ ok: false, status: 503, json: async () => { throw new SyntaxError(); } }));
  const result = await auth.postStepUp('/auth/example', {});
  assert.equal(result.ok, false);
  assert.equal(result.status, 503);
  assert.equal(JSON.stringify(result.body), '{}');
});

test('successful requests retain same-origin credentials and JSON payloads', async () => {
  const auth = client(async (path, options) => {
    assert.equal(path, '/auth/example');
    assert.equal(options.credentials, 'same-origin');
    assert.equal(options.body, '{"code":"123456"}');
    return { ok: true, status: 200, json: async () => ({ accepted: true }) };
  });
  const result = await auth.postStepUp('/auth/example', { code: '123456' });
  assert.equal(result.ok, true);
  assert.equal(result.body.accepted, true);
});

for (const clinicId of ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '00000000-0000-0000-0000-000000000000', '']) {
  test(`sign-in posts the selected clinic without changing the return route (${clinicId || 'home'})`, () => {
    let submitted;
    const form = {
      fields: {},
      appendChild(input) { this.fields[input.name] = input.value; },
      submit() { submitted = this; }
    };
    const context = vm.createContext({
      window: { location: { search: clinicId ? `?clinicId=${clinicId}` : '' } },
      URLSearchParams,
      document: {
        createElement(tag) { return tag === 'form' ? form : {}; },
        body: { appendChild() {} }
      }
    });
    vm.runInContext(source, context);
    context.window.ptdocAuth.submitLogin('synthetic-user', '12345678', '/organizations');
    assert.equal(submitted.action, '/auth/login');
    assert.equal(submitted.method, 'post');
    assert.equal(submitted.fields.clinicId, clinicId);
    assert.equal(submitted.fields.returnUrl, '/organizations');
  });
}
