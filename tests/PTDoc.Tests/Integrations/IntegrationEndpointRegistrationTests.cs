using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Api.Integrations;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public class IntegrationEndpointRegistrationTests
{
    [Fact]
    public void MapIntegrationEndpoints_AlwaysRegistersConnectionBackedHepRoutes()
    {
        var app = CreateApp(new Dictionary<string, string?>
        {
            ["Integrations:Hep:Enabled"] = "false",
            ["Integrations:Hep:PatientLaunchEnabled"] = "false",
            ["Integrations:Hep:ClinicianAssignmentEnabled"] = "false"
        });

        app.MapIntegrationEndpoints();

        var routes = GetRoutes(app);

        Assert.DoesNotContain("/api/v1/integrations/hep/assign", routes);
        Assert.Contains("/api/v1/integrations/hep/patient-launch", routes);
        Assert.Contains("/api/v1/integrations/hep/patient-launch-ticket", routes);
        Assert.Contains("/api/v1/integrations/hep/patient-programs", routes);
        Assert.Contains("/api/v1/integrations/hep/patients/{patientId:guid}/programs", routes);
        Assert.Contains("/api/v1/integrations/hep/programs/{programId:guid}/publish", routes);
        Assert.Contains("/api/v1/integrations/operations/dead-letters", routes);
        Assert.Contains("/api/v1/integrations/operations/dead-letters/{jobId:guid}/replay", routes);
    }

    [Fact]
    public void MapIntegrationEndpoints_RegistersOnlyPatientLaunch_WhenClinicianAssignmentDisabled()
    {
        var app = CreateApp(new Dictionary<string, string?>
        {
            ["Integrations:Hep:Enabled"] = "true",
            ["Integrations:Hep:PatientLaunchEnabled"] = "true",
            ["Integrations:Hep:ClinicianAssignmentEnabled"] = "false"
        });

        app.MapIntegrationEndpoints();

        var routes = GetRoutes(app);

        Assert.DoesNotContain("/api/v1/integrations/hep/assign", routes);
        Assert.Contains("/api/v1/integrations/hep/patient-launch", routes);
    }

    private static WebApplication CreateApp(Dictionary<string, string?> configurationValues)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(configurationValues);
        builder.Services.AddAuthorization();
        return builder.Build();
    }

    private static IReadOnlyCollection<string?> GetRoutes(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
    }
}
