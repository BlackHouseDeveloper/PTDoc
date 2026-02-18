using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Integrations;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Service for managing external system mappings with unique constraint enforcement.
/// Prevents duplicate patient creation in external systems.
/// </summary>
public class ExternalSystemMappingService : IExternalSystemMappingService
{
    private readonly ApplicationDbContext _context;

    public ExternalSystemMappingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ExternalSystemMappingResult> GetOrCreateMappingAsync(
        Guid internalPatientId,
        string externalSystemName,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        // Check if mapping already exists
        var existing = await _context.ExternalSystemMappings
            .FirstOrDefaultAsync(
                m => m.ExternalSystemName == externalSystemName && m.ExternalId == externalId,
                cancellationToken);

        if (existing != null)
        {
            // Mapping exists - reuse it
            return new ExternalSystemMappingResult
            {
                Id = existing.Id,
                ExternalSystemName = existing.ExternalSystemName,
                ExternalId = existing.ExternalId,
                InternalPatientId = existing.InternalPatientId,
                CreatedAt = existing.CreatedAt,
                LastSyncedAt = existing.LastSyncedAt,
                IsActive = existing.IsActive,
                IsNewMapping = false
            };
        }

        // Create new mapping
        var mapping = new ExternalSystemMapping
        {
            Id = Guid.NewGuid(),
            ExternalSystemName = externalSystemName,
            ExternalId = externalId,
            InternalPatientId = internalPatientId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.ExternalSystemMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);

        return new ExternalSystemMappingResult
        {
            Id = mapping.Id,
            ExternalSystemName = mapping.ExternalSystemName,
            ExternalId = mapping.ExternalId,
            InternalPatientId = mapping.InternalPatientId,
            CreatedAt = mapping.CreatedAt,
            LastSyncedAt = mapping.LastSyncedAt,
            IsActive = mapping.IsActive,
            IsNewMapping = true
        };
    }

    public async Task<ExternalSystemMappingResult?> GetMappingByExternalIdAsync(
        string externalSystemName,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var mapping = await _context.ExternalSystemMappings
            .FirstOrDefaultAsync(
                m => m.ExternalSystemName == externalSystemName && m.ExternalId == externalId,
                cancellationToken);

        if (mapping == null)
        {
            return null;
        }

        return new ExternalSystemMappingResult
        {
            Id = mapping.Id,
            ExternalSystemName = mapping.ExternalSystemName,
            ExternalId = mapping.ExternalId,
            InternalPatientId = mapping.InternalPatientId,
            CreatedAt = mapping.CreatedAt,
            LastSyncedAt = mapping.LastSyncedAt,
            IsActive = mapping.IsActive,
            IsNewMapping = false
        };
    }

    public async Task<List<ExternalSystemMappingResult>> GetPatientMappingsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var mappings = await _context.ExternalSystemMappings
            .Where(m => m.InternalPatientId == patientId && m.IsActive)
            .ToListAsync(cancellationToken);

        return mappings.Select(m => new ExternalSystemMappingResult
        {
            Id = m.Id,
            ExternalSystemName = m.ExternalSystemName,
            ExternalId = m.ExternalId,
            InternalPatientId = m.InternalPatientId,
            CreatedAt = m.CreatedAt,
            LastSyncedAt = m.LastSyncedAt,
            IsActive = m.IsActive,
            IsNewMapping = false
        }).ToList();
    }
}
