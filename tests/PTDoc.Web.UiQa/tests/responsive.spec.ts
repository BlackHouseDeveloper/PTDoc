import { expect, Page, test } from '@playwright/test';
import {
  attachConsoleCapture,
  authenticateAs,
  authenticateIfNeeded,
  expectNoRelevantConsoleErrors,
  waitForAppInteractive
} from './helpers/auth';

type ViewportCase = {
  name: string;
  width: number;
  height: number;
};

type RouteCase = {
  name: string;
  path: string;
  titlePattern: RegExp;
};

const DESKTOP_BREAKPOINT = 1200;

const responsiveViewports: ViewportCase[] = [
  { name: '1280x720', width: 1280, height: 720 },
  { name: '1366x768', width: 1366, height: 768 },
  { name: '1440x900', width: 1440, height: 900 },
  { name: '1536x864', width: 1536, height: 864 }
];

const routeCases: RouteCase[] = [
  { name: 'dashboard', path: '/', titlePattern: /Dashboard/i },
  { name: 'appointments', path: '/appointments', titlePattern: /Appointments/i },
  { name: 'intake', path: '/intake', titlePattern: /Intake/i },
  { name: 'notes', path: '/notes', titlePattern: /Notes/i },
  { name: 'settings', path: '/settings', titlePattern: /Settings/i }
];

const settingsViewports: ViewportCase[] = [
  { name: 'roles-900px-breakpoint', width: 900, height: 900 },
  { name: 'shared-767px-breakpoint', width: 767, height: 900 },
  { name: 'shared-520px-breakpoint', width: 520, height: 844 }
];

const noteWorkspacePath = process.env.PTDOC_UI_QA_NOTE_WORKSPACE_PATH;
if (noteWorkspacePath) {
  routeCases.push({
    name: 'note-workspace',
    path: noteWorkspacePath,
    titlePattern: /Note|SOAP|Workspace|Daily/i
  });
}

