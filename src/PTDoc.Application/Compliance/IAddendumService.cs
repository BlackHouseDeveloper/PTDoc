using System.Text.Json;

namespace PTDoc.Application.Compliance;

public interface IAddendumService
{
    Task<AddendumResult> CreateAddendumAsync(Guid noteId, JsonElement content, Guid userId, CancellationToken ct = default);
}
