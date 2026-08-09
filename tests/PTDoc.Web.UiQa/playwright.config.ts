import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PTDOC_WEB_BASE_URL ?? 'http://localhost:5145';
const storageState = process.env.PTDOC_UI_QA_STORAGE_STATE || undefined;
const requireFullFixtures = process.env.PTDOC_UI_QA_REQUIRE_FULL_FIXTURES === 'true';
const artifactDirectory = process.env.PTDOC_UI_QA_ARTIFACT_DIR;

if (requireFullFixtures) {
  const requiredVariables = [
    'PTDOC_WEB_BASE_URL',
    'PTDOC_UI_QA_API_BASE_URL',
    'PTDOC_UI_QA_CHROME_CHANNEL',
    'PTDOC_UI_QA_PIN',
    'PTDOC_UI_QA_USERNAME',
    'PTDOC_UI_QA_ADMIN_USERNAME',
    'PTDOC_UI_QA_PT_USERNAME',
    'PTDOC_UI_QA_PTA_USERNAME',
    'PTDOC_UI_QA_PATIENT_USERNAME',
    'PTDOC_UI_QA_PATIENT_CHART_PATH',
    'PTDOC_UI_QA_INTAKE_PATH',
    'PTDOC_UI_QA_NOTE_WORKSPACE_PATH',
    'PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH',
    'PTDOC_UI_QA_EVALUATION_DRAFT_PATH'
  ];
  const missingVariables = requiredVariables.filter(name => !process.env[name]?.trim());

  if (missingVariables.length > 0) {
    throw new Error(
      `PTDOC_UI_QA_REQUIRE_FULL_FIXTURES=true requires these missing variables: ${missingVariables.join(', ')}`);
  }
}

export default defineConfig({
  testDir: './tests',
  outputDir: artifactDirectory ? `${artifactDirectory}/test-results` : './test-results',
  timeout: 45_000,
  retries: 0,
  workers: 1,
  expect: {
    timeout: 10_000
  },
  fullyParallel: false,
  reporter: [
    ['list'],
    ['html', { outputFolder: artifactDirectory ? `${artifactDirectory}/playwright-report` : 'playwright-report', open: 'never' }]
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
