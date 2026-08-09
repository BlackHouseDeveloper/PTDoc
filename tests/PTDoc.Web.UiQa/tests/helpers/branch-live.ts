import { APIResponse, expect, Page, TestInfo } from '@playwright/test';
import { appendFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { authenticateAs, waitForAppInteractive } from './auth';

export type FaultRule = {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  path: string;
  delayMs?: number;
  status?: number;
  occurrences?: number;
};

export type EvidenceDisposition = 'Pass' | 'Pass with limitation' | 'Fail' | 'Blocked';

const artifactDirectory = process.env.PTDOC_UI_QA_ARTIFACT_DIR;
const faultControlUrl = process.env.PTDOC_UI_QA_FAULT_PROXY_CONTROL_URL;
const faultNonce = process.env.PTDOC_UI_QA_FAULT_PROXY_NONCE;
export const fixturePrefix = sanitizePrefix(process.env.PTDOC_UI_QA_FIXTURE_PREFIX ?? 'live-unblock');
let activeRole = 'not established';
let activeRoute = 'API workflow through Web origin';
const recentFixtureIds: string[] = [];

export async function loginAs(page: Page, role: 'admin' | 'pt' | 'pta') {
  const username = role === 'admin'
    ? process.env.PTDOC_UI_QA_ADMIN_USERNAME ?? process.env.PTDOC_UI_QA_USERNAME
    : role === 'pt'
      ? process.env.PTDOC_UI_QA_PT_USERNAME
      : process.env.PTDOC_UI_QA_PTA_USERNAME;
  const pin = role === 'admin'
    ? process.env.PTDOC_UI_QA_ADMIN_PIN ?? process.env.PTDOC_UI_QA_PIN
    : role === 'pt'
      ? process.env.PTDOC_UI_QA_PT_PIN ?? process.env.PTDOC_UI_QA_PIN
      : process.env.PTDOC_UI_QA_PTA_PIN ?? process.env.PTDOC_UI_QA_PIN;
  if (!username || !pin) {
    throw new Error(`Missing ${role} live-verification credentials.`);
  }
  await authenticateAs(page, username, pin);
  activeRole = role;
}

export async function gotoInteractive(page: Page, route: string) {
  activeRoute = route;
  await page.goto(route);
  await page.waitForLoadState('domcontentloaded');
  await waitForAppInteractive(page);
}

export async function apiJson<T>(page: Page, method: string, route: string, data?: unknown): Promise<T> {
  const response = await apiResponse(page, method, route, data);
  if (!response.ok()) {
    throw new Error(`Expected ${method} ${route} to succeed, received HTTP ${response.status()}.`);
  }
  return await response.json() as T;
}

export async function apiResponse(page: Page, method: string, route: string, data?: unknown): Promise<APIResponse> {
  return page.request.fetch(route, {
    method,
    data,
    failOnStatusCode: false,
    headers: data === undefined ? undefined : { 'content-type': 'application/json' }
  });
}

export async function configureFault(rule: FaultRule) {
  requireFaultControl();
  const response = await fetch(faultControlUrl!, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-ptdoc-live-control': faultNonce!
    },
    body: JSON.stringify(rule)
  });
  if (response.status !== 201) {
    throw new Error(`Fault proxy rejected an allowlisted rule with HTTP ${response.status()}.`);
  }
}

export async function clearFaults() {
  requireFaultControl();
  const response = await fetch(faultControlUrl!, {
    method: 'DELETE',
    headers: { 'x-ptdoc-live-control': faultNonce! }
  });
  if (response.status !== 204) {
    throw new Error(`Fault proxy clear failed with HTTP ${response.status()}.`);
  }
}

export async function createSyntheticPatient(page: Page, suffix: string, payerInfoJson?: string) {
  const response = await apiJson<{ id: string }>(page, 'POST', '/api/v1/patients/', {
    firstName: 'Live',
    lastName: `${fixturePrefix}-${suffix}`.slice(0, 90),
    dateOfBirth: '1990-01-15T00:00:00Z',
    email: `${fixturePrefix}.${suffix}@example.test`.toLowerCase(),
    phone: '555-010-0199',
    payerInfoJson
  });
  await recordFixture('Patient', response.id, 'Disposable database teardown');
  return response;
}

export async function verifyWithEvidence(
  testInfo: TestInfo,
  checkIds: string[],
  expected: string,
  action: () => Promise<string | void>,
  limitation?: string) {
  try {
    const observed = await action();
    await recordEvidence({
      checkIds,
      disposition: limitation ? 'Pass with limitation' : 'Pass',
      expected,
      observed: limitation ?? observed ?? 'Expected behavior was directly observed.',
      artifact: `${testInfo.title}; evidence.jsonl; api.log; web.log`
    });
  } catch (error) {
    await recordEvidence({
      checkIds,
      disposition: 'Fail',
      expected,
      observed: 'Assertion failed; inspect the retained Playwright trace and screenshot.',
      artifact: `${testInfo.title}; retained Playwright trace/screenshot; api.log; web.log`
    });
    throw error;
  }
}

export async function recordBlocked(checkIds: string[], expected: string, reason: string) {
  await recordEvidence({ checkIds, disposition: 'Blocked', expected, observed: reason });
}

export async function expectStatus(response: APIResponse, allowed: number[]) {
  expect(allowed, `unexpected HTTP ${response.status()}`).toContain(response.status());
}

export async function recordFixture(type: string, id: string, cleanup: string) {
  recentFixtureIds.push(`${type}:${id}`);
  if (recentFixtureIds.length > 20) recentFixtureIds.shift();
  if (!artifactDirectory) return;
  await appendJsonLine('FIXTURE_LEDGER.jsonl', { type, id, cleanup });
}

async function recordEvidence(record: {
  checkIds: string[];
  disposition: EvidenceDisposition;
  expected: string;
  observed: string;
  artifact?: string;
}) {
  if (!artifactDirectory) return;
  await appendJsonLine('evidence.jsonl', {
    ...record,
    timestampUtc: new Date().toISOString(),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    role: activeRole,
    route: activeRoute,
    syntheticFixtureIds: [...recentFixtureIds],
    viewport: 'Desktop Chrome project viewport',
    zoom: '100%',
    theme: 'Scenario-defined',
    inputMethod: 'Keyboard and pointer automation'
  });
}

async function appendJsonLine(fileName: string, value: unknown) {
  await mkdir(artifactDirectory!, { recursive: true });
  await appendFile(path.join(artifactDirectory!, fileName), `${JSON.stringify(value)}\n`, 'utf8');
}

function requireFaultControl() {
  if (!faultControlUrl || !faultNonce) {
    throw new Error('The branch live suite must be launched through test:branch-live-unblock.');
  }
}

function sanitizePrefix(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9-]/g, '').slice(-24) || 'live-unblock';
}
