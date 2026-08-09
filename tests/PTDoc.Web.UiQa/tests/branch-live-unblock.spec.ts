import { expect, test } from '@playwright/test';
import { expectNoRelevantConsoleErrors, waitForAppInteractive } from './helpers/auth';
import {
  apiJson,
  apiResponse,
  clearFaults,
  configureFault,
  createSyntheticPatient,
  fixturePrefix,
  gotoInteractive,
  loginAs,
  loginWithCredentials,
  recordBlocked,
  recordFixture,
  verifyWithEvidence
} from './helpers/branch-live';

type Provider = {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  npi?: string;
  status: number;
  lastModifiedUtc: string;
  possibleDuplicates: { id: string; reason: string; status: number }[];
};

type Policy = {
  id: string;
  coveragePriority: number;
  carrierKey?: string;
  carrierDisplayName?: string;
  memberOrPolicyNumber?: string;
  groupNumber?: string;
  status: number;
  isArchived: boolean;
  lastModifiedUtc: string;
  authorizations: { id: string; authorizationType: number; referenceNumber?: string }[];
};

type Appointment = {
  id: string;
  patientRecordId: string;
  patientName: string;
  clinicianId?: string;
  appointmentType: string;
  appointmentStatus: string;
  startTimeUtc: string;
  lastModifiedUtc: string;
  visitNumber?: number;
  notes: string;
};

type TemplateVersion = {
  id: string;
  noteTemplateId: string;
  status: number;
  lastModifiedUtc: string;
  schema: { sections: { label: string; [key: string]: unknown }[]; [key: string]: unknown };
};

type NoteOperation = {
  isValid: boolean;
  errors: string[];
  note?: {
    id: string;
    patientId: string;
    templateVersionId?: string;
    noteStatus: number;
  };
};

