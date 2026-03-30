---
title: "[]{#_v0t099s366g0 .anchor}**Policies and consent forms.docx**"
---

Below is a structured, traceable requirements package derived from
**Policies and consent forms.docx**. Where the document is silent, I've
marked items as **Missing / Not Defined** rather than filling gaps.
Source references use section headings because page numbers were not
available from the extracted DOCX text.

## **1. Document Summary**

This document is a bundled patient-facing policy and consent packet for
Physically Fit Physical Therapy, Inc. It covers: informed consent for PT
treatment, PHI release and HIPAA acknowledgement, authorization to speak
with designated contacts, digital image/video consent,
cancellation/no-show fees, consent for appointment reminders via
phone/text/email, payment obligations, Medicare-related constraints, dry
needling consent, client rights/grievance/responsibility statements,
pelvic floor consent, financial responsibility including credit
card-on-file, and a financial hardship policy. Its scope is primarily
legal/compliance, patient authorization, communication consent, payment
policy, and specialty-treatment consent rather than application workflow
or technical implementation.

## **2. Extracted Requirements Register**

### **RQ-001**

**Title:** Record patient informed consent for PT evaluation and
treatment\
**Description:** The system must support capture of patient consent for
Physically Fit Physical Therapy, Inc. to evaluate the patient and
provide physical therapy treatment deemed necessary and proper by the
therapist and other healthcare providers.\
**Category:** Functional / Compliance\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Practice/Clinic Staff\
**Acceptance Criteria:** Patient can acknowledge consent before care;
revocation right exists in writing. Specific workflow steps are not
defined.\
**UI Expectations:** Consent acknowledgment area with
signature/attestation is implied; exact control type not defined.\
**Integrations / Dependencies:** None stated\
**Business Rules / Constraints:** Consent may be revoked in writing at
any time.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Informed Consent*; follow-on consent
language near payment section\
**Notes:** Document does not define whether revocation applies
prospectively only for treatment consent.

### **RQ-002**

**Title:** Present treatment risks, benefits, alternatives, and
no-guarantee statement\
**Description:** Patients must be informed of purpose, risks, expected
benefits, reasonable alternatives, and that no warranties/guarantees are
made about outcomes.\
**Category:** Functional / Compliance / Content\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinician\
**Acceptance Criteria:** These disclosures are presented before
consent.\
**UI Expectations:** Disclosure text visible in consent workflow.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Must preserve no-guarantee language.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Informed Consent\
* **Notes:** The document does not require user-by-user acknowledgment
of each bullet separately.

### **RQ-003**

**Title:** Present and preserve patient rights information\
**Description:** The system/package must communicate patient rights
including nondiscrimination, second opinion, declining treatment, access
to records, policy/charge transparency, and discussion of treatment
options/risks. Expanded rights also include dignity, continuity of care,
informed billing disclosures, grievance rights, and abuse-free
treatment.\
**Category:** Functional / Compliance\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Rights text is provided to client and/or
representative.\
**UI Expectations:** Rights disclosure section; acknowledgment implied.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Rights may be exercised by designated
representative where legally permitted.\
**Priority:** Unspecified\
**Source Reference(s):** Sections: *Informed Consent*; *Client Rights,
Grievances, and Responsibilities\
* **Notes:** The app requirement is mainly content capture/display, not
adjudication of rights.

### **RQ-004**

**Title:** Capture authorization for PHI use/disclosure for treatment,
payment, operations\
**Description:** The system must support patient authorization and
acknowledgement regarding PHI use/disclosure for treatment, payment,
administrative activities, quality evaluation, appointment reminders,
treatment alternatives, and legally required disclosures.\
**Category:** Functional / Compliance / Privacy\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Patient can authorize PHI uses described;
additional disclosures outside listed purposes require written
authorization.\
**UI Expectations:** Authorization text and consent acknowledgment.\
**Integrations / Dependencies:** Insurance carriers, workers'
compensation carriers, other medical providers are named disclosure
recipients.\
**Business Rules / Constraints:** Written authorization required for
non-routine disclosures; revocable by later written statement for future
disclosures.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Release of Medical Information\
* **Notes:** No retention or workflow detail for storing revocation
timestamps is specified.

### **RQ-005**

