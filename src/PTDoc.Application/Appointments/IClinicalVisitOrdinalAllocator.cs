namespace PTDoc.Application.Appointments;

/// <summary>
/// Allocates the next patient-scoped clinical visit ordinal on the server.
/// Database uniqueness remains the final concurrency guard.
/// </summary>
public interface IClinicalVisitOrdinalAllocator
{
    Task<int> GetNextAsync(Guid patientId, CancellationToken cancellationToken = default);
}
