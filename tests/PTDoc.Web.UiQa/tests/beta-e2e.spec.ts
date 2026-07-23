import { expect, Page, test } from '@playwright/test';
import { attachConsoleCapture, expectNoRelevantConsoleErrors } from './helpers/auth';

const webBaseUrl = process.env.PTDOC_WEB_BASE_URL ?? 'https://ptdoc.bhdevsites.com';
const apiBaseUrl = process.env.PTDOC_UI_QA_API_BASE_URL ?? getDefaultApiBaseUrl(webBaseUrl);
const patientChartPath = process.env.PTDOC_UI_QA_PATIENT_CHART_PATH;
const evaluationDraftPath = process.env.PTDOC_UI_QA_EVALUATION_DRAFT_PATH;
const sharedPin = process.env.PTDOC_UI_QA_PIN;

const roles = {
  admin: process.env.PTDOC_UI_QA_ADMIN_USERNAME ?? 'january.beta',
  pt: process.env.PTDOC_UI_QA_PT_USERNAME ?? 'dani.beta',
  pta: process.env.PTDOC_UI_QA_PTA_USERNAME ?? 'pta.beta',
  patient: process.env.PTDOC_UI_QA_PATIENT_USERNAME ?? 'patient.beta'
};

const rolePins = {
  admin: process.env.PTDOC_UI_QA_ADMIN_PIN ?? sharedPin,
  pt: process.env.PTDOC_UI_QA_PT_PIN ?? sharedPin,
  pta: process.env.PTDOC_UI_QA_PTA_PIN ?? sharedPin,
  patient: process.env.PTDOC_UI_QA_PATIENT_PIN ?? sharedPin
};

test.describe('PTDoc hosted beta E2E gate', () => {
  test('hosted beta preflight is healthy and does not load development origins', async ({ page, request }) => {
    expect(new URL(webBaseUrl).hostname).toBe('ptdoc.bhdevsites.com');

    const [webResponse, liveResponse, readyResponse] = await Promise.all([
      request.get(webBaseUrl),
      request.get(`${apiBaseUrl}/health/live`),
      request.get(`${apiBaseUrl}/health/ready`)
    ]);
    expect(webResponse.ok()).toBeTruthy();
    expect(liveResponse.ok()).toBeTruthy();
    expect(readyResponse.ok()).toBeTruthy();

    attachConsoleCapture(page);
    await page.goto('/login');
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('form[data-testid="login-form"]')).toBeVisible();
    await expect(page.locator('link[rel="stylesheet"]')).not.toHaveCount(0);
    await expectNoDevelopmentOrigins(page);
    await expectNoRelevantConsoleErrors(page);
  });

  for (const role of Object.keys(roles) as Array<keyof typeof roles>) {
    test(`${role} seeded beta account completes the login UX`, async ({ page }) => {
      await loginAs(page, roles[role], rolePins[role]);
      await expect(page.locator('#username')).toHaveCount(0);
      await expectNoVisibleFrameworkError(page);
      await expectNoDevelopmentOrigins(page);
      await expectNoRelevantConsoleErrors(page);
    });
  }

  test('admin patient search and chart navigation remain usable after refresh at the beta floor viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await loginAs(page, roles.admin, rolePins.admin);
    await page.goto('/patients');
    await page.waitForLoadState('domcontentloaded');

    const search = page.getByRole('textbox', { name: 'Search by name, MRN, or email' });
    await search.fill('BETA-PT-001');
    const patientCard = page.getByTestId(/patient-card-/).filter({ hasText: /Avery Adams|BETA-PT-001/i });
    await expect(patientCard).toHaveCount(1);
    await expectNoDocumentHorizontalOverflow(page);

    await patientCard.click();
    await expect(page).toHaveURL(/\/patient\/[0-9a-f-]+$/i);
    await expect(page.getByRole('heading', { name: 'Patient Information' })).toBeVisible();

    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await expect(page.getByRole('heading', { name: 'Patient Information' })).toBeVisible();
    await expectNoDevelopmentOrigins(page);
    await expectNoRelevantConsoleErrors(page);
  });

  test('PT chart tabs are route-backed, usable with browser history, and retain their active panel after refresh', async ({ page }) => {
    await loginAs(page, roles.pt, rolePins.pt);
    const chartPath = patientChartPath ?? await findSeededPatientChartPath(page);
    await page.goto(chartPath);
    await page.waitForLoadState('domcontentloaded');

    const notesTab = page.getByTestId('patient-profile-tab-notes');
    const documentsTab = page.getByTestId('patient-profile-tab-documents');
    await expect(notesTab).toHaveAttribute('href', /\?tab=notes$/);
    await expect(documentsTab).toHaveAttribute('href', /\?tab=documents$/);

    await notesTab.click();
    await expect(page.getByTestId('patient-profile-panel-notes')).toBeVisible();
    await documentsTab.click();
    await expect(page.getByTestId('patient-profile-panel-documents')).toBeVisible();
    await page.goBack();
    await expect(page.getByTestId('patient-profile-panel-notes')).toBeVisible();

    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await expect(page.getByTestId('patient-profile-panel-notes')).toBeVisible();
    await expectNoDevelopmentOrigins(page);
    await expectNoRelevantConsoleErrors(page);
  });

  test('patient role has no clinician navigation and cannot render patient-directory data from a direct route', async ({ page }) => {
    await loginAs(page, roles.patient, rolePins.patient);
    await expect(page.getByRole('link', { name: 'Patients' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Appointments' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Notes' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0);

    await page.goto('/patients');
    await page.waitForLoadState('domcontentloaded');
    await expect(page.getByTestId('patients-card-section')).toHaveCount(0);
    await expectNoRelevantConsoleErrors(page);
  });

  test('theme toggle is keyboard-operable and its preference persists through refresh', async ({ page }) => {
    await loginAs(page, roles.admin, rolePins.admin);
    await page.goto('/dashboard');
    await page.waitForLoadState('domcontentloaded');

    const originalTheme = await page.evaluate(() => localStorage.getItem('ptdoc-theme'));
    try {
      const themeToggle = page.getByRole('button', { name: /Switch to (dark|light) theme/i });
      const expectedTheme = originalTheme === 'dark' ? 'light' : 'dark';
      await themeToggle.focus();
      await page.keyboard.press('Enter');
      await expect.poll(() => page.evaluate(() => localStorage.getItem('ptdoc-theme'))).toBe(expectedTheme);

      await page.reload();
      await page.waitForLoadState('domcontentloaded');
      await expect.poll(() => page.evaluate(() => localStorage.getItem('ptdoc-theme'))).toBe(expectedTheme);
    } finally {
      await restoreTheme(page, originalTheme);
    }

    await expectNoRelevantConsoleErrors(page);
  });

  test('reversible evaluation draft change persists across refresh and is restored', async ({ page }) => {
    test.skip(
      !evaluationDraftPath,
      'Set PTDOC_UI_QA_EVALUATION_DRAFT_PATH to an approved reversible PT Evaluation draft before mutating beta data.');

    await loginAs(page, roles.pt, rolePins.pt);
    await page.goto(evaluationDraftPath!);
    await page.waitForLoadState('domcontentloaded');

    const field = page.locator('#additional-functional-limitations');
    await expect(field).toBeVisible();
    const originalValue = await field.inputValue();
    const marker = `Beta E2E persistence ${Date.now()} | stairs, 2 flights; carry 18 lb.`;
    let mutationAttempted = false;

    try {
      await field.fill(marker);
      mutationAttempted = true;
      await saveAndExpectConfirmation(page);
      await page.reload();
      await expect(field).toHaveValue(marker);
    } finally {
      if (mutationAttempted) {
        await page.goto(evaluationDraftPath!);
        await page.waitForLoadState('domcontentloaded');
        await expect(field).toBeVisible();
        await field.fill(originalValue);
        await saveAndExpectConfirmation(page);
        await page.reload();
        await expect(field).toHaveValue(originalValue);
      }
    }

    await expectNoRelevantConsoleErrors(page);
  });
});

