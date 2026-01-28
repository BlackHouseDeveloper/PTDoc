using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        app.MapPost("/login", async context =>
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "demo@ptdoc.app"),
                new Claim(ClaimTypes.Role, "User")
            };

            var identity = new ClaimsIdentity(claims, "PTDocAuth");
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync("PTDocAuth", principal);
            context.Response.Redirect("/");
        });

        app.MapPost("/logout", async context =>
        {
            await context.SignOutAsync("PTDocAuth");
            context.Response.Redirect("/");
        });
    }
}
