import { readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';

const modalSource = readFileSync(path.resolve(__dirname, '../../../src/PTDoc.UI/wwwroot/js/modal.js'), 'utf8');
const keyboardSource = readFileSync(path.resolve(__dirname, '../../../src/PTDoc.UI/wwwroot/js/settings-keyboard.js'), 'utf8');
test.use({ storageState: { cookies: [], origins: [] } });

test('Documentation tabs prevent navigation-key scrolling and preserve native Tab behavior', async ({ page }) => {
  await page.setContent('<section id="root"><button id="tab" class="documentation-settings__tab" role="tab">Hard Stops</button><button id="detach">Detach</button></section>');
  await page.addScriptTag({ type: 'module', content: `${keyboardSource}
    const root = document.getElementById('root');
    attachKeyboardGuards(root);
    document.getElementById('detach').addEventListener('click', () => detachKeyboardGuards(root));
    root.dataset.ready = 'true';` });
  await expect(page.locator('#root')).toHaveAttribute('data-ready', 'true');
  const defaultsAllowed = await page.locator('#tab').evaluate(element =>
    ['ArrowLeft', 'ArrowRight', 'Home', 'End', 'Tab', 'Enter'].map(key =>
      element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))));
  expect(defaultsAllowed).toEqual([false, false, false, false, true, true]);
  await page.locator('#tab').focus();
  await page.keyboard.press('Tab');
  await expect(page.locator('#detach')).toBeFocused();
  await page.locator('#detach').click();
  expect(await page.locator('#tab').evaluate(element =>
    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true })))).toBe(true);
});

for (const backdrop of ['scheduling-dialog-backdrop', 'kiosk-admin__dialog-backdrop']) {
  test(`${backdrop}: focus, Tab containment, background isolation and Escape restoration`, async ({ page }) => {
    // Exercise the real helper without a running application or authenticated fixtures.
    await page.setContent(`
      <nav><button id="navigation">Background navigation</button></nav>
      <aside inert aria-hidden="true" id="already-inert">Already inactive</aside>
      <main><section>
        <button id="opener">Open</button>
        <div class="${backdrop}" hidden>
          <section id="confirmation" role="alertdialog" aria-modal="true" tabindex="-1">
            <h2>Confirm action</h2><button id="cancel">Cancel</button><button id="confirm">Confirm</button>
          </section>
        </div>
      </section></main>`);
    await page.addScriptTag({ type: 'module', content: `${modalSource}
      document.getElementById('opener').addEventListener('click', () => {
        const overlay = document.querySelector('.${backdrop}');
        overlay.hidden = false;
        registerEscapeHandler('confirmation', {
          invokeMethodAsync: async method => {
            if (method !== 'CloseFromJs') throw new Error('Unexpected callback');
            unregisterEscapeHandler('confirmation');
            overlay.hidden = true;
          }
        });
      });
      window.modalTestReady = true;` });
    await page.waitForFunction(() => (window as Window & { modalTestReady?: boolean }).modalTestReady);
    await page.locator('#opener').click();
    await expect(page.locator('#cancel')).toBeFocused();
    await expect(page.locator('nav')).toHaveAttribute('inert', '');
    await expect(page.locator('#opener')).toHaveAttribute('inert', '');
    await page.keyboard.press('Shift+Tab');
    await expect(page.locator('#confirm')).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(page.locator('#cancel')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.locator('#opener')).toBeFocused();
    await expect(page.locator('nav')).not.toHaveAttribute('inert', '');
    await expect(page.locator('#opener')).not.toHaveAttribute('aria-hidden', 'true');
    await expect(page.locator('#already-inert')).toHaveAttribute('inert', '');
    await expect(page.locator('#already-inert')).toHaveAttribute('aria-hidden', 'true');
  });
}
