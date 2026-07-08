import { expect, Page, test } from '@playwright/test';
import { attachConsoleCapture, authenticateIfNeeded, expectNoRelevantConsoleErrors } from './helpers/auth';

const intakePath = process.env.PTDOC_UI_QA_INTAKE_PATH;
const writableNoteWorkspacePath = process.env.PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH;
const patientChartPath = process.env.PTDOC_UI_QA_PATIENT_CHART_PATH
  ?? '/patient/f9c2cb68-4ab4-4f57-a1db-73ed8e2da789';
const ptUsername = process.env.PTDOC_UI_QA_PT_USERNAME ?? 'amorgan';
const ptPin = process.env.PTDOC_UI_QA_PT_PIN ?? process.env.PTDOC_UI_QA_PIN;

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
    await page.goto('/appointments');
    await page.waitForLoadState('domcontentloaded');

    const weekView = page.getByRole('link', { name: 'Week View' });
    await expect(weekView).toHaveAttribute('href', '/appointments?dateRange=week');
    await page.goto('/appointments?dateRange=week');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('.week-grouping-control')).toBeVisible();
    await expect(page.locator('body')).toContainText(/Week Schedule|Week of/i);

    const clinicianGrid = page.locator('.scheduler-grid.week-grouping-clinician');
    if (await clinicianGrid.count() > 0) {
      await expect(clinicianGrid).toBeVisible();
    } else {
      await expect(page.locator('body')).toContainText(/No appointments scheduled for this week|No appointments need notes for this period/i);
    }

    await page.getByRole('button', { name: 'Day' }).click();
    const dayGrid = page.locator('.scheduler-grid.week-grouping-day');
    if (await dayGrid.count() > 0) {
      await expect(dayGrid).toBeVisible();
    } else {
      await expect(page.getByRole('button', { name: 'Day' })).toHaveAttribute('aria-pressed', 'true');
    }
    await expectNoRelevantConsoleErrors(page);
  });

  test('patients add action opens modal without relying on hydrated click only', async ({ page }) => {
    await authenticateIfNeeded(page);
    await page.goto('/patients');
    await page.waitForLoadState('domcontentloaded');

    const addPatient = page.getByRole('link', { name: /^Add Patient$/ });
    await expect(addPatient).toHaveAttribute('href', '/patients?action=add');
    await page.goto('/patients?action=add');
    await page.waitForLoadState('domcontentloaded');

    await expect(page).toHaveURL(/\/patients\?action=add$/);
    await expect(page.getByRole('heading', { name: 'Add New Patient' })).toBeVisible();
    await expect(page.locator('#firstName')).toBeVisible();
    await expectNoRelevantConsoleErrors(page);
  });

  test('patient chart tabs and PT start-new-note entry are route-backed', async ({ page }) => {
    test.skip(!ptPin, 'Set PTDOC_UI_QA_PIN or PTDOC_UI_QA_PT_PIN to verify PT note-entry coverage.');

    await loginThroughForm(page, ptUsername, ptPin!);
    await page.goto(patientChartPath);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.getByTestId('patient-primary-action')).toHaveAttribute('href', /\/patient\/[^?]+\?action=new-note$/);

    for (const tabName of ['Notes', 'Documents', 'Communications']) {
      const tab = page.getByTestId(`patient-profile-tab-${tabName.toLowerCase()}`);
      const href = await tab.getAttribute('href');
      expect(href).not.toBeNull();
      expect(href!).toMatch(new RegExp(`\\/patient\\/[^?]+\\?tab=${tabName.toLowerCase()}$`));

      await page.goto(href!);
      await page.waitForLoadState('domcontentloaded');
      await expect(page.getByTestId(`patient-profile-panel-${tabName.toLowerCase()}`)).toBeVisible();
      await expect(page).toHaveURL(new RegExp(`\\/patient\\/[^?]+\\?tab=${tabName.toLowerCase()}$`));
    }

    const insurance = page.getByTestId('patient-profile-tab-insurance-authorization');
    await expect(insurance).toHaveAttribute('href', /\/patient\/[^/]+\/info$/);

    const newNoteHref = await page.getByTestId('patient-primary-action').getAttribute('href');
    expect(newNoteHref).not.toBeNull();
    await page.goto(newNoteHref!);
    await page.waitForLoadState('domcontentloaded');
    await expect(page.getByTestId('patient-note-type-chooser')).toBeVisible();
    await page.getByRole('button', { name: 'Evaluation Note' }).click();
    await expect(page).toHaveURL(/\/patient\/[^/]+\/new-note\?noteType=Evaluation%20Note$/);
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
      const slider = page.locator("input[type='range'][aria-label='Pain severity from 0 to 10']");
      await slider.evaluate(element => {
        (element as HTMLInputElement).value = '0';
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
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

async function loginThroughForm(page: Page, username: string, pin: string) {
  attachConsoleCapture(page);
  await page.context().clearCookies();
  await page.goto('/login');
  await page.waitForLoadState('domcontentloaded');
  await page.locator('#username').fill(username);
  await page.locator('#pin').fill(pin);
  await page.locator('form[data-testid="login-form"] button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
  await expect(page.locator('#username')).toHaveCount(0);
}
