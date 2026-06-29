using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
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
        Assert.Equal("Cancelled", appointmentsByPatient[cancelledWithDraft.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[cancelledWithDraft.PatientName].VisitNoteId);
        Assert.Equal("No Show", appointmentsByPatient[noShowWithSigned.PatientName].VisitWorkflowStatus);
        Assert.Null(appointmentsByPatient[noShowWithSigned.PatientName].VisitNoteId);
    }

    private static SeededAppointmentCase SeedAppointmentCase(
        ApplicationDbContext db,
        Guid clinicianId,
        string prefix,
        string suffix,
        DateTime startTimeUtc,
        AppointmentStatus appointmentStatus,
        NoteStatus? noteStatus = null)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = prefix,
            LastName = suffix,
            DateOfBirth = new DateTime(1980, 1, 1),
            MedicalRecordNumber = $"{prefix[..12]}-{suffix}",
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

    private sealed record SeededAppointmentCase(Guid AppointmentId, Guid? NoteId, string PatientName);
}