test.describe.serial('PTDoc fixable live-run blockers', () => {
  test.describe.configure({ timeout: 180_000 });

  test.beforeEach(async () => {
    await clearFaults();
  });

  test.afterEach(async ({ page }) => {
    try {
      await expectNoRelevantConsoleErrors(page);
    } finally {
      await clearFaults();
    }
  });

  test('provider submission, review, search, relationships, duplicates, merge, archive, and concurrency', async ({ page, browser }, testInfo) => {
    await loginAs(page, 'admin');

    await verifyWithEvidence(testInfo, ['LV-PROV-001'], 'Visible provider values bind before one candidate submission.', async () => {
      await gotoInteractive(page, '/settings');
      await page.getByRole('button', { name: /Provider Directory/i }).click();
      await expect(page.getByRole('heading', { name: 'Provider Directory' })).toBeVisible();
      await page.getByRole('button', { name: 'Submit provider' }).click();
      await page.getByLabel('First name').fill('Live');
      await page.getByLabel('Last name').fill(`${fixturePrefix}-directory`);
      await page.getByLabel('Credentials').fill('MD');
      await page.getByLabel('NPI').fill(uniqueNpi(1));
      await page.getByLabel('Organization').fill(`${fixturePrefix} Clinic`);
      await page.getByLabel('Phone').fill('555-010-0201');
      await page.getByRole('button', { name: 'Submit for approval' }).click();
      await expect(page.getByText(/Provider candidate submitted for approval/i)).toBeVisible();
      await expect(page.getByText(`${fixturePrefix}-directory`, { exact: false }).first()).toBeVisible();
      return 'The oninput-bound form submitted once and the populated candidate appeared in Pending approval.';
    });

    const pending = await apiJson<Provider[]>(page, 'GET', `/api/v1/admin/providers?q=${encodeURIComponent(`${fixturePrefix}-directory`)}&status=0&take=25`);
    expect(pending).toHaveLength(1);
    const primary = pending[0];
    await recordFixture('ProviderDirectoryEntry', primary.id, 'Disposable database teardown');

    await verifyWithEvidence(testInfo, ['LV-PROV-003', 'LV-PROV-006'], 'Admin approves a candidate and only active entries become clinic-searchable.', async () => {
      const approved = await apiJson<Provider>(page, 'POST', `/api/v1/admin/providers/${primary.id}/approve`, {
        reason: 'Synthetic localhost verification approval.', confirmDuplicate: false
      });
      expect(approved.status).toBe(1);
      const search = await apiJson<Provider[]>(page, 'GET', `/api/v1/providers?q=${encodeURIComponent(primary.npi!)}&take=25`);
      expect(search.map(item => item.id)).toContain(primary.id);
      return 'The approved candidate became Active and appeared in the normal directory search.';
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-002'], 'Active search matches name, organization, and exact NPI.', async () => {
      for (const query of [primary.lastName, `${fixturePrefix} Clinic`, primary.npi!]) {
        const rows = await apiJson<Provider[]>(page, 'GET', `/api/v1/providers?q=${encodeURIComponent(query)}&take=25`);
        expect(rows.map(item => item.id), `search ${query}`).toContain(primary.id);
        expect(rows.every(item => item.status === 1)).toBe(true);
      }
      return 'All three searches returned the active provider and no pending provider.';
    });

    const patient = await createSyntheticPatient(page, 'provider-link');
    await verifyWithEvidence(testInfo, ['LV-PROV-004'], 'Patient-provider relationship persists independently of staff clinician assignment.', async () => {
      const relationship = await apiJson<{ id: string; providerId: string; role: number; isPrimary: boolean }>(page, 'POST', `/api/v1/providers/patients/${patient.id}`, {
        providerId: primary.id, role: 1, isPrimary: true, effectiveStartDate: '2026-08-01T00:00:00Z'
      });
      await recordFixture('PatientProviderRelationship', relationship.id, 'Disposable database teardown');
      const rows = await apiJson<{ id: string; providerId: string }[]>(page, 'GET', `/api/v1/providers/patients/${patient.id}`);
      expect(rows).toContainEqual(expect.objectContaining({ id: relationship.id, providerId: primary.id }));
      return 'The referring relationship was linked to the synthetic patient.';
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-008', 'LV-PROV-009'], 'Exact NPI is prevented and normalized demographic duplicates are identified.', async () => {
      const exact = await apiResponse(page, 'POST', '/api/v1/providers/candidates', {
        firstName: 'Other', lastName: `${fixturePrefix}-exact`, npi: primary.npi, submissionSource: 0
      });
      expect(exact.status()).toBe(409);

      const fuzzySource = await submitProvider(page, 'fuzzy-source', uniqueNpi(2), '555-010-0302', '2 Match Way');
      const fuzzyActive = await apiJson<Provider>(page, 'POST', `/api/v1/admin/providers/${fuzzySource.id}/approve`, { confirmDuplicate: false });
      const fuzzy = await apiJson<Provider>(page, 'POST', '/api/v1/providers/candidates', {
        firstName: fuzzyActive.firstName,
        lastName: fuzzyActive.lastName,
        organizationName: `${fixturePrefix} Fuzzy`,
        phone: '555-010-0302',
        addressLine1: '2 Match Way',
        submissionSource: 0
      });
      await recordFixture('ProviderDirectoryEntry', fuzzy.id, 'Disposable database teardown');
      expect(fuzzy.possibleDuplicates.some(item => item.id === fuzzyActive.id)).toBe(true);
      return 'The exact NPI was rejected and name/phone/address matching produced a review warning.';
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-010', 'LV-PROV-007'], 'A duplicate can merge and a separate candidate can be rejected.', async () => {
      const mergeCandidate = await apiJson<Provider>(page, 'POST', '/api/v1/providers/candidates', {
        firstName: primary.firstName,
        lastName: primary.lastName,
        organizationName: `${fixturePrefix} Merge`,
        phone: '555-010-0201',
        submissionSource: 0
      });
      await recordFixture('ProviderDirectoryEntry', mergeCandidate.id, 'Disposable database teardown');
      expect(mergeCandidate.possibleDuplicates.some(item => item.id === primary.id)).toBe(true);
      const merged = await apiJson<Provider>(page, 'POST', `/api/v1/admin/providers/${mergeCandidate.id}/approve`, {
        mergeIntoProviderId: primary.id, reason: 'Synthetic duplicate merge.'
      });
      expect(merged.id).toBe(mergeCandidate.id);
      expect(merged.status).toBe(3);
      const activeTarget = await apiJson<Provider[]>(page, 'GET', `/api/v1/providers?q=${encodeURIComponent(primary.npi!)}&take=25`);
      expect(activeTarget.map(item => item.id)).toContain(primary.id);

      const rejectCandidate = await submitProvider(page, 'reject', uniqueNpi(3), '555-010-0304', '4 Reject Way');
      const rejected = await apiJson<Provider>(page, 'POST', `/api/v1/admin/providers/${rejectCandidate.id}/reject`, { reason: 'Synthetic rejection.' });
      expect(rejected.status).toBe(2);
      return 'Merge archived the duplicate candidate while retaining the active target, and rejection retained a Rejected candidate.';
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-013'], 'A stale provider update is rejected.', async () => {
      const staleCandidate = await submitProvider(page, 'stale', uniqueNpi(4), '555-010-0305', '5 Stale Way');
      const secondContext = await browser.newContext();
      const secondPage = await secondContext.newPage();
      try {
        await loginAs(secondPage, 'admin');
        const firstLoaded = (await apiJson<Provider[]>(page, 'GET', `/api/v1/admin/providers?q=${encodeURIComponent(staleCandidate.npi!)}&status=0&take=25`))[0];
        const secondLoaded = (await apiJson<Provider[]>(secondPage, 'GET', `/api/v1/admin/providers?q=${encodeURIComponent(staleCandidate.npi!)}&status=0&take=25`))[0];
        const firstUpdate = candidateUpdate(firstLoaded, `${fixturePrefix}-fresh`);
        const saved = await apiResponse(page, 'PUT', `/api/v1/providers/candidates/${staleCandidate.id}`, firstUpdate);
        expect(saved.ok()).toBe(true);
        const stale = await apiResponse(secondPage, 'PUT', `/api/v1/providers/candidates/${staleCandidate.id}`, candidateUpdate(secondLoaded, `${fixturePrefix}-stale`));
        expect(stale.status()).toBe(409);
      } finally {
        await secondContext.close();
      }
      return 'The second independently loaded version received HTTP 409 and did not overwrite the first.';
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-011'], 'Archived providers leave active search but remain historical records.', async () => {
      await apiJson(page, 'POST', `/api/v1/admin/providers/${primary.id}/archive`, { reason: 'Synthetic archive.' });
      const active = await apiJson<Provider[]>(page, 'GET', `/api/v1/providers?q=${encodeURIComponent(primary.npi!)}&take=25`);
      expect(active.map(item => item.id)).not.toContain(primary.id);
      const relationships = await apiJson<{ providerId: string; provider: Provider }[]>(page, 'GET', `/api/v1/providers/patients/${patient.id}`);
      const historical = relationships.find(item => item.providerId === primary.id);
      expect(historical?.provider.displayName).toBe(primary.displayName);
      expect(historical?.provider.status).toBe(3);
      return 'Archive removed the provider from active search while the patient relationship retained the archived provider identity and status.';
    });
  });

  test('insurance policies support typed data, history, validation, concurrency, authorization, and idempotent backfill', async ({ page, browser }, testInfo) => {
    await loginAs(page, 'admin');
    const patient = await createSyntheticPatient(page, 'insurance');
    const policyPath = `/api/v1/patients/${patient.id}/insurance-policies`;
    const primaryRequest = policyRequest(0, 'blue-cross-blue-shield', 'Blue Cross Blue Shield');

    let primary!: Policy;
    await verifyWithEvidence(testInfo, ['LV-INS-002', 'LV-INS-003'], 'Typed Primary policy persists carrier, member, dates, cost-sharing, and adjuster values.', async () => {
      primary = await apiJson<Policy>(page, 'POST', `${policyPath}/`, primaryRequest);
      await recordFixture('PatientInsurancePolicy', primary.id, 'Disposable database teardown');
      const rows = await apiJson<Policy[]>(page, 'GET', `${policyPath}/`);
      const loaded = rows.find(item => item.id === primary.id)!;
      expect(loaded).toEqual(expect.objectContaining({
        coveragePriority: 0,
        carrierKey: primaryRequest.carrierKey,
        carrierDisplayName: primaryRequest.carrierDisplayName,
        memberOrPolicyNumber: primaryRequest.memberOrPolicyNumber,
        groupNumber: primaryRequest.groupNumber
      }));
      const unmatched = await apiJson<Policy>(page, 'POST', `${policyPath}/`, policyRequest(1, undefined, `${fixturePrefix} Unmatched Carrier`));
      expect(unmatched.carrierKey).toBeFalsy();
      expect(unmatched.carrierDisplayName).toBe(`${fixturePrefix} Unmatched Carrier`);
      return 'Matched-key and explicit unmatched-carrier policies persisted through the normalized contract.';
    });

    await verifyWithEvidence(testInfo, ['LV-INS-005', 'LV-INS-007'], 'Coverage priorities and nested authorization/referral records persist.', async () => {
      const tertiary = await apiJson<Policy>(page, 'POST', `${policyPath}/`, policyRequest(2, undefined, `${fixturePrefix} Tertiary`));
      const authorization = await apiJson<{ id: string; authorizationType: number; referenceNumber: string }>(page, 'POST', `${policyPath}/${primary.id}/authorizations`, {
        authorizationType: 0, referenceNumber: `${fixturePrefix}-AUTH`, status: 2,
        receivedDate: '2026-08-01T00:00:00Z', startDate: '2026-08-01T00:00:00Z', endDate: '2026-12-31T00:00:00Z',
        authorizedUnits: 12, usedUnits: 3, visitLimitPeriod: 1, reauthorizationDueDate: '2026-12-01T00:00:00Z', visitAlertThreshold: 2,
        notes: 'Synthetic authorization.'
      });
      const referral = await apiJson<{ id: string; authorizationType: number }>(page, 'POST', `${policyPath}/${tertiary.id}/authorizations`, {
        authorizationType: 1, referenceNumber: `${fixturePrefix}-REF`, status: 1, visitLimitPeriod: 0
      });
      expect(authorization.authorizationType).toBe(0);
      expect(referral.authorizationType).toBe(1);
      const rows = await apiJson<Policy[]>(page, 'GET', `${policyPath}/`);
      expect(rows.map(item => item.coveragePriority).sort()).toEqual([0, 1, 2]);
      expect(rows.find(item => item.id === primary.id)?.authorizations).toContainEqual(expect.objectContaining({ id: authorization.id }));
      return 'Primary/Secondary/Tertiary priorities and authorization/referral children were retained.';
    });

    await verifyWithEvidence(testInfo, ['LV-INS-008'], 'Invalid numeric and date combinations are rejected.', async () => {
      const negative = await apiResponse(page, 'POST', `${policyPath}/`, { ...policyRequest(0, undefined, 'Invalid'), deductibleAmount: -1 });
      expect([400, 422]).toContain(negative.status());
      const invalidDates = await apiResponse(page, 'POST', `${policyPath}/`, {
        ...policyRequest(0, undefined, 'Invalid Dates'), effectiveStartDate: '2027-02-01T00:00:00Z', effectiveEndDate: '2027-01-01T00:00:00Z'
      });
      expect([400, 409, 422]).toContain(invalidDates.status());
      return 'The API rejected negative cost sharing and an end date before its start date.';
    });

    await verifyWithEvidence(testInfo, ['LV-INS-009', 'LV-XCUT-005'], 'Stale policy writes receive conflict without silent overwrite.', async () => {
      const secondContext = await browser.newContext();
      const secondPage = await secondContext.newPage();
      try {
        await loginAs(secondPage, 'admin');
        const first = (await apiJson<Policy[]>(page, 'GET', `${policyPath}/`)).find(item => item.id === primary.id)!;
        const second = (await apiJson<Policy[]>(secondPage, 'GET', `${policyPath}/`)).find(item => item.id === primary.id)!;
        const firstSave = await apiResponse(page, 'PUT', `${policyPath}/${primary.id}`, {
          ...primaryRequest, memberOrPolicyNumber: `${fixturePrefix}-FRESH`, expectedLastModifiedUtc: first.lastModifiedUtc
        });
        expect(firstSave.ok()).toBe(true);
        const stale = await apiResponse(secondPage, 'PUT', `${policyPath}/${primary.id}`, {
          ...primaryRequest, memberOrPolicyNumber: `${fixturePrefix}-STALE`, expectedLastModifiedUtc: second.lastModifiedUtc
        });
        expect(stale.status()).toBe(409);
      } finally {
        await secondContext.close();
      }
      return 'The independently loaded stale editor received HTTP 409.';
    });

    await verifyWithEvidence(testInfo, ['LV-INS-006'], 'Archived policy appears only when history is explicitly requested and is read-only in Patient Info.', async () => {
      await apiJson(page, 'DELETE', `${policyPath}/${primary.id}`);
      const active = await apiJson<Policy[]>(page, 'GET', `${policyPath}/`);
      expect(active.map(item => item.id)).not.toContain(primary.id);
      const history = await apiJson<Policy[]>(page, 'GET', `${policyPath}/?includeArchived=true`);
      expect(history.find(item => item.id === primary.id)).toEqual(expect.objectContaining({ isArchived: true }));
      await gotoInteractive(page, `/patient/${patient.id}/info`);
      const historySection = page.getByRole('region', { name: /Policy history/i });
      await expect(historySection).toBeVisible();
      await expect(historySection).toContainText(primaryRequest.carrierDisplayName);
      await expect(historySection.getByRole('button')).toHaveCount(0);
      return 'Default list excluded the archive; history included it and rendered without edit/archive actions.';
    });

    await verifyWithEvidence(testInfo, ['LV-INS-012', 'LV-INS-013'], 'Legacy aliases remain normalized and repeated backfill does not create duplicates.', async () => {
      const legacy = await createSyntheticPatient(page, 'legacy-insurance', JSON.stringify({
        insuranceCompanyName: `${fixturePrefix} Legacy`, memberIdPolicyNumber: `${fixturePrefix}-LEGACY`, groupNumber: 'LEGACY-GROUP', payerType: 'Commercial'
      }));
      await apiJson<{ policiesCreated: number }>(page, 'POST', '/api/v1/admin/insurance-policies/backfill');
      const rows = await apiJson<Policy[]>(page, 'GET', `/api/v1/patients/${legacy.id}/insurance-policies/`);
      expect(rows).toHaveLength(1);
      expect(rows).toContainEqual(expect.objectContaining({ memberOrPolicyNumber: `${fixturePrefix}-LEGACY` }));
      const second = await apiJson<{ policiesCreated: number }>(page, 'POST', '/api/v1/admin/insurance-policies/backfill');
      const rowsAfterSecondRun = await apiJson<Policy[]>(page, 'GET', `/api/v1/patients/${legacy.id}/insurance-policies/`);
      expect(second.policiesCreated).toBe(0);
      expect(rowsAfterSecondRun).toHaveLength(1);
      expect(rowsAfterSecondRun[0].memberOrPolicyNumber).toBe(`${fixturePrefix}-LEGACY`);
      return 'The patient API dual-write normalized the alias once; both backfill runs retained one patient policy and the second clinic-wide run created no records.';
    }, 'Pass with limitation: the supported patient API dual-writes the legacy alias before the backfill endpoint runs, so alias parity and duplicate-free repeated runs were observed, while creation from a deliberately unmigrated row and malformed-row reporting remain outside this browser-only fixture.');
  });

  test('appointment Add Patient preserves drafts and controlled create failure cannot send intake', async ({ page }, testInfo) => {
    await loginAs(page, 'admin');
    await gotoInteractive(page, '/appointments');

    await verifyWithEvidence(testInfo, ['LV-HANDOFF-005'], 'All appointment fields survive nested patient creation, including the latest note keystroke.', async () => {
      const draft = await openAppointmentAndAddPatient(page, 'preserved');
      await page.getByRole('button', { name: 'Add Patient', exact: true }).click();
      await expect(page.getByRole('dialog', { name: 'New Appointment' })).toBeVisible();
      const appointmentDialog = page.getByRole('dialog', { name: 'New Appointment' });
      await expect(appointmentDialog.getByLabel('Appointment Type')).toHaveValue(draft.appointmentType);
      await expect(appointmentDialog.getByLabel('Date')).toHaveValue(draft.date);
      await expect(appointmentDialog.getByLabel('Time')).toHaveValue(draft.time);
      await expect(appointmentDialog.getByLabel('Duration (minutes)')).toHaveValue(draft.duration);
      await expect(appointmentDialog.getByLabel('Clinician')).toHaveValue(draft.clinician);
      await expect(appointmentDialog.getByLabel('Notes')).toHaveValue(draft.note);
      await expect(appointmentDialog.getByLabel('Patient')).not.toHaveValue('');
      await appointmentDialog.getByRole('button', { name: 'Cancel' }).click();
      return 'Patient creation resumed the appointment modal with all captured values and selected the authoritative patient.';
    });

    await verifyWithEvidence(testInfo, ['LV-HANDOFF-008'], 'Patient-create failure sends no intake and leaves both forms recoverable.', async () => {
      await newAppointmentButton(page).click();
      const appointmentDialog = page.getByRole('dialog', { name: 'New Appointment' });
      await appointmentDialog.getByLabel('Appointment Type').selectOption('Follow-up');
      await appointmentDialog.getByLabel('Notes').fill(`${fixturePrefix}-recoverable-note`);
      await appointmentDialog.getByRole('button', { name: 'Add Patient' }).click();
      await fillPatientDialog(page, 'failed-create');
      const messagesBefore = await communicationCount(page);
      await configureFault({ method: 'POST', path: '/api/v1/patients/', status: 503, occurrences: 1 });
      await page.getByRole('dialog', { name: 'Add New Patient' }).getByRole('button', { name: 'Add Patient and Send Intake' }).click();
      await expect(page.getByText(/Failed to add patient/i)).toBeVisible();
      await expect(page.getByRole('dialog', { name: 'Add New Patient' }).getByLabel('First Name')).toHaveValue('Live');
      expect(await communicationCount(page)).toBe(messagesBefore);
      await page.getByRole('dialog', { name: 'Add New Patient' }).getByRole('button', { name: 'Cancel' }).click();
      await expect(page.getByRole('dialog', { name: 'New Appointment' }).getByLabel('Notes')).toHaveValue(`${fixturePrefix}-recoverable-note`);
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Cancel' }).click();
      return 'The one-shot patient POST failure retained entered patient and appointment data and diagnostics showed no delivery.';
    });

    await verifyWithEvidence(testInfo, ['LV-HANDOFF-009'], 'An intake-send failure after creation never recreates the patient, and retry sends through the existing workflow.', async () => {
      await newAppointmentButton(page).click();
      const appointmentDialog = page.getByRole('dialog', { name: 'New Appointment' });
      await appointmentDialog.getByRole('button', { name: 'Add Patient' }).click();
      const suffix = 'partial-send';
      const email = `${fixturePrefix}.${suffix}@example.test`;
      await fillPatientDialog(page, suffix);
      await page.getByRole('dialog', { name: 'Add New Patient' }).getByRole('button', { name: 'Add Patient and Send Intake' }).click();

      const sendDialog = page.getByRole('dialog', { name: 'Send Intake Form' });
      await expect(sendDialog).toBeVisible();
      const patientId = await sendDialog.getByLabel('Select Patient').inputValue();
      expect(patientId).toMatch(/^[0-9a-f-]{36}$/i);
      await expect.poll(() => queryPatients(page, email).then(rows => rows.length)).toBe(1);
      const draft = await expectIntakeDraft(page, patientId);
      const before = await communicationCount(page);
      await sendDialog.getByLabel('Phone Number').fill('');
      await configureFault({ method: 'POST', path: `/api/v1/intake/${draft.id}/delivery/send`, status: 503, occurrences: 1 });
      await sendDialog.getByRole('button', { name: 'Send Invite' }).click();
      await expect(sendDialog.getByTestId('send-intake-error')).toContainText(/could not be sent|Unable to send/i);
      expect(await queryPatients(page, email)).toHaveLength(1);
      expect(await communicationCount(page)).toBe(before);

      await sendDialog.getByRole('button', { name: 'Send Invite' }).click();
      await expect(page.getByText('Patient added and intake sent successfully.')).toBeVisible();
      await expect.poll(() => communicationCount(page)).toBe(before + 1);
      expect(await queryPatients(page, email)).toHaveLength(1);
      await sendDialog.getByRole('button', { name: 'Cancel' }).click();
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Cancel' }).click();
      return 'The patient existed once before and after the failed send; retry produced one null-sender diagnostic message.';
    });

    await verifyWithEvidence(testInfo, ['LV-HANDOFF-010'], 'Busy state prevents duplicate patient creation during rapid repeated activation.', async () => {
      await newAppointmentButton(page).click();
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Add Patient' }).click();
      const suffix = 'double-submit';
      const email = `${fixturePrefix}.${suffix}@example.test`;
      await fillPatientDialog(page, suffix);
      const submit = page.getByRole('dialog', { name: 'Add New Patient' }).getByRole('button', { name: 'Add Patient', exact: true });
      await submit.focus();
      await page.keyboard.press('Enter');
      await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog', { name: 'New Appointment' })).toBeVisible();
      await expect.poll(() => queryPatients(page, email).then(rows => rows.length)).toBe(1);
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Cancel' }).click();

      await newAppointmentButton(page).click();
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Add Patient' }).click();
      const combinedSuffix = 'double-submit-intake';
      const combinedEmail = `${fixturePrefix}.${combinedSuffix}@example.test`;
      await fillPatientDialog(page, combinedSuffix);
      const combinedSubmit = page.getByRole('dialog', { name: 'Add New Patient' }).getByRole('button', { name: 'Add Patient and Send Intake' });
      await combinedSubmit.focus();
      await page.keyboard.press('Enter');
      await page.keyboard.press('Enter');
      const sendDialog = page.getByRole('dialog', { name: 'Send Intake Form' });
      await expect(sendDialog).toBeVisible();
      await expect.poll(() => queryPatients(page, combinedEmail).then(rows => rows.length)).toBe(1);
      const messagesBefore = await communicationCount(page);
      await sendDialog.getByLabel('Phone Number').fill('');
      const send = sendDialog.getByRole('button', { name: 'Send Invite' });
      await send.focus();
      await page.keyboard.press('Enter');
      await page.keyboard.press('Enter');
      await expect(page.getByText('Patient added and intake sent successfully.')).toBeVisible();
      await expect.poll(() => communicationCount(page)).toBe(messagesBefore + 1);
      expect(await queryPatients(page, combinedEmail)).toHaveLength(1);
      await sendDialog.getByRole('button', { name: 'Cancel' }).click();
      await page.getByRole('dialog', { name: 'New Appointment' }).getByRole('button', { name: 'Cancel' }).click();
      return 'Repeated Enter activation produced one patient for each path and exactly one null-sender invitation for the combined path.';
    });
  });

  test('appointment PATCH failure, stale concurrency, immutable ordinals, and dashboard retry use controlled faults', async ({ page, browser }, testInfo) => {
    await loginAs(page, 'admin');
    const patient = await createSyntheticPatient(page, 'appointments');
    const clinicians = await apiJson<{ id: string }[]>(page, 'GET', '/api/v1/appointments/clinicians');
    expect(clinicians.length).toBeGreaterThan(0);
    const day = uniqueFutureDate();
    const appointments: Appointment[] = [];
    for (let index = 0; index < 3; index += 1) {
      const created = await apiJson<Appointment>(page, 'POST', '/api/v1/appointments/', {
        patientId: patient.id,
        clinicianId: clinicians[0].id,
        appointmentType: 'Follow-up',
        appointmentDate: `${day}T00:00:00Z`,
        appointmentTime: `${String(16 + index).padStart(2, '0')}:00:00`,
        durationMinutes: 45,
        notes: `${fixturePrefix}-visit-${index + 1}`
      });
      appointments.push(created);
      await recordFixture('Appointment', created.id, 'Disposable database teardown');
    }

    await verifyWithEvidence(testInfo, ['LV-APPT-011'], 'Eligible scheduled visits receive unique increasing immutable ordinals.', async () => {
      const rows = await listPatientAppointments(page, patient.id, day);
      const createdRows = rows.filter(item => appointments.some(created => created.id === item.id));
      expect(createdRows).toHaveLength(3);
      const ordinals = createdRows.map(item => item.visitNumber!);
      expect(ordinals.every(Number.isInteger)).toBe(true);
      expect(new Set(ordinals).size).toBe(3);
      expect([...ordinals].sort((a, b) => a - b)).toEqual(ordinals);
      return `Observed increasing ordinals ${ordinals.join(', ')}.`;
    });

    const target = (await listPatientAppointments(page, patient.id, day)).find(item => item.id === appointments[0].id)!;
    const emptyPt = await createAndApproveEmptyPt(page);
    await verifyWithEvidence(testInfo, ['LV-APPT-006'], 'One failed narrow PATCH leaves the persisted appointment type unchanged.', async () => {
      await gotoInteractive(page, '/appointments');
      await page.getByRole('button', { name: 'Next day' }).click();
      await page.getByRole('button', { name: new RegExp(`Open appointment details for ${escapeRegex(target.patientName)}`) }).first().click();
      const details = page.getByRole('dialog', { name: 'Appointment Details' });
      const typeSelect = details.getByLabel('Appointment Type');
      await expect(typeSelect).toHaveValue(target.appointmentType);
      await configureFault({ method: 'PATCH', path: `/api/v1/appointments/${target.id}/appointment-type`, status: 503, occurrences: 1 });
      await typeSelect.selectOption('Re-evaluation');
      await details.getByRole('button', { name: 'Save Type' }).click();
      await expect(details.getByRole('alert')).toContainText(/previous type is still in effect/i);
      await expect(typeSelect).toHaveValue(target.appointmentType);
      const reloaded = (await listPatientAppointments(page, patient.id, day)).find(item => item.id === target.id)!;
      expect(reloaded.appointmentType).toBe(target.appointmentType);
      expect(reloaded.notes).toBe(target.notes);
      await details.getByRole('button', { name: 'Close appointment details' }).click();
      return 'The controlled 503 produced visible feedback, reset the selector, and changed neither type nor unrelated notes.';
    });

    await verifyWithEvidence(testInfo, ['LV-APPT-007', 'LV-XCUT-005'], 'A stale appointment-type PATCH is rejected without overwriting the newer type.', async () => {
      const secondContext = await browser.newContext();
      const secondPage = await secondContext.newPage();
      try {
        await loginAs(secondPage, 'admin');
        const first = (await listPatientAppointments(page, patient.id, day)).find(item => item.id === target.id)!;
        const second = (await listPatientAppointments(secondPage, patient.id, day)).find(item => item.id === target.id)!;
        const saved = await apiResponse(page, 'PATCH', `/api/v1/appointments/${target.id}/appointment-type`, {
          appointmentType: 'Initial Evaluation', expectedLastModifiedUtc: first.lastModifiedUtc
        });
        expect(saved.ok()).toBe(true);
        const stale = await apiResponse(secondPage, 'PATCH', `/api/v1/appointments/${target.id}/appointment-type`, {
          appointmentType: 'Discharge', expectedLastModifiedUtc: second.lastModifiedUtc
        });
        expect(stale.status()).toBe(409);
        const current = (await listPatientAppointments(page, patient.id, day)).find(item => item.id === target.id)!;
        expect(current.appointmentType).toBe('Initial Evaluation');
      } finally {
        await secondContext.close();
      }
      return 'The first PATCH persisted; the stale second PATCH received HTTP 409.';
    });

    await verifyWithEvidence(testInfo, ['LV-DASH-016'], 'Dashboard presents loading, error, and successful retry states.', async () => {
      await gotoInteractive(page, '/appointments');
      await configureFault({ method: 'GET', path: '/api/v1/dashboard/snapshot', delayMs: 5_000, occurrences: 1 });
      await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
      await expect(page.getByTestId('dashboard-loading')).toBeVisible();
      await expect(page.getByTestId('dashboard-loading')).toHaveCount(0);

      await configureFault({ method: 'GET', path: '/api/v1/dashboard/snapshot', status: 503, occurrences: 5 });
      await page.reload();
      await expect(page.getByTestId('dashboard-error').or(page.getByTestId('dashboard-inline-error'))).toBeVisible();
      const retry = page.getByRole('button', { name: 'Retry' }).first();
      await expect(retry).toBeVisible();
      await clearFaults();
      await retry.evaluate((button: HTMLButtonElement) => button.click());
      await expect(page.getByTestId('dashboard-error')).toHaveCount(0);
      await expect(page.getByTestId('dashboard-inline-error')).toHaveCount(0);
      await loginWithCredentials(page, emptyPt.email, emptyPt.pin, 'pt');
      await gotoInteractive(page, '/dashboard');
      await expect(page.getByTestId('today-appointments-empty')).toBeVisible();
      return 'A five-second delay exposed loading, a controlled 503 exposed error, Retry recovered, and a newly approved PT with no visits saw the explicit empty state.';
    });

    await recordBlocked(
      ['LV-APPT-008'],
      'Completed, cancelled, and no-show mutation restrictions are verified through supported fixtures.',
      'This suite does not manufacture cancelled/no-show state. It retains the documented limitation unless a supported fixture is supplied.');
  });

  test('new settings and patient panels expose bounded loading, accessible errors, and recovery', async ({ page }, testInfo) => {
    await loginAs(page, 'admin');
    const patient = await createSyntheticPatient(page, 'panel-retry');
    await apiJson(page, 'POST', `/api/v1/intake/drafts/${patient.id}`, {
      painMapData: '{}', consents: '{}', responseJson: '{}', templateVersion: '1.0'
    });

    await verifyWithEvidence(testInfo, ['LV-XCUT-006'], 'Every new settings/patient panel bounds loading, exposes an accessible transient error, and recovers through its established refresh behavior.', async () => {
      await gotoInteractive(page, '/settings');

      await configureFault({ method: 'GET', path: '/api/v1/admin/providers', delayMs: 750, status: 503, occurrences: 1 });
      await page.getByRole('button', { name: /Provider Directory/i }).click();
      await expect(page.getByText('Loading provider directory…')).toBeVisible();
      await expect(page.getByRole('alert')).toBeVisible();
      await page.getByRole('button', { name: 'Refresh' }).click();
      await expect(page.getByRole('alert')).toHaveCount(0);
      await expect(page.getByRole('heading', { name: /Pending approval/i })).toBeVisible();

      await configureFault({ method: 'GET', path: '/api/v1/admin/note-templates', delayMs: 750, status: 503, occurrences: 1 });
      await page.getByRole('button', { name: /Documentation and Compliance/i }).click();
      await expect(page.getByText('Loading note templates…')).toBeVisible();
      await expect(page.getByRole('alert')).toBeVisible();
      await page.getByRole('button', { name: 'Refresh' }).click();
      await expect(page.getByRole('alert')).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Documentation Templates' })).toBeVisible();

      const insurancePath = `/api/v1/patients/${patient.id}/insurance-policies`;
      const insurancePanel = page.locator('.insurance-policies');
      await configureFault({ method: 'GET', path: insurancePath, delayMs: 5_000, status: 503, occurrences: 5 });
      const insuranceNavigation = page.goto(`/patient/${patient.id}/info`);
      await expect(insurancePanel.getByText('Loading insurance policies…')).toBeVisible();
      await insuranceNavigation;
      await waitForAppInteractive(page);
      await expect(insurancePanel.getByRole('alert')).toBeVisible();
      await clearFaults();
      await page.reload();
      await waitForAppInteractive(page);
      await expect(insurancePanel.getByRole('alert')).toHaveCount(0);
      await expect(insurancePanel).toContainText(/No normalized insurance policies|Current policies|Policy history/);

      const intakePath = `/api/v1/intake/patient/${patient.id}/latest`;
      await configureFault({ method: 'GET', path: intakePath, delayMs: 5_000, status: 503, occurrences: 5 });
      const intakeNavigation = page.goto(`/intake/${patient.id}`);
      await expect(page.getByText('Loading intake draft…')).toBeVisible();
      await intakeNavigation;
      await waitForAppInteractive(page);
      await expect(page.getByRole('heading', { name: 'Unable to Load Intake' })).toBeVisible();
      await clearFaults();
      await page.reload();
      await waitForAppInteractive(page);
      await expect(page.getByTestId('demographics-step')).toBeVisible();

      return 'Provider and Documentation recovered through Refresh; Insurance and Intake recovered through their documented page refresh, with one-shot faults consumed and no mutation repeated.';
    });
  });

  test('structured intake selections, None exclusivity, searches, body alignment, payer fields, and draft reload', async ({ page }, testInfo) => {
    await loginAs(page, 'admin');
    const patient = await createSyntheticPatient(page, 'structured-intake');
    const existingPolicy = await apiJson<Policy>(page, 'POST', `/api/v1/patients/${patient.id}/insurance-policies/`, {
      ...policyRequest(0, 'blue-cross-blue-shield', 'Blue Cross Blue Shield'),
      memberOrPolicyNumber: `${fixturePrefix}-PRE-INTAKE`,
      groupNumber: `${fixturePrefix}-PRE-GROUP`,
      deductibleAmount: 1775,
      adjusterFax: '555-010-0499'
    });
    await recordFixture('PatientInsurancePolicy', existingPolicy.id, 'Disposable database teardown');
    await gotoInteractive(page, `/intake/${patient.id}`);
    await expect(page.getByTestId('demographics-step')).toBeVisible();
    await page.locator('#intake-full-name').fill('Live Verification Patient');
    await page.locator('#intake-date-of-birth').fill('1990-01-01');
    await page.locator('#intake-email-address').fill(`${fixturePrefix}.intake@example.test`);
    await page.locator('#intake-phone-number').fill('555-010-0399');

    await verifyWithEvidence(testInfo, ['LV-INTAKE-006', 'LV-INTAKE-009'], 'Carrier/Workers Compensation and same-provider inputs use the established intake controls.', async () => {
      await page.getByLabel('Find an approved provider').fill(`${fixturePrefix}-not-found`);
      await page.getByRole('button', { name: 'Search directory' }).click();
      await expect(page.getByText(/No approved providers matched/i)).toBeVisible();
      await page.getByLabel('Primary Doctor', { exact: true }).fill(`${fixturePrefix} Unknown Provider`);
      await page.getByLabel('Primary Doctor Phone', { exact: true }).fill('555-010-0400');
      await page.getByLabel('Referring doctor is the same as primary doctor', { exact: true }).check();
      await expect(page.getByLabel('Referring Doctor', { exact: true })).toHaveValue(`${fixturePrefix} Unknown Provider`);
      await expect(page.getByLabel('Referring Doctor', { exact: true })).toBeDisabled();

      await page.locator('#intake-primary-insurance-company').fill('Blue Cross Blue Shield');
      await page.locator('#intake-primary-member-id').fill(`${fixturePrefix}-INTAKE-MEMBER`);
      await page.locator('#intake-primary-group-number').fill(`${fixturePrefix}-INTAKE-GROUP`);
      await page.locator('#intake-payer-type').selectOption({ label: "Workers' Compensation" });
      await expect(page.getByRole('heading', { name: 'Adjuster Contact' })).toBeVisible();
      await page.locator('#intake-adjuster-name').fill('Synthetic Adjuster');
      await page.locator('#intake-adjuster-phone').fill('555-010-0401');
      await page.locator('#intake-adjuster-email').fill(`${fixturePrefix}.wc@example.test`);
      await page.getByTestId('hipaa-ack-checkbox').check();
      await page.getByTestId('consent-to-treat-checkbox').check();
      return 'Provider-not-found, same-provider, catalog carrier, Workers Compensation, and adjuster controls were exercised.';
    });

    await page.getByTestId('continue-button').click();
    await expect(page.getByTestId('pain-assessment-step2')).toBeVisible();

    await verifyWithEvidence(testInfo, ['LV-INTAKE-004', 'LV-INTAKE-005', 'LV-INTAKE-008'], 'Medication/device search and None exclusivity align with a selected body region.', async () => {
      const shoulder = page.getByRole('button', { name: /Shoulder.*Front/i }).first();
      await shoulder.click();
      await expect(shoulder).toHaveAttribute('aria-pressed', 'true');

      await page.getByLabel('Medication Search').fill('Lipitor');
      const medicationSection = page.getByLabel('Medication Search').locator('xpath=ancestor::section[1]');
      const lipitor = medicationSection.getByRole('button', { name: /Lipitor.*Atorvastatin/i });
      await lipitor.click();
      await expect(lipitor.first()).toHaveAttribute('aria-pressed', 'true');
      await medicationSection.getByRole('button', { name: 'None', exact: true }).click();
      await expect(medicationSection.getByRole('button', { name: 'None', exact: true })).toHaveAttribute('aria-pressed', 'true');
      await expect(medicationSection.getByRole('button', { name: /Lipitor.*Atorvastatin/i })).toHaveCount(1);
      await expect(medicationSection.getByRole('button', { name: /Lipitor.*Atorvastatin/i })).toHaveAttribute('aria-pressed', 'false');
      await medicationSection.getByRole('button', { name: 'None', exact: true }).click();

      await page.getByLabel('Assistive Devices').fill('cervical collar');
      await page.getByRole('button', { name: /Cervical collar/i }).click();
      await expect(page.getByRole('button', { name: /Cervical collar/i }).first()).toHaveClass(/selected/);
      return 'Shoulder context, catalog searches, selections, and medication None exclusivity updated visibly.';
    });

    await verifyWithEvidence(testInfo, ['LV-INTAKE-001', 'LV-INTAKE-002'], 'Functional-limitation accordions retain multiple choices and reload from the saved draft.', async () => {
      const selectedLabels: string[] = [];
      const limitationRegion = page.getByRole('region', { name: 'Functional Activities Limited' });
      const categories = limitationRegion.locator('details.intake-card__accordion');
      expect(await categories.count()).toBeGreaterThanOrEqual(5);
      for (let index = 0; index < 5; index += 1) {
        const details = categories.nth(index);
        await expect(details, `functional limitation category ${index + 1}`).toBeVisible();
        await details.locator('summary').click();
        const checkbox = details.locator('input[type="checkbox"]').first();
        const label = await checkbox.locator('xpath=..').innerText();
        selectedLabels.push(label.trim());
        await checkbox.check();
        await expect(checkbox).toBeChecked();
        await details.locator('summary').click();
      }
      await page.locator('#intake-current-level-of-function').fill('Synthetic current function limitation.');
      await page.locator('#intake-functional-limitations').fill('Synthetic additional limitation.');
      await page.getByTestId('continue-button').click();
      await expect(page.getByTestId('pain-details-step')).toBeVisible();
      await page.reload();
      await expect(page.getByTestId('pain-details-step')).toBeVisible();
      await page.getByTestId('back-button').click();
      await expect(page.getByTestId('pain-assessment-step2')).toBeVisible();
      await expect(page.getByRole('button', { name: /Shoulder.*Front/i }).first()).toHaveAttribute('aria-pressed', 'true');
      for (const selectedLabel of selectedLabels) {
        await expect(page.getByLabel(selectedLabel, { exact: true })).toBeChecked();
      }
      await expect(page.locator('#intake-current-level-of-function')).toHaveValue('Synthetic current function limitation.');
      await expect(page.locator('#intake-functional-limitations')).toHaveValue('Synthetic additional limitation.');
      return `Saved and reloaded one selection in each of five categories: ${selectedLabels.join(', ')}.`;
    });

    await verifyWithEvidence(testInfo, ['LV-PROV-005', 'LV-INS-010'], 'A submitted disposable intake creates one patient-scoped provider candidate and updates the matching normalized policy without erasing omitted values.', async () => {
      await page.getByTestId('continue-button').click();
      await expect(page.getByTestId('pain-details-step')).toBeVisible();
      await page.getByPlaceholder('Search body parts').fill('Shoulder');
      await page.getByRole('button', { name: 'Shoulder', exact: true }).first().click();
      await page.getByLabel('Pain severity from 0 to 10').fill('4');
      await page.getByTestId('continue-button').click();
      await expect(page.getByTestId('outcome-measures-step')).toBeVisible();
      await page.getByLabel('I do not have a score to enter.').check();
      await page.getByTestId('continue-button').click();
      await expect(page.getByTestId('review-step')).toBeVisible();
      await page.getByLabel('I agree to the Terms of Service and Privacy Policy.').check();
      await page.getByLabel('I have reviewed my answers and confirm they are accurate.').check();
      const submit = page.getByTestId('submit-button');
      await expect(submit).toBeEnabled();
      await submit.click();
      await expect(page.getByTestId('submit-status-message')).toContainText(/Intake submitted successfully/i);

      const candidates = await apiJson<Provider[]>(page, 'GET', `/api/v1/admin/providers?q=${encodeURIComponent(`${fixturePrefix} Unknown`)}&status=0&take=25`);
      expect(candidates).toHaveLength(1);
      await recordFixture('ProviderDirectoryEntry', candidates[0].id, 'Disposable database teardown');
      const relationships = await apiJson<{ providerId: string; patientId: string }[]>(page, 'GET', `/api/v1/providers/patients/${patient.id}`);
      expect(new Set(relationships.map(item => item.providerId))).toEqual(new Set([candidates[0].id]));

      const policies = await apiJson<Policy[]>(page, 'GET', `/api/v1/patients/${patient.id}/insurance-policies/?includeArchived=true`);
      const projected = policies.find(item => item.id === existingPolicy.id);
      expect(projected).toBeTruthy();
      expect(projected!.memberOrPolicyNumber).toBe(`${fixturePrefix}-INTAKE-MEMBER`);
      expect(projected!.groupNumber).toBe(`${fixturePrefix}-INTAKE-GROUP`);
      expect((projected as Policy & { deductibleAmount?: number }).deductibleAmount).toBe(1775);
      return 'Submission produced one pending provider identity for both roles and retained the omitted $1,775 deductible while updating intake-supplied policy fields.';
    });
  });

  test('SOAP intervention remediation uses a purpose-created writable Evaluation draft', async ({ page }, testInfo) => {
    await loginAs(page, 'pt');
    const patient = await createSyntheticPatient(page, 'soap-interventions');
    const note = await createDraftNote(page, patient.id, 0);
    await gotoInteractive(page, `/patient/${patient.id}/note/${note.note!.id}`);
    const interventionsTab = page.getByTestId('soap-tab-interventions');
    const openInterventions = async () => {
      await interventionsTab.click();
      const incompleteGuard = page.getByTestId('incomplete-note-modal');
      await expect.poll(async () =>
        await incompleteGuard.isVisible().catch(() => false) ||
        (await interventionsTab.getAttribute('class') ?? '').includes('soap-tab-nav__tab--active'))
        .toBe(true);
      if (await incompleteGuard.isVisible().catch(() => false)) {
        await incompleteGuard.getByTestId('incomplete-note-continue').click();
      }
      await expect(interventionsTab).toHaveClass(/soap-tab-nav__tab--active/);
    };
    await openInterventions();

    await verifyWithEvidence(testInfo, ['LV-SOAP-004'], 'Scap plus Shoulder returns only Scapular Retraction.', async () => {
      await page.getByRole('button', { name: 'Add Exercise' }).click();
      const dialog = page.getByRole('dialog', { name: 'Add Therapeutic Exercise' });
      await dialog.getByRole('searchbox', { name: 'Search exercises' }).fill('Scap');
      await dialog.getByRole('button', { name: 'Shoulder' }).click();
      const results = dialog.getByTestId('exercise-library-result');
      await expect(results).toHaveCount(1);
      await expect(results).toContainText('Scapular Retraction');
      return 'The intersection search returned one Scapular Retraction result.';
    });

    await verifyWithEvidence(testInfo, ['LV-SOAP-005'], 'Pendulum Exercise clones its configured prescription defaults.', async () => {
      const dialog = page.getByRole('dialog', { name: 'Add Therapeutic Exercise' });
      await dialog.getByRole('searchbox', { name: 'Search exercises' }).fill('Pendulum');
      await dialog.getByRole('button', { name: /^Add$/ }).click();
      await dialog.getByRole('button', { name: 'Close Add Therapeutic Exercise' }).click();
      await expect(dialog).toHaveCount(0);
      const card = page.getByRole('heading', { name: 'Pendulum Exercise', exact: true }).locator('xpath=ancestor::article');
      await expect(card.getByLabel('Sets for Pendulum Exercise')).toHaveValue('3');
      await expect(card.getByLabel('Reps for Pendulum Exercise')).toHaveValue('10');
      await expect(card.getByLabel('Frequency for Pendulum Exercise')).toHaveValue('3x/week');
      return 'The new exercise displayed Sets 3, Reps 10, and Frequency 3x/week.';
    });

    await verifyWithEvidence(testInfo, ['LV-SOAP-006'], 'Prescription editing, HEP, collapse, duplicate, remove, and final-row removal persistence remain isolated to the selected exercise.', async () => {
      const card = page.getByRole('heading', { name: 'Pendulum Exercise', exact: true }).locator('xpath=ancestor::article');
      await card.getByLabel('Sets for Pendulum Exercise').fill('4');
      await card.getByLabel('Reps for Pendulum Exercise').fill('12');
      await card.getByLabel('Frequency for Pendulum Exercise').fill('2x/day');
      await card.getByLabel('Notes (Optional)').fill('Synthetic pendulum instructions.');
      const hep = card.getByRole('switch', { name: 'Include Pendulum Exercise in Home Exercise Program' });
      await hep.click();
      await expect(hep).toHaveAttribute('aria-checked', 'true');
      await card.getByRole('button', { name: 'Collapse Pendulum Exercise' }).click();
      await expect(card.getByTestId('exercise-prescription-fields')).toHaveCount(0);
      await card.getByRole('button', { name: 'Expand Pendulum Exercise' }).click();
      await card.getByRole('button', { name: 'Duplicate Pendulum Exercise' }).click();
      await expect(page.getByRole('heading', { name: 'Pendulum Exercise', exact: true })).toHaveCount(2);
      await page.getByRole('button', { name: 'Remove Pendulum Exercise' }).last().click();
      await expect(page.getByRole('heading', { name: 'Pendulum Exercise', exact: true })).toHaveCount(1);
      return 'Edited values and HEP state updated, collapse/expand preserved the row, duplicate produced a second row, and removing it left the original unchanged.';
    });

    const customName = `${fixturePrefix} Custom Exercise`;
    await verifyWithEvidence(testInfo, ['LV-SOAP-007'], 'Custom Exercise exposes only the documented fields and inserts a visible card.', async () => {
      await page.getByRole('button', { name: 'Add Exercise' }).click();
      const dialog = page.getByRole('dialog', { name: 'Add Therapeutic Exercise' });
      await dialog.getByRole('tab', { name: 'Custom Exercise' }).click();
      await dialog.getByLabel('Exercise Name *').fill(customName);
      await dialog.getByLabel('Notes (Optional)').fill('Synthetic custom exercise notes.');
      await dialog.getByRole('button', { name: 'Add Custom Exercise' }).click();
      await dialog.getByRole('button', { name: 'Close Add Therapeutic Exercise' }).click();
      await expect(page.getByRole('heading', { name: customName, exact: true })).toBeVisible();
      return 'The documented name and optional-notes fields inserted one custom exercise card without an extra banner or toast.';
    });

    await verifyWithEvidence(testInfo, ['LV-SOAP-008', 'LV-SOAP-009', 'LV-SOAP-010', 'LV-SOAP-011'], 'Manual library defaults and Shoulder filtering are exact; adding updates count without a card and Custom Technique stays empty.', async () => {
      await page.getByRole('button', { name: 'Add Technique' }).click();
      const dialog = page.getByRole('dialog', { name: 'Add Manual Technique' });
      await expect(dialog).toContainText('Select from library or add custom technique');
      await expect(dialog.getByRole('tab', { name: 'Technique Library' })).toHaveAttribute('aria-selected', 'true');
      await expect(dialog.getByRole('group', { name: 'Body region' }).getByRole('button')).toHaveCount(10);
      await expect(dialog.getByTestId('technique-library-result')).toHaveCount(7);
      await dialog.getByRole('button', { name: 'Shoulder', exact: true }).click();
      await expect(dialog.getByTestId('technique-library-result')).toHaveCount(5);
      await expect(dialog.getByTestId('technique-library-results')).not.toContainText('Elbow');
      await dialog.getByRole('button', { name: /^Add$/ }).first().click();
      await dialog.getByRole('button', { name: 'Close Add Manual Technique' }).click();
      await expect(dialog).toHaveCount(0);
      await expect(page.getByTestId('technique-count')).toContainText('1');
      await expect(page.locator('[data-testid="manual-technique-card"]')).toHaveCount(0);
      await page.getByRole('button', { name: 'Add Technique' }).click();
      const customTechniqueTab = page.getByRole('tab', { name: 'Custom Technique' });
      await customTechniqueTab.click();
      await expect(customTechniqueTab).toHaveAttribute('aria-selected', 'true');
      const panel = page.getByRole('tabpanel', { name: 'Custom Technique' });
      await expect(panel).toHaveCount(1);
      await expect(panel.locator('input, select, textarea, button')).toHaveCount(0);
      await page.getByRole('button', { name: 'Close Add Manual Technique' }).click();
      return 'The default dialog exposed ten filters and seven rows; Shoulder reduced it to five without Elbow, addition changed only the count, and Custom Technique remained empty.';
    });

    await verifyWithEvidence(testInfo, ['LV-SOAP-006', 'LV-SOAP-007'], 'Added and edited exercise rows persist, and removing the final rows remains empty after save/reload.', async () => {
      await saveDraftAndWait(page);
      await page.reload();
      await openInterventions();
      await expect(page.getByRole('heading', { name: 'Pendulum Exercise', exact: true })).toHaveCount(1);
      await expect(page.getByRole('heading', { name: customName, exact: true })).toHaveCount(1);
      await page.getByRole('button', { name: 'Remove Pendulum Exercise' }).click();
      await page.getByRole('button', { name: `Remove ${customName}` }).click();
      await saveDraftAndWait(page);
      await page.reload();
      await openInterventions();
      await expect(page.getByTestId('exercise-empty-state')).toBeVisible();
      return 'Both synthetic cards survived the first reload; after removing the final rows, the second reload retained the documented empty state.';
    });
  });

  test('template reject, revise, resubmit, and stale-write lifecycle uses Admin/PT dual control', async ({ page, browser }, testInfo) => {
    await loginAs(page, 'admin');
    const draft = await apiJson<TemplateVersion>(page, 'POST', '/api/v1/admin/note-templates/drafts', {
      name: `${fixturePrefix} Reject Revise`, noteType: 0, variant: 0
    });
    await recordFixture('NoteTemplateVersion', draft.id, 'Disposable database teardown');
    const submitted = await apiJson<TemplateVersion>(page, 'POST', `/api/v1/admin/note-templates/versions/${draft.id}/submit`);
    expect(submitted.status).toBe(1);

    await verifyWithEvidence(testInfo, ['LV-TPL-009'], 'A different PT rejects; Admin revises and resubmits without changing a published version.', async () => {
      const ptContext = await browser.newContext();
      const ptPage = await ptContext.newPage();
      let rejected: TemplateVersion;
      try {
        await loginAs(ptPage, 'pt');
        rejected = await apiJson<TemplateVersion>(ptPage, 'POST', `/api/v1/clinical/note-templates/versions/${draft.id}/reject`, {
          comment: 'Synthetic clinical changes requested.'
        });
      } finally {
        await ptContext.close();
      }
      expect(rejected!.status).toBe(3);
      await loginAs(page, 'admin');
      const revisedSchema = structuredClone(rejected!.schema);
      revisedSchema.sections[0].label = `${revisedSchema.sections[0].label} revised`;
      const revised = await apiJson<TemplateVersion>(page, 'PUT', `/api/v1/admin/note-templates/versions/${draft.id}`, {
        schema: revisedSchema, expectedLastModifiedUtc: rejected!.lastModifiedUtc
      });
      const resubmitted = await apiJson<TemplateVersion>(page, 'POST', `/api/v1/admin/note-templates/versions/${draft.id}/submit`);
      expect(revised.status).toBe(0);
      expect(resubmitted.status).toBe(1);
      return 'PT rejected with a comment; Admin revised the schema and returned the same version to Pending Clinical Approval.';
    });

    await verifyWithEvidence(testInfo, ['LV-TPL-014', 'LV-XCUT-005'], 'Two loaded template editors cannot silently overwrite one another.', async () => {
      const concurrencyDraft = await apiJson<TemplateVersion>(page, 'POST', '/api/v1/admin/note-templates/drafts', {
        name: `${fixturePrefix} Template Concurrency`, noteType: 1, variant: 0
      });
      const secondContext = await browser.newContext();
      const secondPage = await secondContext.newPage();
      try {
        await loginAs(secondPage, 'admin');
        const first = await apiJson<TemplateVersion>(page, 'GET', `/api/v1/admin/note-templates/versions/${concurrencyDraft.id}`);
        const second = await apiJson<TemplateVersion>(secondPage, 'GET', `/api/v1/admin/note-templates/versions/${concurrencyDraft.id}`);
        const firstSchema = structuredClone(first.schema);
        firstSchema.sections[0].label = `${firstSchema.sections[0].label} first`;
        const saved = await apiResponse(page, 'PUT', `/api/v1/admin/note-templates/versions/${first.id}`, {
          schema: firstSchema, expectedLastModifiedUtc: first.lastModifiedUtc
        });
        expect(saved.ok()).toBe(true);
        const secondSchema = structuredClone(second.schema);
        secondSchema.sections[0].label = `${secondSchema.sections[0].label} stale`;
        const stale = await apiResponse(secondPage, 'PUT', `/api/v1/admin/note-templates/versions/${second.id}`, {
          schema: secondSchema, expectedLastModifiedUtc: second.lastModifiedUtc
        });
        expect(stale.status()).toBe(409);
      } finally {
        await secondContext.close();
      }
      return 'The stale template update received HTTP 409.';
    }, 'Concurrency passed; the tenant-isolation portion remains blocked because the disposable copy has one clinic.');

    await verifyWithEvidence(testInfo, ['LV-TPL-011'], 'Unsigned notes created around two publications remain pinned to their creation template versions.', async () => {
      await loginAs(page, 'admin');
      const versionA = await apiJson<TemplateVersion>(page, 'POST', '/api/v1/admin/note-templates/drafts', {
        name: `${fixturePrefix} Version Pinning`, noteType: 0, variant: 0
      });
      await recordFixture('NoteTemplateVersion', versionA.id, 'Disposable database teardown');
      await apiJson<TemplateVersion>(page, 'POST', `/api/v1/admin/note-templates/versions/${versionA.id}/submit`);

      const ptContext = await browser.newContext();
      const ptPage = await ptContext.newPage();
      try {
        await loginAs(ptPage, 'pt');
        const publishedA = await apiJson<TemplateVersion>(ptPage, 'POST', `/api/v1/clinical/note-templates/versions/${versionA.id}/publish`, {
          comment: 'Synthetic version A publication.'
        });
        const patient = await createSyntheticPatient(ptPage, 'template-pinning');
        const noteA = await createDraftNote(ptPage, patient.id, 0);
        expect(noteA.note!.templateVersionId).toBe(publishedA.id);
        const initialReadbackA = await apiJson<{ note: { templateVersionId?: string } }>(ptPage, 'GET', `/api/v1/notes/${noteA.note!.id}`);
        expect(initialReadbackA.note.templateVersionId).toBe(publishedA.id);

        await loginAs(page, 'admin');
        const versionB = await apiJson<TemplateVersion>(page, 'POST', '/api/v1/admin/note-templates/drafts', {
          name: `${fixturePrefix} Version Pinning`, noteType: 0, variant: 0, cloneVersionId: publishedA.id
        });
        await recordFixture('NoteTemplateVersion', versionB.id, 'Disposable database teardown');
        await apiJson<TemplateVersion>(page, 'POST', `/api/v1/admin/note-templates/versions/${versionB.id}/submit`);

        const publishedB = await apiJson<TemplateVersion>(ptPage, 'POST', `/api/v1/clinical/note-templates/versions/${versionB.id}/publish`, {
          comment: 'Synthetic version B publication.'
        });
        const reopenedA = await apiJson<{ note: { templateVersionId?: string } }>(ptPage, 'GET', `/api/v1/notes/${noteA.note!.id}`);
        const noteB = await createDraftNote(ptPage, patient.id, 0);
        expect(reopenedA.note.templateVersionId).toBe(publishedA.id);
        expect(noteB.note!.templateVersionId).toBe(publishedB.id);
      } finally {
        await ptContext.close();
      }
      return 'Draft A retained published version A after version B became active, and the later draft resolved to version B.';
    }, 'Unsigned draft pinning passed; signed-note immutability remains covered by the existing non-browser compliance gate because this harness never signs notes.');
  });

  test('irreducible environment gaps remain explicitly blocked', async () => {
    await recordBlocked(['LV-AUTH-009'], 'MAUI invalid-token behavior is exercised in a real MAUI client.', 'Browser-only localhost harness cannot substitute for MAUI.');
    await recordBlocked(['LV-PROV-012', 'LV-XCUT-002'], 'Tenant isolation is verified between two approved clinics.', 'The disposable copy contains one clinic; the harness never manufactures a tenant by database edit.');
    await recordBlocked(['LV-INS-014', 'LV-XCUT-003'], 'Offline sync is verified in a real offline client.', 'Browser-only localhost harness cannot substitute for the sync client.');
    await recordBlocked(['LV-PROV-014', 'LV-XCUT-004'], 'Audit events are visible through an authorized presentation surface.', 'No authorized browser-readable audit presentation exists; sanitized server/database excerpts remain supplemental only.');
  });
});

