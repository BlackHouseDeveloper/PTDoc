# Plan 08 review stack

The owner approved splitting the combined Plan 08 completion on 2026-11-05. PR 163 stays focused on encounter-bound Evaluation/Re-evaluation intake and its provider migrations. The additional review units build on it in the following order; each PR targets the preceding branch so its diff contains only its own changes.

| Order | Proposed branch | Scope | Prior PR relationship |
| --- | --- | --- | --- |
| Base | `codex/plan-08-note-type-intake` | Existing encounter association, scoped access, concurrency and Evaluation flow | PR 163 stays intact |
| 1 | `codex/plan08-questionnaire-foundations` | Daily questionnaire and Progress activities, including history safeguards and verified review fixes | Incorporates PRs 176 and 187; do not merge duplicate foundations |
| 2 | `codex/plan08-visit-intake` | Daily/Progress/Discharge Subjective intake, issued choices, review/consent and matching-note answers | Completes the visit-flow portion of Plan 08 |
| 3 | `codex/plan08-outcome-scoring` | Versioned contracts, clinical definitions, source manifests and scoring | Incorporates PR 180 plus approved patient source/scoring decisions |
| 4 | `codex/plan08-patient-outcomes` | Patient selection and save/resume, server enforcement, locked note/PDF evidence and styling | Covers patient portions of Plans 24/25; clinician instruments remain separate |

Keep each feature's tests with its implementation. The large generated migration metadata remains with PR 163. The clinical questionnaire JSON stays readable and source-pinned in layer 3; it is not minified or hidden to reduce the diff count.

No new schema migration is required by the four completion layers. Layers 1–2 do not activate patient outcome collection. Layer 3 exposes the scoring service to server composition but adds no patient collection surface. Layer 4 connects the full outcome workflow and its evidence protections together.

## Verification and CI

Use the per-layer evidence below and each PR's description. The prior combined implementation's 2,455-case CoreCi result and browser/PDF evidence are historical context, not proof of an intermediate layer. Release test-project builds include API, Web and the shared UI; native builds and live SQL Server/PostgreSQL checks remain separate release gates.

GitHub native stacks evaluate branch protections and pull-request workflow filters against the stack base (`main`), so Core CI, provider CI, Secret Policy and release gates apply to every layer. Verify checks against each current head; publication alone does not establish a passing result. Native stacks require fully linear history between branches: use a cascading rebase when the base changes, and obtain explicit authorization before updating published history. Ordinary dependent PRs outside a native stack follow their immediate-base workflow filters instead. See [GitHub stacked-PR requirements](https://docs.github.com/en/pull-requests/reference/stacked-pull-requests). No deployment is part of this split.

## Existing PR reconciliation

PRs 176, 187 and 180 remain open until the replacement layer is accepted; their implementation and actionable review fixes are retained in this stack. Do not merge them again after their replacement lands. PRs 181/182 must reconcile their patient workflow acceptance with layer 4; their clinician-only, prior-result comparison and correction scope is not silently marked complete. Plan 31 retains separate clinician recommendation-vocabulary work. No existing PR or review thread is closed, replied to or resolved by preparing the split.

## Layer evidence

### Layer 1 — questionnaire/activity foundations

- Release test-project build: PASS, zero warnings/errors.
- Full CoreCi discovery/run: 2,379 passed out of 2,380; the old Progress catalog test fixture omitted its knee body-region context. Retain the already-corrected fixture from the combined implementation, without changing production behavior.
- Final affected-suite rerun: **256/256 PASS**, including the previously failing catalog case, Daily/Progress, encounter HTTP validation, workspace, note UI and PDF coverage. No test assertion or timeout was weakened.
- Original feedback: PR 176 has none; PR 187 has three unique actionable findings. The signature predicate, first-save baseline and HTTP 422 fixes and regressions are included. Review-summary bullets duplicate those findings. Threads remain unchanged.

### Layer 2 — visit-specific intake

- Release test-project build: PASS, zero warnings/errors.
- Intake, note-workspace service/page and Progress activity regressions: **581/581 PASS**, zero skipped. Includes API/OTP scoping, short-visit validation, partial saves, consent, matching-note copying and keyboard model stability.
- No `PatientOutcomes`, outcome persistence/projection or patient measure component is introduced in this layer. Clinical outcome collection remains unavailable.

### Layer 3 — outcome definitions and scoring

- Release test-project build: PASS, zero warnings/errors.
- Outcome regression suite: **151/151 PASS**, zero skipped; includes versioned activation, malformed definitions, source arithmetic, missing/N/A policies and corrected HOOS/FAAM subscales.
- All thirteen definitions preserve the combined implementation's exact catalog hash: `af058df5455a52c928847d0d5b53c866859049c9948554f74356e8b5e63266ca`.
- PR 180's one unique actionable null-item/option finding and its regression are retained. The review-summary bullet duplicates the same finding; the thread remains unchanged.
- No intake collection or note/PDF projection hook is added in this layer.

### Layer 4 — complete patient outcome workflow

- Release test-project build with `--no-incremental`: PASS, zero warnings/errors. Restoring backup timestamps initially reused intermediate assemblies; refreshing those timestamps and rebuilding the graph removed that build-artifact issue without source changes.
- Dedicated Web Release build: PASS, zero warnings/errors.
- Full CoreCi suite on the final reconstructed source: **2,455/2,455 PASS**, zero skipped, 4m32s.
- All 69 changed application/test files match the preserved combined implementation byte-for-byte; no unexpected source files were introduced. The catalog hash and prior styling remain unchanged.
- `git diff --check` and the tracked appsettings secret-policy scan pass. Prior headed browser and PDF evidence remains applicable to the unchanged UI/source, but was not rerun solely for the Git split.

## Publication status

Four incremental Git trees, replayable patches and PR descriptions are prepared locally. No implementation commit or branch push has been made by this split; PR 163 and the older PRs remain unchanged. Before implementation commits, `AGENTS.md` requires owner confirmation that the relevant build/tests passed or explicit permission to commit without that confirmation. Remote formatter/provider/release gates and native acceptance are not claimed by these local checks.
