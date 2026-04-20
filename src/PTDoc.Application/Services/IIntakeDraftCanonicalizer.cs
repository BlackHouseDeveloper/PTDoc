using PTDoc.Application.Intake;

namespace PTDoc.Application.Services;

public interface IIntakeDraftCanonicalizer
{
    IntakeResponseDraft CreateCanonicalCopy(
        IntakeResponseDraft draft,
        IntakeStructuredDataDto? structuredDataOverride = null);
}
