using System.Collections.Concurrent;

namespace PTDoc.Api.Diagnostics;

public static class AiDiagnosticsFaultModes
{
    public const string PlanGenerationFailure = "plan_generation_failure";
    public const string ClinicalSummaryAcceptFailure = "clinical_summary_accept_failure";

    public static string? Normalize(string? mode)
    {
        var trimmed = mode?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (string.Equals(trimmed, PlanGenerationFailure, StringComparison.OrdinalIgnoreCase))
        {
            return PlanGenerationFailure;
        }

        if (string.Equals(trimmed, ClinicalSummaryAcceptFailure, StringComparison.OrdinalIgnoreCase))
        {
            return ClinicalSummaryAcceptFailure;
        }

        return null;
    }
}

public sealed class AiDiagnosticsFaultRequest
{
    public string Mode { get; set; } = string.Empty;
    public Guid NoteId { get; set; }
    public Guid? TargetUserId { get; set; }
}

public sealed class AiDiagnosticsFaultSnapshot
{
    public string Mode { get; init; } = string.Empty;
    public Guid NoteId { get; init; }
    public Guid TargetUserId { get; init; }
    public Guid ArmedByUserId { get; init; }
    public DateTimeOffset ArmedAtUtc { get; init; }
}

internal readonly record struct AiDiagnosticsFaultKey(string Mode, Guid NoteId, Guid TargetUserId);

internal sealed class AiDiagnosticsFaultEntry
{
    public required string Mode { get; init; }
    public required Guid NoteId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required Guid ArmedByUserId { get; init; }
    public required DateTimeOffset ArmedAtUtc { get; init; }
}

public sealed class AiDiagnosticsFaultStore(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<AiDiagnosticsFaultKey, AiDiagnosticsFaultEntry> faults = new();

    public IReadOnlyList<AiDiagnosticsFaultSnapshot> List()
    {
        return faults.Values
            .Select(ToSnapshot)
            .OrderBy(entry => entry.Mode, StringComparer.Ordinal)
            .ThenBy(entry => entry.NoteId)
            .ThenBy(entry => entry.TargetUserId)
            .ToArray();
    }

    public AiDiagnosticsFaultSnapshot Arm(
        string mode,
        Guid noteId,
        Guid targetUserId,
        Guid armedByUserId)
    {
        var normalizedMode = AiDiagnosticsFaultModes.Normalize(mode)
            ?? throw new ArgumentException($"Unsupported AI fault mode '{mode}'.", nameof(mode));

        if (noteId == Guid.Empty)
        {
            throw new ArgumentException("NoteId must be a non-empty GUID.", nameof(noteId));
        }

        if (targetUserId == Guid.Empty)
        {
            throw new ArgumentException("TargetUserId must be a non-empty GUID.", nameof(targetUserId));
        }

        if (armedByUserId == Guid.Empty)
        {
            throw new ArgumentException("ArmedByUserId must be a non-empty GUID.", nameof(armedByUserId));
        }

        var entry = new AiDiagnosticsFaultEntry
        {
            Mode = normalizedMode,
            NoteId = noteId,
            TargetUserId = targetUserId,
            ArmedByUserId = armedByUserId,
            ArmedAtUtc = timeProvider.GetUtcNow()
        };

        faults[new AiDiagnosticsFaultKey(normalizedMode, noteId, targetUserId)] = entry;
        return ToSnapshot(entry);
    }

    public bool Clear(
        string mode,
        Guid noteId,
        Guid targetUserId,
        out AiDiagnosticsFaultSnapshot? clearedFault)
    {
        clearedFault = null;

        var normalizedMode = AiDiagnosticsFaultModes.Normalize(mode);
        if (normalizedMode is null || noteId == Guid.Empty || targetUserId == Guid.Empty)
        {
            return false;
        }

        if (!faults.TryRemove(new AiDiagnosticsFaultKey(normalizedMode, noteId, targetUserId), out var entry))
        {
            return false;
        }

        clearedFault = ToSnapshot(entry);
        return true;
    }

    public bool TryConsume(
        string mode,
        Guid noteId,
        Guid currentUserId,
        out AiDiagnosticsFaultSnapshot? consumedFault)
    {
        consumedFault = null;

        var normalizedMode = AiDiagnosticsFaultModes.Normalize(mode);
        if (normalizedMode is null || noteId == Guid.Empty || currentUserId == Guid.Empty)
        {
            return false;
        }

        if (!faults.TryRemove(new AiDiagnosticsFaultKey(normalizedMode, noteId, currentUserId), out var entry))
        {
            return false;
        }

        consumedFault = ToSnapshot(entry);
        return true;
    }

    private static AiDiagnosticsFaultSnapshot ToSnapshot(AiDiagnosticsFaultEntry entry)
    {
        return new AiDiagnosticsFaultSnapshot
        {
            Mode = entry.Mode,
            NoteId = entry.NoteId,
            TargetUserId = entry.TargetUserId,
            ArmedByUserId = entry.ArmedByUserId,
            ArmedAtUtc = entry.ArmedAtUtc
        };
    }
}
