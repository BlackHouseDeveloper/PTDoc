import { expect, test } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { authenticateIfNeeded, expectNoRelevantConsoleErrors } from './helpers/auth';

const patientChartPath = process.env.PTDOC_UI_QA_PATIENT_CHART_PATH
  ?? '/patient/f9c2cb68-4ab4-4f57-a1db-73ed8e2da789';

test.describe('PTDoc patient document upload QA', () => {
  test('uploads a synthetic patient document and renders it after reload', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await authenticateIfNeeded(page);

    const marker = `Synthetic upload QA ${Date.now()}`;
    const fileName = `ptdoc-synthetic-upload-${Date.now()}.txt`;
    const filePath = testInfo.outputPath(fileName);
    await mkdir(path.dirname(filePath), { recursive: true });
    await writeFile(
      filePath,
      [
        'PTDoc synthetic browser QA upload.',
        `Marker: ${marker}`,
        'This file contains no patient information.'
      ].join('\n'),
      'utf8');

    await gotoPatientChart(page);
    await openDocumentsTab(page);

    await page.locator('#patient-document-type').selectOption({ label: 'Authorization/referral' });
    await page.locator('#patient-document-notes').fill(marker);
    await page.locator('#patient-document-notes').press('Tab');
    await page.locator('#patient-document-file').setInputFiles(filePath);

    await expect(page.getByText('Document uploaded.', { exact: true })).toBeVisible();
    await expectUploadedDocument(page, fileName, marker);
    await expectNoDocumentStorageErrors(page);

    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await openDocumentsTab(page);

    await expectUploadedDocument(page, fileName, marker);
    await expectNoDocumentStorageErrors(page);
    await expectNoRelevantConsoleErrors(page);
  });
});

async function gotoPatientChart(page: import('@playwright/test').Page) {
  await page.goto(patientChartPath);
  await page.waitForLoadState('domcontentloaded');
  await expect(page.getByRole('heading', { name: 'Patient Information' })).toBeVisible();
}

async function openDocumentsTab(page: import('@playwright/test').Page) {
  await page.getByRole('link', { name: 'Documents' }).click();
  await expect(page.getByRole('region', { name: 'Patient documents' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Patient Documents' })).toBeVisible();
}

async function expectNoDocumentStorageErrors(page: import('@playwright/test').Page) {
  await expect(page.getByText('Unable to load patient documents')).toHaveCount(0);
  await expect(page.getByText('Unable to upload document')).toHaveCount(0);
}

async function expectUploadedDocument(page: import('@playwright/test').Page, fileName: string, marker: string) {
  const uploadedDocuments = page.getByLabel('Uploaded patient documents');
  await expect(uploadedDocuments).toBeVisible();

  const uploadedDocument = uploadedDocuments.locator('article', { hasText: fileName });
  await expect(uploadedDocument).toHaveCount(1);
  await expect(uploadedDocument.getByRole('heading', { name: 'Authorization/referral' })).toBeVisible();
  await expect(uploadedDocument.getByText(fileName)).toBeVisible();
  await expect(uploadedDocument.getByText(marker)).toBeVisible();
}
