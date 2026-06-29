using Microsoft.AspNetCore.Components.Forms;
using PTDoc.Application.DTOs;

namespace PTDoc.UI.Services;

public interface IPatientChartStorageService
{
    Task<IReadOnlyList<PatientDocumentResponse>> ListDocumentsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<PatientDocumentResponse> UploadDocumentAsync(
        Guid patientId,
        IBrowserFile file,
        string documentType,
        string? notes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatientCommunicationLogEntryResponse>> ListCommunicationLogEntriesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<PatientCommunicationLogEntryResponse> CreateCommunicationLogEntryAsync(
        Guid patientId,
        CreatePatientCommunicationLogEntryRequest request,
        CancellationToken cancellationToken = default);
}
