namespace PTDoc.UI.Services;

/// <summary>Remembers the current verified invite in this circuit or WebView for OTP renewal.</summary>
public sealed class IntakeInviteRenewal
{
    private string? patientPath;
    private string? verifiedInvite;

    public void Remember(Uri uri, string invite)
    {
        patientPath = uri.AbsolutePath.TrimEnd('/');
        verifiedInvite = invite;
    }

    public string? ForSession(Uri uri, IntakeSessionToken session)
    {
        if (!string.Equals(patientPath, uri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return session.MatchesInvite(verifiedInvite) ? verifiedInvite : null;
    }
}
