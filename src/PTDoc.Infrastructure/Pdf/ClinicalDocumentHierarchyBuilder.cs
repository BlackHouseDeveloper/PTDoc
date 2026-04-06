using System.Globalization;
using System.Text;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Content;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Pdf;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Pdf;

public sealed class ClinicalDocumentHierarchyBuilder : IClinicalDocumentHierarchyBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ClinicalDocumentHierarchy Build(NoteExportDto noteData)
    {
        var context = ExportDocumentContext.Create(noteData);
        var hierarchy = noteData.NoteType switch
        {
            NoteType.Evaluation => BuildInitialEvaluation(noteData, context),
            NoteType.ProgressNote => BuildProgressNote(noteData, context),
            NoteType.Daily => BuildDailyNote(noteData, context),
            NoteType.Discharge => BuildDischargeSummary(noteData, context),
            _ => BuildFallbackDocument(noteData, context)
        };
        return SanitizeHierarchy(hierarchy);
    }

    private static ClinicalDocumentHierarchy BuildInitialEvaluation(NoteExportDto noteData, ExportDocumentContext context)
    {
        var evaluation = context.EvaluationContent;
        var workspace = context.WorkspacePayload;

        return CreateDocument(
            noteData.NoteType,
            "Physical Therapy Initial Evaluation",
            BuildHeaderSection(noteData, "Physical Therapy Initial Evaluation"),
            Section("Subjective Patient Report", ClinicalDocumentSourceKind.Note,
                Paragraph("Narrative Summary", FirstNonEmpty(
                    workspace?.Subjective.NarrativeContext.HistoryOfPresentIllness,
                    evaluation?.SubjectiveComplaints)),
                Field("Onset Injury Date", FormatDate(FirstNonNull(
                    workspace?.Subjective.NarrativeContext.DateOfInjury,
                    workspace?.Subjective.OnsetDate))),
                Paragraph("Primary Issue / Problem", BuildProblemSummary(workspace)),
                Paragraph("Difficulty Experienced", FirstNonEmpty(
                    workspace?.Subjective.NarrativeContext.DifficultyExperienced,
                    BuildFunctionalLimitationSummary(workspace),
                    evaluation?.FunctionalLimitations)),
                Paragraph("Pain Rating", BuildPainSummary(workspace)),
                Paragraph("Pain Description", BuildPainDescription(workspace)),
                Paragraph("Medical History / Comorbidities", FirstNonEmpty(
                    JoinSet(workspace?.Subjective.Comorbidities),
                    evaluation?.MedicalHistory)),
                TodoNode(
                    "Surgical history as structured evaluation content",
                    "Clinical note",
                    "Update NoteWorkspaceV2Payload evaluation export projection to include surgical-history fields"),
                Paragraph("Diagnostic Testing / Imaging", BuildImagingSummary(workspace)),
                Paragraph("Patient Reported Goals", FirstNonEmpty(
                    workspace?.Assessment.PatientPersonalGoals,
                    BuildLegacyJsonValue(noteData.ContentJson, "patientReportedGoals"))),
                Paragraph("Previous Treatment / Reported Effectiveness", BuildPriorTreatmentSummary(workspace))),
            BuildEvaluationSection(noteData, context),
            Section("Treatment", ClinicalDocumentSourceKind.Note,
                TableNode("Treatment Table", ClinicalDocumentSourceKind.Note,
                    columns:
                    [
                        Column("name", "Name"),
                        Column("details", "Details"),
                        Column("performed", "Performed")
                    ]),
                TodoNode(
                    "Structured performed-treatment rows for evaluation exports",
                    "Clinical note",
                    "Update NoteWorkspaceV2Payload export projection to expose treatment rows and performed-state details"),
                TodoNode(
                    "Evaluation treatment comments as a dedicated export field",
                    "Clinical note",
                    "Update evaluation export projection to include treatment comment text")),
            Section("Clinical Assessment", ClinicalDocumentSourceKind.Note,
                Paragraph("Narrative", FirstNonEmpty(
                    workspace?.Assessment.AssessmentNarrative,
                    evaluation?.Assessment)),
                Paragraph("Skilled PT Justification", workspace?.Assessment.SkilledPtJustification)),
            BuildDiagnosisSection(context),
            Section("Contraindications To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Contraindications text",
                    "Clinical note",
                    "Update evaluation/progress export projection to include contraindications")),
            Section("Precautions To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Precautions text",
                    "Clinical note",
                    "Update evaluation/progress export projection to include precautions")),
            Section("Rehab Potential", ClinicalDocumentSourceKind.Note,
                Field("Rehab Potential", FirstNonEmpty(
                    workspace?.Assessment.OverallPrognosis,
                    evaluation?.Prognosis))),
            BuildGoalsSection(context),
            Section("Plan Of Care", ClinicalDocumentSourceKind.Note,
                Paragraph("Treatment Plan", BuildTreatmentPlanSummary(workspace, evaluation)),
                Paragraph("Plan Narrative", FirstNonEmpty(
                    workspace?.Plan.PlanOfCareNarrative,
                    BuildLegacyPlanOfCareSummary(evaluation?.PlanOfCare))),
                Field("Frequency", FirstNonEmpty(
                    workspace?.Plan.ComputedPlanOfCare.FrequencyDisplay,
                    evaluation?.PlanOfCare.FrequencyDuration)),
                Field("Duration", FirstNonEmpty(
                    workspace?.Plan.ComputedPlanOfCare.DurationDisplay,
                    ExtractDurationFromFrequency(evaluation?.PlanOfCare.FrequencyDuration)))),
            noteData.IncludeSignatureBlock ? BuildCertificationSection(noteData) : null,
            noteData.IncludeMedicareCompliance ? BuildChargesSection(noteData, context.CptCodes) : null);
    }

    private static ClinicalDocumentHierarchy BuildProgressNote(NoteExportDto noteData, ExportDocumentContext context)
    {
        var workspace = context.WorkspacePayload;
        var progress = context.ProgressContent;

        return CreateDocument(
            noteData.NoteType,
            "Physical Therapy Progress Note",
            BuildHeaderSection(noteData, "Physical Therapy Progress Note"),
            Section("Subjective", ClinicalDocumentSourceKind.Note,
                Paragraph("Functional Changes Narrative", FirstNonEmpty(
                    BuildProgressQuestionnaireSummary(workspace),
                    progress?.ProgressDescription,
                    BuildLegacyJsonValue(noteData.ContentJson, "subjective")))),
            BuildEvaluationSection(noteData, context),
            Section("Treatment", ClinicalDocumentSourceKind.Note,
                TableNode("Treatment Table", ClinicalDocumentSourceKind.Note,
                    columns:
                    [
                        Column("name", "Name"),
                        Column("details", "Details"),
                        Column("performed", "Performed")
                    ]),
                TodoNode(
                    "Structured performed-treatment rows for progress-note exports",
                    "Clinical note",
                    "Update NoteWorkspaceV2Payload progress export projection to expose treatment rows and performed-state details"),
                TodoNode(
                    "Progress-note treatment comments as a dedicated export field",
                    "Clinical note",
                    "Update progress export projection to include treatment comment text")),
            Section("Clinical Assessment", ClinicalDocumentSourceKind.Note,
                Paragraph("Narrative", FirstNonEmpty(
                    workspace?.Assessment.AssessmentNarrative,
                    progress?.ProgressDescription)),
                Paragraph("Comparison To Initial Evaluation", progress?.ComparisonToInitialEval),
                Paragraph("Justification For Continued Care", progress?.JustificationForContinuedCare)),
            BuildDiagnosisSection(context),
            Section("Contraindications To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Contraindications text",
                    "Clinical note",
                    "Update evaluation/progress export projection to include contraindications")),
            Section("Precautions To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Precautions text",
                    "Clinical note",
                    "Update evaluation/progress export projection to include precautions")),
            Section("Rehab Potential", ClinicalDocumentSourceKind.Note,
                Field("Rehab Potential", workspace?.Assessment.OverallPrognosis)),
            BuildGoalsSection(context),
            Section("Plan Of Care Progress", ClinicalDocumentSourceKind.Note,
                Paragraph("Plan Of Care Progress Narrative", FirstNonEmpty(
                    workspace?.Plan.PlanOfCareNarrative,
                    workspace?.Plan.ClinicalSummary,
                    progress?.PlanForNextPeriod)),
                Paragraph("Plan For Next Visit", FirstNonEmpty(
                    progress?.PlanForNextPeriod,
                    workspace?.Plan.FollowUpInstructions))),
            noteData.IncludeSignatureBlock ? BuildCertificationSection(noteData) : null,
            noteData.IncludeMedicareCompliance ? BuildChargesSection(noteData, context.CptCodes) : null);
    }

    private static ClinicalDocumentHierarchy BuildDailyNote(NoteExportDto noteData, ExportDocumentContext context)
    {
        var daily = context.DailyNoteContent;

        return CreateDocument(
            noteData.NoteType,
            "Physical Therapy Daily Note",
            BuildHeaderSection(noteData, "Physical Therapy Daily Note"),
            Section("Subjective", ClinicalDocumentSourceKind.Note,
                Paragraph("Functional Changes Narrative", BuildDailySubjectiveSummary(daily))),
            Section("Treatment", ClinicalDocumentSourceKind.Note,
                BuildDailyTreatmentTable(daily, context.CptCodes),
                TodoNode(
                    "Row-level treatment groups with row-level performed/minutes output",
                    "Clinical note",
                    "Update DailyNoteContentDto export projection to expose grouped treatment rows for manual therapy, exercise, education, and HEP"),
                Paragraph("Additional Comments", daily?.AssessmentComments)),
            Section("Assessment", ClinicalDocumentSourceKind.Note,
                Field("Functional Goal Addressed", JoinList(daily?.FocusedActivities)),
                Field("VC", BuildCueSummary(daily)),
                Field("Assistance", BuildAssistanceSummary(daily)),
                Field("Visual Instruction", BuildVisualInstructionSummary(daily)),
                Field("Education", BuildEducationSummary(daily)),
                Field("Response", daily?.TreatmentResponse.HasValue == true
                    ? ((TreatmentResponse)daily.TreatmentResponse.Value).ToString()
                    : string.Empty),
                Paragraph("Clinical Assessment Narrative", FirstNonEmpty(
                    daily?.AssessmentNarrative,
                    daily?.ClinicalInterpretation))),
            Section("Therapy Diagnosis", ClinicalDocumentSourceKind.Note,
                BuildDiagnosisTableNode(context)),
            Section("Contraindications To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Contraindications text",
                    "Clinical note",
                    "Update DailyNoteContentDto export projection to include contraindications")),
            Section("Precautions To Therapy", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Precautions text",
                    "Clinical note",
                    "Update DailyNoteContentDto export projection to include precautions")),
            Section("Rehab Potential", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Rehab potential displayed on daily note",
                    "Clinical note history",
                    "Add carry-forward projection from the latest applicable evaluation/progress note into the daily-note export model")),
            Section("Plan (Daily Note)", ClinicalDocumentSourceKind.Note,
                Paragraph("Plan", BuildDailyPlanSummary(daily))),
            Section("Goals", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Goal table with goal type, description, and status",
                    "Clinical note history",
                    "Add goal carry-forward projection into the daily-note export model")),
            noteData.IncludeSignatureBlock ? BuildClinicianSignatureSection(noteData) : null,
            noteData.IncludeMedicareCompliance ? BuildChargesSection(noteData, context.CptCodes) : null);
    }

    private static ClinicalDocumentHierarchy BuildDischargeSummary(NoteExportDto noteData, ExportDocumentContext context)
    {
        var workspace = context.WorkspacePayload;
        var discharge = context.DischargeContent;

        return CreateDocument(
            noteData.NoteType,
            "Physical Therapy Discharge Summary",
            BuildHeaderSection(noteData, "Physical Therapy Discharge Summary"),
            Section("Subjective", ClinicalDocumentSourceKind.Note,
                Paragraph("Final Functional Changes Narrative", FirstNonEmpty(
                    BuildLegacyJsonValue(noteData.ContentJson, "subjective"),
                    discharge?.FunctionalStatusAtDischarge,
                    discharge?.ProgressSummary,
                    workspace?.Plan.ClinicalSummary))),
            BuildDischargeEvaluationSection(noteData, context),
            Section("Assessment", ClinicalDocumentSourceKind.Note,
                TodoNode(
                    "Structured discharge summary lines for functional goal addressed, cues, assistance, education, and response",
                    "Clinical note",
                    "Update discharge export projection to expose structured discharge-assessment fields"),
                Paragraph("Clinical Assessment Narrative", FirstNonEmpty(
                    workspace?.Plan.ClinicalSummary,
                    discharge?.ProgressSummary,
                    workspace?.Plan.DischargePlanningNotes))),
            BuildDiagnosisSection(context),
            Section("Rehab Potential", ClinicalDocumentSourceKind.Note,
                Field("Rehab Potential", workspace?.Assessment.OverallPrognosis)),
            Section("Treatment", ClinicalDocumentSourceKind.Note,
                TableNode("Treatment Table", ClinicalDocumentSourceKind.Note,
                    columns:
                    [
                        Column("name", "Name"),
                        Column("details", "Details"),
                        Column("performed", "Performed")
                    ]),
                TodoNode(
                    "Structured performed-treatment rows for discharge exports",
                    "Clinical note",
                    "Update discharge export projection to expose treatment rows and performed-state details"),
                TodoNode(
                    "Discharge treatment comments as a dedicated export field",
                    "Clinical note",
                    "Update discharge export projection to include treatment comment text")),
            BuildGoalsSection(context),
            Section("Discharge Plan Of Care", ClinicalDocumentSourceKind.Note,
                Field("Reason For Discharge", FirstNonEmpty(
                    discharge?.ReasonForDischarge,
                    BuildLegacyJsonValue(noteData.ContentJson, "reasonForDischarge"))),
                Field("Discharge Prognosis", workspace?.Assessment.OverallPrognosis),
                Paragraph("Discharge Instructions", FirstNonEmpty(
                    discharge?.FollowUpRecommendations,
                    discharge?.HepRecommendations,
                    workspace?.Plan.FollowUpInstructions,
                    workspace?.Plan.DischargePlanningNotes)),
                TodoNode(
                    "Initial evaluation date, last treatment date, total visits",
                    "Clinical note history",
                    "Add episode-summary projection for discharge exports")),
            noteData.IncludeSignatureBlock ? BuildClinicianSignatureSection(noteData) : null,
            noteData.IncludeMedicareCompliance ? BuildChargesSection(noteData, context.CptCodes) : null);
    }

    private static ClinicalDocumentHierarchy BuildFallbackDocument(NoteExportDto noteData, ExportDocumentContext context)
    {
        return CreateDocument(
            noteData.NoteType,
            noteData.NoteTypeDisplayName,
            BuildHeaderSection(noteData, noteData.NoteTypeDisplayName),
            Section("Clinical Content", ClinicalDocumentSourceKind.Note,
                Paragraph("Subjective", BuildLegacyJsonValue(noteData.ContentJson, "subjective")),
                Paragraph("Objective", BuildLegacyJsonValue(noteData.ContentJson, "objective")),
                Paragraph("Assessment", BuildLegacyJsonValue(noteData.ContentJson, "assessment")),
                Paragraph("Plan", BuildLegacyJsonValue(noteData.ContentJson, "plan"))),
            noteData.IncludeMedicareCompliance ? BuildChargesSection(noteData, context.CptCodes) : null);
    }

    private static ClinicalDocumentHierarchy CreateDocument(
        NoteType noteType,
        string documentType,
        params ClinicalDocumentNode?[] children)
    {
        return new ClinicalDocumentHierarchy
        {
            NoteType = noteType,
            DocumentType = documentType,
            Root = new ClinicalDocumentNode
            {
                Title = documentType,
                Kind = ClinicalDocumentNodeKind.Document,
                Source = ClinicalDocumentSourceKind.Static,
                Children = children.Where(child => child is not null).Cast<ClinicalDocumentNode>().ToList()
            }
        };
    }

    private static ClinicalDocumentHierarchy SanitizeHierarchy(ClinicalDocumentHierarchy hierarchy)
    {
        hierarchy.Root.Children = hierarchy.Root.Children
            .Select(CleanNode)
            .Where(node => node is not null)
            .Cast<ClinicalDocumentNode>()
            .ToList();
        return hierarchy;
    }

    private static ClinicalDocumentNode? CleanNode(ClinicalDocumentNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Kind is ClinicalDocumentNodeKind.Todo or ClinicalDocumentNodeKind.RenderHint)
        {
            return null;
        }

        node.Children = node.Children
            .Select(CleanNode)
            .Where(child => child is not null)
            .Cast<ClinicalDocumentNode>()
            .ToList();

        if (node.Kind is ClinicalDocumentNodeKind.Section or ClinicalDocumentNodeKind.Group or ClinicalDocumentNodeKind.Signature
            && string.IsNullOrWhiteSpace(node.Value)
            && node.Children.Count == 0
            && node.Table is null)
        {
            return null;
        }

        return node;
    }

    private static ClinicalDocumentNode BuildHeaderSection(NoteExportDto noteData, string title)
    {
        return Section("Header", ClinicalDocumentSourceKind.Static,
            Group("Facility Brand", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Clinic logo, clinic name, address, phone, fax, website",
                    "Clinic profile",
                    "Update note export projection to include facility metadata")),
            Group("Patient Header", ClinicalDocumentSourceKind.Patient,
                Field("Patient Name", BuildPatientName(noteData)),
                Field("Date Of Birth", FormatDate(noteData.PatientDateOfBirth)),
                Field("Document Date", noteData.DateOfService.ToString("M/d/yyyy", CultureInfo.InvariantCulture))),
            Field("Document Title", title, ClinicalDocumentSourceKind.Static));
    }

    private static ClinicalDocumentNode BuildEvaluationSection(NoteExportDto noteData, ExportDocumentContext context)
    {
        var workspace = context.WorkspacePayload;

        return Section("Evaluations", ClinicalDocumentSourceKind.Note,
            BuildOutcomeMeasureTable(context),
            Paragraph("Posture Findings", BuildPostureSummary(workspace)),
            Paragraph("Palpation Findings", BuildPalpationSummary(workspace)),
            BuildSpecialTestTable(noteData, workspace),
            BuildManualMuscleTestingTable(workspace),
            BuildRangeOfMotionTable(workspace),
            Paragraph("Additional Findings", workspace?.Objective.ClinicalObservationNotes),
            BuildObjectiveMetricTodo(workspace));
    }

    private static ClinicalDocumentNode BuildDischargeEvaluationSection(NoteExportDto noteData, ExportDocumentContext context)
    {
        var workspace = context.WorkspacePayload;

        return Section("Evaluations", ClinicalDocumentSourceKind.Aggregate,
            BuildOutcomeMeasureTable(context),
            Paragraph("Posture Findings", BuildPostureSummary(workspace)),
            Paragraph("Palpation Findings", BuildPalpationSummary(workspace)),
            BuildManualMuscleTestingTable(workspace),
            Paragraph("Additional Findings", workspace?.Objective.ClinicalObservationNotes),
            BuildObjectiveMetricTodo(workspace));
    }

    private static ClinicalDocumentNode BuildOutcomeMeasureTable(ExportDocumentContext context)
    {
        var rows = context.WorkspacePayload?.Objective.OutcomeMeasures
            .Select(entry => Row(
                GetOutcomeMeasureDisplay(entry.MeasureType),
                BuildOutcomeScore(entry),
                BuildOutcomeNotes(entry)))
            .ToList() ?? [];

        return TableNode("Outcome Measure Table", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("measure", "Outcome Measure"),
                Column("score", "Score"),
                Column("notes", "Notes")
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode BuildSpecialTestTable(NoteExportDto noteData, NoteWorkspaceV2Payload? workspace)
    {
        var rows = workspace?.Objective.SpecialTests
            .Select(test => Row(
                noteData.DateOfService.ToString("M/d/yy", CultureInfo.InvariantCulture),
                test.Name,
                test.Result,
                Fallback(test.Notes)))
            .ToList() ?? [];

        return TableNode("Special Test Table", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("date", "Date"),
                Column("test", "Special Test"),
                Column("findings", "Findings"),
                Column("notes", "Notes")
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode BuildManualMuscleTestingTable(NoteWorkspaceV2Payload? workspace)
    {
        var rows = workspace?.Objective.Metrics
            .Where(metric => metric.MetricType == MetricType.MMT)
            .Select(metric => Row(
                "Manual Muscle Testing",
                metric.BodyPart.ToString(),
                "-",
                Fallback(metric.NormValue),
                Fallback(metric.PreviousValue),
                string.Empty,
                Fallback(metric.Value),
                string.Empty,
                metric.IsWithinNormalLimits ? "Within Normal Limits" : "Needs Review"))
            .ToList() ?? [];

        return TableNode("Manual Muscle Testing Table", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("family", "Family"),
                Column("type", "Type"),
                Column("specific", "Specific"),
                Column("normalScore", "Normal Score"),
                Column("initialScore", "Initial Score"),
                Column("initialDeficit", "Initial Deficit"),
                Column("currentScore", "Current Score"),
                Column("currentDeficit", "Current Deficit"),
                Column("observation", "Observation")
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode BuildRangeOfMotionTable(NoteWorkspaceV2Payload? workspace)
    {
        var rows = workspace?.Objective.Metrics
            .Where(metric => metric.MetricType == MetricType.ROM)
            .Select(metric => Row(
                metric.BodyPart.ToString(),
                Fallback(metric.NormValue),
                Fallback(metric.PreviousValue),
                string.Empty,
                Fallback(metric.Value),
                string.Empty,
                Fallback(metric.NormValue),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                metric.IsWithinNormalLimits ? "Within Normal Limits" : string.Empty))
            .ToList() ?? [];

        return TableNode("Range Of Motion Table", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("measure", "Measure"),
                Column("activeNorm", "Norm"),
                Column("activeInitialLeft", "Initial L"),
                Column("activeInitialRight", "Initial R"),
                Column("activeCurrentLeft", "Current L"),
                Column("activeCurrentRight", "Current R"),
                Column("passiveNorm", "Norm"),
                Column("passiveInitialLeft", "Initial L"),
                Column("passiveInitialRight", "Initial R"),
                Column("passiveCurrentLeft", "Current L"),
                Column("passiveCurrentRight", "Current R"),
                Column("comments", "Comments")
            ],
            columnGroups:
            [
                new ClinicalDocumentTableColumnGroup { Title = "Measure", Span = 1 },
                new ClinicalDocumentTableColumnGroup { Title = "Active", Span = 5 },
                new ClinicalDocumentTableColumnGroup { Title = "Passive", Span = 5 },
                new ClinicalDocumentTableColumnGroup { Title = "Method / Comments", Span = 1 }
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode BuildObjectiveMetricTodo(NoteWorkspaceV2Payload? workspace)
    {
        var hasMetrics = workspace?.Objective.Metrics.Count > 0;
        return hasMetrics is true
            ? TodoNode(
                "Structured laterality, movement specificity, and deficit fields for ROM/MMT rows",
                "Clinical note",
                "Update ObjectiveMetricInputV2 export projection to expose side-specific structured measurement fields")
            : TodoNode(
                "Structured ROM/MMT measurement rows",
                "Clinical note",
                "Update ObjectiveMetricInputV2 export projection to expose structured measurement fields for evaluation, progress, and discharge exports");
    }

    private static ClinicalDocumentNode BuildDiagnosisSection(ExportDocumentContext context)
    {
        return Section("Therapy Diagnosis", ClinicalDocumentSourceKind.Note,
            BuildDiagnosisTableNode(context));
    }

    private static ClinicalDocumentNode BuildDiagnosisTableNode(ExportDocumentContext context)
    {
        var rows = GetDiagnosisRows(context);

        return TableNode("Diagnosis Code List", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("code", "Code"),
                Column("description", "Description")
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode BuildGoalsSection(ExportDocumentContext context)
    {
        var rows = context.WorkspacePayload?.Assessment.Goals
            .Select(goal => Row(
                FormatGoalType(goal.Timeframe),
                goal.Description,
                goal.Status.ToString()))
            .ToList() ?? [];

        return Section("Goals", ClinicalDocumentSourceKind.Note,
            TableNode("Goals Table", ClinicalDocumentSourceKind.Note,
                columns:
                [
                    Column("goalType", "Goal Type"),
                    Column("goalDescription", "Goal Description"),
                    Column("goalStatus", "Goal Status")
                ],
                rows: rows));
    }

    private static ClinicalDocumentNode BuildCertificationSection(NoteExportDto noteData)
    {
        return Section("Signatures / Certification", ClinicalDocumentSourceKind.Note,
            BuildClinicianSignatureSection(noteData),
            Field("Signature Verification QR", "Renderer-generated verification graphic", ClinicalDocumentSourceKind.Render),
            Group("Certification Of Medical Necessity", ClinicalDocumentSourceKind.Static,
                Paragraph("Certification Statement",
                    "I establish this plan of treatment and certify the need for services furnished under this plan of treatment while under my care.",
                    ClinicalDocumentSourceKind.Static),
                Field("Referring Physician / NPI", BuildReferringPhysicianSummary(noteData), ClinicalDocumentSourceKind.Patient),
                TodoNode(
                    "Physician certification signature date / return status",
                    "Compliance or certification record",
                    "Update export/compliance projection to include physician-certification status")));
    }

    private static ClinicalDocumentNode BuildClinicianSignatureSection(NoteExportDto noteData)
    {
        return Group("Clinician Signature Block", ClinicalDocumentSourceKind.Note,
            Field("Signed By", Fallback(noteData.ClinicianDisplayName)),
            Field("Credentials", Fallback(noteData.ClinicianCredentials)),
            Field("Therapist NPI", Fallback(noteData.TherapistNpi)),
            Field("Signed On", FormatDateTime(noteData.SignedUtc)),
            Field("Signature Hash", Fallback(noteData.SignatureHash)));
    }

    private static ClinicalDocumentNode BuildChargesSection(NoteExportDto noteData, IReadOnlyList<CptCodeEntry> cptCodes)
    {
        var rows = cptCodes
            .Select(code => Row(
                "CPT",
                BuildChargeCodeLabel(code),
                string.Empty,
                code.Units.ToString(CultureInfo.InvariantCulture),
                code.Minutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                string.Empty))
            .ToList();

        return Section("Charges & Reporting", ClinicalDocumentSourceKind.Note,
            TableNode("Charges Table", ClinicalDocumentSourceKind.Note,
                columns:
                [
                    Column("type", "Type"),
                    Column("code", "Code"),
                    Column("severity", "Severity"),
                    Column("units", "Units"),
                    Column("minutes", "Minutes"),
                    Column("assistantInvolved", "Assistant Involved")
                ],
                rows: rows),
            TodoNode(
                "Severity and assistant-involved values per line item",
                "Clinical note billing line items",
                $"Update {GetChargeActionTarget(noteData.NoteType)} to include charge-line severity and assistant-involved metadata"),
            Field("Total Timed Minutes", BuildTotalTimedMinutes(cptCodes), ClinicalDocumentSourceKind.Note),
            Field("Total Treatment Minutes", noteData.TotalTreatmentMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, ClinicalDocumentSourceKind.Note),
            Group("Service Facility", ClinicalDocumentSourceKind.Todo,
                TodoNode(
                    "Service facility display name",
                    "Clinic profile",
                    "Update note export projection to include service-facility metadata")));
    }

    private static ClinicalDocumentNode BuildDailyTreatmentTable(DailyNoteContentDto? daily, IReadOnlyList<CptCodeEntry> cptCodes)
    {
        var rows = new List<ClinicalDocumentTableRow>();

        if (cptCodes.Any(code => string.Equals(code.Code, "97140", StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add(Row("Manual Therapy", BuildCodeDetail("97140", cptCodes), "Yes"));
        }

        if (daily?.Exercises.Count > 0)
        {
            rows.Add(Row("Exercise Program", JoinList(daily.Exercises.Select(exercise => exercise.ExerciseName).ToList()), "Yes"));
        }

        var education = BuildEducationSummary(daily);
        if (!string.IsNullOrWhiteSpace(education))
        {
            rows.Add(Row("Patient Education", education, "Yes"));
        }

        if (!string.IsNullOrWhiteSpace(daily?.HepUpdates) || daily?.HepCompleted == true)
        {
            rows.Add(Row("HEP Development", FirstNonEmpty(daily?.HepUpdates, "Home exercise program reviewed"), "Yes"));
        }

        return TableNode("Treatment Table", ClinicalDocumentSourceKind.Note,
            columns:
            [
                Column("name", "Name"),
                Column("details", "Details"),
                Column("performed", "Performed")
            ],
            rows: rows);
    }

    private static ClinicalDocumentNode Section(string title, ClinicalDocumentSourceKind source, params ClinicalDocumentNode[] children)
        => new()
        {
            Title = title,
            Kind = ClinicalDocumentNodeKind.Section,
            Source = source,
            Children = children.ToList()
        };

    private static ClinicalDocumentNode Group(string title, ClinicalDocumentSourceKind source, params ClinicalDocumentNode[] children)
        => new()
        {
            Title = title,
            Kind = ClinicalDocumentNodeKind.Group,
            Source = source,
            Children = children.ToList()
        };

    private static ClinicalDocumentNode Field(string title, string? value, ClinicalDocumentSourceKind source = ClinicalDocumentSourceKind.Note)
        => new()
        {
            Title = title,
            Kind = ClinicalDocumentNodeKind.Field,
            Source = source,
            Value = value
        };

    private static ClinicalDocumentNode Paragraph(string title, string? value, ClinicalDocumentSourceKind source = ClinicalDocumentSourceKind.Note)
        => new()
        {
            Title = title,
            Kind = ClinicalDocumentNodeKind.Paragraph,
            Source = source,
            Value = value
        };

    private static ClinicalDocumentNode TableNode(
        string title,
        ClinicalDocumentSourceKind source,
        IEnumerable<ClinicalDocumentTableColumn> columns,
        IEnumerable<ClinicalDocumentTableRow>? rows = null,
        IEnumerable<ClinicalDocumentTableColumnGroup>? columnGroups = null)
        => new()
        {
            Title = title,
            Kind = ClinicalDocumentNodeKind.Table,
            Source = source,
            Table = new ClinicalDocumentTable
            {
                Columns = columns.ToList(),
                Rows = rows?.ToList() ?? [],
                ColumnGroups = columnGroups?.ToList() ?? []
            }
        };

    private static ClinicalDocumentNode TodoNode(string requiredField, string sourceNeeded, string action)
        => new()
        {
            Title = "TODO: Missing Data Mapping",
            Kind = ClinicalDocumentNodeKind.Todo,
            Source = ClinicalDocumentSourceKind.Todo,
            Todo = new ClinicalDocumentTodo
            {
                RequiredField = requiredField,
                SourceNeeded = sourceNeeded,
                Action = action
            }
        };

    private static ClinicalDocumentTableColumn Column(string key, string title) => new()
    {
        Key = key,
        Title = title
    };

    private static ClinicalDocumentTableRow Row(params string?[] values) => new()
    {
        Values = values.ToList()
    };

    private static string BuildPatientName(NoteExportDto noteData)
    {
        var fullName = $"{noteData.PatientFirstName} {noteData.PatientLastName}".Trim();
        return Fallback(fullName, "Patient name not recorded");
    }

    private static string BuildProblemSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspace.Subjective.NarrativeContext.ChiefComplaint))
        {
            parts.Add(workspace.Subjective.NarrativeContext.ChiefComplaint);
        }

        if (workspace.Subjective.Problems.Count > 0)
        {
            parts.Add(string.Join(", ", workspace.Subjective.Problems.OrderBy(value => value)));
        }

        if (!string.IsNullOrWhiteSpace(workspace.Subjective.OtherProblem))
        {
            parts.Add(workspace.Subjective.OtherProblem);
        }

        return string.Join("; ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildPainSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (workspace.Subjective.CurrentPainScore > 0)
        {
            values.Add($"Current {workspace.Subjective.CurrentPainScore}/10");
        }

        if (workspace.Subjective.BestPainScore > 0)
        {
            values.Add($"Best {workspace.Subjective.BestPainScore}/10");
        }

        if (workspace.Subjective.WorstPainScore > 0)
        {
            values.Add($"Worst {workspace.Subjective.WorstPainScore}/10");
        }

        if (!string.IsNullOrWhiteSpace(workspace.Subjective.PainFrequency))
        {
            values.Add($"Frequency {workspace.Subjective.PainFrequency}");
        }

        return string.Join("; ", values);
    }

    private static string BuildPainDescription(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (workspace.Subjective.Locations.Count > 0)
        {
            values.Add($"Locations: {string.Join(", ", workspace.Subjective.Locations.OrderBy(value => value))}");
        }

        if (!string.IsNullOrWhiteSpace(workspace.Subjective.OtherLocation))
        {
            values.Add($"Other location: {workspace.Subjective.OtherLocation}");
        }

        return string.Join("; ", values);
    }

    private static string BuildFunctionalLimitationSummary(NoteWorkspaceV2Payload? workspace)
        => workspace is null
            ? string.Empty
            : string.Join(", ",
                workspace.Subjective.FunctionalLimitations
                    .Select(item => item.Description)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string BuildImagingSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var imaging = workspace.Subjective.Imaging;
        var values = new List<string>();

        if (imaging.HasImaging.HasValue)
        {
            values.Add(imaging.HasImaging.Value ? "Imaging completed" : "Imaging not completed");
        }

        if (imaging.Modalities.Count > 0)
        {
            values.Add($"Modalities: {string.Join(", ", imaging.Modalities.OrderBy(value => value))}");
        }

        if (!string.IsNullOrWhiteSpace(imaging.Findings))
        {
            values.Add($"Findings: {imaging.Findings}");
        }

        return string.Join("; ", values);
    }

    private static string BuildPriorTreatmentSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (workspace.Subjective.PriorTreatment.Treatments.Count > 0)
        {
            values.Add($"Treatments: {string.Join(", ", workspace.Subjective.PriorTreatment.Treatments.OrderBy(value => value))}");
        }

        if (!string.IsNullOrWhiteSpace(workspace.Subjective.PriorTreatment.OtherTreatment))
        {
            values.Add($"Other: {workspace.Subjective.PriorTreatment.OtherTreatment}");
        }

        if (workspace.Subjective.PriorTreatment.WasHelpful.HasValue)
        {
            values.Add(workspace.Subjective.PriorTreatment.WasHelpful.Value ? "Helpful" : "Not helpful");
        }

        return string.Join("; ", values);
    }

    private static string BuildProgressQuestionnaireSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        var questionnaire = workspace.ProgressQuestionnaire;
        var values = new List<string>();

        AddIfPresent(values, questionnaire.OverallCondition, "Overall condition");
        AddIfPresent(values, questionnaire.GoalProgress, "Goal progress");
        if (questionnaire.CurrentPainLevel > 0)
        {
            values.Add($"Current pain: {questionnaire.CurrentPainLevel}/10");
        }

        AddIfPresent(values, questionnaire.PainChange, "Pain change");
        AddIfPresent(values, questionnaire.DailyActivityEase, "Daily activity ease");
        if (questionnaire.ImprovedActivities.Count > 0)
        {
            values.Add($"Improved activities: {string.Join(", ", questionnaire.ImprovedActivities.OrderBy(value => value))}");
        }

        if (!string.IsNullOrWhiteSpace(questionnaire.ReturnedToActivities))
        {
            values.Add($"Returned to activities: {questionnaire.ReturnedToActivities}");
        }

        return string.Join("; ", values);
    }

    private static string BuildPostureSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        if (workspace.Objective.PostureObservation.IsNormal)
        {
            return "Normal";
        }

        var values = workspace.Objective.PostureObservation.Findings
            .OrderBy(value => value)
            .ToList();

        if (!string.IsNullOrWhiteSpace(workspace.Objective.PostureObservation.Other))
        {
            values.Add(workspace.Objective.PostureObservation.Other);
        }

        return string.Join(", ", values);
    }

    private static string BuildPalpationSummary(NoteWorkspaceV2Payload? workspace)
    {
        if (workspace is null)
        {
            return string.Empty;
        }

        if (workspace.Objective.PalpationObservation.IsNormal)
        {
            return "Normal";
        }

        var values = workspace.Objective.PalpationObservation.TenderMuscles
            .OrderBy(value => value)
            .ToList();

        if (!string.IsNullOrWhiteSpace(workspace.Objective.PalpationObservation.Other))
        {
            values.Add(workspace.Objective.PalpationObservation.Other);
        }

        return string.Join(", ", values);
    }

    private static string BuildTreatmentPlanSummary(NoteWorkspaceV2Payload? workspace, EvaluationContent? evaluation)
    {
        var values = new List<string>();

        if (workspace is not null)
        {
            if (workspace.Plan.TreatmentFocuses.Count > 0)
            {
                values.Add($"Treatment focus: {string.Join(", ", workspace.Plan.TreatmentFocuses.OrderBy(value => value))}");
            }

            if (workspace.Plan.SelectedCptCodes.Count > 0)
            {
                values.Add($"Planned CPTs: {string.Join(", ", workspace.Plan.SelectedCptCodes.Select(code => $"{code.Code} x{code.Units}"))}");
            }
        }

        if (!string.IsNullOrWhiteSpace(evaluation?.PlanOfCare.SkilledInterventions))
        {
            values.Add(evaluation.PlanOfCare.SkilledInterventions);
        }

        return string.Join("; ", values);
    }

    private static string BuildLegacyPlanOfCareSummary(PlanOfCareContent? planOfCare)
    {
        if (planOfCare is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        AddIfPresent(values, planOfCare.FrequencyDuration, "Frequency / duration");
        AddIfPresent(values, planOfCare.SkilledInterventions, "Interventions");
        if (planOfCare.ShortTermGoals.Count > 0)
        {
            values.Add($"Short-term goals: {string.Join(", ", planOfCare.ShortTermGoals)}");
        }

        if (planOfCare.LongTermGoals.Count > 0)
        {
            values.Add($"Long-term goals: {string.Join(", ", planOfCare.LongTermGoals)}");
        }

        return string.Join("; ", values);
    }

    private static string? ExtractDurationFromFrequency(string? frequencyDuration)
    {
        if (string.IsNullOrWhiteSpace(frequencyDuration))
        {
            return null;
        }

        var parts = frequencyDuration.Split("for", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : frequencyDuration;
    }

    private static List<ClinicalDocumentTableRow> GetDiagnosisRows(ExportDocumentContext context)
    {
        if (context.WorkspacePayload?.Assessment.DiagnosisCodes.Count > 0)
        {
            return context.WorkspacePayload.Assessment.DiagnosisCodes
                .Select(code => Row(code.Code, code.Description))
                .ToList();
        }

        return context.PatientDiagnoses
            .Select(code => Row(code.IcdCode, code.Description))
            .ToList();
    }

    private static string FormatGoalType(GoalTimeframe timeframe)
        => timeframe == GoalTimeframe.ShortTerm ? "Short-Term" : "Long-Term";

    private static string BuildOutcomeScore(OutcomeMeasureEntryV2 entry)
        => entry.Score.ToString("0.##", CultureInfo.InvariantCulture);

    private static string BuildOutcomeNotes(OutcomeMeasureEntryV2 entry)
    {
        var values = new List<string> { $"Recorded {entry.RecordedAtUtc:M/d/yy}" };
        if (entry.MinimumDetectableChange.HasValue)
        {
            values.Add($"MDC {entry.MinimumDetectableChange.Value:0.##}");
        }

        return string.Join("; ", values);
    }

    private static string GetOutcomeMeasureDisplay(OutcomeMeasureType measureType)
        => measureType switch
        {
            OutcomeMeasureType.DASH => "DASH",
            OutcomeMeasureType.LEFS => "LEFS",
            OutcomeMeasureType.NPRS => "NPRS",
            OutcomeMeasureType.OswestryDisabilityIndex => "Oswestry Disability Index",
            OutcomeMeasureType.NeckDisabilityIndex => "Neck Disability Index",
            OutcomeMeasureType.PSFS => "PSFS",
            OutcomeMeasureType.VAS => "VAS",
            _ => measureType.ToString()
        };

    private static string BuildDailySubjectiveSummary(DailyNoteContentDto? daily)
    {
        if (daily is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (daily.ConditionChange.HasValue)
        {
            values.Add($"Condition change: {((ConditionChange)daily.ConditionChange.Value).ToString()}");
        }

        if (daily.CurrentPainScore.HasValue)
        {
            values.Add($"Current pain: {daily.CurrentPainScore.Value}/10");
        }

        if (daily.BestPainScore.HasValue)
        {
            values.Add($"Best pain: {daily.BestPainScore.Value}/10");
        }

        if (daily.WorstPainScore.HasValue)
        {
            values.Add($"Worst pain: {daily.WorstPainScore.Value}/10");
        }

        if (daily.LimitedActivities.Count > 0)
        {
            values.Add($"Limited activities: {string.Join(", ", daily.LimitedActivities.Select(activity => activity.ActivityName))}");
        }

        AddIfPresent(values, daily.ChangesSinceLastSession, "Changes");
        AddIfPresent(values, daily.PatientAdditionalComments, "Additional comments");
        return string.Join("; ", values);
    }

    private static string BuildCueSummary(DailyNoteContentDto? daily)
    {
        if (daily is null)
        {
            return string.Empty;
        }

        var cueLabels = daily.CueTypes
            .Select(value => ((CueType)value).ToString())
            .ToList();

        if (daily.CueIntensity.HasValue)
        {
            cueLabels.Add(((CueIntensity)daily.CueIntensity.Value).ToString());
        }

        return string.Join(", ", cueLabels);
    }

    private static string BuildAssistanceSummary(DailyNoteContentDto? daily)
        => daily is null
            ? string.Empty
            : string.Join(", ", daily.AssistanceLevels.Select(value => ((AssistanceLevel)value).ToString()));

    private static string BuildVisualInstructionSummary(DailyNoteContentDto? daily)
        => daily is null || !daily.CueTypes.Contains((int)CueType.Visual)
            ? string.Empty
            : "Visual cueing documented";

    private static string BuildEducationSummary(DailyNoteContentDto? daily)
    {
        if (daily is null)
        {
            return string.Empty;
        }

        var values = daily.EducationTopics
            .Select(value => ((EducationTopic)value).ToString())
            .ToList();

        if (!string.IsNullOrWhiteSpace(daily.EducationOther))
        {
            values.Add(daily.EducationOther);
        }

        return string.Join(", ", values);
    }

    private static string BuildDailyPlanSummary(DailyNoteContentDto? daily)
    {
        if (daily is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (daily.PlanDirection.HasValue)
        {
            values.Add(((PlanDirection)daily.PlanDirection.Value).ToString());
        }

        AddIfPresent(values, daily.PlanFreeText, "Plan");
        AddIfPresent(values, daily.NextSessionPlan, "Next session");
        AddIfPresent(values, daily.GoalReassessmentPlan, "Goal reassessment");
        AddIfPresent(values, daily.ProgressionReasoning, "Progression reasoning");
        return string.Join("; ", values);
    }

    private static string BuildReferringPhysicianSummary(NoteExportDto noteData)
    {
        var values = new List<string>();
        AddIfPresent(values, noteData.ReferringPhysician, null);
        AddIfPresent(values, noteData.ReferringPhysicianNpi, "NPI");
        AddIfPresent(values, FormatDateTime(noteData.PhysicianSignedUtc), "Physician signed on");
        return string.Join("; ", values);
    }

    private static string BuildChargeCodeLabel(CptCodeEntry entry)
        => entry.Code;

    private static string BuildCodeDetail(string code, IReadOnlyList<CptCodeEntry> cptCodes)
    {
        var match = cptCodes.FirstOrDefault(entry => string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return string.Empty;
        }

        if (match.Minutes.HasValue)
        {
            return $"{BuildChargeCodeLabel(match)} ({match.Minutes.Value} min)";
        }

        return BuildChargeCodeLabel(match);
    }

    private static string BuildTotalTimedMinutes(IReadOnlyList<CptCodeEntry> cptCodes)
    {
        var total = cptCodes
            .Where(code => KnownTimedCptCodes.Codes.Contains(code.Code) && code.Minutes.HasValue)
            .Sum(code => code.Minutes!.Value);

        return total == 0 ? string.Empty : total.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetChargeActionTarget(NoteType noteType)
        => noteType switch
        {
            NoteType.Daily => "daily-note export line-item projection",
            NoteType.Discharge => "discharge export line-item projection",
            _ => "note export line-item projection"
        };

    private static string BuildLegacyJsonValue(string contentJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return string.Empty;
            }

            return JsonToText(property);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string JsonToText(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(JsonToText).Where(value => !string.IsNullOrWhiteSpace(value))),
            JsonValueKind.Object => string.Join("; ", element.EnumerateObject().Select(property => $"{ToTitle(property.Name)}: {JsonToText(property.Value)}")),
            _ => string.Empty
        };

    private static string ToTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (i > 0 && char.IsUpper(character) && char.IsLower(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(i == 0 ? char.ToUpperInvariant(character) : character);
        }

        return builder.ToString();
    }

    private static string JoinSet(IEnumerable<string>? values)
        => values is null ? string.Empty : string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value));

    private static string JoinList(IEnumerable<string>? values)
        => values is null ? string.Empty : string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static DateTime? FirstNonNull(params DateTime?[] values)
        => values.FirstOrDefault(value => value.HasValue);

    private static void AddIfPresent(List<string> values, string? candidate, string? label)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        values.Add(string.IsNullOrWhiteSpace(label) ? candidate : $"{label}: {candidate}");
    }

    private static string FormatDate(DateTime? value)
        => value.HasValue ? value.Value.ToString("M/d/yyyy", CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatDateTime(DateTime? value)
        => value.HasValue ? value.Value.ToString("M/d/yyyy h:mm tt", CultureInfo.InvariantCulture) : string.Empty;

    private static string Fallback(string? value, string fallback = "")
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private sealed class ExportDocumentContext
    {
        public NoteWorkspaceV2Payload? WorkspacePayload { get; init; }
        public DailyNoteContentDto? DailyNoteContent { get; init; }
        public EvaluationContent? EvaluationContent { get; init; }
        public ProgressNoteContent? ProgressContent { get; init; }
        public DischargeContent? DischargeContent { get; init; }
        public IReadOnlyList<CptCodeEntry> CptCodes { get; init; } = [];
        public IReadOnlyList<PatientDiagnosisDto> PatientDiagnoses { get; init; } = [];

        public static ExportDocumentContext Create(NoteExportDto noteData)
        {
            return new ExportDocumentContext
            {
                WorkspacePayload = TryDeserialize<NoteWorkspaceV2Payload>(noteData.ContentJson, payload =>
                    payload is not null && payload.SchemaVersion == WorkspaceSchemaVersions.EvalReevalProgressV2),
                DailyNoteContent = noteData.NoteType == NoteType.Daily
                    ? TryDeserialize<DailyNoteContentDto>(noteData.ContentJson)
                    : null,
                EvaluationContent = noteData.NoteType == NoteType.Evaluation
                    ? TryDeserialize<EvaluationContent>(noteData.ContentJson)
                    : null,
                ProgressContent = noteData.NoteType == NoteType.ProgressNote
                    ? TryDeserialize<ProgressNoteContent>(noteData.ContentJson)
                    : null,
                DischargeContent = noteData.NoteType == NoteType.Discharge
                    ? TryDeserialize<DischargeContent>(noteData.ContentJson)
                    : null,
                CptCodes = ParseCptCodes(noteData.CptCodesJson),
                PatientDiagnoses = ParseDiagnoses(noteData.PatientDiagnosisCodesJson)
            };
        }

        private static T? TryDeserialize<T>(string json, Func<T?, bool>? predicate = null)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
                if (predicate is not null && !predicate(value))
                {
                    return default;
                }

                return value;
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static IReadOnlyList<CptCodeEntry> ParseCptCodes(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<CptCodeEntry>>(json, SerializerOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static IReadOnlyList<PatientDiagnosisDto> ParseDiagnoses(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<PatientDiagnosisDto>>(json, SerializerOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