**Title:** Capture authorized contact persons for medical
care/treatment/billing discussions\
**Description:** The system must allow designation of named individuals,
phone numbers, and relationships for disclosure/discussion of medical
care, treatment, and/or billing information.\
**Category:** Functional / Privacy\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Up to three designated contacts are supported
in the form as shown.\
**UI Expectations:** Repeating fields for name, phone number,
relationship.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Authorization revocable at any time.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Release of Medical Information\
* **Notes:** The document shows three contact slots, but does not state
whether more/fewer must be supported.

### **RQ-006**

**Title:** Acknowledge HIPAA Notice of Privacy Practices\
**Description:** The system must support patient acknowledgment that
they have the right to receive the Notice of Privacy Practices and may
contact the Privacy Officer with questions or complaints.\
**Category:** Functional / Compliance / Privacy\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Privacy Officer/Clinic Staff\
**Acceptance Criteria:** Patient can acknowledge notice rights and
consent to use/disclosure as described.\
**UI Expectations:** HIPAA acknowledgment section.\
**Integrations / Dependencies:** Privacy Officer role is referenced but
not operationally defined.\
**Business Rules / Constraints:** Electronic disclosure by
provider/business associates is acknowledged.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *HIPAA Acknowledgement\
* **Notes:** Privacy Officer identity/contact details are missing from
this document.

### **RQ-007**

**Title:** Capture consent for digital images and videos\
**Description:** The system must support consent for photographs and
digital videotapes recorded to document care. Patients may request a
copy. Identifiable images released outside treatment/payment/operations
require written authorization.\
**Category:** Functional / Compliance / Media Consent\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Patient can consent; policy text includes
ownership and release conditions.\
**UI Expectations:** Media consent acknowledgment.\
**Integrations / Dependencies:** None stated\
**Business Rules / Constraints:** Photos may be taken during initial
evaluation, progress evaluation, and discharge summary. External release
requires written authorization except for TPO uses.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Digital Images and Videos\
* **Notes:** Storage, retention, deletion, and request fulfillment
workflow are not defined.

### **RQ-008**

**Title:** Enforce patient-facing prohibition on recording at clinic\
**Description:** Patients are not permitted to take pictures or make
video/audio recordings at clinic locations or of care, other patients,
or personnel.\
**Category:** Business Rule / Compliance\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient\
**Acceptance Criteria:** Policy is disclosed.\
**UI Expectations:** Policy text only; no technical enforcement
defined.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Applies to any clinic/location and
personnel/patients.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Digital Images and Videos\
* **Notes:** No app-level device permission control is required by the
document.

### **RQ-009**

**Title:** Present cancellation, no-show, and late-arrival policy\
**Description:** The system must disclose that a \$75 fee is charged for
appointments without 24-hour notice and/or appointments where the
patient is more than 15 minutes late.\
**Category:** Functional / Billing Policy\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff\
**Acceptance Criteria:** Patient acknowledges policy.\
**UI Expectations:** Policy acknowledgment section.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Fee amount \$75; 24-hour notice
threshold; \>15-minute late threshold.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Cancellation / No Show Policy\
* **Notes:** Another section later introduces a \$45 late cancellation
fee and \$75 no-show fee with different wording, creating a
contradiction.

### **RQ-010**

**Title:** Capture consent for phone/text/email appointment reminders
and healthcare communications\
**Description:** The system must allow patients to authorize text/cell
calls and email for appointment reminders, feedback, and general health
reminders/information.\
**Category:** Functional / Communication Consent\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Patient can authorize by channel and provide
cell number/email. Opt-out is possible with written consent.\
**UI Expectations:** Separate authorization fields for phone/text and
email with contact data entry.\
**Integrations / Dependencies:** Email, phone, text messaging systems
implied.\
**Business Rules / Constraints:** Calls may include
prerecorded/artificial voice or autodialing device; standard carrier
rates may apply; clinic does not charge for service. Opt-out via written
consent.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Consent to mail, Cellular Telephone,
or Text Usage for Appointment Reminders and Other Healthcare
Communications\
* **Notes:** "Written consent" appears to be used for opt-out; exact
opt-out mechanism is undefined.

### **RQ-011**

**Title:** Present general payment responsibility policy\
**Description:** The system must disclose that co-payments are due at
time of service and that the patient is financially responsible for
deductibles, co-pays, coinsurance, denied/non-covered charges, and
providing current insurance information.\
**Category:** Functional / Billing\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff\
**Acceptance Criteria:** Patient acknowledges payment responsibility.\
**UI Expectations:** Payment policy section with acknowledgment.\
**Integrations / Dependencies:** Insurance plans, Medicare, other
programs.\
**Business Rules / Constraints:** Payments due at time of service;
out-of-network reimbursement not guaranteed.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Payment Policy\
* **Notes:** Collection timing beyond time-of-service is not fully
specified here.

