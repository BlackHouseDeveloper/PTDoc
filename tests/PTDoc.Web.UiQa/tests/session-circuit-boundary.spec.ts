import { expect, Page, test } from '@playwright/test';
import { authenticateAs } from './helpers/auth';
import { clearFaults, configureFault, gotoInteractive, loginAs } from './helpers/branch-live';

// Run only through the disposable localhost harness with --session-boundary.
// Fault rules both count attempted API operations and prevent accidental writes.
test.describe('session circuit boundary', () => {
  test.describe.configure({ mode: 'serial', timeout: 150_000 });
  test.beforeEach(async () => {
    const origin = new URL(process.env.PTDOC_WEB_BASE_URL ?? 'http://localhost');
    if (!['127.0.0.1', 'localhost'].includes(origin.hostname)
      || !process.env.PTDOC_UI_QA_FAULT_PROXY_CONTROL_URL) {
      throw new Error('Use the isolated localhost harness with --session-boundary.');
    }
    await clearFaults();
  });
  test.afterEach(async () => { await clearFaults(); });

  for (const status of [401, 409]) {
    test(`activity ${status} reaches sibling UI and blocks protected reads and saves`, async ({ page }) => {
      let activityStatus = 204;
      await page.route('**/auth/activity', route => route.fulfill({ status: activityStatus }));
      await openDraft(page);
      // Preserve production throttling; wait beyond the last real setup input.
      await page.waitForTimeout(61_000);
      activityStatus = status;
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/auth/activity' && r.status() === status);
      await page.getByLabel('First name', { exact: true }).press('End');
      await response;
      await expectRecovery(page, status === 409 ? 'Reload session' : 'Sign in');
      await expectBlockedOperations(page);
    });
  }

  for (const status of [401, 409, 503]) {
    test(`API ${status} has the correct recovery behavior in the sibling monitor`, async ({ page }) => {
      await openDraft(page);
      await configureFault({ method: 'POST', path: '/api/v1/providers/candidates', status });
      await page.getByRole('button', { name: 'Submit for approval', exact: true }).click();
      await expect.poll(async () => (await remainingRules()).length).toBe(0);
      await expect(page.locator('.provider-admin__state--error')).toBeVisible();
      await expect(page.getByLabel('First name', { exact: true })).toHaveValue('Retained');
      if (status === 401) {
        await expectRecovery(page, 'Sign in');
        await expectBlockedOperations(page);
      } else {
        // API 409 is a business conflict; only activity 409 means changed sign-in.
        await expect(page.getByRole('button', { name: /^(Sign in|Reload session)$/ })).toHaveCount(0);
        await configureFault({ method: 'GET', path: '/api/v1/admin/providers', status: 503, occurrences: 2 });
        await page.getByRole('button', { name: 'Search', exact: true }).click();
        await expect.poll(async () => (await remainingRules()).length).toBe(0);
        await expect(page.getByLabel('First name', { exact: true })).toHaveValue('Retained');
      }
    });
  }

  for (const identity of ['replacement', 'anonymous']) {
    test(`reconnect with ${identity} identity cannot authorize or remove the retained draft`, async ({ page, context }) => {
      // Keep the activity endpoint successful so only the reconnect identity boundary
      // can produce the expected notice; no synthetic callback ends the circuit.
      await page.route('**/auth/activity', route => route.fulfill({ status: 204 }));
      await openDraft(page);
      const originalUrl = page.url();
      await page.evaluate(async () => {
        const blazor = (window as any).Blazor;
        // Disable automatic retries while changing cookies in the second tab.
        blazor.defaultReconnectionHandler.onConnectionDown = () => {};
        await blazor._internal.forceCloseConnection();
      });
      const replacement = await context.newPage();
      try {
        // Even the same account receives a different sign-in ID. The helper replaces
        // browser cookies; no bearer token is exposed to the page or test output.
        if (identity === 'replacement') {
          await authenticateAs(replacement,
            process.env.PTDOC_UI_QA_ADMIN_USERNAME ?? process.env.PTDOC_UI_QA_USERNAME!,
            process.env.PTDOC_UI_QA_ADMIN_PIN ?? process.env.PTDOC_UI_QA_PIN!);
        } else {
          await context.clearCookies();
        }
        expect(await page.evaluate(() => (window as any).Blazor.reconnect())).toBe(true);
        await expectRecovery(page, identity === 'replacement' ? 'Reload session' : 'Sign in');
        await expect(page).toHaveURL(originalUrl);
        await expectBlockedOperations(page);
        // The replacement tab can still load protected data in its fresh circuit.
        if (identity === 'replacement') {
          await clearFaults();
          await gotoInteractive(replacement, '/settings');
          await replacement.getByRole('button', { name: /Provider Directory/i }).click();
          await expect(replacement.getByRole('heading', { name: 'Provider Directory', exact: true })).toBeVisible();
          await expect(replacement.getByText('Loading provider directory…', { exact: true })).toHaveCount(0);
          await expect(replacement.locator('.provider-admin__state--error')).toHaveCount(0);
          await expect(replacement.getByRole('button', { name: /^(Sign in|Reload session)$/ })).toHaveCount(0);
        }
      } finally {
        await replacement.close();
      }
    });
  }
});