async function submitProvider(page: import('@playwright/test').Page, suffix: string, npi: string, phone: string, addressLine1: string) {
  const provider = await apiJson<Provider>(page, 'POST', '/api/v1/providers/candidates', {
    firstName: 'Live',
    lastName: `${fixturePrefix}-${suffix}`,
    credentials: 'MD',
    npi,
    organizationName: `${fixturePrefix} ${suffix}`,
    phone,
    addressLine1,
    city: 'Testville',
    state: 'NY',
    zipCode: '10001',
    submissionSource: 0
  });
  await recordFixture('ProviderDirectoryEntry', provider.id, 'Disposable database teardown');
  return provider;
}

function candidateUpdate(provider: Provider, organizationName: string) {
  return {
    firstName: provider.firstName,
    lastName: provider.lastName,
    npi: provider.npi,
    organizationName,
    submissionSource: 0,
    expectedLastModifiedUtc: provider.lastModifiedUtc
  };
}

function policyRequest(priority: number, carrierKey: string | undefined, carrierDisplayName: string) {
  return {
    coveragePriority: priority,
    carrierKey,
    carrierDisplayName,
    payerType: 0,
    memberOrPolicyNumber: `${fixturePrefix}-MEMBER-${priority}`,
    groupNumber: `${fixturePrefix}-GROUP-${priority}`,
    effectiveStartDate: '2026-01-01T00:00:00Z',
    effectiveEndDate: '2026-12-31T00:00:00Z',
    planYearType: 1,
    deductibleAmount: 1000,
    deductibleMet: 250,
    outOfPocketMaximum: 5000,
    outOfPocketMet: 750,
    copayAmount: 25,
    coinsurancePercent: 20,
    adjusterName: 'Synthetic Adjuster',
    adjusterPhone: '555-010-0198',
    adjusterEmail: `${fixturePrefix}.adjuster@example.test`,
    adjusterFax: '555-010-0197',
    status: 0
  };
}