test.describe('PTDoc responsive UI QA', () => {
  test('mobile signup keeps populated text fields bound and focuses the first invalid field without creating an account', async ({ page }) => {
    attachConsoleCapture(page);
    await page.addInitScript(() => {
      localStorage.clear();
      sessionStorage.clear();
    });
    await page.context().clearCookies();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/signup');
    await page.waitForLoadState('domcontentloaded');
    await waitForAppInteractive(page);

    const form = page.getByTestId('signup-form');
    await expect(form).toBeVisible();
    await expect(page.locator('#roleKey option')).not.toHaveCount(1);
    await expect(page.locator('#clinicId option')).not.toHaveCount(1);

    await page.locator('#fullName').fill('Responsive Signup Tester');
    await page.locator('#dateOfBirth').fill('1990-01-01');
    await page.locator('#email').fill('not-an-email');
    await page.locator('#roleKey').selectOption('PT');
    await expect(page.locator('#licenseNumber')).toBeVisible();
    await page.locator('#clinicId').selectOption({ index: 1 });
    await page.locator('#pinSignup').fill('1234');
    await page.locator('#confirmPinSignup').fill('1234');
    await page.locator('#licenseNumber').fill('PT-1001');
    await page.locator('#licenseState').selectOption('MA');

    await form.getByRole('button', { name: 'Create Account' }).click();

    await expect(page.getByTestId('signup-validation-summary')).toBeVisible();
    await expect(page.locator('#email')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#fullName')).toHaveAttribute('aria-invalid', 'false');
    await expect(page.locator('#pinSignup')).toHaveAttribute('aria-invalid', 'false');
    await expect(page.locator('#confirmPinSignup')).toHaveAttribute('aria-invalid', 'false');
    await expect(page.locator('#email')).toBeFocused();
    await expectNoFrameworkOverlay(page);
    await expectNoRelevantConsoleErrors(page);
  });

  for (const viewport of responsiveViewports) {
    for (const route of routeCases) {
      test(`${route.name} is usable at ${viewport.name} in light mode`, async ({ page }) => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        if (route.path === '/settings') {
          await authenticateForSettings(page);
        } else {
          await authenticateIfNeeded(page);
        }
        await setTheme(page, 'light');
        await gotoAppRoute(page, route.path);

        await expectPageReady(page, route.titlePattern);
        await expectLayoutMode(page, viewport.width);
        await expectNoFrameworkOverlay(page);
        await expectNoDocumentHorizontalOverflow(page);
        await expectNoRelevantConsoleErrors(page);
      });
    }
  }

  test('dashboard is readable at 1280x720 in dark mode', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);
    await setTheme(page, 'dark');
    await gotoAppRoute(page, '/');

    await expectPageReady(page, /Dashboard/i);
    await expectMenuToggleVisible(page);
    await expectLayoutMode(page, 1280);
    await expectNoFrameworkOverlay(page);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoRelevantConsoleErrors(page);
  });

  for (const theme of ['light', 'dark'] as const) {
    for (const viewport of settingsViewports) {
      test(`settings tabs are usable at ${viewport.name} in ${theme} mode`, async ({ page }) => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        await authenticateForSettings(page);
        await setTheme(page, theme);
        await gotoAppRoute(page, '/settings');

        await expect(page.getByRole('heading', { name: 'Roles & Permissions', exact: true })).toBeVisible();
        const rolePermissionsTab = page.getByRole('tab', { name: 'Role Permissions' });
        const securitySettingsTab = page.getByRole('tab', { name: 'Security Settings' });
        await expect(rolePermissionsTab).toHaveAttribute('aria-selected', 'true');
        await securitySettingsTab.click();
        await expect(securitySettingsTab).toHaveAttribute('aria-selected', 'true');
        await rolePermissionsTab.click();

        const permissionLevel = page.getByRole('radio', { name: 'View Clinical Notes: View' });
        await permissionLevel.scrollIntoViewIfNeeded();
        await permissionLevel.focus();
        const permissionScrollBefore = await page.evaluate(() => window.scrollY);
        await permissionLevel.press('End');
        await expect(permissionLevel).toHaveAttribute('aria-checked', 'true');
        const permissionScrollAfter = await page.evaluate(() => window.scrollY);
        expect(Math.abs(permissionScrollAfter - permissionScrollBefore)).toBeLessThanOrEqual(1);

        await page.getByRole('button', { name: /Scheduling & Visit Types/i }).click();
        await expect(page.getByRole('heading', { name: 'Scheduling & Visit Types', exact: true })).toBeVisible();

        const visitTypesTab = page.getByRole('tab', { name: 'Visit Types' });
        const scheduleBlocksTab = page.getByRole('tab', { name: 'Schedule Blocks' });
        const calendarBehaviorTab = page.getByRole('tab', { name: 'Calendar Behavior' });
        const clinicHoursTab = page.getByRole('tab', { name: 'Clinic Hours' });
        await scheduleBlocksTab.click();
        await expect(scheduleBlocksTab).toHaveAttribute('aria-selected', 'true');
        await expect.poll(() => scheduleBlocksTab.evaluate(element => getComputedStyle(element).borderTopColor))
          .not.toBe('rgba(0, 0, 0, 0)');

        await calendarBehaviorTab.click();
        const doubleBookingSwitch = page.getByRole('switch', { name: 'Allow Double Booking' });
        const switchThumb = doubleBookingSwitch.locator('span');
        const disabledThumbTransform = await switchThumb.evaluate(element => getComputedStyle(element).transform);
        await doubleBookingSwitch.click();
        await expect(doubleBookingSwitch).toHaveAttribute('aria-checked', 'true');
        await expect.poll(() => switchThumb.evaluate(element => getComputedStyle(element).transform))
          .not.toBe(disabledThumbTransform);

        await visitTypesTab.click();
        await visitTypesTab.scrollIntoViewIfNeeded();
        await visitTypesTab.focus();
        const tabScrollBefore = await page.evaluate(() => window.scrollY);
        await visitTypesTab.press('End');
        await expect(clinicHoursTab).toHaveAttribute('aria-selected', 'true');
        const tabScrollAfter = await page.evaluate(() => window.scrollY);
        expect(Math.abs(tabScrollAfter - tabScrollBefore)).toBeLessThanOrEqual(1);

        await expectNoFrameworkOverlay(page);
        await expectNoDocumentHorizontalOverflow(page);
        await expectNoRelevantConsoleErrors(page);
      });
    }
  }

  test('desktop sidebar collapses to an icon rail without clipping controls', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);
    await gotoAppRoute(page, '/');
    await expectPageReady(page, /Dashboard/i);

    const toggle = page.locator('button.menu-toggle').first();
    await expect(toggle).toBeVisible();

    const sidebar = page.locator('.sidebar').first();
    await expect(sidebar).toBeVisible();

    if (!(await sidebar.evaluate(element => element.classList.contains('closed')))) {
      await toggle.click();
    }

    await expect(page.locator('.sidebar.closed')).toBeVisible();
    await expect(page.locator('.ptdoc-nav-brand')).toHaveCount(0);
    await expectSidebarControlsNotClipped(page);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoRelevantConsoleErrors(page);
  });

  test('expanded navigation preserves balanced vertical space at 1280x720', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);
    await gotoAppRoute(page, '/');
    await expectPageReady(page, /Dashboard/i);

    const sidebar = page.locator('.sidebar').first();
    const toggle = page.locator('button.menu-toggle').first();
    if (await sidebar.evaluate(element => element.classList.contains('closed'))) {
      await toggle.click();
    }

    await expect(page.locator('.sidebar:not(.closed) .ptdoc-nav-brand')).toBeVisible();
    const proportions = await page.locator('.ptdoc-nav-container').evaluate(container => {
      const element = container as HTMLElement;
      const brand = element.querySelector<HTMLElement>('.ptdoc-nav-brand');
      const scrollable = element.querySelector<HTMLElement>('.ptdoc-nav-scrollable');
      const footer = element.querySelector<HTMLElement>('.ptdoc-nav-footer');
      if (!brand || !scrollable || !footer) {
        throw new Error('Expanded navigation sections were not rendered.');
      }

      const containerHeight = element.getBoundingClientRect().height;
      const brandHeight = brand.getBoundingClientRect().height;
      const scrollableHeight = scrollable.getBoundingClientRect().height;
      const footerHeight = footer.getBoundingClientRect().height;
      return {
        brand: brandHeight / containerHeight,
        scrollable: scrollableHeight / containerHeight,
        footer: footerHeight / containerHeight,
        spacing: (containerHeight - brandHeight - scrollableHeight - footerHeight) / containerHeight,
        scrollableOverflowY: getComputedStyle(scrollable).overflowY,
        scrollableClientHeight: scrollable.clientHeight,
        scrollableScrollHeight: scrollable.scrollHeight
      };
    });

    expect(proportions.brand).toBeGreaterThanOrEqual(0.25);
    expect(proportions.brand).toBeLessThanOrEqual(0.36);
    expect(proportions.scrollable).toBeGreaterThanOrEqual(0.45);
    expect(proportions.scrollable).toBeLessThanOrEqual(0.58);
    expect(proportions.footer).toBeGreaterThanOrEqual(0.07);
    expect(proportions.footer).toBeLessThanOrEqual(0.15);
    expect(proportions.spacing).toBeGreaterThanOrEqual(0.04);
    expect(proportions.spacing).toBeLessThanOrEqual(0.16);
    expect(proportions.scrollableOverflowY).toMatch(/auto|scroll/);
    expect(proportions.scrollableScrollHeight).toBeGreaterThanOrEqual(proportions.scrollableClientHeight);
    await expectSidebarControlsNotClipped(page);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoRelevantConsoleErrors(page);
  });

  test('viewport diagnostics query override can disable a previously enabled overlay', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);

    await page.goto('/?ptdocViewportDiagnostics=1');
    await page.waitForLoadState('domcontentloaded');
    await waitForAppInteractive(page);
    await expectPageReady(page, /Dashboard/i);
    await expect(page.locator('[data-viewport-diagnostics-overlay]')).toBeVisible();

    await page.goto('/?ptdocViewportDiagnostics=0');
    await page.waitForLoadState('domcontentloaded');
    await waitForAppInteractive(page);
    await expect(page.locator('body')).toContainText(/Dashboard/i);
    await expect(page.locator('[data-viewport-diagnostics-overlay]')).toHaveCount(0);
    await expectNoRelevantConsoleErrors(page);
  });

  test('drawer sidebar opens and closes below 1200 CSS pixels', async ({ page }) => {
    await page.setViewportSize({ width: 1199, height: 720 });
    await authenticateIfNeeded(page);
    await gotoAppRoute(page, '/');
    await expectPageReady(page, /Dashboard/i);

    await expect(page.locator('.sidebar')).toHaveCount(0);
    const toggle = page.locator('button.menu-toggle').first();
    await expect(toggle).toBeVisible();

    await toggle.click();
    await expect(page.locator('.sidebar.open')).toBeVisible();
    await expect(page.locator('.sidebar-backdrop')).toBeVisible();
    await expectSidebarControlsNotClipped(page);

    await page.locator('.sidebar-backdrop').click();
    await expect(page.locator('.sidebar')).toHaveCount(0);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoRelevantConsoleErrors(page);
  });

  test('appointments scheduler allows internal width without document overflow at 1280x720', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);
    await gotoAppRoute(page, '/appointments');
    await expectPageReady(page, /Appointments/i);

    await expectNoFrameworkOverlay(page);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoRelevantConsoleErrors(page);
  });
});

