namespace PTDoc.UI.Services;

/// <summary>Remembers the current verified invite in this circuit or WebView for OTP renewal.</summary>
public sealed class IntakeInviteRenewal
{
    private string? patientPath;
    private string? verifiedInvite;

    public void Clear()
    {
        patientPath = null;
        verifiedInvite = null;
    }

    public void Remember(Uri uri, string invite)
    {
        patientPath = uri.AbsolutePath.TrimEnd('/');
        verifiedInvite = invite;
    }

    // This recovers only the invite needed for a new OTP. Access still requires a valid session.
    public string? ForSession(Uri uri, IntakeSessionToken? session)
    {
        if (StandaloneIntakeLink.IsStaffVisit(uri) ||
            !string.Equals(patientPath, uri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return session is null || session.MatchesInvite(verifiedInvite) ? verifiedInvite : null;
    }
}
