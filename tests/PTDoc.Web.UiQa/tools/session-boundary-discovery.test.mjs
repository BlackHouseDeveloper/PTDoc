import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const qaRoot = fileURLToPath(new URL('../', import.meta.url));
const cli = fileURLToPath(new URL('../node_modules/@playwright/test/cli.js', import.meta.url));

function discover(config) {
  const output = execFileSync(process.execPath, [cli, 'test', '--list', '--reporter=json', '--config', config], {
    cwd: qaRoot,
    encoding: 'utf8',
    env: {
      ...process.env,
      PTDOC_WEB_BASE_URL: 'https://example.test',
      PTDOC_UI_QA_REQUIRE_FULL_FIXTURES: 'false',
      PTDOC_UI_QA_FAULT_PROXY_CONTROL_URL: '',
      PTDOC_UI_QA_FAULT_PROXY_NONCE: ''
    }
  });
  const report = JSON.parse(output);
  assert.deepEqual(report.errors, []);
  const specs = suite => [...(suite.specs ?? []), ...(suite.suites ?? []).flatMap(specs)];
  return report.suites.flatMap(specs);
}

test('ordinary hosted QA discovery excludes the localhost session-boundary suite', () => {
  const specs = discover('playwright.config.ts');
  assert.ok(specs.length > 0);
  assert.ok(specs.every(spec => spec.file !== 'session-circuit-boundary.spec.ts'));
  assert.ok(specs.some(spec => spec.file === 'beta-e2e.spec.ts'));
});

test('explicit session-boundary configuration discovers only its seven cases', () => {
  const specs = discover('playwright.session-boundary.config.ts');
  assert.equal(specs.length, 7);
  assert.ok(specs.every(spec => spec.file === 'session-circuit-boundary.spec.ts'));
});