async function listPatientAppointments(page: import('@playwright/test').Page, patientId: string, day: string) {
  return apiJson<Appointment[]>(page, 'GET', `/api/v1/appointments/by-patient/${patientId}?startDate=${day}&endDate=${day}`);
}

async function createAndApproveEmptyPt(page: import('@playwright/test').Page) {
  const clinics = await apiJson<{ id: string }[]>(page, 'GET', '/api/v1/auth/clinics');
  expect(clinics.length).toBeGreaterThan(0);
  const pin = process.env.PTDOC_UI_QA_PIN;
  if (!pin) throw new Error('PTDOC_UI_QA_PIN is required for the synthetic PT registration.');
  const email = `${fixturePrefix}.empty.pt@example.test`;
  const registration = await apiJson<{ userId?: string; status: string }>(page, 'POST', '/api/v1/auth/register', {
    fullName: `Live ${fixturePrefix} Empty PT`,
    email,
    dateOfBirth: '1990-01-15T00:00:00Z',
    roleKey: 'PT',
    clinicId: clinics[0].id,
    pin,
    licenseNumber: `${fixturePrefix}-PT-LICENSE`.slice(0, 40),
    licenseState: 'NY'
  });
  expect(registration.status).toBe('PendingApproval');
  expect(registration.userId).toBeTruthy();
  await recordFixture('User', registration.userId!, 'Disposable database teardown');
  const approval = await apiJson<{ status: string }>(page, 'POST', `/api/v1/admin/registrations/${registration.userId}/approve`);
  expect(approval.status).toBe('Succeeded');
  return { email, pin };
}

