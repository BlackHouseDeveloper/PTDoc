using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Tests.Integration;
using Xunit;

namespace PTDoc.Tests.Security;

[Trait("Category", "RBAC")]
public sealed class RbacHttpSmokeTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;

    public RbacHttpSmokeTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_Request_Returns_401()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Billing_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Billing);

        using var response = await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Owner);

        using var response = await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PTA_Cannot_Create_EvalNote_Returns_403()
    {
        using var ptaClient = _factory.CreateClientWithRole(Roles.PTA);
        using var ptClient = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await EndToEndWorkflowTests.CreatePatientAsync(ptClient);

        using var response = await ptaClient.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PT_Can_Access_Sync_Status_Returns_200()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
