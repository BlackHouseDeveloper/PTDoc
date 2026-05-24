import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PTDOC_WEB_BASE_URL ?? 'http://localhost:5145';
const storageState = process.env.PTDOC_UI_QA_STORAGE_STATE || undefined;

export default defineConfig({
  testDir: './tests',
  outputDir: './test-results',
  timeout: 45_000,
  expect: {
    timeout: 10_000
  },
  fullyParallel: false,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }]
  ],
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.PTDOC_UI_QA_CHROME_CHANNEL || undefined
      }
    }
  ]
});
