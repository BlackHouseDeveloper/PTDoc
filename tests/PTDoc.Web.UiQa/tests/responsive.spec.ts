import { expect, Page, test } from '@playwright/test';

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

async function authenticateIfNeeded(page: Page) {
  attachConsoleCapture(page);
  await page.goto('/');
  await page.waitForLoadState('domcontentloaded');

  const usernameInput = page.locator('#username, input[name="username"], input[autocomplete="username"]').first();
  const needsLogin = await usernameInput.isVisible().catch(() => false);
  if (!needsLogin) {
    return;
  }

  const username = process.env.PTDOC_UI_QA_USERNAME;
  const pin = process.env.PTDOC_UI_QA_PIN;
  if (!username || !pin) {
    throw new Error('Login form is visible, but PTDOC_UI_QA_USERNAME/PTDOC_UI_QA_PIN are not set and PTDOC_UI_QA_STORAGE_STATE did not provide an authenticated session. Set credentials or provide a valid storage-state file.');
  }

  const loginResponse = await page.request.post('/auth/login', {
    form: {
      username,
      pin,
      returnUrl: '/'
    },
    maxRedirects: 0,
    failOnStatusCode: false
  });

  if (loginResponse.status() !== 302 && loginResponse.status() !== 303) {
    throw new Error(`Login failed with HTTP ${loginResponse.status()}. Verify PTDOC_UI_QA_USERNAME/PTDOC_UI_QA_PIN and that API/Web are using the same seeded database.`);
  }

  await normalizeAuthCookiesForLocalHttp(page, loginResponse.headersArray());
  await page.goto('/');
  await page.waitForLoadState('domcontentloaded');

  const loginStillVisible = await usernameInput.isVisible().catch(() => false);
  if (loginStillVisible) {
    const authAlert = await page.locator('.auth-alert').textContent().catch(() => null);
    throw new Error(`Login did not establish a Web session. ${authAlert?.trim() || 'No auth error message was rendered.'}`);
  }
}

async function normalizeAuthCookiesForLocalHttp(page: Page, headers: { name: string; value: string }[]) {
  const baseUrl = new URL(page.url());
  if (baseUrl.protocol !== 'http:') {
    return;
  }

  const cookieHeaders = headers
    .filter(header => header.name.toLowerCase() === 'set-cookie')
    .map(header => header.value);

  for (const cookieHeader of cookieHeaders) {
    const [nameValue] = cookieHeader.split(';');
    const separatorIndex = nameValue.indexOf('=');
    if (separatorIndex <= 0) {
      continue;
    }

    const name = nameValue.slice(0, separatorIndex).trim();
    const value = nameValue.slice(separatorIndex + 1).trim();
    if (!name || !value) {
      continue;
    }

    await page.context().addCookies([
      {
        name,
        value,
        url: `${baseUrl.origin}/`,
        httpOnly: cookieHeader.toLowerCase().includes('httponly'),
        secure: false,
        sameSite: cookieHeader.toLowerCase().includes('samesite=strict') ? 'Strict' : 'Lax'
      }
    ]);
  }
}

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

async function expectNoRelevantConsoleErrors(page: Page) {
  const messages = getConsoleCapture(page);
  expect(messages).toEqual([]);
}

function attachConsoleCapture(page: Page) {
  if ((page as Page & { __ptdocConsoleErrors?: string[] }).__ptdocConsoleErrors) {
    return;
  }

  (page as Page & { __ptdocConsoleErrors: string[] }).__ptdocConsoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error' && !isAllowedConsoleMessage(message.text())) {
      getConsoleCapture(page).push(message.text());
    }
  });
  page.on('pageerror', error => {
    if (!isAllowedConsoleMessage(error.message)) {
      getConsoleCapture(page).push(error.message);
    }
  });
}

function getConsoleCapture(page: Page): string[] {
  return (page as Page & { __ptdocConsoleErrors: string[] }).__ptdocConsoleErrors;
}

function isAllowedConsoleMessage(message: string) {
  return /favicon|ResizeObserver loop/i.test(message);
}

declare global {
  interface Window {
    ptdocTheme?: {
      setTheme: (theme: 'light' | 'dark') => void;
    };
  }
}
