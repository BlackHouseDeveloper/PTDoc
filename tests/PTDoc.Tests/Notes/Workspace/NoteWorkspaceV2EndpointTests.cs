using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Api.Notes;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspaceV2EndpointTests
{
    [Fact]
    public async Task SaveWorkspace_RejectsUndefinedNoteTypeBeforeCallingService()
    {
        var service = new Mock<INoteWorkspaceV2Service>(MockBehavior.Strict);
        var request = new NoteWorkspaceV2SaveRequest
        {
            PatientId = Guid.NewGuid(),
            DateOfService = new DateTime(2026, 5, 19),
            NoteType = (NoteType)999,
            Payload = new NoteWorkspaceV2Payload()
        };

        var response = await ExecuteSaveWorkspaceAsync(request, service.Object);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NoteType", response.Body, StringComparison.Ordinal);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveWorkspace_ReturnsConflictForConcurrencyFailure()
    {
        var service = new Mock<INoteWorkspaceV2Service>(MockBehavior.Strict);
        service
            .Setup(item => item.SaveAsync(It.IsAny<NoteWorkspaceV2SaveRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Metric row was stale."));

        var request = new NoteWorkspaceV2SaveRequest
        {
            PatientId = Guid.NewGuid(),
            DateOfService = new DateTime(2026, 5, 19),
            NoteType = NoteType.Evaluation,
            Payload = new NoteWorkspaceV2Payload()
        };

        var response = await ExecuteSaveWorkspaceAsync(request, service.Object);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed while it was being saved", response.Body, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode StatusCode, string Body)> ExecuteSaveWorkspaceAsync(
        NoteWorkspaceV2SaveRequest request,
        INoteWorkspaceV2Service service)
    {
        var method = typeof(NoteWorkspaceV2Endpoints).GetMethod(
            "SaveWorkspace",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var task = (Task<IResult>)method!.Invoke(null, [request, service, CancellationToken.None])!;
        var result = await task;
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        context.Response.Body = body;

        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return ((HttpStatusCode)context.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
