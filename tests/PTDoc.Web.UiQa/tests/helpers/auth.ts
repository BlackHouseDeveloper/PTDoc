import { expect, Page } from '@playwright/test';

export async function authenticateIfNeeded(page: Page) {
  attachConsoleCapture(page);
  await page.goto('/');
  await page.waitForLoadState('domcontentloaded');
  await waitForAppInteractive(page);

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

  await authenticateAs(page, username, pin);
}

export async function authenticateAs(page: Page, username: string, pin: string) {
  attachConsoleCapture(page);
  await page.context().clearCookies();
  await page.goto('/login');
  await page.waitForLoadState('domcontentloaded');

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
  await waitForAppInteractive(page);

  const usernameInput = page.locator('#username, input[name="username"], input[autocomplete="username"]').first();
  const loginStillVisible = await usernameInput.isVisible().catch(() => false);
  if (loginStillVisible) {
    const authAlert = await page.locator('.auth-alert').textContent().catch(() => null);
    throw new Error(`Login did not establish a Web session. ${authAlert?.trim() || 'No auth error message was rendered.'}`);
  }
}

export async function waitForAppInteractive(page: Page) {
  const appRoot = page.getByTestId('ptdoc-app-root');

  try {
    await expect(appRoot).toHaveAttribute('data-interactive', 'true', { timeout: 15_000 });
    await expect(appRoot).toHaveAttribute('aria-busy', 'false');
    await expect.poll(() => appRoot.getAttribute('inert')).toBeNull();
    await expect(page.getByTestId('ptdoc-app-connecting')).toHaveCount(0);

    const frameworkErrorVisible = await page.locator('#blazor-error-ui').isVisible().catch(() => false);
    const reconnectVisible = await page.locator('#components-reconnect-modal').isVisible().catch(() => false);
    if (frameworkErrorVisible || reconnectVisible) {
      throw new Error('A Blazor framework or reconnect error UI is visible.');
    }
  } catch (error) {
    const frameworkErrorVisible = await page.locator('#blazor-error-ui').isVisible().catch(() => false);
    const reconnectVisible = await page.locator('#components-reconnect-modal').isVisible().catch(() => false);
    const reconnectText = reconnectVisible
      ? await page.locator('#components-reconnect-modal').innerText().catch(() => 'Connection interrupted')
      : null;

    throw new Error(
      `PTDoc did not become interactive at ${page.url()}. ` +
      `Framework error visible: ${frameworkErrorVisible}. ` +
      `Reconnect dialog: ${reconnectText?.replace(/\s+/g, ' ').trim() ?? 'not visible'}. ` +
      `Original error: ${(error as Error).message}`);
  }
}

export async function expectNoRelevantConsoleErrors(page: Page) {
  attachConsoleCapture(page);
  const messages = getConsoleCapture(page);
  expect(messages).toEqual([]);
}

export function attachConsoleCapture(page: Page) {
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

function getConsoleCapture(page: Page): string[] {
  return (page as Page & { __ptdocConsoleErrors: string[] }).__ptdocConsoleErrors;
}

function isAllowedConsoleMessage(message: string) {
  return /favicon|ResizeObserver loop/i.test(message);
}