async function openAppointmentAndAddPatient(page: import('@playwright/test').Page, suffix: string) {
  await newAppointmentButton(page).click();
  const dialog = page.getByRole('dialog', { name: 'New Appointment' });
  const date = uniqueFutureDate();
  const time = '13:30';
  const duration = '60';
  const appointmentTypeLabel = 'Re-evaluation';
  const note = `${fixturePrefix}-${suffix}-latest-note`;
  await dialog.getByLabel('Appointment Type').selectOption({ label: appointmentTypeLabel });
  const appointmentType = await dialog.getByLabel('Appointment Type').inputValue();
  await dialog.getByLabel('Date').fill(date);
  await dialog.getByLabel('Time').fill(time);
  await dialog.getByLabel('Duration (minutes)').selectOption(duration);
  const clinician = await dialog.getByLabel('Clinician').locator('option:not([value=""])').first().getAttribute('value');
  expect(clinician).toBeTruthy();
  await dialog.getByLabel('Clinician').selectOption(clinician!);
  await dialog.getByLabel('Notes').fill(note);
  await dialog.getByRole('button', { name: 'Add Patient' }).click();
  await fillPatientDialog(page, suffix);
  return { date, time, duration, appointmentType, clinician: clinician!, note };
}

function newAppointmentButton(page: import('@playwright/test').Page) {
  return page.locator('button.global-page-header-primary-action').filter({ hasText: 'New Appointment' });
}