### **RQ-012**

**Title:** Present Medicare-specific outpatient therapy constraints\
**Description:** For Medicare patients scheduling without physician
referral/prescription, a signed therapy plan of care must be obtained
from the physician within 30 days of the initial visit; patient must be
discharged from home health before outpatient therapy begins; Medicare
will not pay simultaneously for home health and outpatient care.\
**Category:** Business Rule / Compliance / Billing\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Clinic Staff, Referring Physician\
**Acceptance Criteria:** Policy disclosed; downstream workflows may need
tracking.\
**UI Expectations:** Policy text; no specific alert/workflow defined in
this document.\
**Integrations / Dependencies:** Physician plan of care; Medicare; home
health agency status.\
**Business Rules / Constraints:** 30-day signed POC requirement; no
concurrent home health/outpatient billing.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Payment Policy -- Medicare Patients\
* **Notes:** Implementation details for tracking/refusing scheduling are
not provided here.

### **RQ-013**

**Title:** Present auto PIP / third-party motor vehicle collision
billing policy\
**Description:** The clinic will bill PIP as a courtesy; if there is no
direct PIP claim, patient may self-pay; letters of protection may be
accepted case by case.\
**Category:** Functional / Billing\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff, Attorney\
**Acceptance Criteria:** Policy disclosed.\
**UI Expectations:** Policy text.\
**Integrations / Dependencies:** PIP insurance; attorneys/LOP handling.\
**Business Rules / Constraints:** LOP acceptance is discretionary.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Payment Policy -- Motor Vehicle
Collision\
* **Notes:** No criteria for LOP acceptance provided.

### **RQ-014**

**Title:** Present direct payment / cash-pay policy\
**Description:** If patient elects cash-based/direct payment, full
payment is expected at time of service.\
**Category:** Functional / Billing\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff\
**Acceptance Criteria:** Policy disclosed.\
**UI Expectations:** Policy text.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Full payment at time of service.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Direct Payment Notification\
* **Notes:** Self-pay pricing not specified.

### **RQ-015**

**Title:** Capture dry needling consent and refusal choice\
**Description:** The system must allow patient to explicitly consent or
not consent to trigger point dry needling.\
**Category:** Functional / Specialty Treatment Consent\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Treating Therapist\
**Acceptance Criteria:** Patient can choose consent checkbox state and
acknowledge risks/benefits/questions.\
**UI Expectations:** Yes/No consent control plus explanatory content.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Must preserve informed-risk content
and patient's right to refuse at any time.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Dry Needling Consent\
* **Notes:** Example text contains a named patient in the snippet;
template behavior is not defined.

### **RQ-016**

**Title:** Present dry needling risks, alternatives, and required
disclosures\
**Description:** The system must disclose that dry needling is generally
safe, possible side effects include bruising/soreness/discomfort and
rare dizziness/fainting or pneumothorax; alternative methods and
risks/benefits have been explained. Patients must notify therapist of
bleeding disorders, anticoagulants, pacemaker/defibrillator, implants,
or pregnancy.\
**Category:** Business Rule / Compliance / Clinical Screening\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Treating Therapist\
**Acceptance Criteria:** Disclosure displayed before consent.\
**UI Expectations:** Risk disclosure and possible
contraindication/intake questions are implied.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Patient must notify clinic if status
changes during treatment.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Dry Needling Consent\
* **Notes:** Specific validation questions are implied but not
explicitly structured.

### **RQ-017**

**Title:** Present client grievance process\
**Description:** The system/package must disclose grievance rights and
process: patient may submit concerns verbally or in writing to Director
of Rehabilitation; director investigates and documents grievance
activities; decision communicated within 10 days; unresolved concerns
may be escalated to Administration.\
**Category:** Functional / Compliance / Workflow\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Director of Rehabilitation,
Administration\
**Acceptance Criteria:** Grievance instructions are displayed; timeline
of 10 days stated.\
**UI Expectations:** Grievance information section; submission mechanism
is implied but not required by text.\
**Integrations / Dependencies:** Administration escalation path\
**Business Rules / Constraints:** No discrimination/coercion/reprisal;
documentation of investigation and resolution.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Client Grievance\
* **Notes:** "Facility Phone" placeholder indicates missing contact
details.