async function loginAs(page: Page, username: string, pin: string | undefined) {
  if (!pin) {
    throw new Error('A beta PIN is required. Set PTDOC_UI_QA_PIN or the role-specific PTDOC_UI_QA_<ROLE>_PIN variable outside the repository.');
  }

  attachConsoleCapture(page);
  await page.context().clearCookies();
  await page.goto('/login');
  await page.waitForLoadState('domcontentloaded');
  await page.locator('#username').fill(username);
  await page.locator('#pin').fill(pin);
  await page.locator('form[data-testid="login-form"] button[type="submit"]').click();
  await expect(page.locator('#username')).toHaveCount(0);
}

async function findSeededPatientChartPath(page: Page) {
  await page.goto('/patients');
  await page.waitForLoadState('domcontentloaded');
  const search = page.getByRole('textbox', { name: 'Search by name, MRN, or email' });
  await search.fill('BETA-PT-001');
  const patientCard = page.getByTestId(/patient-card-/).filter({ hasText: /Avery Adams|BETA-PT-001/i });
  await expect(patientCard).toHaveCount(1);
  const testId = await patientCard.getAttribute('data-testid');
  const patientId = testId?.replace(/^patient-card-/, '');
  expect(patientId).toMatch(/^[0-9a-f-]+$/i);
  return `/patient/${patientId}`;
}

async function saveAndExpectConfirmation(page: Page) {
  await page.getByRole('button', { name: /Save Draft/i }).click();
  await expect(page.getByText(/saved|draft saved/i)).toBeVisible();
}

async function expectNoDevelopmentOrigins(page: Page) {
  const developmentOrigins = await page.evaluate(() => performance.getEntriesByType('resource')
    .map(entry => entry.name)
    .filter(name => /localhost|127\.0\.0\.1|devtunnels\.ms|azurewebsites\.net/i.test(name)));

  expect(developmentOrigins).toEqual([]);
}

async function expectNoDocumentHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => Math.ceil(document.documentElement.scrollWidth - document.documentElement.clientWidth));
  expect(overflow).toBeLessThanOrEqual(1);
}

async function expectNoVisibleFrameworkError(page: Page) {
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await expect(page.locator('text=/Unhandled exception|An unhandled error has occurred/i')).toBeHidden();
}

async function restoreTheme(page: Page, theme: string | null) {
  await page.evaluate(value => {
    if (value) {
      localStorage.setItem('ptdoc-theme', value);
      window.ptdocTheme?.setTheme(value);
      return;
    }

    localStorage.removeItem('ptdoc-theme');
    window.ptdocTheme?.setTheme('light');
  }, theme);
}

function getDefaultApiBaseUrl(baseUrl: string) {
  const url = new URL(baseUrl);
  if (url.hostname === 'ptdoc.bhdevsites.com') {
    return 'https://api-ptdoc.bhdevsites.com';
  }

  return url.origin;
}

declare global {
  interface Window {
    ptdocTheme?: {
      setTheme: (theme: 'light' | 'dark') => void;
    };
  }
}
