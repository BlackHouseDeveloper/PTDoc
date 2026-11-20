namespace PTDoc.UI.Services;

public static class StandaloneIntakeLink
{
    // Patient invitations identify the intake through the signed invite. noteId is a staff chart link.
    public static bool IsStaffVisit(Uri uri)
        => System.Web.HttpUtility.ParseQueryString(uri.Query).GetValues("noteId") is not null;

    public static bool IsPatientMode(Uri uri, out string? invite)
    {
        invite = null;
        if (!uri.AbsolutePath.Equals("/intake", StringComparison.OrdinalIgnoreCase) &&
            !uri.AbsolutePath.StartsWith("/intake/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        invite = query["invite"];
        return invite is not null || query.GetValues("mode")?.Contains("patient", StringComparer.OrdinalIgnoreCase) == true;
    }
}
