using System.Globalization;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components;
using PTDoc.UI.Components.ProgressTracking.Models;

namespace PTDoc.UI.Services;

public sealed class ProgressTrackingAggregationService(
    INoteService noteService,
    IAppointmentService appointmentService,
    IOutcomeMeasureRegistry outcomeMeasureRegistry)
{
    private const int BatchReadLimit = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProgressTrackingSnapshot> LoadAsync(
        ProgressTrackingFilterState filterState,
        CancellationToken cancellationToken = default)
    {
        var lookbackDays = GetLookbackDays(filterState.DateRange);
        var startDate = DateTime.UtcNow.Date.AddDays(-lookbackDays);
        var endDate = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

        var notesTask = noteService.GetNotesAsync(take: 500, cancellationToken: cancellationToken);
        var appointmentsTask = appointmentService.GetOverviewAsync(startDate, endDate, cancellationToken);

        await Task.WhenAll(notesTask, appointmentsTask);

        var notes = (await notesTask)
            .Where(note => note.DateOfService >= startDate && note.DateOfService <= endDate)
            .OrderByDescending(note => note.LastModifiedUtc)
            .ThenByDescending(note => note.DateOfService)
            .ToList();

        var appointments = (await appointmentsTask).Appointments
            .Where(appointment => appointment.StartTimeUtc >= startDate && appointment.StartTimeUtc <= endDate)
            .OrderByDescending(appointment => appointment.StartTimeUtc)
            .ToList();

        if (notes.Count == 0 && appointments.Count == 0)
        {
            return new ProgressTrackingSnapshot();
        }

        var groups = BuildActivityGroups(notes, appointments);
        var latestNoteIds = groups
            .Select(GetLatestNote)
            .Where(note => note is not null)
            .Select(note => note!.Id)
            .Distinct()
            .ToList();
        var latestNoteDetails = await BatchLoadNoteDetailsAsync(latestNoteIds, cancellationToken);
        var patients = await Task.WhenAll(groups.Select(group => BuildPatientAsync(group, latestNoteDetails)));

        var filteredPatients = patients
            .Where(patient => MatchesFilters(patient, filterState))
            .OrderByDescending(patient => patient.LastAssessmentDate ?? DateTime.MinValue)
            .ThenBy(patient => patient.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProgressTrackingSnapshot
        {
            Patients = filteredPatients,
            Alerts = BuildAlerts(filteredPatients, lookbackDays),
            ProviderProgress = BuildProviderProgress(filteredPatients),
            OverviewSummary = BuildOverviewSummary(filteredPatients, lookbackDays)
        };
    }

    public async Task<IReadOnlyList<ProgressTrendPointVm>> LoadTrendPointsAsync(
        IReadOnlyList<Guid> noteIds,
        OutcomeMeasureType? measureType = null,
        CancellationToken cancellationToken = default)
    {
        var recentNoteIds = noteIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(4)
            .ToList();

        if (recentNoteIds.Count == 0)
        {
            return Array.Empty<ProgressTrendPointVm>();
        }

        var details = await BatchLoadNoteDetailsAsync(recentNoteIds, cancellationToken);
        var points = new List<(DateTime Date, ParsedOutcomeMeasure Measure)>();
        var resolvedMeasureType = measureType;

        foreach (var noteId in recentNoteIds)
        {
            if (!details.TryGetValue(noteId, out var detail))
            {
                continue;
            }

            var note = detail?.Note;
            if (note is null)
            {
                continue;
            }

            var payload = ParsePayload(note.ContentJson);
            if (payload?.OutcomeMeasures is not { Count: > 0 } measures)
            {
                continue;
            }

            resolvedMeasureType ??= GetLatestOutcomeMeasure(measures)?.MeasureType;
            if (resolvedMeasureType is null)
            {
                continue;
            }

            var measure = measures
                .Where(item => item.MeasureType == resolvedMeasureType.Value)
                .OrderByDescending(item => item.RecordedAtUtc ?? DateTime.MinValue)
                .FirstOrDefault();
            if (measure is not null)
            {
                points.Add((note.DateOfService, measure));
            }
        }

        return points
            .OrderBy(point => point.Date)
            .Select(point => new ProgressTrendPointVm
            {
                Label = point.Date.ToString("MMM d", CultureInfo.InvariantCulture),
                Value = GetNormalizedScorePercent(point.Measure),
                MeasureType = point.Measure.MeasureType,
                MeasureLabel = GetOutcomeMeasureLabel(point.Measure.MeasureType),
                ScoreDisplay = FormatOutcomeScore(point.Measure),
                Interpretation = GetOutcomeInterpretation(point.Measure)
            })
            .ToList();
    }

    private Task<ProgressTrackingPatientVm> BuildPatientAsync(
        PatientActivityGroup group,
        IReadOnlyDictionary<Guid, NoteDetailResponse?> noteDetailsById)
    {
        var latestNote = GetLatestNote(group);
        var latestAppointment = GetLatestAppointment(group);
        var latestActivityUtc = GetLatestActivityUtc(latestNote, latestAppointment);

        var latestNoteResponse = latestNote is not null && noteDetailsById.TryGetValue(latestNote.Id, out var latestNoteDetail)
            ? latestNoteDetail?.Note
            : null;
        var payload = latestNoteResponse is null
            ? null
            : ParsePayload(latestNoteResponse.ContentJson);

        var goals = ExtractGoals(payload).ToList();
        var goalStatuses = ExtractGoalStatuses(payload);
        var recentNoteIds = group.Notes
            .OrderByDescending(note => note.LastModifiedUtc)
            .ThenByDescending(note => note.DateOfService)
            .Select(note => note.Id)
            .Take(4)
            .ToList();

        var latestOutcomeMeasure = payload is null ? null : GetLatestOutcomeMeasure(payload.OutcomeMeasures);
        var hasOutcomeScore = latestOutcomeMeasure is not null;
        var currentScore = latestOutcomeMeasure is null ? 0 : GetNormalizedScorePercent(latestOutcomeMeasure);

        return Task.FromResult(new ProgressTrackingPatientVm
        {
            Id = group.PatientId.ToString(),
            PatientId = group.PatientId,
            DisplayName = group.PatientName,
            Condition = BuildCondition(latestNoteResponse, latestAppointment, goals.Count),
            Provider = string.IsNullOrWhiteSpace(latestAppointment?.ClinicianName)
                ? "Unassigned"
                : latestAppointment.ClinicianName,
            LastAssessment = BuildLastAssessmentLabel(latestActivityUtc),
            LastAssessmentDate = latestActivityUtc,
            StatusLabel = BuildStatusLabel(latestNoteResponse, latestAppointment),
            StatusVariant = BuildStatusVariant(latestNoteResponse, latestAppointment),
            CurrentScore = currentScore,
            ScoreDelta = 0,
            HasOutcomeScore = hasOutcomeScore,
            CurrentOutcomeMeasureType = latestOutcomeMeasure?.MeasureType,
            CurrentOutcomeMeasureLabel = latestOutcomeMeasure is null ? string.Empty : GetOutcomeMeasureLabel(latestOutcomeMeasure.MeasureType),
            CurrentOutcomeScoreDisplay = latestOutcomeMeasure is null ? string.Empty : FormatOutcomeScore(latestOutcomeMeasure),
            CurrentOutcomeInterpretation = latestOutcomeMeasure is null ? string.Empty : GetOutcomeInterpretation(latestOutcomeMeasure),
            MetGoalCount = goalStatuses.MetGoals,
            ActiveGoalCount = goalStatuses.ActiveGoals,
            ArchivedGoalCount = goalStatuses.ArchivedGoals,
            Goals = goals,
            RecentNoteIds = recentNoteIds,
            TreatmentPhase = BuildTreatmentPhase(latestNoteResponse, latestAppointment)
        });
    }

    private static IReadOnlyList<ClinicalAlertVm> BuildAlerts(
        IReadOnlyList<ProgressTrackingPatientVm> patients,
        int lookbackDays)
    {
        var alerts = new List<ClinicalAlertVm>();

        foreach (var patient in patients
                     .Where(patient => patient.LastAssessmentDate.HasValue)
                     .OrderByDescending(patient => patient.LastAssessmentDate))
        {
            var ageInDays = (DateTime.UtcNow.Date - patient.LastAssessmentDate!.Value.Date).Days;

            if (string.Equals(patient.StatusLabel, "Pending", StringComparison.OrdinalIgnoreCase) && ageInDays >= 7)
            {
                alerts.Add(new ClinicalAlertVm
                {
                    PatientId = patient.Id,
                    TargetUrl = patient.RecentNoteIds.Count == 0
                        ? $"/patient/{patient.PatientId:D}"
                        : $"/patient/{patient.PatientId:D}/note/{patient.RecentNoteIds[0]:D}?section=review",
                    Message = $"{patient.DisplayName}: latest note is still pending review.",
                    Meta = $"{patient.Condition} · Updated {FormatAge(ageInDays)} ago",
                    Severity = "warning",
                    ActionLabel = "Review note"
                });
            }
            else if (string.Equals(patient.StatusLabel, "Active", StringComparison.OrdinalIgnoreCase) && ageInDays >= Math.Max(lookbackDays / 2, 7))
            {
                alerts.Add(new ClinicalAlertVm
                {
                    PatientId = patient.Id,
                    TargetUrl = $"/patient/{patient.PatientId:D}",
                    Message = $"{patient.DisplayName}: reassessment may be due based on recent activity.",
                    Meta = $"{patient.Condition} · Last assessment {FormatAge(ageInDays)} ago",
                    Severity = "info",
                    ActionLabel = "Open patient"
                });
            }

            if (alerts.Count >= 3)
            {
                break;
            }
        }

        return alerts;
    }

    private static IReadOnlyList<ProviderGoalProgressVm> BuildProviderProgress(
        IReadOnlyList<ProgressTrackingPatientVm> patients)
    {
        return patients
            .GroupBy(patient => string.IsNullOrWhiteSpace(patient.Provider) ? "Unassigned" : patient.Provider)
            .Select(group => new ProviderGoalProgressVm
            {
                ProviderName = group.Key,
                Achieved = group.Sum(patient => patient.MetGoalCount),
                InProgress = group.Sum(patient => patient.ActiveGoalCount)
            })
            .OrderByDescending(provider => provider.Achieved + provider.InProgress)
            .ThenBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string BuildOverviewSummary(
        IReadOnlyList<ProgressTrackingPatientVm> patients,
        int lookbackDays)
    {
        if (patients.Count == 0)
        {
            return "No patient records match the current filters.";
        }

        var activePrograms = patients.Count(patient => string.Equals(patient.StatusLabel, "Active", StringComparison.OrdinalIgnoreCase));
        var dischargedPrograms = patients.Count(patient => string.Equals(patient.StatusLabel, "Discharged", StringComparison.OrdinalIgnoreCase));
        var scoredPatients = patients.Count(patient => patient.HasOutcomeScore);
        var goalsTracked = patients.Sum(patient => patient.Goals.Count);

        return $"{patients.Count} patients with activity in the last {lookbackDays} days, {activePrograms} active programs, {dischargedPrograms} discharged programs, {scoredPatients} scored patients, and {goalsTracked} tracked goals.";
    }

    private static bool MatchesFilters(
        ProgressTrackingPatientVm patient,
        ProgressTrackingFilterState filterState)
    {
        if (!string.Equals(filterState.ProgramStatus, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(patient.StatusLabel, filterState.ProgramStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(filterState.TreatmentPhase, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(patient.TreatmentPhase, filterState.TreatmentPhase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string BuildCondition(
        NoteResponse? latestNote,
        AppointmentListItemResponse? latestAppointment,
        int goalCount)
    {
        if (latestNote is not null)
        {
            var noteLabel = FormatNoteType(latestNote.NoteType);
            var statusLabel = latestNote.NoteStatus switch
            {
                NoteStatus.Signed => "signed",
                NoteStatus.PendingCoSign => "pending co-sign",
                NoteStatus.Draft => "draft",
                _ => latestNote.NoteStatus.ToString().ToLowerInvariant()
            };

            return goalCount > 0
                ? $"{noteLabel} · {goalCount} tracked goals"
                : $"{noteLabel} · {statusLabel}";
        }

        if (latestAppointment is not null)
        {
            return $"Appointment: {latestAppointment.AppointmentType}";
        }

        return "No recent activity recorded";
    }

    private static string BuildLastAssessmentLabel(DateTime? latestActivityUtc)
    {
        return latestActivityUtc.HasValue
            ? latestActivityUtc.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : "No recent assessment";
    }

    private static string BuildStatusLabel(
        NoteResponse? latestNote,
        AppointmentListItemResponse? latestAppointment)
    {
        if (latestNote?.NoteType == NoteType.Discharge)
        {
            return "Discharged";
        }

        if (latestNote?.NoteStatus is NoteStatus.PendingCoSign or NoteStatus.Draft)
        {
            return "Pending";
        }

        if (latestNote is not null || latestAppointment is not null)
        {
            return "Active";
        }

        return "Pending";
    }

    private static BadgeVariant BuildStatusVariant(
        NoteResponse? latestNote,
        AppointmentListItemResponse? latestAppointment)
    {
        var status = BuildStatusLabel(latestNote, latestAppointment);
        return status switch
        {
            "Active" => BadgeVariant.Success,
            "Pending" => BadgeVariant.Warning,
            "Discharged" => BadgeVariant.Info,
            _ => BadgeVariant.Default
        };
    }

    private static string BuildTreatmentPhase(
        NoteResponse? latestNote,
        AppointmentListItemResponse? latestAppointment)
    {
        if (latestNote?.NoteType == NoteType.Discharge
            || ContainsIgnoreCase(latestAppointment?.AppointmentType, "discharge"))
        {
            return "discharge";
        }

        if (latestNote?.NoteType == NoteType.Evaluation
            || ContainsIgnoreCase(latestAppointment?.AppointmentType, "eval"))
        {
            return "evaluation";
        }

        return "rehab";
    }

    private static IEnumerable<string> ExtractGoals(ParsedProgressTrackingPayload? payload)
    {
        return payload?.Goals
            .Where(goal => !string.IsNullOrWhiteSpace(goal.Description))
            .OrderByDescending(goal => goal.Status == GoalStatus.Met)
            .ThenByDescending(goal => goal.Status == GoalStatus.Active)
            .ThenBy(goal => goal.Description, StringComparer.OrdinalIgnoreCase)
            .Select(goal => goal.Description.Trim())
            .Take(3)
            ?? Array.Empty<string>();
    }

    private static (int MetGoals, int ActiveGoals, int ArchivedGoals, int TotalGoals) ExtractGoalStatuses(ParsedProgressTrackingPayload? payload)
    {
        if (payload?.Goals is not { Count: > 0 } goals)
        {
            return (0, 0, 0, 0);
        }

        var met = goals.Count(goal => goal.Status == GoalStatus.Met);
        var active = goals.Count(goal => goal.Status == GoalStatus.Active);
        var archived = goals.Count(goal => goal.Status == GoalStatus.Archived);
        return (met, active, archived, goals.Count);
    }

    private ParsedOutcomeMeasure? GetLatestOutcomeMeasure(IReadOnlyList<ParsedOutcomeMeasure> measures)
    {
        if (measures.Count == 0)
        {
            return null;
        }

        return measures
            .OrderByDescending(measure => measure.RecordedAtUtc ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    private int GetNormalizedScorePercent(ParsedOutcomeMeasure measure)
    {
        var definition = outcomeMeasureRegistry.GetDefinition(measure.MeasureType);
        var range = definition.MaxScore - definition.MinScore;
        if (range <= 0)
        {
            return 0;
        }

        var normalized = (measure.Score - definition.MinScore) / range * 100;
        return Math.Clamp((int)Math.Round(normalized, MidpointRounding.AwayFromZero), 0, 100);
    }

    private string GetOutcomeMeasureLabel(OutcomeMeasureType measureType) =>
        outcomeMeasureRegistry.GetDefinition(measureType).Abbreviation;

    private string FormatOutcomeScore(ParsedOutcomeMeasure measure)
    {
        var definition = outcomeMeasureRegistry.GetDefinition(measure.MeasureType);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{measure.Score:0.#} {definition.ScoreUnit}");
    }

    private string GetOutcomeInterpretation(ParsedOutcomeMeasure measure) =>
        outcomeMeasureRegistry.InterpretScore(measure.MeasureType, measure.Score);

    private static bool TryParseScore(string? value, out double score)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            score = 0;
            return false;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out score)
            || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out score);
    }

    private static bool ContainsIgnoreCase(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatAge(int days)
    {
        return days switch
        {
            <= 0 => "today",
            1 => "1 day",
            _ => $"{days} days"
        };
    }

    private static int GetLookbackDays(string dateRange)
    {
        return dateRange switch
        {
            "7days" => 7,
            "30days" => 30,
            "90days" => 90,
            _ => 30
        };
    }

    private static ParsedProgressTrackingPayload? ParsePayload(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (LooksLikeWorkspaceV2Payload(document.RootElement))
            {
                var v2Payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, SerializerOptions);
                if (v2Payload is not null)
                {
                    var outcomeMeasures = v2Payload.Objective?.OutcomeMeasures?
                        .OfType<OutcomeMeasureEntryV2>()
                        .Where(entry => Enum.IsDefined(typeof(OutcomeMeasureType), entry.MeasureType))
                        .Select(entry => new ParsedOutcomeMeasure
                        {
                            MeasureType = entry.MeasureType,
                            Score = entry.Score,
                            RecordedAtUtc = entry.RecordedAtUtc
                        })
                        .ToList() ?? [];

                    var goals = v2Payload.Assessment?.Goals?
                        .OfType<WorkspaceGoalEntryV2>()
                        .Select(goal => new ParsedGoal
                        {
                            Description = goal.Description,
                            Status = goal.Status
                        })
                        .ToList() ?? [];

                    return new ParsedProgressTrackingPayload
                    {
                        OutcomeMeasures = outcomeMeasures,
                        Goals = goals
                    };
                }
            }

            return ParseLegacyPayload(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatNoteType(NoteType noteType)
    {
        return WorkspaceNoteTypeMapper.ToWorkspaceNoteType(noteType);
    }

    private static bool LooksLikeWorkspaceV2Payload(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.Number
                   && property.Value.TryGetInt32(out var schemaVersion)
                   && schemaVersion == WorkspaceSchemaVersions.EvalReevalProgressV2;
        }

        return false;
    }

    private static ParsedProgressTrackingPayload ParseLegacyPayload(JsonElement root)
    {
        var outcomeMeasures = new List<ParsedOutcomeMeasure>();
        if (TryGetPropertyIgnoreCase(root, "objective", out var objective)
            && objective.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(objective, "outcomeMeasures", out var measuresElement)
            && measuresElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var measure in measuresElement.EnumerateArray())
            {
                if (measure.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var scoreText = TryGetPropertyIgnoreCase(measure, "score", out var scoreElement)
                    ? GetScalarText(scoreElement)
                    : null;
                if (!TryParseScore(scoreText, out var parsedScore)
                    || !TryResolveLegacyMeasureType(measure, out var measureType))
                {
                    continue;
                }

                DateTime? recordedAtUtc = null;
                if (TryGetPropertyIgnoreCase(measure, "date", out var dateElement) && dateElement.ValueKind == JsonValueKind.String)
                {
                    recordedAtUtc = dateElement.TryGetDateTime(out var parsedDate)
                        ? parsedDate
                        : null;
                }

                outcomeMeasures.Add(new ParsedOutcomeMeasure
                {
                    MeasureType = measureType,
                    Score = parsedScore,
                    RecordedAtUtc = recordedAtUtc
                });
            }
        }

        var goals = new List<ParsedGoal>();
        if (TryGetPropertyIgnoreCase(root, "assessment", out var assessment)
            && assessment.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(assessment, "goals", out var goalsElement)
            && goalsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var goal in goalsElement.EnumerateArray())
            {
                if (goal.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var description = TryGetPropertyIgnoreCase(goal, "description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                    ? descriptionElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                var status = GoalStatus.Active;
                if (TryGetPropertyIgnoreCase(goal, "status", out var statusElement))
                {
                    if (statusElement.ValueKind == JsonValueKind.Number && statusElement.TryGetInt32(out var numericStatus))
                    {
                        status = Enum.IsDefined(typeof(GoalStatus), numericStatus)
                            ? (GoalStatus)numericStatus
                            : GoalStatus.Active;
                    }
                    else if (statusElement.ValueKind == JsonValueKind.String
                             && Enum.TryParse<GoalStatus>(statusElement.GetString(), ignoreCase: true, out var parsedStatus))
                    {
                        status = parsedStatus;
                    }
                }

                goals.Add(new ParsedGoal
                {
                    Description = description,
                    Status = status
                });
            }
        }

        return new ParsedProgressTrackingPayload
        {
            OutcomeMeasures = outcomeMeasures,
            Goals = goals
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetScalarText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }

    private static bool TryResolveLegacyMeasureType(JsonElement measure, out OutcomeMeasureType measureType)
    {
        measureType = default;
        foreach (var propertyName in new[] { "measureType", "type", "name", "measure", "abbreviation" })
        {
            if (!TryGetPropertyIgnoreCase(measure, propertyName, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var numeric)
                && Enum.IsDefined(typeof(OutcomeMeasureType), numeric))
            {
                measureType = (OutcomeMeasureType)numeric;
                return true;
            }

            var raw = GetScalarText(element);
            if (!string.IsNullOrWhiteSpace(raw)
                && TryResolveOutcomeMeasureType(raw, out measureType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveOutcomeMeasureType(string raw, out OutcomeMeasureType measureType)
    {
        var normalized = raw.Trim();
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            && Enum.IsDefined(typeof(OutcomeMeasureType), numeric))
        {
            measureType = (OutcomeMeasureType)numeric;
            return true;
        }

        var key = normalized.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);

        var resolved = key.ToUpperInvariant() switch
        {
            "ODI" or "OSWESTRY" or "OSWESTRYDISABILITYINDEX" => OutcomeMeasureType.OswestryDisabilityIndex,
            "DASH" => OutcomeMeasureType.DASH,
            "QUICKDASH" => OutcomeMeasureType.QuickDASH,
            "LEFS" or "LOWEREXTREMITYFUNCTIONALSCALE" => OutcomeMeasureType.LEFS,
            "NDI" or "NECKDISABILITYINDEX" => OutcomeMeasureType.NeckDisabilityIndex,
            "PSFS" or "PATIENTSPECIFICFUNCTIONALSCALE" => OutcomeMeasureType.PSFS,
            "VAS" or "VISUALANALOGSCALE" => OutcomeMeasureType.VAS,
            "NPRS" or "NUMERICPAINRATINGSCALE" => OutcomeMeasureType.NPRS,
            _ => (OutcomeMeasureType?)null
        };

        if (resolved is null)
        {
            return Enum.TryParse(normalized, ignoreCase: true, out measureType)
                && Enum.IsDefined(typeof(OutcomeMeasureType), measureType);
        }

        measureType = resolved.Value;
        return true;
    }

    private sealed class ParsedProgressTrackingPayload
    {
        public IReadOnlyList<ParsedOutcomeMeasure> OutcomeMeasures { get; init; } = [];
        public IReadOnlyList<ParsedGoal> Goals { get; init; } = [];
    }

    private sealed class ParsedOutcomeMeasure
    {
        public OutcomeMeasureType MeasureType { get; init; }
        public double Score { get; init; }
        public DateTime? RecordedAtUtc { get; init; }
    }

    private sealed class ParsedGoal
    {
        public string Description { get; init; } = string.Empty;
        public GoalStatus Status { get; init; }
    }

    private static IReadOnlyList<PatientActivityGroup> BuildActivityGroups(
        IReadOnlyList<NoteListItemApiResponse> notes,
        IReadOnlyList<AppointmentListItemResponse> appointments)
    {
        var groups = new Dictionary<Guid, PatientActivityGroup>();

        foreach (var note in notes)
        {
            if (!groups.TryGetValue(note.PatientId, out var group))
            {
                group = new PatientActivityGroup(note.PatientId, note.PatientName);
                groups[note.PatientId] = group;
            }

            group.Notes.Add(note);
        }

        foreach (var appointment in appointments)
        {
            if (!groups.TryGetValue(appointment.PatientRecordId, out var group))
            {
                group = new PatientActivityGroup(appointment.PatientRecordId, appointment.PatientName);
                groups[appointment.PatientRecordId] = group;
            }

            group.Appointments.Add(appointment);
        }

        return groups.Values
            .OrderByDescending(group => group.LatestActivityUtc)
            .ThenBy(group => group.PatientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class PatientActivityGroup(Guid patientId, string patientName)
    {
        public Guid PatientId { get; } = patientId;
        public string PatientName { get; } = patientName;
        public List<NoteListItemApiResponse> Notes { get; } = new();
        public List<AppointmentListItemResponse> Appointments { get; } = new();

        public DateTime LatestActivityUtc =>
            new[]
            {
                Notes.Count > 0 ? Notes.Max(note => note.LastModifiedUtc) : DateTime.MinValue,
                Appointments.Count > 0 ? Appointments.Max(appointment => appointment.StartTimeUtc) : DateTime.MinValue
            }.Max();
    }

    private async Task<Dictionary<Guid, NoteDetailResponse?>> BatchLoadNoteDetailsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var details = new Dictionary<Guid, NoteDetailResponse?>();
        foreach (var batch in ids
                     .Where(id => id != Guid.Empty)
                     .Distinct()
                     .Chunk(BatchReadLimit))
        {
            var result = await noteService.GetByIdsAsync(batch, cancellationToken);
            foreach (var detail in result)
            {
                if (detail?.Note is null)
                {
                    continue;
                }

                details[detail.Note.Id] = detail;
            }
        }

        return details;
    }

    private static NoteListItemApiResponse? GetLatestNote(PatientActivityGroup group)
    {
        return group.Notes
            .OrderByDescending(note => note.LastModifiedUtc)
            .ThenByDescending(note => note.DateOfService)
            .FirstOrDefault();
    }

    private static AppointmentListItemResponse? GetLatestAppointment(PatientActivityGroup group)
    {
        return group.Appointments
            .OrderByDescending(appointment => appointment.StartTimeUtc)
            .FirstOrDefault();
    }

    private static DateTime? GetLatestActivityUtc(
        NoteListItemApiResponse? latestNote,
        AppointmentListItemResponse? latestAppointment)
    {
        var noteTimestamp = latestNote?.LastModifiedUtc;
        var appointmentTimestamp = latestAppointment?.StartTimeUtc;

        return noteTimestamp switch
        {
            not null when appointmentTimestamp is null || noteTimestamp >= appointmentTimestamp => noteTimestamp,
            _ => appointmentTimestamp
        };
    }
}
