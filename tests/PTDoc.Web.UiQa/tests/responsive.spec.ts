import { expect, Page, test } from '@playwright/test';
import { attachConsoleCapture, authenticateIfNeeded, expectNoRelevantConsoleErrors } from './helpers/auth';

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
  { name: 'notes', path: '/notes', titlePattern: /Notes/i }
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
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/signup');
    await page.waitForLoadState('domcontentloaded');

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
        await authenticateIfNeeded(page);
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

  test('viewport diagnostics query override can disable a previously enabled overlay', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);

    await page.goto('/?ptdocViewportDiagnostics=1');
    await page.waitForLoadState('domcontentloaded');
    await expectPageReady(page, /Dashboard/i);
    await expect(page.locator('[data-viewport-diagnostics-overlay]')).toBeVisible();

    await page.goto('/?ptdocViewportDiagnostics=0');
    await page.waitForLoadState('domcontentloaded');
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
