using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Api.Auth;
using PTDoc.Application.Auth;
using PTDoc.Application.Communication;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Tests.Integration;

[Trait("Category", "EndToEnd")]
public sealed class LocalBearerSecurityTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    [Theory]
    [InlineData(MfaEnforcementMode.Off, false)]
    [InlineData(MfaEnforcementMode.Enforced, false)]
    [InlineData(MfaEnforcementMode.Off, true)]
    [InlineData(MfaEnforcementMode.Enforced, true)]
    public async Task SignedLocalBearer_RejectsOldPinAndMfaGenerationsInMixedMode(
        MfaEnforcementMode mode, bool forcedChange)
    {
        // Enable claim transformation without configuring a network Entra metadata dependency.
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<EntraExternalIdOptions>(options => options.Enabled = true)));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var clinic = new Clinic { Name = "Bearer Boundary", Slug = $"bearer-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = $"bearer-{Guid.NewGuid():N}",
            FirstName = "Bearer",
            LastName = "Boundary",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true,
            PinHash = AuthService.HashPin("12345678"),
            PinChangedAtUtc = DateTime.UtcNow
        };
        var credential = new UserMfaCredential
        {
            UserId = user.Id,
            IsActive = true,
            EncryptedSecret = "original-test-generation"
        };
        db.AddRange(clinic, user, credential);
        await db.SaveChangesAsync();
        var policy = await db.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.MfaEnforcementMode = mode;
        policy.MfaEffectiveAtUtc = DateTime.UtcNow.AddMinutes(-1);
        policy.RequirePinChangeOnFirstLogin = false;
        await db.SaveChangesAsync();

        var issuer = scope.ServiceProvider.GetRequiredService<JwtTokenIssuer>();
        Task<TokenResponse> IssueAsync(bool includePinBinding = true)
        {
            var claims = new List<Claim>
            {
                new(PTDocClaimTypes.InternalUserId, user.Id.ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role),
                new(HttpTenantContextAccessor.ClinicIdClaimType, clinic.Id.ToString()),
                new(PTDocClaimTypes.AuthenticationType, "pin_step_up_jwt"),
                new("amr", "mfa"),
                new(PTDocClaimTypes.MfaCredentialBinding,
                    MfaCredentialBinding.Create(credential.Id, credential.EncryptedSecret))
            };
            if (includePinBinding)
                claims.Add(new Claim(PTDocClaimTypes.PinCredentialBinding, PinCredentialBinding.Create(user.PinHash)));
            return issuer.IssueAsync(new ClaimsIdentity(claims, "test-issuance"), CancellationToken.None);
        }

        async Task<AuthenticateResult> AuthenticateAsync(string token)
        {
            // New request scope prevents a cached AuthenticateResult from hiding a revocation.
            await using var requestScope = host.Services.CreateAsyncScope();
            var http = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
            http.Request.Headers.Authorization = $"Bearer {token}";
            requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
            return await http.AuthenticateAsync("LegacyJwt");
        }

        var original = await IssueAsync();
        var originalPinHash = user.PinHash;
        var authenticated = await AuthenticateAsync(original.AccessToken);
        Assert.True(authenticated.Succeeded);
        Assert.Contains(authenticated.Principal!.Claims, claim =>
            claim.Type == PTDocClaimTypes.MappedAuthenticationMethodsReference && claim.Value == "mfa");
        Assert.False(authenticated.Principal.HasClaim(claim => claim.Type == PTDocClaimTypes.ExternalProvider));
        Assert.False((await AuthenticateAsync((await IssueAsync(includePinBinding: false)).AccessToken)).Succeeded);

        var security = scope.ServiceProvider.GetRequiredService<ISecurityPolicyAdministrationService>();
        if (forcedChange)
        {
            Assert.True((await security.ForcePinChangeAsync(clinic.Id, user.Id, user.Id, "bearer-force")).Succeeded);
            Assert.False((await AuthenticateAsync(original.AccessToken)).Succeeded);
            var mfa = scope.ServiceProvider.GetRequiredService<IMfaAuthenticationService>();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            Assert.NotNull(await auth.CompletePinChangeAsync(
                mfa.CreatePinChangeChallenge(user.Id, user.PinHash), "12345678", sessionMode: AuthSessionMode.IdentityOnly));
        }
        else
        {
            const string resetToken = "bearer-boundary-reset-token";
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken))).ToLowerInvariant(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync();
            var reset = await new PasswordResetTokenService(db).ResetPinAsync(new PasswordResetCompletionRequest
            {
                Token = resetToken,
                NewPin = "12345678"
            });
            Assert.True(reset.Succeeded);
        }

        await db.Entry(user).ReloadAsync();
        // Reusing the same PIN text still creates a fresh salted credential generation.
        Assert.NotEqual(originalPinHash, user.PinHash);
        Assert.False(user.MustChangePin);
        Assert.False((await AuthenticateAsync(original.AccessToken)).Succeeded);
        var refreshStore = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        Assert.Null(await refreshStore.GetAsync(original.RefreshToken, CancellationToken.None));
        var replacement = await IssueAsync();
        Assert.True((await AuthenticateAsync(replacement.AccessToken)).Succeeded);
        Assert.NotNull(await refreshStore.GetAsync(replacement.RefreshToken, CancellationToken.None));

        Assert.True((await security.ResetMfaAsync(clinic.Id, user.Id, user.Id, "bearer-mfa-reset")).Succeeded);
        Assert.False((await AuthenticateAsync(replacement.AccessToken)).Succeeded);
        // Reuse the same credential row, as enrollment does, but with a different secret generation.
        credential.IsActive = true;
        credential.EncryptedSecret = "replacement-test-generation";
        await db.SaveChangesAsync();
        Assert.False((await AuthenticateAsync(replacement.AccessToken)).Succeeded);
        Assert.True((await AuthenticateAsync((await IssueAsync()).AccessToken)).Succeeded);
    }
}
