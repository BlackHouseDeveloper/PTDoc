using PTDoc.Application.Dashboard;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Mock implementation of IDashboardService for development
/// TODO: Replace with actual implementation that fetches from API/database
/// </summary>
public class MockDashboardService : IDashboardService
{
    public Task<DashboardData> GetDashboardDataAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        var data = new DashboardData
        {
            Metrics = GenerateMockMetrics(),
            RecentActivities = GenerateMockActivities(),
            ExpiringAuthorizations = GenerateMockExpiringAuthorizations(),
            PatientVolume = GenerateMockPatientVolume(PatientVolumePeriod.Last7Days),
            LastUpdated = DateTime.UtcNow
        };
        
        return Task.FromResult(data);
    }
    
    public Task<DashboardMetrics> GetMetricsAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GenerateMockMetrics());
    }
    
    public Task<List<RecentActivity>> GetRecentActivitiesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var activities = GenerateMockActivities();
        return Task.FromResult(activities.Take(count).ToList());
    }
    
    public Task<List<ExpiringAuthorization>> GetExpiringAuthorizationsAsync(int daysThreshold = 30, CancellationToken cancellationToken = default)
    {
        var authorizations = GenerateMockExpiringAuthorizations();
        return Task.FromResult(authorizations
            .Where(a => DashboardHelpers.DaysUntilExpiration(a.ExpirationDate) <= daysThreshold)
            .ToList());
    }
    
    public Task<PatientVolumeData> GetPatientVolumeAsync(PatientVolumePeriod period, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GenerateMockPatientVolume(period));
    }
    
    private static DashboardMetrics GenerateMockMetrics()
    {
        return new DashboardMetrics
        {
            ActivePatients = 247,
            ActivePatientsTrend = 12, // +12 from last period
            
            PendingNotes = 18,
            UrgentPendingNotes = 5, // 5 need attention within 24 hours
            
            AuthorizationsExpiring = 23,
            AuthorizationsExpiringUrgent = 8 // 8 expiring within 7 days
        };
    }
    
    private static List<RecentActivity> GenerateMockActivities()
    {
        var now = DateTime.UtcNow;
        
        return new List<RecentActivity>
        {
            new()
            {
                Id = "1",
                Type = ActivityType.NoteCompleted,
                Description = "SOAP note completed",
                PatientId = "P001",
                PatientName = "Sarah Johnson",
                Timestamp = now.AddMinutes(-5)
            },
            new()
            {
                Id = "2",
                Type = ActivityType.AppointmentCheckedIn,
                Description = "Patient checked in for appointment",
                PatientId = "P002",
                PatientName = "Michael Chen",
                Timestamp = now.AddMinutes(-15)
            },
            new()
            {
                Id = "3",
                Type = ActivityType.IntakeCompleted,
                Description = "Patient intake completed",
                PatientId = "P003",
                PatientName = "Emily Rodriguez",
                Timestamp = now.AddHours(-1)
            },
            new()
            {
                Id = "4",
                Type = ActivityType.AuthorizationUpdated,
                Description = "Authorization updated",
                PatientId = "P004",
                PatientName = "David Martinez",
                Timestamp = now.AddHours(-2)
            },
            new()
            {
                Id = "5",
                Type = ActivityType.NoteCompleted,
                Description = "Progress note completed",
                PatientId = "P005",
                PatientName = "Jessica Williams",
                Timestamp = now.AddHours(-3)
            },
            new()
            {
                Id = "6",
                Type = ActivityType.PatientAdded,
                Description = "New patient profile created",
                PatientId = "P006",
                PatientName = "Robert Thompson",
                Timestamp = now.AddHours(-4)
            },
            new()
            {
                Id = "7",
                Type = ActivityType.AppointmentScheduled,
                Description = "Appointment scheduled",
                PatientId = "P007",
                PatientName = "Maria Garcia",
                Timestamp = now.AddHours(-5)
            },
            new()
            {
                Id = "8",
                Type = ActivityType.NoteUpdated,
                Description = "Evaluation note updated",
                PatientId = "P001",
                PatientName = "Sarah Johnson",
                Timestamp = now.AddHours(-6)
            }
        };
    }
    
    private static List<ExpiringAuthorization> GenerateMockExpiringAuthorizations()
    {
        var today = DateTime.UtcNow.Date;
        
        var authorizations = new List<ExpiringAuthorization>
        {
            new()
            {
                Id = "A001",
                PatientId = "P001",
                PatientName = "Sarah Johnson",
                MedicalRecordNumber = "MRN-2024-001",
                ExpirationDate = today.AddDays(3),
                VisitsUsed = 8,
                VisitsTotal = 12,
                Payer = "Blue Cross Blue Shield"
            },
            new()
            {
                Id = "A002",
                PatientId = "P008",
                PatientName = "James Wilson",
                MedicalRecordNumber = "MRN-2024-008",
                ExpirationDate = today.AddDays(5),
                VisitsUsed = 10,
                VisitsTotal = 12,
                Payer = "Aetna"
            },
            new()
            {
                Id = "A003",
                PatientId = "P009",
                PatientName = "Patricia Brown",
                MedicalRecordNumber = "MRN-2024-009",
                ExpirationDate = today.AddDays(7),
                VisitsUsed = 6,
                VisitsTotal = 10,
                Payer = "United Healthcare"
            },
            new()
            {
                Id = "A004",
                PatientId = "P010",
                PatientName = "Christopher Lee",
                MedicalRecordNumber = "MRN-2024-010",
                ExpirationDate = today.AddDays(10),
                VisitsUsed = 5,
                VisitsTotal = 8,
                Payer = "Cigna"
            },
            new()
            {
                Id = "A005",
                PatientId = "P011",
                PatientName = "Linda Davis",
                MedicalRecordNumber = "MRN-2024-011",
                ExpirationDate = today.AddDays(12),
                VisitsUsed = 7,
                VisitsTotal = 12,
                Payer = "Medicare"
            },
            new()
            {
                Id = "A006",
                PatientId = "P012",
                PatientName = "Daniel Moore",
                MedicalRecordNumber = "MRN-2024-012",
                ExpirationDate = today.AddDays(14),
                VisitsUsed = 4,
                VisitsTotal = 10,
                Payer = "Blue Cross Blue Shield"
            },
            new()
            {
                Id = "A007",
                PatientId = "P013",
                PatientName = "Karen Taylor",
                MedicalRecordNumber = "MRN-2024-013",
                ExpirationDate = today.AddDays(18),
                VisitsUsed = 9,
                VisitsTotal = 12,
                Payer = "Humana"
            },
            new()
            {
                Id = "A008",
                PatientId = "P014",
                PatientName = "Steven Anderson",
                MedicalRecordNumber = "MRN-2024-014",
                ExpirationDate = today.AddDays(22),
                VisitsUsed = 3,
                VisitsTotal = 8,
                Payer = "Aetna"
            },
            new()
            {
                Id = "A009",
                PatientId = "P015",
                PatientName = "Nancy Thomas",
                MedicalRecordNumber = "MRN-2024-015",
                ExpirationDate = today.AddDays(25),
                VisitsUsed = 8,
                VisitsTotal = 12,
                Payer = "United Healthcare"
            },
            new()
            {
                Id = "A010",
                PatientId = "P016",
                PatientName = "Paul Jackson",
                MedicalRecordNumber = "MRN-2024-016",
                ExpirationDate = today.AddDays(28),
                VisitsUsed = 6,
                VisitsTotal = 10,
                Payer = "Cigna"
            }
        };
        
        // Calculate urgency for each authorization
        var updatedAuthorizations = new List<ExpiringAuthorization>();
        foreach (var auth in authorizations)
        {
            var updated = auth with { Urgency = DashboardHelpers.CalculateAuthorizationUrgency(auth.ExpirationDate) };
            updatedAuthorizations.Add(updated);
        }
        
        return updatedAuthorizations.OrderBy(a => a.ExpirationDate).ToList();
    }
    
    private static PatientVolumeData GenerateMockPatientVolume(PatientVolumePeriod period)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = period switch
        {
            PatientVolumePeriod.Last7Days => 7,
            PatientVolumePeriod.Last30Days => 30,
            PatientVolumePeriod.Last90Days => 90,
            _ => 7
        };
        
        var random = new Random(42); // Fixed seed for consistent mock data
        var dailyData = new List<DailyVolume>();
        
        for (int i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var baseCount = 25;
            var variance = random.Next(-8, 12);
            var count = Math.Max(0, baseCount + variance);
            
            dailyData.Add(new DailyVolume
            {
                Date = date,
                PatientCount = count
            });
        }
        
        var total = dailyData.Sum(d => d.PatientCount);
        var average = dailyData.Average(d => d.PatientCount);
        var peak = dailyData.MaxBy(d => d.PatientCount);
        var trend = DashboardHelpers.CalculateTrend(dailyData);
        
        return new PatientVolumeData
        {
            Period = period,
            DailyData = dailyData,
            Summary = new VolumeSummary
            {
                Total = total,
                AveragePerDay = Math.Round(average, 1),
                Peak = peak?.PatientCount ?? 0,
                PeakDate = peak?.Date ?? today,
                Trend = trend
            }
        };
    }
}
