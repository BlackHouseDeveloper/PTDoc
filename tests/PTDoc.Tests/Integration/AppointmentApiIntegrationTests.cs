using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class AppointmentApiIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;

    public AppointmentApiIntegrationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AppointmentsOverview_ReturnsVisitWorkflowStatus_FromLinkedNotes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var date = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var prefix = $"PR12-{Guid.NewGuid():N}";

        var scheduled = SeedAppointmentCase(db, clinician.Id, prefix, "Scheduled", date.AddHours(8), AppointmentStatus.Scheduled);
        var checkedIn = SeedAppointmentCase(db, clinician.Id, prefix, "CheckedIn", date.AddHours(9), AppointmentStatus.CheckedIn);
        var draftNote = SeedAppointmentCase(db, clinician.Id, prefix, "DraftNote", date.AddHours(10), AppointmentStatus.CheckedIn, NoteStatus.Draft);
        var pendingNote = SeedAppointmentCase(db, clinician.Id, prefix, "PendingNote", date.AddHours(11), AppointmentStatus.CheckedIn, NoteStatus.PendingCoSign);
        var signedNote = SeedAppointmentCase(db, clinician.Id, prefix, "SignedNote", date.AddHours(12), AppointmentStatus.CheckedIn, NoteStatus.Signed);
        var completed = SeedAppointmentCase(db, clinician.Id, prefix, "Completed", date.AddHours(13), AppointmentStatus.Completed);
        var completedWithDraft = SeedAppointmentCase(db, clinician.Id, prefix, "CompletedWithDraft", date.AddHours(16), AppointmentStatus.Completed, NoteStatus.Draft);
        var cancelledWithDraft = SeedAppointmentCase(db, clinician.Id, prefix, "CancelledWithDraft", date.AddHours(14), AppointmentStatus.Cancelled, NoteStatus.Draft);
        var noShowWithSigned = SeedAppointmentCase(db, clinician.Id, prefix, "NoShowWithSigned", date.AddHours(15), AppointmentStatus.NoShow, NoteStatus.Signed);

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync($"/api/v1/appointments?startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var overview = await response.Content.ReadFromJsonAsync<AppointmentsOverviewResponse>();
        Assert.NotNull(overview);

        var appointmentsByPatient = overview!.Appointments
            .Where(appointment => appointment.PatientName.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(appointment => appointment.PatientName);

        Assert.Equal("Scheduled", appointmentsByPatient[scheduled.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[scheduled.PatientName].VisitNoteId);
        Assert.Equal("Checked In", appointmentsByPatient[checkedIn.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[checkedIn.PatientName].VisitNoteId);
        Assert.Equal("Note Started", appointmentsByPatient[draftNote.PatientName].VisitWorkflowStatus);
        Assert.Equal(draftNote.NoteId, appointmentsByPatient[draftNote.PatientName].VisitNoteId);
        Assert.Equal("Note Started", appointmentsByPatient[pendingNote.PatientName].VisitWorkflowStatus);
        Assert.Equal(pendingNote.NoteId, appointmentsByPatient[pendingNote.PatientName].VisitNoteId);
        Assert.Equal("Completed", appointmentsByPatient[signedNote.PatientName].VisitWorkflowStatus);
        Assert.Equal(signedNote.NoteId, appointmentsByPatient[signedNote.PatientName].VisitNoteId);
        Assert.Equal("Completed", appointmentsByPatient[completed.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[completed.PatientName].VisitNoteId);
        Assert.Equal("Completed", appointmentsByPatient[completedWithDraft.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[completedWithDraft.PatientName].VisitNoteId);
        Assert.Equal("Cancelled", appointmentsByPatient[cancelledWithDraft.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[cancelledWithDraft.PatientName].VisitNoteId);
        Assert.Equal("No Show", appointmentsByPatient[noShowWithSigned.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[noShowWithSigned.PatientName].VisitNoteId);
    }

    [Fact]
    public async Task CheckInAppointment_WithCopayDue_RequiresPaymentBeforeStatusChange()
    {
        using var factory = CreatePaymentConfiguredFactory(new FixedPaymentService(new PaymentResult { Success = true }));
        await EnsurePaymentFactoryDatabaseAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var seeded = SeedAppointmentCase(
            db,
            clinician.Id,
            $"PAY-{Guid.NewGuid():N}",
            "Due",
            new DateTime(2026, 7, 5, 14, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled,
            payerInfoJson: """{"copayAmount":"30.00"}""");
        await db.SaveChangesAsync();

        using var client = CreateClientWithRole(factory, Roles.FrontDesk);
        using var response = await client.PostAsync($"/api/v1/appointments/{seeded.AppointmentId}/check-in", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var appointmentStatus = await db.Appointments
            .Where(appointment => appointment.Id == seeded.AppointmentId)
            .Select(appointment => appointment.Status)
            .SingleAsync();
        Assert.Equal(AppointmentStatus.Scheduled, appointmentStatus);

        using var overviewResponse = await client.GetAsync("/api/v1/appointments?startDate=2026-07-05&endDate=2026-07-05");
        Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);
        var overview = await overviewResponse.Content.ReadFromJsonAsync<AppointmentsOverviewResponse>();
        var appointment = Assert.Single(
            overview!.Appointments,
            item => item.Id == seeded.AppointmentId);
        Assert.Equal(30m, appointment.CopayAmount);
        Assert.Equal("Copay due", appointment.CopayStatusLabel);
    }

    [Fact]
    public async Task CheckInPayment_WhenPaymentFails_RecordsFailureAndKeepsAppointmentScheduled()
    {
        var gatewayErrorCode = new string('E', 90);
        var gatewayErrorMessage = new string('M', 550);
        using var factory = CreatePaymentConfiguredFactory(new FixedPaymentService(new PaymentResult
        {
            Success = false,
            ErrorCode = gatewayErrorCode,
            ErrorMessage = gatewayErrorMessage,
            Amount = 30m
        }));
        await EnsurePaymentFactoryDatabaseAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var seeded = SeedAppointmentCase(
            db,
            clinician.Id,
            $"PAY-{Guid.NewGuid():N}",
            "Failed",
            new DateTime(2026, 7, 5, 15, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled,
            payerInfoJson: """{"copayAmount":"30.00"}""");
        await db.SaveChangesAsync();

        using var client = CreateClientWithRole(factory, Roles.FrontDesk);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/appointments/{seeded.AppointmentId}/check-in-payment",
            new AppointmentCheckInPaymentRequest
            {
                OpaqueDataDescriptor = "COMMON.ACCEPT.INAPP.PAYMENT",
                OpaqueDataToken = "opaque-token"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AppointmentCheckInPaymentResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Payment.Success);
        Assert.Equal("Scheduled", body.Appointment?.AppointmentStatus);

        var transaction = await db.AppointmentPaymentTransactions.SingleAsync(payment => payment.AppointmentId == seeded.AppointmentId);
        Assert.Equal(AppointmentPaymentStatus.Failed, transaction.Status);
        Assert.Equal(gatewayErrorCode[..80], transaction.GatewayErrorCode);
        Assert.Equal(gatewayErrorMessage[..500], transaction.GatewayErrorMessage);
    }

    [Fact]
    public async Task CheckInPayment_WhenPaymentSucceeds_RecordsTransactionAndChecksIn()
    {
        var gatewayTransactionId = new string('T', 130);
        var gatewayAuthorizationCode = new string('A', 90);
        using var factory = CreatePaymentConfiguredFactory(new FixedPaymentService(new PaymentResult
        {
            Success = true,
            TransactionId = gatewayTransactionId,
            AuthorizationCode = gatewayAuthorizationCode,
            Amount = 30m,
            ProcessedAt = DateTime.UtcNow
        }));
        await EnsurePaymentFactoryDatabaseAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var seeded = SeedAppointmentCase(
            db,
            clinician.Id,
            $"PAY-{Guid.NewGuid():N}",
            "Success",
            new DateTime(2026, 7, 5, 16, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled,
            payerInfoJson: """{"copayAmount":"30.00"}""");
        await db.SaveChangesAsync();

        using var client = CreateClientWithRole(factory, Roles.FrontDesk);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/appointments/{seeded.AppointmentId}/check-in-payment",
            new AppointmentCheckInPaymentRequest
            {
                OpaqueDataDescriptor = "COMMON.ACCEPT.INAPP.PAYMENT",
                OpaqueDataToken = "opaque-token"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AppointmentCheckInPaymentResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Payment.Success);
        Assert.Equal("Checked In", body.Appointment?.AppointmentStatus);
        Assert.False(body.Appointment?.CanRecordCopay);
        Assert.Equal("Copay paid", body.Appointment?.CopayStatusLabel);

        var transaction = await db.AppointmentPaymentTransactions.SingleAsync(payment => payment.AppointmentId == seeded.AppointmentId);
        Assert.Equal(AppointmentPaymentStatus.Succeeded, transaction.Status);
        Assert.Equal(gatewayTransactionId[..120], transaction.TransactionId);
        Assert.Equal(gatewayAuthorizationCode[..80], transaction.AuthorizationCode);
    }

    [Fact]
    public async Task CheckInPayment_WhenConcurrentRequests_ChargesGatewayOnce()
    {
        var paymentService = new BlockingPaymentService();
        using var factory = CreatePaymentConfiguredFactory(paymentService);
        await EnsurePaymentFactoryDatabaseAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var seeded = SeedAppointmentCase(
            db,
            clinician.Id,
            $"PAY-{Guid.NewGuid():N}",
            "Concurrent",
            new DateTime(2026, 7, 5, 17, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled,
            payerInfoJson: """{"copayAmount":"30.00"}""");
        await db.SaveChangesAsync();

        using var client = CreateClientWithRole(factory, Roles.FrontDesk);
        var firstRequest = PostCheckInPaymentAsync(client, seeded.AppointmentId);
        await paymentService.WaitForFirstRequestAsync();
        var secondRequest = PostCheckInPaymentAsync(client, seeded.AppointmentId);
        paymentService.Release();

        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(1, paymentService.CallCount);

        var transactions = await db.AppointmentPaymentTransactions
            .Where(payment => payment.AppointmentId == seeded.AppointmentId)
            .ToListAsync();
        var transaction = Assert.Single(transactions);
        Assert.Equal(AppointmentPaymentStatus.Succeeded, transaction.Status);
        Assert.Equal("60123456789", transaction.TransactionId);
    }

    private static SeededAppointmentCase SeedAppointmentCase(
        ApplicationDbContext db,
        Guid clinicianId,
        string prefix,
        string suffix,
        DateTime startTimeUtc,
        AppointmentStatus appointmentStatus,
        NoteStatus? noteStatus = null,
        string? payerInfoJson = null)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = prefix,
            LastName = suffix,
            DateOfBirth = new DateTime(1980, 1, 1),
            MedicalRecordNumber = $"{prefix[..12]}-{suffix}",
            PayerInfoJson = payerInfoJson ?? "{}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ClinicalId = clinicianId,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = startTimeUtc.AddMinutes(45),
            AppointmentType = AppointmentType.FollowUp,
            Status = appointmentStatus,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };

        db.Patients.Add(patient);
        db.Appointments.Add(appointment);

        Guid? noteId = null;
        if (noteStatus.HasValue)
        {
            noteId = Guid.NewGuid();
            db.ClinicalNotes.Add(new ClinicalNote
            {
                Id = noteId.Value,
                PatientId = patient.Id,
                AppointmentId = appointment.Id,
                NoteType = NoteType.Daily,
                NoteStatus = noteStatus.Value,
                ContentJson = "{}",
                CptCodesJson = "[]",
                DateOfService = startTimeUtc.Date,
                CreatedUtc = startTimeUtc,
                LastModifiedUtc = startTimeUtc,
                ModifiedByUserId = clinicianId,
                SyncState = SyncState.Pending,
                SignatureHash = noteStatus == NoteStatus.Signed ? "signed-pr12-note" : null,
                SignedUtc = noteStatus == NoteStatus.Signed ? startTimeUtc : null,
                SignedByUserId = noteStatus == NoteStatus.Signed ? clinicianId : null
            });
        }

        return new SeededAppointmentCase(appointment.Id, noteId, $"{patient.FirstName} {patient.LastName}");
    }

    private WebApplicationFactory<Program> CreatePaymentConfiguredFactory(IPaymentService paymentService) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Integrations:Payments:Enabled"] = "true",
                    ["Integrations:Payments:Environment"] = "Sandbox",
                    ["Integrations:Payments:ApiLoginId"] = "test-login",
                    ["Integrations:Payments:TransactionKey"] = "test-transaction-key",
                    ["Integrations:Payments:ClientKey"] = "test-client-key"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentService>();
                services.AddSingleton(paymentService);
            });
        });

    private static HttpClient CreateClientWithRole(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static Task<HttpResponseMessage> PostCheckInPaymentAsync(HttpClient client, Guid appointmentId) =>
        client.PostAsJsonAsync(
            $"/api/v1/appointments/{appointmentId}/check-in-payment",
            new AppointmentCheckInPaymentRequest
            {
                OpaqueDataDescriptor = "COMMON.ACCEPT.INAPP.PAYMENT",
                OpaqueDataToken = "opaque-token"
            });

    private static async Task EnsurePaymentFactoryDatabaseAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        if (await db.Users.AnyAsync(user => user.Username == "integration-pt"))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "integration-pt",
            PinHash = "integration-test-pin-hash",
            FirstName = "Integration",
            LastName = "PT",
            Role = Roles.PT,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private sealed class FixedPaymentService(PaymentResult result) : IPaymentService
    {
        public Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        {
            result.Amount ??= request.Amount;
            result.ProcessedAt = result.ProcessedAt == default ? DateTime.UtcNow : result.ProcessedAt;
            return Task.FromResult(result);
        }

        public Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentResult
            {
                Success = true,
                TransactionId = transactionId,
                Amount = amount,
                ProcessedAt = DateTime.UtcNow
            });

        public Task<PaymentResult> GetTransactionDetailsAsync(string transactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentResult
            {
                Success = true,
                TransactionId = transactionId,
                ProcessedAt = DateTime.UtcNow
            });
    }

    private sealed class BlockingPaymentService : IPaymentService
    {
        private readonly TaskCompletionSource firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            firstRequestStarted.TrySetResult();
            await releaseRequest.Task.WaitAsync(cancellationToken);

            return new PaymentResult
            {
                Success = true,
                TransactionId = "60123456789",
                AuthorizationCode = "AUTH42",
                Amount = request.Amount,
                ProcessedAt = DateTime.UtcNow
            };
        }

        public Task WaitForFirstRequestAsync() => firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => releaseRequest.TrySetResult();

        public Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentResult
            {
                Success = true,
                TransactionId = transactionId,
                Amount = amount,
                ProcessedAt = DateTime.UtcNow
            });

        public Task<PaymentResult> GetTransactionDetailsAsync(string transactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentResult
            {
                Success = true,
                TransactionId = transactionId,
                ProcessedAt = DateTime.UtcNow
            });
    }

    private sealed record SeededAppointmentCase(Guid AppointmentId, Guid? NoteId, string PatientName);
}
