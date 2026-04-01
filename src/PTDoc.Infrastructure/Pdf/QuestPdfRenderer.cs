using System.Text;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Production PDF renderer using QuestPDF.
/// </summary>
public sealed class QuestPdfRenderer : IPdfRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static QuestPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<PdfExportResult> ExportNoteToPdfAsync(NoteExportDto noteData)
    {
        var content = ExportNoteContent.Create(noteData);

        var pdfBytes = await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0.75f, Unit.Inch);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

                    page.Header().Element(container => ComposeHeader(container, noteData));
                    page.Content().Element(container => ComposeContent(container, noteData, content));
                    page.Footer().Element(container => ComposeFooter(container, noteData, content));
                });
            });

            return document.GeneratePdf();
        });

        return new PdfExportResult
        {
            PdfBytes = pdfBytes,
            FileName = $"note_{noteData.NoteId}_{DateTime.UtcNow:yyyyMMdd}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = pdfBytes.Length
        };
    }

    private static void ComposeHeader(IContainer container, NoteExportDto noteData)
    {
        container.Column(column =>
        {
            column.Item().Text("PTDoc Clinical Export")
                .FontSize(18)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(noteData.NoteTypeDisplayName)
                    .FontSize(11)
                    .SemiBold();
                row.RelativeItem().AlignRight().Text($"Date of Service: {noteData.DateOfService:MM/dd/yyyy}");
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeContent(IContainer container, NoteExportDto noteData, ExportNoteContent content)
    {
        container.PaddingTop(12).Column(column =>
        {
            column.Item().Element(section => ComposePatientInfo(section, noteData));

            column.Item().PaddingTop(14).Element(section => ComposeSoapSection(section, "Subjective", content.Subjective));
            column.Item().PaddingTop(10).Element(section => ComposeSoapSection(section, "Objective", content.Objective));

            if (content.OutcomeMeasures.Count > 0)
            {
                column.Item().PaddingTop(8).Element(section => ComposeOutcomeTable(section, content.OutcomeMeasures));
            }

            column.Item().PaddingTop(10).Element(section => ComposeSoapSection(section, "Assessment", content.Assessment));

            if (content.Goals.Count > 0)
            {
                column.Item().PaddingTop(8).Element(section => ComposeGoalsTable(section, content.Goals));
            }

            column.Item().PaddingTop(10).Element(section => ComposeSoapSection(section, "Plan", content.Plan));

            if (noteData.IncludeSignatureBlock)
            {
                column.Item().PaddingTop(14).Element(section => ComposeSignatureBlock(section, noteData));
            }
        });
    }

    private static void ComposePatientInfo(IContainer container, NoteExportDto noteData)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Patient: {BuildPatientName(noteData)}").SemiBold();
                row.RelativeItem().AlignRight().Text($"MRN: {Fallback(noteData.PatientMedicalRecordNumber, "N/A")}");
            });

            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Note ID: {noteData.NoteId}");
                row.RelativeItem().AlignRight().Text($"Generated: {DateTime.UtcNow:MM/dd/yyyy HH:mm} UTC");
            });
        });
    }

    private static void ComposeSoapSection(IContainer container, string title, string body)
    {
        container.Column(column =>
        {
            column.Item().Text(title)
                .FontSize(12)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            column.Item().PaddingTop(4).Border(1).BorderColor(Colors.Grey.Lighten3).Padding(10)
                .Text(string.IsNullOrWhiteSpace(body) ? "No data documented." : body)
                .LineHeight(1.35f);
        });
    }

    private static void ComposeOutcomeTable(IContainer container, IReadOnlyList<OutcomeMeasureRow> rows)
    {
        container.Column(column =>
        {
            column.Item().Text("Outcome Measures")
                .FontSize(11)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeaderCell).Text("Measure").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("Score").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("Recorded").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("MDC").SemiBold();
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(TableBodyCell).Text(row.Measure);
                    table.Cell().Element(TableBodyCell).Text(row.Score);
                    table.Cell().Element(TableBodyCell).Text(row.Recorded);
                    table.Cell().Element(TableBodyCell).Text(row.MinimumDetectableChange);
                }
            });
        });
    }

    private static void ComposeGoalsTable(IContainer container, IReadOnlyList<GoalRow> rows)
    {
        container.Column(column =>
        {
            column.Item().Text("Goals")
                .FontSize(11)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeaderCell).Text("Goal").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("Category").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("Timeframe").SemiBold();
                    header.Cell().Element(TableHeaderCell).Text("Status").SemiBold();
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(TableBodyCell).Text(row.Description);
                    table.Cell().Element(TableBodyCell).Text(row.Category);
                    table.Cell().Element(TableBodyCell).Text(row.Timeframe);
                    table.Cell().Element(TableBodyCell).Text(row.Status);
                }
            });
        });
    }

    private static void ComposeSignatureBlock(IContainer container, NoteExportDto noteData)
    {
        if (string.IsNullOrWhiteSpace(noteData.SignatureHash) || !noteData.SignedUtc.HasValue)
        {
            container.Border(2).BorderColor(Colors.Red.Medium).Padding(16).Column(column =>
            {
                column.Item().AlignCenter().Text("UNSIGNED DRAFT")
                    .FontSize(22)
                    .Bold()
                    .FontColor(Colors.Red.Medium);

                column.Item().PaddingTop(6).AlignCenter().Text("This document has not been electronically signed.")
                    .FontSize(10)
                    .FontColor(Colors.Red.Darken1);
            });

            return;
        }

        container.Border(1).BorderColor(Colors.Blue.Darken2).Padding(12).Column(column =>
        {
            column.Item().Text("Electronic Signature")
                .FontSize(11)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            column.Item().PaddingTop(4).Text($"Signed by: {Fallback(noteData.ClinicianDisplayName, noteData.SignedByUserId?.ToString() ?? "Unknown clinician")}");
            column.Item().Text($"Credentials: {Fallback(noteData.ClinicianCredentials, "Not recorded")}");
            column.Item().Text($"Signed on: {noteData.SignedUtc.Value:MM/dd/yyyy HH:mm} UTC");
            column.Item().Text($"Signature hash: {noteData.SignatureHash}")
                .FontSize(8)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComposeFooter(IContainer container, NoteExportDto noteData, ExportNoteContent content)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            if (noteData.IncludeMedicareCompliance)
            {
                var cptSummary = content.CptCodes.Count == 0
                    ? "No CPT codes documented."
                    : string.Join("; ", content.CptCodes.Select(code => $"{code.Code} x{code.Units}{(code.IsTimed ? " timed" : string.Empty)}"));

                column.Item().PaddingTop(6).Text("Medicare / Billing Summary")
                    .FontSize(9)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);

                column.Item().PaddingTop(2).Text(cptSummary)
                    .FontSize(8);
            }

            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Clinician: {Fallback(noteData.ClinicianDisplayName, "PTDoc clinician")} {FormatCredentialSuffix(noteData.ClinicianCredentials)}")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken2);

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(TextStyle.Default.FontSize(8).FontColor(Colors.Grey.Darken2));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    private static IContainer TableHeaderCell(IContainer container)
        => container.Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6);

    private static IContainer TableBodyCell(IContainer container)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6);

    private static string BuildPatientName(NoteExportDto noteData)
    {
        var fullName = $"{noteData.PatientFirstName} {noteData.PatientLastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Patient name not recorded" : fullName;
    }

    private static string FormatCredentialSuffix(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $"({value})";

    private static string Fallback(string? primary, string fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private sealed class ExportNoteContent
    {
        public string Subjective { get; init; } = string.Empty;
        public string Objective { get; init; } = string.Empty;
        public string Assessment { get; init; } = string.Empty;
        public string Plan { get; init; } = string.Empty;
        public IReadOnlyList<OutcomeMeasureRow> OutcomeMeasures { get; init; } = [];
        public IReadOnlyList<GoalRow> Goals { get; init; } = [];
        public IReadOnlyList<CptCodeEntry> CptCodes { get; init; } = [];

        public static ExportNoteContent Create(NoteExportDto noteData)
        {
            var cptCodes = ParseCptCodes(noteData.CptCodesJson);
            if (TryParseWorkspacePayload(noteData.ContentJson, out var payload))
            {
                return FromWorkspacePayload(payload!, cptCodes);
            }

            return new ExportNoteContent
            {
                Subjective = ReadLegacySection(noteData.ContentJson, "subjective"),
                Objective = ReadLegacySection(noteData.ContentJson, "objective"),
                Assessment = ReadLegacySection(noteData.ContentJson, "assessment"),
                Plan = ReadLegacySection(noteData.ContentJson, "plan"),
                CptCodes = cptCodes
            };
        }

        private static ExportNoteContent FromWorkspacePayload(NoteWorkspaceV2Payload payload, IReadOnlyList<CptCodeEntry> cptCodes)
        {
            var subjective = JoinParagraphs(
                FormatNamedValue("Chief complaint", payload.Subjective.NarrativeContext.ChiefComplaint),
                FormatPainSummary(payload.Subjective),
                FormatFunctionalLimitations(payload.Subjective.FunctionalLimitations),
                FormatNamedValue("History", payload.Subjective.NarrativeContext.HistoryOfPresentIllness),
                FormatNamedValue("Mechanism of injury", payload.Subjective.NarrativeContext.MechanismOfInjury),
                FormatNamedValue("Additional limitations", payload.Subjective.AdditionalFunctionalLimitations));

            var objective = JoinParagraphs(
                FormatMetrics(payload.Objective.Metrics),
                FormatObservation("Clinical observations", payload.Objective.ClinicalObservationNotes),
                FormatObservation("Gait", payload.Objective.GaitObservation.AdditionalObservations),
                FormatObservation("Posture", string.Join(", ", payload.Objective.PostureObservation.Findings)),
                FormatObservation("Palpation", string.Join(", ", payload.Objective.PalpationObservation.TenderMuscles)));

            var assessment = JoinParagraphs(
                payload.Assessment.AssessmentNarrative,
                FormatNamedValue("Functional limitations", payload.Assessment.FunctionalLimitationsSummary),
                FormatNamedValue("Deficits", payload.Assessment.DeficitsSummary),
                FormatNamedValue("Diagnosis", string.Join(", ", payload.Assessment.DiagnosisCodes.Select(code => $"{code.Code} {code.Description}".Trim()))),
                FormatNamedValue("Skilled PT justification", payload.Assessment.SkilledPtJustification),
                FormatNamedValue("Overall prognosis", payload.Assessment.OverallPrognosis));

            var plan = JoinParagraphs(
                payload.Plan.PlanOfCareNarrative ?? string.Empty,
                FormatNamedValue("Treatment frequency", payload.Plan.ComputedPlanOfCare.FrequencyDisplay),
                FormatNamedValue("Treatment duration", payload.Plan.ComputedPlanOfCare.DurationDisplay),
                FormatNamedValue("Treatment focus", string.Join(", ", payload.Plan.TreatmentFocuses)),
                FormatNamedValue("Clinical summary", payload.Plan.ClinicalSummary),
                FormatNamedValue("Home exercise program", payload.Plan.HomeExerciseProgramNotes),
                FormatNamedValue("Follow-up instructions", payload.Plan.FollowUpInstructions),
                FormatNamedValue("Discharge planning", payload.Plan.DischargePlanningNotes));

            return new ExportNoteContent
            {
                Subjective = subjective,
                Objective = objective,
                Assessment = assessment,
                Plan = plan,
                OutcomeMeasures = payload.Objective.OutcomeMeasures
                    .Select(entry => new OutcomeMeasureRow(
                        entry.MeasureType.ToString(),
                        entry.Score.ToString("0.##"),
                        entry.RecordedAtUtc.ToString("MM/dd/yyyy"),
                        entry.MinimumDetectableChange?.ToString("0.##") ?? "-"))
                    .ToList(),
                Goals = payload.Assessment.Goals
                    .Select(goal => new GoalRow(
                        goal.Description,
                        Fallback(goal.Category, "-"),
                        goal.Timeframe.ToString(),
                        goal.Status.ToString()))
                    .ToList(),
                CptCodes = cptCodes.Count > 0
                    ? cptCodes
                    : payload.Plan.SelectedCptCodes
                        .Select(code => new CptCodeEntry
                        {
                            Code = code.Code,
                            Units = code.Units,
                            IsTimed = KnownTimedCptCodes.Codes.Contains(code.Code)
                        })
                        .ToList()
            };
        }

        private static bool TryParseWorkspacePayload(string contentJson, out NoteWorkspaceV2Payload? payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(contentJson))
            {
                return false;
            }

            try
            {
                payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, SerializerOptions);
                return payload is not null && payload.SchemaVersion == WorkspaceSchemaVersions.EvalReevalProgressV2;
            }
            catch (JsonException)
            {
                return false;
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

        private static string ReadLegacySection(string contentJson, string sectionName)
        {
            if (string.IsNullOrWhiteSpace(contentJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(contentJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return contentJson;
                }

                if (!document.RootElement.TryGetProperty(sectionName, out var section))
                {
                    return contentJson;
                }

                return JsonElementToPlainText(section);
            }
            catch (JsonException)
            {
                return contentJson;
            }
        }

        private static string JsonElementToPlainText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(JsonElementToPlainText).Where(value => !string.IsNullOrWhiteSpace(value))),
                JsonValueKind.Object => string.Join(Environment.NewLine, element.EnumerateObject()
                    .Select(property => $"{ToTitleCase(property.Name)}: {JsonElementToPlainText(property.Value)}")
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !value.EndsWith(": "))),
                JsonValueKind.True => "Yes",
                JsonValueKind.False => "No",
                JsonValueKind.Number => element.ToString(),
                _ => string.Empty
            };
        }

        private static string FormatPainSummary(WorkspaceSubjectiveV2 subjective)
        {
            var parts = new List<string>();
            if (subjective.CurrentPainScore > 0 || subjective.BestPainScore > 0 || subjective.WorstPainScore > 0)
            {
                parts.Add($"Pain scores: current {subjective.CurrentPainScore}/10, best {subjective.BestPainScore}/10, worst {subjective.WorstPainScore}/10.");
            }

            if (!string.IsNullOrWhiteSpace(subjective.PainFrequency))
            {
                parts.Add($"Frequency: {subjective.PainFrequency}.");
            }

            return string.Join(" ", parts);
        }

        private static string FormatFunctionalLimitations(IEnumerable<FunctionalLimitationEntryV2> limitations)
        {
            var items = limitations
                .Select(item => item.Description)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return items.Count == 0 ? string.Empty : $"Functional limitations: {string.Join(", ", items)}.";
        }

        private static string FormatMetrics(IEnumerable<ObjectiveMetricInputV2> metrics)
        {
            var parts = metrics
                .Where(metric => !string.IsNullOrWhiteSpace(metric.Value))
                .Select(metric => $"{metric.BodyPart} {metric.MetricType}: {metric.Value}")
                .ToList();

            return parts.Count == 0 ? string.Empty : $"Objective metrics: {string.Join("; ", parts)}.";
        }

        private static string FormatObservation(string label, string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}.";
        }

        private static string FormatNamedValue(string label, string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";
        }

        private static string JoinParagraphs(params string[] values)
        {
            return string.Join(Environment.NewLine + Environment.NewLine, values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string ToTitleCase(string value)
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
    }

    private sealed record OutcomeMeasureRow(string Measure, string Score, string Recorded, string MinimumDetectableChange);
    private sealed record GoalRow(string Description, string Category, string Timeframe, string Status);
}
