using PTDoc.Application.DTOs;
using PTDoc.Core.Models;

namespace PTDoc.Application.Services;

public interface INoteWriteService
{
    Task<NoteOperationResponse> CreateAsync(CreateNoteRequest request, CancellationToken ct = default);
    Task<NoteOperationResponse> UpdateAsync(ClinicalNote note, UpdateNoteRequest request, CancellationToken ct = default);
}
