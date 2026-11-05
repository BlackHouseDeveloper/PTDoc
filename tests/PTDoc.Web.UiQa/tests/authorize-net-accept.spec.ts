import { readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';

const bridge = readFileSync(path.resolve(__dirname, '../../../src/PTDoc.UI/wwwroot/js/authorize-net-accept.js'), 'utf8');
const host = readFileSync(path.resolve(__dirname, '../../../src/PTDoc.UI/wwwroot/authorize-net-host.html'), 'utf8');
test.use({ storageState: { cookies: [], origins: [] } });

// Model the vendor's load-time, first-button binding and captured configuration.
// All traffic is intercepted; no card data or merchant calls are involved.
const provider = `
  const trigger = document.getElementsByClassName('AcceptUI')[0];
  if (!trigger) throw new Error('Missing payment button at script load');
  const merchant = trigger.getAttribute('data-apiLoginID');
  const handler = trigger.getAttribute('data-responseHandler');
  const form = document.createElement('section');
  form.id = 'provider-form'; form.hidden = true;
  const submit = document.createElement('button');
  submit.textContent = 'Submit ' + merchant;
  submit.onclick = () => window[handler]({ opaqueData: { dataValue: merchant } });
  form.appendChild(submit); document.body.appendChild(form);
  trigger.addEventListener('click', () => { form.hidden = false; });
`;

async function setup(page: import('@playwright/test').Page, failScript = false) {
  await page.route('https://payments.test/**', route => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname.endsWith('/authorize-net-accept.js'))
      return route.fulfill({ contentType: 'text/javascript', body: bridge });
    if (pathname.endsWith('/authorize-net-host.html'))
      return route.fulfill({ contentType: 'text/html', body: host });
    return route.fulfill({ contentType: 'text/html', body: '<main id="root"></main>' });
  });
  await page.route(/https:\/\/(js|jstest)\.authorize\.net\/.*/, route => failScript
    ? route.abort() : route.fulfill({ contentType: 'text/javascript', body: provider }));
  await page.goto('https://payments.test/');
  await page.addScriptTag({ type: 'module', content: `
    import { initialize, dispose } from '/js/authorize-net-accept.js';
    window.callbacks = [];
    window.mountPayment = async (id, merchant, modal = false) => {
      const button = document.createElement('button');
      button.className = 'AcceptUI'; button.textContent = 'Pay'; button.disabled = true;
      button.setAttribute('data-apiLoginID', merchant);
      button.setAttribute('data-clientKey', 'public-test-key');
      button.setAttribute('data-responseHandler', 'ptdocAuthorizeNetAcceptResponseHandler');
      const root = modal ? document.createElement('section') : button;
      root.id = id;
      if (modal) root.appendChild(button);
      document.getElementById('root').appendChild(root);
      try {
        await initialize(id, { invokeMethodAsync: async (method, response) => {
          window.callbacks.push({ id, method, response });
        } }, 'Sandbox');
        button.disabled = false;
      } catch { root.dataset.failed = 'true'; }
    };
    window.unmountPayment = id => { dispose(id); document.getElementById(id)?.remove(); };
    window.initializeMissing = () => initialize('missing', {}, 'Sandbox').then(() => false, () => true);
    window.ready = true;
  ` });
  await page.waitForFunction(() => (window as any).ready);
}

for (const modal of [false, true]) {
  test(`revisit binds a fresh ${modal ? 'copay modal' : 'subscription button'} and routes one callback`, async ({ page }) => {
    await setup(page);
    await page.evaluate(modal => (window as any).mountPayment('payment', 'first-merchant', modal), modal);
    await page.getByRole('button', { name: 'Pay', exact: true }).click();
    await page.frameLocator('iframe[title="Authorize.Net secure payment"]').getByRole('button', { name: 'Submit first-merchant' }).click();
    await expect(page.locator('iframe[title="Authorize.Net secure payment"]')).toBeHidden();
    await page.evaluate(() => (window as any).unmountPayment('payment'));
    await expect(page.locator('iframe')).toHaveCount(0);
    await page.evaluate(modal => (window as any).mountPayment('payment', 'second-merchant', modal), modal);
    await page.getByRole('button', { name: 'Pay', exact: true }).click();
    await page.frameLocator('iframe[title="Authorize.Net secure payment"]').getByRole('button', { name: 'Submit second-merchant' }).click();
    expect(await page.evaluate(() => (window as any).callbacks.map((item: any) => item.response.opaqueData.dataValue)))
      .toEqual(['first-merchant', 'second-merchant']);
    await expect(page.getByRole('button', { name: 'Pay', exact: true })).toBeFocused();
  });
}

test('initialization requires a rendered button and a successfully loaded provider', async ({ page }) => {
  await setup(page, true);
  expect(await page.evaluate(() => (window as any).initializeMissing())).toBe(true);
  await page.evaluate(() => (window as any).mountPayment('payment', 'test-merchant'));
  await expect(page.locator('#payment')).toHaveAttribute('data-failed', 'true');
  await expect(page.locator('#payment')).toBeDisabled();
  await expect(page.locator('iframe')).toHaveCount(0);
});