### **RQ-018**

**Title:** Present client responsibilities\
**Description:** The system/package must disclose patient
responsibilities including accurate medical history, following
treatment, respecting staff rights, advance cancellation notice, payment
of non-covered services, and compliance with provider rules.\
**Category:** Functional / Policy Disclosure\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient\
**Acceptance Criteria:** Responsibilities are provided and
acknowledged.\
**UI Expectations:** Responsibilities section with acknowledgment.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** None beyond policy statement.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Client Has the Responsibility\
* **Notes:** This is disclosure content, not a technical enforcement
spec.

### **RQ-019**

**Title:** Capture pelvic floor therapy consent including
internal/external examinations\
**Description:** The system must support informed consent for pelvic
health physical therapy, including external and internal pelvic
examinations when deemed necessary by the therapist.\
**Category:** Functional / Specialty Treatment Consent\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Pelvic Health Therapist\
**Acceptance Criteria:** Patient acknowledges explanation and
voluntarily consents.\
**UI Expectations:** Dedicated pelvic floor consent section.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Patient can refuse or withdraw consent
at any time without affecting future care; may request chaperone or
same-gender therapist.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Pelvic Floor Consent\
* **Notes:** Chaperone/same-gender request workflow is not defined.

### **RQ-020**

**Title:** Present pelvic floor benefits, risks, confidentiality, and
rights\
**Description:** The system must disclose purpose, exam descriptions,
benefits, risks/discomforts, refusal rights, right to ask questions, and
confidentiality protections under HIPAA/professional ethics.\
**Category:** Compliance / Clinical Consent\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Therapist\
**Acceptance Criteria:** Disclosures visible before consent.\
**UI Expectations:** Informational consent content.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** None beyond disclosure.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Pelvic Floor Consent\
* **Notes:** No contraindication screening fields are specified.

### **RQ-021**

**Title:** Present financial policy and credit-card-on-file
authorization\
**Description:** The system must support disclosure that patients are
responsible for copays, coinsurance, deductibles, non-covered services,
self-pay balances, and outstanding balances, and that a valid
credit/debit card is required to be securely stored on file for balances
and fees.\
**Category:** Functional / Billing / Payments\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff\
**Acceptance Criteria:** Patient can review and presumably authorize
card-on-file policy.\
**UI Expectations:** Financial policy section and card authorization
acknowledgment.\
**Integrations / Dependencies:** Payment processor / secure card storage
is implied.\
**Business Rules / Constraints:** Payment expected at time of service
unless prior arrangements exist. Card may be used for copays,
deductibles, outstanding balances, no-show and late cancellation fees.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Financial Responsibility and credit
card storage\
* **Notes:** Security standard, tokenization method, and charge timing
are not defined.

### **RQ-022**

**Title:** Present alternate cancellation/no-show fee schedule in
financial policy\
**Description:** A later section states a \$75 no-show fee applies if
the patient misses an appointment and does not reschedule within the
same calendar week, and a \$45 late cancellation fee applies for
cancellations made less than 24 hours before appointment time. Repeated
no-shows/late cancellations may result in discharge from care.\
**Category:** Business Rule / Billing\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff, Clinic Staff\
**Acceptance Criteria:** Policy visible.\
**UI Expectations:** Policy text.\
**Integrations / Dependencies:** Scheduling logic implied.\
**Business Rules / Constraints:** \$75 no-show; \$45 late cancellation;
potential discharge after repeated events.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Financial Responsibility and credit
card storage -- Appointment Cancellation & No-Show Policy\
* **Notes:** Contradicts earlier section that only mentions \$75 and
\>15 minutes late. This must be resolved downstream.

### **RQ-023**

**Title:** Present financial hardship policy and payment-plan terms\
**Description:** Patients experiencing hardship may qualify for a
modified plan requiring 50% of each visit's obligation upfront and
remaining 50% within six months of the visit; overdue balances may go to
collections. Payment methods and contact information are partly
specified.\
**Category:** Functional / Billing / Policy\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient, Billing Staff\
**Acceptance Criteria:** Policy text available; patients can discuss
arrangements by phone.\
**UI Expectations:** Policy disclosure; eligibility workflow is not
defined.\
**Integrations / Dependencies:** Payment methods; portal link on
request; collections process.\
**Business Rules / Constraints:** 50% due at time of service; remaining
50% due within six months; overdue may incur collection fees; provider
may amend/terminate policy.\
**Priority:** Unspecified\
**Source Reference(s):** Section: *Financial Hardship\
* **Notes:** Qualification criteria are missing; accepted payment
methods contain placeholders.

