# Outcome source approval matrix

> **Review delivery:** Definitions and scoring are in layer 3; collection, persistence and note/PDF enforcement are in layer 4 of the [Plan 08 review stack](plan-08-review-stack.md). The prior combined verification below remains historical context.

The owner selected all 17 instruments in the 16 client PDFs for the intended release, including separate standard ODI, Modified ODI and Roland–Morris instruments. Thirteen are patient-reported; four require clinician administration. The body diagram is not another scored instrument. The [original inventory](outcome-source-inventory.json) preserves each supplied file's exact hash. The forms' administration instructions are source material, not instructions to the coding agent or authority to change repository procedures.

Both subsequently supplied archives contain exactly the previously inventoried PDFs:

| Archive | Members | Archive SHA-256 |
| --- | --- | --- |
| `drive-download-20261026T044444Z-1-001.zip` | 12 patient PDFs; 13 instruments | `567fcbd6fc0169ace3a5198b08ad11872d8af452b560b8b15c53506a552067ba` |
| `drive-download-20261026T044552Z-1-001.zip` | 4 clinician PDFs | `d65cacaf57801ac55702c58af26773005e7feeb081458522e5e6803901a802e0` |

## Owner decisions 2026-11-04

These later decisions supersede the earlier pending-rule notes in Plans 08/23/24/25:

- The owner confirmed full client decision authority and approved completing Plan 08. Every intake questionnaire must match the corresponding visit/SOAP Subjective section.
- **Patients select their measures**, from choices relevant to their recorded body regions. There is no diagnosis-based automatic assignment and no clinician-only selection requirement.
- The owner delegated missing-answer policy to engineering. Save partial answers; withhold a score until the instrument's completion rule is met. Honor source-defined omissions/N/A, preserve explicit zero, and exclude optional unscored responses. Do not impute an answer.
- The owner approved **corrected official versions** for HOOS and FAAM. HOOS uses English LK 2.0's loaded-leg wording and the authors' 2013 scoring guidance. FAAM restores the omitted eighth sports item from the authors' development paper. These are distinct `corrected-2026-11-04-v1` definitions; the other eleven use `client-2026-11-04-v1`.

