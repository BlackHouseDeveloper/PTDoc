namespace PTDoc.Application.Intake;

public enum IntakeProfileAction
{
    FillBlankFields,
    UsePrimary,
    OmitPrimary,
    UseSecondary,
    OmitSecondary
}

/// <summary>Explicit consent to use the profile version shown by the server.</summary>
public sealed class IntakeProfileUpdate
{
    public IntakeProfileAction Action { get; set; }
    public string? ExpectedFingerprint { get; set; }
}