### **RQ-024**

**Title:** Support written revocation handling across multiple consent
types\
**Description:** Multiple sections state the patient can revoke
authorization/consent in writing at any time. This applies at least to
treatment consent, medical information authorization, and communication
preferences.\
**Category:** Functional / Compliance\
**Explicit / Inferred / Missing:** Inferred\
**Related User Role(s):** Patient, Clinic Staff\
**Acceptance Criteria:** Missing / Not Defined in source.\
**UI Expectations:** Revocation intake or document upload workflow is
implied but not defined.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** Revocation is in writing; one section
explicitly limits it to stopping future disclosures.\
**Priority:** Unspecified\
**Source Reference(s):** Multiple sections above\
**Notes:** Downstream audit should verify whether unified
consent-revocation handling exists.

### **RQ-025**

**Title:** Preserve acknowledgment that provided information is true and
policies were read/understood\
**Description:** The document includes attestation that information
provided is true to the best of patient's knowledge and that policies
were read and understood.\
**Category:** Functional / Legal Acknowledgment\
**Explicit / Inferred / Missing:** Explicit\
**Related User Role(s):** Patient\
**Acceptance Criteria:** Acknowledgment present before completion.\
**UI Expectations:** Final attestation checkbox/signature implied.\
**Integrations / Dependencies:** None\
**Business Rules / Constraints:** None beyond acknowledgment text.\
**Priority:** Unspecified\
**Source Reference(s):** End of general policies / rights sections\
**Notes:** Signature standard and witness requirements are not defined.

## **3. User Roles and Actors**

**Patient**

-   Reviews policies, rights, responsibilities, and consents.

-   Authorizes treatment, PHI disclosures, communications, specialty
    services, and possibly card-on-file.

-   Can designate authorized contacts.

-   May revoke certain consents/authorizations in writing.

-   May file grievances and may request accommodations such as chaperone
    or same-gender therapist for pelvic examinations.

**Physical Therapist / Treating Therapist**

-   Provides treatment and informed disclosures.

-   Receives patient disclosures about contraindications/status changes
    for dry needling or pelvic treatment.

-   Deemed decision-maker for necessary evaluation/treatment procedures.

**Practice / Clinic Staff**

-   Manage payment collection, appointment reminders, PHI handling, and
    policy administration.

-   May speak with authorized contacts and process insurance/billing
    disclosures.

**Insurance Company / Worker's Compensation Carrier**

-   Receives medical information for claim processing when authorized.

**Other Medical Providers**

-   May release/receive/discuss patient medical information as
    participating providers.

**Privacy Officer**

-   Point of contact for HIPAA/privacy questions or complaints.
    Identity/contact not defined in the document.

**Director of Rehabilitation**

-   Receives and investigates grievances; communicates decision; may
    escalate to Administration.

**Administration**

-   Escalation point for unresolved grievances.

**Legal Representative / Designated Representative**

-   May exercise client rights where permitted by law.

## **4. UI / Workflow Expectations**

The document implies a **multi-section consent/intake packet** rather
than a single simple form. Likely sections include:

-   Informed Consent

-   Release of Medical Information

-   Authorized Contacts

-   HIPAA Acknowledgement

-   Digital Images and Videos

-   Cancellation / No Show Policy

-   Communication Consent

-   Payment Policy

-   Dry Needling Consent

-   Client Rights / Grievances / Responsibilities

-   Pelvic Floor Consent

-   Financial Responsibility / Credit Card Storage

-   Financial Hardship Policy

Expected interactions implied by the source:

-   Read-and-acknowledge consent content.

-   Enter contact information for authorized release recipients.

-   Provide phone/email for communications consent.

-   Choose specialty-treatment consent options, at least for dry
    needling.

-   Final attestations/signature acknowledging truthfulness and
    understanding.

Validation or constraints implied:

-   Written revocation capability exists, but capture mechanism is
    undefined.

-   Some fields are optional in practice, but requirement level is not
    stated.

-   No explicit lock/finalization behavior is defined for the document,
    though legal acknowledgment suggests completion state should be
    recorded.

-   No explicit workflow order, required-vs-optional section list, or
    conditional branching rules are defined in the document itself.

## **5. Integrations / Dependencies**