async function openDraft(page: Page) {
  await loginAs(page, 'admin');
  await gotoInteractive(page, '/settings');
  await page.getByRole('button', { name: /Provider Directory/i }).click();
  await expect(page.getByRole('heading', { name: 'Provider Directory', exact: true })).toBeVisible();
  await expect(page.getByText('Loading provider directory…', { exact: true })).toHaveCount(0);
  await expect(page.locator('.provider-admin__state--error')).toHaveCount(0);
  await page.getByRole('button', { name: 'Submit provider', exact: true }).click();
  await page.getByLabel('First name', { exact: true }).fill('Retained');
  await page.getByLabel('Last name', { exact: true }).fill('CircuitBoundarySynthetic');
}

async function expectRecovery(page: Page, action: string) {
  await expect(page.getByRole('button', { name: action, exact: true })).toBeVisible();
  await expect(page.getByLabel('First name', { exact: true })).toHaveValue('Retained');
  await expect(page.getByLabel('Last name', { exact: true })).toHaveValue('CircuitBoundarySynthetic');
}

async function expectBlockedOperations(page: Page) {
  const originalUrl = page.url();
  await configureFault({ method: 'GET', path: '/api/v1/admin/providers', status: 503, occurrences: 2 });
  await configureFault({ method: 'POST', path: '/api/v1/providers/candidates', status: 503 });
  const initial = await remainingRules();
  // Use a distinct, local validation message before each operation so an old
  // error cannot satisfy the assertion before its SignalR event has completed.
  await requireLocalValidation(page);
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.locator('.provider-admin__state--error')).toHaveText('Your session is missing or expired. Sign in again.');
  await requireLocalValidation(page);
  await page.getByRole('button', { name: 'Submit for approval', exact: true }).click();
  await expect(page.locator('.provider-admin__state--error')).toHaveText('Your session is missing or expired. Sign in again.');
  expect(await remainingRules()).toEqual(initial);
  await expect(page).toHaveURL(originalUrl);
  await expect(page.getByLabel('First name', { exact: true })).toHaveValue('Retained');
}

async function requireLocalValidation(page: Page) {
  await page.getByLabel('First name', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Submit for approval', exact: true }).click();
  await expect(page.locator('.provider-admin__state--error')).toHaveText('First and last name are required.');
  await page.getByLabel('First name', { exact: true }).fill('Retained');
}

async function remainingRules(): Promise<unknown[]> {
  const response = await fetch(process.env.PTDOC_UI_QA_FAULT_PROXY_CONTROL_URL!, {
    headers: { 'x-ptdoc-live-control': process.env.PTDOC_UI_QA_FAULT_PROXY_NONCE! }
  });
  expect(response.status).toBe(200);
  return (await response.json() as { rules: unknown[] }).rules;
}