async function gotoAppRoute(page: Page, path: string) {
  const separator = path.includes('?') ? '&' : '?';
  await page.goto(`${path}${separator}ptdocViewportDiagnostics=1`);
  await page.waitForLoadState('domcontentloaded');
  await waitForAppInteractive(page);
}

async function authenticateForSettings(page: Page) {
  const adminUsername = process.env.PTDOC_UI_QA_ADMIN_USERNAME?.trim();
  const adminPin = (process.env.PTDOC_UI_QA_ADMIN_PIN ?? process.env.PTDOC_UI_QA_PIN)?.trim();
  if (!adminUsername || !adminPin) {
    throw new Error(
      'Settings responsive checks require PTDOC_UI_QA_ADMIN_USERNAME and either ' +
      'PTDOC_UI_QA_ADMIN_PIN or PTDOC_UI_QA_PIN for an Admin/Owner-capable fixture.');
  }

  await authenticateAs(page, adminUsername, adminPin);
}

async function setTheme(page: Page, theme: 'light' | 'dark') {
  await page.evaluate(value => {
    localStorage.setItem('ptdoc-theme', value);
    if (window.ptdocTheme?.setTheme) {
      window.ptdocTheme.setTheme(value);
      return;
    }

    document.documentElement.classList.toggle('dark', value === 'dark');
  }, theme);
}