**Insurance / Worker's Compensation**

-   Used for PHI release and payment/claims processing.

**Other Medical Providers**

-   Authorized PHI exchange participants.

**Email / SMS / Cellular Calling**

-   For appointment reminders, feedback, and general health
    reminders/information. Autodialed/prerecorded calls are
    contemplated.

**Payment Infrastructure**

-   Credit/debit card storage is required by policy; secure payment
    handling is implied.

**Collections Process**

-   Financial hardship overdue balances may be sent to collections.

**Physician / Referring Provider**

-   Medicare-related signed therapy plan of care required within 30 days
    in certain cases.

**Privacy Officer / Internal Grievance Handling**

-   Organizational roles referenced for complaints and grievance
    escalation.

## **6. Open Questions / Ambiguities / Contradictions**

1.  **Fee policy conflict**

    -   One section says **\$75** for no 24-hour notice and/or being
        **more than 15 minutes late**.

    -   Another section says **\$75 no-show** only if patient does not
        reschedule within the same calendar week, plus **\$45 late
        cancellation fee** for under-24-hour cancellation.

2.  **Credit card on file**

    -   Policy requires secure storage, but no details are provided on
        authorization text completion, card-token retention, charge
        timing, receipts, dispute process, or PCI controls.

3.  **Revocation handling**

    -   Several sections allow written revocation, but the document does
        not define intake method, effective date rules, or audit
        requirements.

4.  **Authorized contacts**

    -   Three contact slots are shown, but the minimum/maximum supported
        contacts is not explicitly defined.

5.  **Privacy Officer / Director of Rehabilitation contact details**

    -   Roles are referenced, but actual names/contact values are
        missing or placeholders such as "Facility Phone."

6.  **Specialty consent applicability**

    -   Dry needling and pelvic floor consents appear as included forms,
        but source does not define whether they are always shown or
        conditionally presented.

7.  **Financial hardship qualification**

    -   Terms are described, but criteria for who qualifies are not
        defined.

8.  **Accepted payment methods**

    -   Financial hardship section includes placeholders and partial
        instructions rather than a finalized accepted-methods list.

9.  **Communication opt-out wording**

    -   "You may opt out \... with written consent" is unusual phrasing
        and could mean written notice/request, but exact intent is not
        clarified.

10. **Document metadata gaps**

-   No page numbers, revision date, document owner, version number, or
    effective date were visible in the extracted text.

## **7. Supporting Notes (Non-Functional / Contextual)**

-   The document is **compliance-heavy** and would materially affect
    privacy, legal acknowledgment, communications consent,
    specialty-treatment consent, and billing-policy features.

-   It implies the system should preserve **auditability of consent and
    revocation**, though explicit audit requirements are not defined in
    this file.

-   It implies handling of **sensitive patient data**, especially PHI
    and potentially intimate clinical exam consent, so privacy/security
    controls are contextually important.

-   The content is suitable for **patient intake/onboarding workflows**
    and not limited to clinician-side use.

-   Multiple sections are written as **templated legal forms**,
    suggesting the downstream system may need templating/version
    control, though that is inferred rather than explicitly required.

## **8. Audit Preparation Summary**

A downstream audit agent should validate these areas first:

-   Whether the codebase supports **capture, storage, and retrieval of
    discrete consent records** for treatment, HIPAA acknowledgment, PHI
    release, communications, digital media, and specialty procedures.

-   Whether there is a mechanism for **written revocation tracking** and
    whether revocations affect future actions only where required.

-   Whether the system supports **authorized contact management** with
    name/phone/relationship fields.

-   Whether **billing and scheduling rules** match the source policy,
    especially the contradictory no-show/late-cancel fee language.

-   Whether **Medicare-specific timing/eligibility policies** are
    represented anywhere in workflows or validations.

-   Whether there is any implementation for **card-on-file
    authorization** and secure payment handling aligned with the policy.

-   Whether **grievance and complaint pathways** are surfaced anywhere
    and whether placeholder contacts remain unresolved.

-   Whether specialty consents are **conditionally triggered** by
    clinical context or missing entirely.

Key risks/unknowns:

-   Conflicting cancellation fee language.

-   Missing operational details for revocation, grievance contacts, and
    hardship eligibility.

-   Unclear separation between always-required and
    conditionally-required consent forms.

-   Potential mismatch between legal policy text and current
    app/payment/scheduling implementation.

**Source file:** Policies and consent forms.docx.
