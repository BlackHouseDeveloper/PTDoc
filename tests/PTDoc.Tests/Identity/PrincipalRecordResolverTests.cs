using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Identity;
using Xunit;

namespace PTDoc.Tests.Identity;

public class PrincipalRecordResolverTests
{
    [Fact]
    public void ApplicationDbContext_Resolves_Without_Recursive_Service_Graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddScoped<PrincipalRecordResolver>();
        services.AddScoped<IIdentityContextAccessor, HttpIdentityContextAccessor>();
        services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
        services.AddScoped<IPatientContextAccessor, HttpPatientContextAccessor>();
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseInMemoryDatabase(Guid.NewGuid().ToString());

            var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
            options.AddInterceptors(new SyncMetadataInterceptor(identityContext));
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.NotNull(context);
    }
}