async function expectPageReady(page: Page, titlePattern: RegExp) {
  await expect(page.locator('body')).toContainText(titlePattern);
  await expectViewportDiagnosticsOverlay(page);
}

async function expectLayoutMode(page: Page, viewportWidth: number) {
  const expected = viewportWidth < DESKTOP_BREAKPOINT ? 'drawer' : /desktop-(full|icon-rail)/;
  const overlay = page.locator('[data-viewport-diagnostics-overlay]');
  await expect(overlay).toContainText(expected);
}

async function expectViewportDiagnosticsOverlay(page: Page) {
  const overlay = page.locator('[data-viewport-diagnostics-overlay]');
  try {
    await expect(overlay).toBeVisible();
  } catch (error) {
    throw new Error(`Viewport diagnostics overlay was not found. Restart PTDoc.Web after applying this branch and verify the route includes ?ptdocViewportDiagnostics=1. Original error: ${(error as Error).message}`);
  }
}

async function expectMenuToggleVisible(page: Page) {
  const toggle = page.locator('button.menu-toggle').first();
  await expect(toggle).toBeVisible();
  const box = await toggle.boundingBox();
  expect(box?.width ?? 0).toBeGreaterThanOrEqual(24);
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(24);
}

async function expectNoDocumentHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => {
    const root = document.documentElement;
    return Math.ceil(root.scrollWidth - root.clientWidth);
  });

  expect(overflow).toBeLessThanOrEqual(1);
}

async function expectSidebarControlsNotClipped(page: Page) {
  const clipped = await page.evaluate(() => {
    const sidebar = document.querySelector<HTMLElement>('.sidebar');
    if (!sidebar) {
      return [];
    }

    const sidebarBounds = sidebar.getBoundingClientRect();
    const controls = Array.from(sidebar.querySelectorAll<HTMLElement>('a, button, [role="button"]'));
    const isInsideScrollableRegion = (control: HTMLElement) => {
      let current: HTMLElement | null = control.parentElement;
      while (current && current !== sidebar) {
        const style = window.getComputedStyle(current);
        const scrollsVertically = /(auto|scroll)/.test(style.overflowY)
          && current.scrollHeight > current.clientHeight + 1;
        if (scrollsVertically) {
          return true;
        }

        current = current.parentElement;
      }

      return false;
    };

    return controls
      .filter(control => {
        const bounds = control.getBoundingClientRect();
        const isHorizontallyClipped = bounds.left < sidebarBounds.left - 1
          || bounds.right > sidebarBounds.right + 1;
        const isVerticallyClipped = bounds.top < sidebarBounds.top - 1
          || bounds.bottom > sidebarBounds.bottom + 1;

        return bounds.width > 0
          && bounds.height > 0
          && (isHorizontallyClipped || (isVerticallyClipped && !isInsideScrollableRegion(control)));
      })
      .map(control => control.textContent?.trim() || control.getAttribute('aria-label') || control.tagName);
  });

  expect(clipped).toEqual([]);
}

async function expectNoFrameworkOverlay(page: Page) {
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await expect(page.locator('text=/Unhandled exception|An unhandled error has occurred/i')).toBeHidden();
}

declare global {
  interface Window {
    ptdocTheme?: {
      setTheme: (theme: 'light' | 'dark') => void;
    };
  }
}
