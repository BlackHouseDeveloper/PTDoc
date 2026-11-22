namespace PTDoc.Infrastructure.Services;

public static class StaffTokenForwarding
{
    /// <summary>Prepares intake and kiosk requests that authenticate independently of the staff session.</summary>
    public static bool TryPrepareWithoutStaffToken(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (path is null) return false;

        // Standalone intake supplies its own session credentials.
        if (path.StartsWith("/api/v1/intake/access/", StringComparison.OrdinalIgnoreCase)) return true;

        var kioskPath = path.TrimEnd('/');
        var publicKioskPost = request.Method == HttpMethod.Post &&
            (kioskPath.Equals("/api/v1/kiosk/enroll", StringComparison.OrdinalIgnoreCase) ||
             kioskPath.Equals("/api/v1/kiosk/check-in", StringComparison.OrdinalIgnoreCase));
        if (publicKioskPost)
        {
            // Kiosk credentials are in the body. Do not forward a client's default staff bearer.
            request.Headers.Authorization = null;
        }

        return publicKioskPost;
    }
}
