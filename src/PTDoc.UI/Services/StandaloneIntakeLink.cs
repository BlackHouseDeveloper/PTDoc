namespace PTDoc.UI.Services;

public static class StandaloneIntakeLink
{
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