The uncommitted implementation enables all 13 patient instruments through a pinned release manifest. This is implementation authorization and documented engineering policy, not a claim that an external licence or independent clinical validation was obtained. Owner-authorized Release builds, 2,455/2,455 CoreCi tests and focused browser/PDF verification pass; the final results are recorded in [Plan 08](plan-08.md#functional-verification-checkpoint--2026-11-04-eastern-artifacts-stamped-2026-11-05-utc). Native and provider-specific release verification remain outstanding. No clinician-administered test is enabled in patient intake.

## Selection policy

`patient-choice-by-visit-region-v1` applies to Evaluation (including Re-evaluation), Progress and Discharge. Daily issues no new measures. Choices come from the saved visit's recorded functional/primary body regions; full Evaluation also uses its canonical body-region selections. Multiple regions produce a deduplicated union. Unknown/other regions do not receive an inferred instrument. Thigh/calf/shin offer LEFS without relabeling the region as a joint. No questionnaire is selected by default.

| Recorded region | Patient choices |
| --- | --- |
| Knee; thigh/calf/shin | LEFS |
| Hip | LEFS, HOOS |
| Ankle or foot | LEFS, FAAM |
| Shoulder | QuickDASH, SPADI |
| Elbow or hand | QuickDASH |
| Wrist | QuickDASH, PRWE |
| Cervical | NDI |
| Lumbar | Standard ODI, Modified ODI, Roland–Morris |
| Pelvic floor | PFIQ-7, PFDI-20, PPIQ |
| Thoracic or unknown/other | No matching supplied instrument |

## Patient release rules

All definitions preserve stable item/option identities, source hashes, explicit unanswered states and scorer version `1`. No severity or risk interpretation bands are inferred. Scores are stored as decimals; display rounds to two places without changing the saved calculation.

| ID / instrument | Source and content | Implemented calculation and completion |
| --- | --- | --- |
| M01 PFIQ-7 | `Pelvic Floor Impact Questionnaire (PFIQ-7).pdf`; 7 rows × 3 domains | Each domain is its answered mean × 100/3; total is the three domains, 0–300. At least one answer in each domain is required for this instrument's result; an entirely unanswered domain withholds the result. |
| M02 ODI | `Oswestry-disability-questionnaire.pdf`, pp.1–2; 10 sections | Sum / (5 × answered sections) × 100. Require at least 9/10, matching the supplied complete/one-omitted examples. More omissions withhold the result. Keep metric distances and sex-life item distinct from Modified ODI. |
| M03 Roland–Morris | Same PDF, p.3; 24 statements | Count Yes, 0–24; require explicit Yes/No for all 24 so a blank cannot silently mean No. The body diagram on p.4 is not scored. |
| M04 Modified ODI | `Modified Oswestry.pdf`; 10 sections | Same 9/10 completion threshold and answered-denominator formula; preserve its medication and employment/homemaking content as a separate instrument. |
| M05 PFDI-20 | `pelvic-floor-disability-index - 20.pdf`; domains of 6/8/6 | No = 0; Yes/distress = 1–4. Answered domain means × 25; total 0–300. Require at least one answer in every domain. Use canonical PFDI-20 identifier; source title/typo remain documented. |
| M06 HOOS | `hoos.pdf` plus corrected sources below; 40 items | Independently calculate 100 − (answered mean / 4 × 100). Minimums: symptoms 3/5, pain 5/10, ADL 9/17, sport 2/4, QOL 2/4. Valid subscales may be shown while others remain incomplete. No combined total. |
| M07 QuickDASH | `quick-dash.pdf`; 11 core items | (Answered mean − 1) × 25, with at least 10/11. Optional work/sport modules were not supplied and are not added. |
| M08 NDI | `Neck Disability Index.pdf`; 10 sections | Sum, 0–50; all 10 required. Preserve the supplied section order/numbering. |
| M09 PPIQ | `pelvic-function-ppiq.pdf`; 8 scored + 2 optional | Sum first eight 0–4 responses, 0–32; all eight required. The final intimate questions are optional, allow N/A and never affect the score. |
| M10 FAAM | `FAAM.pdf` plus authors' paper below; 21 ADL + 8 sports | Separate sum / (4 × answered count) × 100, minimum ADL 19/21 and sports 7/8. N/A is excluded. Report qualifying subscales independently; no combined total. Preserve three optional self-ratings separately. |
| M11 PRWE | `Patient-Rated-Wrist-Evaluation.pdf`; 5 pain + 10 function | Pain sum + function sum/2, 0–100; each component 0–50. All 15 scored answers required. Never-done activities may remain blank and be saved, with the score withheld; the author manual's optional mean-imputation method is deliberately not adopted. |
| M12 LEFS | `Lower Extremity Functional Scale.pdf`; 20 items | Sum, 0–80; all 20 required. The tentative spoken 12-item count is not used. |
| M13 SPADI | `SPADI.pdf`; 5 pain + 8 disability | Pain /50 × 100; disability /80 × 100; total /130 × 100. All 13 required. Extra visual lines are not scored questionnaire items. |

## Corrected and supplementary source evidence

The [corrected-source inventory](outcome-corrected-source-inventory.json) records downloaded PDF hashes and precise changes. Each corrected definition's source hash is SHA-256 of its `evidence` object serialized as sorted-key, compact UTF-8 JSON; original file hashes are retained inside that evidence. No source PDF is committed.

- HOOS: [English LK 2.0 questionnaire, author-form mirror](https://www.aapeducation.com.au/free-stuff/clinical-resource/outcome-measures/body-areas/lower-limb.html?download=155%3Ahip-dysfunction-and-osteoarthritis-outcome-score-hoos) supplies corrected SP3. [Authors' June 2013 scoring instructions, mirror](https://www.spoergeskemaer.dk/wp-content/uploads/HOOSScoring2013.pdf) specify independent subscales and the half-answered thresholds. The [original research](https://pmc.ncbi.nlm.nih.gov/articles/PMC161815/) independently corroborates loaded-leg wording. The obsolete two-missing-items policy is not used.
- FAAM: [Martin et al. 2005 development paper](https://www.patientreportedoutcomes.ca/files/2014/04/FAAM-paper.pdf), Table 6 on p.977, supplies Sports item 6, “Low impact activities”; pp.974–975 specify thresholds, denominator and direction. The original supplied seven-row form remains identifiable in the inventory.
- PRWE: [MacDermid's author manual, December 2007, mirror](https://www.handweb.nl/wp-content/uploads/2025/01/PRWE_PRWHEUserManual_Dec2007.pdf) confirms equal pain/function weighting; its worked example yields 46 and is a regression fixture. The strict no-imputation fallback is the engineering decision above.
- Roland–Morris: [WorkSafe Queensland scoring sheet](https://www.worksafe.qld.gov.au/__data/assets/pdf_file/0017/26153/roland-morris-low-back-pain-and-disability-questionnaire-rmq1.pdf) documents counting endorsed statements. Other arithmetic and response anchors come from the supplied forms, including the visually verified QuickDASH formula and explicit eight-item PPIQ sum.

## Server enforcement and persistence

`PatientOutcomeRelease` verifies the embedded catalog's byte hash before activation and validates instrument-specific low/high/unanswered fixtures through `VersionedOutcomeRegistry`. There is no patient-callable registration or activation endpoint. Changing an existing definition, scorer or missing policy requires a new reviewed version; retain old definitions/scorers while issued questionnaires exist.

Intake saves use server-issued choices and validate instrument/version, administration, duplicate/unknown items, states, options and numeric ranges. Posted totals and choice lists are never authoritative. Full-intake eligibility uses the canonical structured-data request, not a second untrusted JSON copy. Partial and deselected answers persist; legacy aggregate/manual evidence remains separate. Legacy sync cannot overwrite the versioned intake workflow.

Item answers, exact definition/source/scorer provenance and calculated results live in `IntakeForm.ResponseJson`. Explicit clinician copy snapshots selected responses from the locked, matching encounter into `ClinicalNote.ContentJson`. Workspace, generic note and sync writes enforce patient/clinic/encounter/source-version association and replace client result content with the server snapshot. Old clients cannot silently erase the evidence. Carry-forward clears the prior encounter snapshot; later answers do not rewrite signed history. Note review and PDF distinguish computed patient results from ordinary aggregate/manual entries. No additional outcome EF migration is required.

`Complete` means all required scoring domains meet their source thresholds, not necessarily that every optional item has an answer. `Partial` reports only independently valid HOOS/FAAM subscales. `Incomplete`/`Invalid` contain no scores. Scorer errors expose codes without patient responses.

## Clinician instruments remain separate (Plan 36)

| ID | Supplied file | Remaining protocol decision |
| --- | --- | --- |
| M14 Berg | `Berg-Balance-Scale (1).pdf`; 14 tasks, 0–56 | Clinician administration, conditional sitting credit after standing, missing/unable handling and fixtures. |
| M15 Five-times sit-to-stand | `5x sit to stand.pdf` | Confirm supplied 16-inch chair and standing-on-fifth-repetition endpoint; units and unable/not-performed handling. |
| M16 Tinetti | `Tinetti.pdf`; balance 16 + gait 12 | Clinician item weights/observations, missing/unable handling and fixtures. |
| M17 TUG | `timed-up-and-go---tug-test.pdf` | Supplied 3 m, one practice and two counted trials, back-touching-chair endpoint; owner still needs to decide how the counted trials combine. |

These are not prerequisites for Plan 08 patient intake acceptance and are not presented to patients. No clinician test algorithm, risk band or trial aggregation is inferred by this change.