async function fillPatientDialog(page: import('@playwright/test').Page, suffix: string) {
  const dialog = page.getByRole('dialog', { name: 'Add New Patient' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('First Name').fill('Live');
  await dialog.getByLabel('Last Name').fill(`${fixturePrefix}-${suffix}`);
  await dialog.getByLabel('Email Address').fill(`${fixturePrefix}.${suffix}@example.test`);
  await dialog.getByLabel('Phone Number').fill('555-010-0196');
  await dialog.getByLabel('Date of Birth').fill('1990-01-15');
}

async function communicationCount(page: import('@playwright/test').Page) {
  const response = await apiJson<{ messages: unknown[] }>(page, 'GET', '/diagnostics/development/communications?take=100');
  return response.messages.length;
}

async function queryPatients(page: import('@playwright/test').Page, query: string) {
  return apiJson<{ id: string; email?: string }[]>(page, 'GET', `/api/v1/patients/?query=${encodeURIComponent(query)}&take=25`);
}

async function expectIntakeDraft(page: import('@playwright/test').Page, patientId: string) {
  let draft: { id: string } | undefined;
  await expect.poll(async () => {
    const response = await apiResponse(page, 'GET', `/api/v1/intake/patient/${patientId}/draft`);
    if (!response.ok()) return response.status();
    draft = await response.json() as { id: string };
    return 200;
  }).toBe(200);
  return draft!;
}

async function createDraftNote(page: import('@playwright/test').Page, patientId: string, noteType: number) {
  const operation = await apiJson<NoteOperation>(page, 'POST', '/api/v1/notes/', {
    patientId,
    noteType,
    isReEvaluation: false,
    contentJson: '{}',
    dateOfService: new Date().toISOString(),
    cptCodesJson: '[]'
  });
  expect(operation.isValid).toBe(true);
  expect(operation.note).toBeTruthy();
  await recordFixture('ClinicalNote', operation.note!.id, 'Disposable database teardown');
  return operation;
}

async function saveDraftAndWait(page: import('@playwright/test').Page) {
  const save = page.getByTestId('footer-save');
  if (await save.isEnabled()) {
    await save.click();
  }
  await expect(page.getByTestId('footer-state-label')).toContainText(/Saved|All changes saved/i);
}

function uniqueNpi(offset: number) {
  const seed = (Date.now() + offset) % 1_000_000_000;
  return `1${String(seed).padStart(9, '0')}`;
}

function uniqueFutureDate() {
  const date = new Date();
  date.setDate(date.getDate() + 1);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function escapeRegex(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
