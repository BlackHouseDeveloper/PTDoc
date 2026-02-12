# PTDoc (PFPT)

## AI Prompt Specification -- Assessment & Plan of Care

**Derived From:** Master FSD, Backend TDD, Blazor Mapping\
**Audience:** AI/Backend Engineers, Clinical SMEs, QA\
**Scope:** SOAP Assessment and Plan generation only

------------------------------------------------------------------------

## 1. Purpose

This document defines exact AI prompt behavior, structure, constraints,
and expected output quality for PFPT. It ensures AI-generated clinical
text is:

-   Clinically accurate\
-   Non-hallucinatory\
-   Editable but high-quality\
-   Consistent with real-world PT documentation\
-   Defensible for audit and compliance

The examples provided by the client are treated as gold-standard output
style references.

------------------------------------------------------------------------

## 2. AI Operating Principles (Non-Negotiable)

-   Stateless generation -- no memory across sessions\
-   No training on patient data\
-   No fabrication of diagnoses, values, timelines, or imaging\
-   No auto-finalization -- clinician must accept\
-   Structured in → narrative out\
-   Clinical tone, not templated

------------------------------------------------------------------------

## 3. Input Contract (What AI Receives)

AI is provided a sanitized, structured DTO only.

### 3.1 Assessment Input DTO (Simplified)

-   Patient demographics (age, sex only if relevant)\
-   Surgical status / diagnosis (if selected)\
-   Body regions involved\
-   Subjective complaints (checkbox + short phrases)\
-   Objective findings:
    -   ROM deficits\
    -   Strength deficits\
    -   Pain levels\
    -   Outcome measures\
-   Functional limitations\
-   Visit type (Eval / Daily / PN / DC)

⚠️ AI must not infer anything not explicitly present.

------------------------------------------------------------------------

## 4. Output Contract -- ASSESSMENT

### 4.1 Structural Requirements

Assessment output must be prose, 1--3 paragraphs, and include:

-   Clinical synthesis ("Patient presents with...")\
-   Key impairments (ROM, strength, mobility, neuromuscular control,
    pain)\
-   Functional impact (ADLs, IADLs, work, sleep, gait, tolerance)\
-   Medical necessity statement (skilled PT required)

### 4.2 Style Rules (Derived from Examples)

-   Long-form, natural clinical reasoning\
-   Uses conditional language ("consistent with", "suggestive of")\
-   Integrates multiple systems when applicable\
-   Avoids bullet points\
-   Avoids repetition\
-   Mirrors how an experienced PT writes

------------------------------------------------------------------------

## 5. Output Contract -- PLAN OF CARE

### 5.1 Required Components

Plan output must include:

-   Treatment focus areas (mobility, strength, coordination,
    desensitization, etc.)\
-   Education provided\
-   Rationale for skilled intervention\
-   Progression intent (not exact exercises)

### 5.2 Explicit Constraints

-   Do NOT list specific exercise names unless provided\
-   Do NOT invent frequencies/durations\
-   Do NOT contradict protocol restrictions

------------------------------------------------------------------------

## 6. Gold-Standard Output Patterns

### 6.1 Complex Multisystem Example (Pelvic / Neuro / Post-Surgical)

**Pattern:**

-   Begins with integrated diagnosis language\
-   Connects symptoms to coordination dysfunction\
-   Explicitly links impairments to quality-of-life domains\
-   Clear justification for staged treatment approach

Example pattern:

> "Patient presents with findings consistent with post-surgical pelvic
> pain with probable neural involvement and pelvic floor dysfunction...
> impacting sitting tolerance, sleep hygiene, clothing selection, and
> driving tolerance."

------------------------------------------------------------------------

### 6.2 Orthopedic / Radicular Example

**Pattern:**

-   Identifies mechanical contributors\
-   Links posture, strength, and mobility\
-   Ends with medical necessity justification

Example pattern:

> "Assessment reveals decreased spinal and lower extremity mobility,
> impaired core coordination, and postural asymmetry contributing to
> pain with weight-bearing activities."

------------------------------------------------------------------------

### 6.3 Post-Operative Example

**Pattern:**

-   References surgical timeline\
-   Respects protocol limitations\
-   Emphasizes ADL/IADL restriction

Example pattern:

> "Patient remains significantly limited in functional mobility and ADLs
> per post-operative protocol and will benefit from skilled PT to safely
> progress toward prior level of function."

------------------------------------------------------------------------

### 6.4 Daily Note / Progress Example

**Pattern:**

-   Describes response to treatment\
-   Explains why progression is or is not appropriate\
-   Reinforces continued skilled need

Example pattern:

> "Patient demonstrates short-term symptomatic improvement; however,
> ongoing strength deficits and soft tissue restrictions continue to
> limit functional mobility."

------------------------------------------------------------------------

## 7. Prompt Template -- ASSESSMENT (Internal)

    You are a licensed physical therapist writing the ASSESSMENT section of a SOAP note.

    Using ONLY the provided data:

    - Synthesize subjective complaints and objective findings
    - Describe impairments and functional limitations
    - Explain why skilled PT is medically necessary

    Do NOT fabricate diagnoses, measurements, or timelines.

    Write in professional clinical prose.

------------------------------------------------------------------------

## 8. Prompt Template -- PLAN (Internal)

    You are a licensed physical therapist writing the PLAN section of a SOAP note.

    Using ONLY the provided data:

    - Describe treatment focus areas
    - Reference education provided
    - Justify need for skilled intervention
    - Indicate progression intent without listing specific exercises

    Do NOT include frequencies, durations, or invented protocols.

------------------------------------------------------------------------

## 9. QA Acceptance Criteria

An AI-generated Assessment/Plan is REJECTED if:

-   It introduces new diagnoses\
-   It invents imaging or surgical details\
-   It reads templated or repetitive\
-   It omits functional impact\
-   It lacks a medical necessity statement

------------------------------------------------------------------------

## 10. Future Extensions

-   Specialty prompt variants (Pelvic, Neuro, Post-Op)\
-   Discharge summary prompts\
-   Addendum prompts

------------------------------------------------------------------------

This AI Prompt Specification is binding and governs all AI-generated
clinical text in PFPT.
