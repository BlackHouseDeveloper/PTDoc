import { expect, Page, test } from '@playwright/test';
import { authenticateIfNeeded, expectNoRelevantConsoleErrors } from './helpers/auth';

const intakePath = process.env.PTDOC_UI_QA_INTAKE_PATH;
const writableNoteWorkspacePath = process.env.PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH;

test.describe('PTDoc audit remediation QA', () => {
  test('login validation and protected dashboard route behave consistently', async ({ page }) => {
    await page.context().clearCookies();
    await page.goto('/login');
    await page.waitForLoadState('domcontentloaded');

    const loginSubmit = page.locator('form[data-testid="login-form"] button[type="submit"]');
    await loginSubmit.click();
    await expect(page.getByText('Username or email is required.')).toBeVisible();
    await expect(page.getByText('PIN is required.')).toBeVisible();

    await page.locator('#username').fill('testuser');
    await page.locator('#pin').fill('12');
    await loginSubmit.click();
    await expect(page.getByText('PIN must be 4 digits.')).toBeVisible();

    for (const route of ['/dashboard', '/appointments', '/notes', '/audit-missing-route']) {
      await page.context().clearCookies();
      await gotoProtectedRouteExpectingLogin(page, route);
      await expect(page.locator('#username')).toBeVisible();
    }

    await authenticateIfNeeded(page);
    await page.goto('/dashboard');
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('body')).toContainText(/Dashboard/i);

    await page.goto('/logout');
    await page.waitForLoadState('domcontentloaded');
    for (const route of ['/dashboard', '/appointments', '/notes', '/audit-missing-route']) {
      await gotoProtectedRouteExpectingLogin(page, route);
      await expect(page.locator('#username')).toBeVisible();
    }
    await expectNoRelevantConsoleErrors(page);
  });

  test('dashboard notes-due card opens the actionable appointments queue', async ({ page }) => {
    await authenticateIfNeeded(page);
    await page.goto('/');
    await page.waitForLoadState('domcontentloaded');

    await page.getByRole('button', { name: 'Open appointments needing notes today' }).click();
    await expect(page).toHaveURL(/\/appointments\?needsNote=true&dateRange=today$/);
    await expect(page.locator('body')).toContainText(/Appointments/i);
    await expectNoRelevantConsoleErrors(page);
  });

  test('appointments week view defaults to clinician grouping and can switch to day grouping', async ({ page }) => {
    await authenticateIfNeeded(page);
    await page.goto('/appointments?dateRange=week');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('.week-grouping-control')).toBeVisible();
    await expect(page.locator('.scheduler-grid.week-grouping-clinician')).toBeVisible();

    await page.getByRole('button', { name: 'Day' }).click();
    await expect(page.locator('.scheduler-grid.week-grouping-day')).toBeVisible();
    await expectNoRelevantConsoleErrors(page);
  });

  test('notes list exposes bounded pagination when more results are available', async ({ page }) => {
    await authenticateIfNeeded(page);
    await page.goto('/notes');
    await page.waitForLoadState('domcontentloaded');

    const loadMore = page.locator('.notes-recent-load-more');
    if (await loadMore.count() === 0) {
      test.skip(true, 'Seed data does not have more than one page of notes.');
    }

    const initialCards = await page.locator('.note-card').count();
    await loadMore.click();
    await expect.poll(() => page.locator('.note-card').count()).toBeGreaterThan(initialCards);
    await expectNoRelevantConsoleErrors(page);
  });

  test('intake pain severity validation and body-map keyboard selection are reachable', async ({ page }) => {
    test.skip(!intakePath, 'Set PTDOC_UI_QA_INTAKE_PATH to a safe editable intake route.');

    await authenticateIfNeeded(page);
    await page.goto(intakePath!);
    await page.waitForLoadState('domcontentloaded');

    await advanceToPainDetailsIfPossible(page);

    const bodyRegion = page.locator('.body-svg-selector__region').first();
    const initialPressed = await bodyRegion.getAttribute('aria-pressed');
    await bodyRegion.focus();
    const focusedScrollY = await page.evaluate(() => window.scrollY);
    await page.keyboard.press('Space');
    await expect(bodyRegion).toHaveAttribute('aria-pressed', initialPressed === 'true' ? 'false' : 'true');
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(focusedScrollY);

    const continueButton = page.getByTestId('continue-button');
    if (await continueButton.isEnabled()) {
      await continueButton.click();
      await expect(page.getByText('Select a pain severity score before continuing.')).toBeVisible();
      await page.locator("input[type='range'][aria-label='Pain severity from 0 to 10']").fill('0');
      await continueButton.click();
      await expect(page.getByText('Select a pain severity score before continuing.')).toHaveCount(0);
    }

    await expectNoRelevantConsoleErrors(page);
  });

  test('writable note workspace saves draft intervention/CPT/HEP edits when a PT fixture is supplied', async ({ page }) => {
    test.skip(!writableNoteWorkspacePath, 'Set PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH to a safe draft note route for a PT-role session.');

    await authenticateIfNeeded(page);
    await page.goto(writableNoteWorkspacePath!);
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('[data-testid="note-workspace-page"]')).toBeVisible();

    await page.getByRole('tab', { name: /Plan|Interventions/i }).first().click();
    await expect(page.getByText(/CPT|Intervention|HEP/i).first()).toBeVisible();

    const cptSelector = page.locator('[data-testid="plan-intervention-cpt"], select')
      .filter({ hasText: '97110' })
      .first();
    if (await cptSelector.count() > 0) {
      const optionValue = await cptSelector.locator('option').filter({ hasText: '97110' }).first().getAttribute('value');
      if (optionValue) {
        await cptSelector.selectOption(optionValue);
      }
    }

    const hepCheckbox = page.getByLabel(/HEP|home exercise/i).first();
    if (await hepCheckbox.count() > 0) {
      await hepCheckbox.check({ force: true });
    }

    await page.getByRole('button', { name: /Save Draft/i }).click();
    await expect(page.getByText(/saved|draft saved/i)).toBeVisible();
    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('[data-testid="note-workspace-page"]')).toBeVisible();
    await expectNoRelevantConsoleErrors(page);
  });
});

async function advanceToPainDetailsIfPossible(page: Page) {
  const painDetails = page.getByText('Pain Details', { exact: true });
  if (await painDetails.isVisible().catch(() => false)) {
    return;
  }

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const continueButton = page.getByTestId('continue-button');
    if (await continueButton.isVisible().catch(() => false)) {
      await continueButton.click();
      if (await painDetails.isVisible().catch(() => false)) {
        return;
      }
    }
  }
}

async function gotoProtectedRouteExpectingLogin(page: Page, route: string) {
  try {
    await page.goto(route, { waitUntil: 'domcontentloaded' });
  } catch (error) {
    if (!String(error).includes('net::ERR_ABORTED')) {
      throw error;
    }
  }

  await expect(page.locator('#username')).toBeVisible();
}
