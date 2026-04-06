using System.Net;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Appointments;

[Trait("Category", "CoreCi")]
public sealed class AppointmentApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetByPatientAsync_CallsPatientScopedEndpoint()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/appointments/by-patient/{patientId}", request.RequestUri!.AbsolutePath);
            Assert.Contains("startDate=2026-04-01", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("endDate=2026-04-30", request.RequestUri.Query, StringComparison.Ordinal);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
            {
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = patientId,
                    PatientName = "Alex Patient",
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc),
                    EndTimeUtc = new DateTime(2026, 4, 10, 15, 0, 0, DateTimeKind.Utc),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Scheduled"
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.GetByPatientAsync(
            patientId,
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30));

        var appointment = Assert.Single(result);
        Assert.Equal(patientId, appointment.PatientRecordId);
    }

    [Fact]
    public async Task GetCliniciansAsync_CallsCliniciansEndpoint()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/appointments/clinicians", request.RequestUri!.AbsolutePath);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
            {
                new AppointmentClinicianResponse
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Taylor PT"
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.GetCliniciansAsync();

        Assert.Collection(result, clinician => Assert.Equal("Taylor PT", clinician.DisplayName));
    }

    private static AppointmentApiService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new AppointmentApiService(client);
    }
}
