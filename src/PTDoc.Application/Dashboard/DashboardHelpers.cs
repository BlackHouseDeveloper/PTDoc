namespace PTDoc.Application.Dashboard;

/// <summary>
/// Utilities for dashboard data formatting and calculations
/// </summary>
public static class DashboardHelpers
{
    /// <summary>
    /// Calculate authorization urgency based on days until expiration
    /// </summary>
    public static AuthorizationUrgency CalculateAuthorizationUrgency(DateTime expirationDate)
    {
        var daysUntilExpiration = (expirationDate.Date - DateTime.UtcNow.Date).Days;
        
        return daysUntilExpiration switch
        {
            <= 7 => AuthorizationUrgency.High,
            <= 14 => AuthorizationUrgency.Medium,
            _ => AuthorizationUrgency.Low
        };
    }
    
    /// <summary>
    /// Format relative time (e.g., "2 hours ago", "Just now")
    /// </summary>
    public static string FormatTimeAgo(DateTime timestamp)
    {
        var timeSpan = DateTime.UtcNow - timestamp;
        
        if (timeSpan.TotalMinutes < 1)
            return "Just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} min ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hr ago";
        if (timeSpan.TotalDays < 7)
            return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays > 1 ? "s" : "")} ago";
        
        return timestamp.ToString("MMM dd");
    }
    
    /// <summary>
    /// Calculate days until expiration
    /// </summary>
    public static int DaysUntilExpiration(DateTime expirationDate)
    {
        return (expirationDate.Date - DateTime.UtcNow.Date).Days;
    }
    
    /// <summary>
    /// Format days remaining text (e.g., "5 days", "Today", "Expired")
    /// </summary>
    public static string FormatDaysRemaining(DateTime expirationDate)
    {
        var days = DaysUntilExpiration(expirationDate);
        
        return days switch
        {
            < 0 => "Expired",
            0 => "Today",
            1 => "Tomorrow",
            _ => $"{days} days"
        };
    }
    
    /// <summary>
    /// Calculate patient volume trend
    /// </summary>
    public static TrendDirection CalculateTrend(List<DailyVolume> data)
    {
        if (data.Count < 2)
            return TrendDirection.Stable;
        
        // Compare first half to second half average
        var midpoint = data.Count / 2;
        var firstHalfAvg = data.Take(midpoint).Average(d => d.PatientCount);
        var secondHalfAvg = data.Skip(midpoint).Average(d => d.PatientCount);
        
        var changePercent = ((secondHalfAvg - firstHalfAvg) / firstHalfAvg) * 100;
        
        return changePercent switch
        {
            > 10 => TrendDirection.Up,
            < -10 => TrendDirection.Down,
            _ => TrendDirection.Stable
        };
    }
}
