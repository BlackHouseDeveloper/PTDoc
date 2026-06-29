import { expect, Page } from '@playwright/test';

export async function authenticateIfNeeded(page: Page) {
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

export async function expectNoRelevantConsoleErrors(page: Page) {
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
