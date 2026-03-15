using Microsoft.EntityFrameworkCore;
using PTDoc.Client.Pages;
using PTDoc.Components;
using PTDoc.Data;
using PTDoc.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Configure SQLite Database with option to switch to SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=ptdoc.db";

// Register scoped tenant context so each request has its own clinic scope.
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<PTDocDbContext>((sp, options) =>
{
    options.UseSqlite(connectionString);
    // PTDocDbContext receives the scoped ITenantContext to apply per-clinic query filters.
    // For future SQL Server/Azure SQL migration, replace UseSqlite with UseSqlServer.
});

// Register application services
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IComplianceService, ComplianceService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISOAPNoteService, SOAPNoteService>();
builder.Services.AddScoped<IInsuranceService, InsuranceService>();
builder.Services.AddScoped<IAppStateService, AppStateService>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PTDocDbContext>();
    // EnsureCreated creates the schema on first run but does not apply incremental changes.
    // If the schema has changed (e.g. new columns), delete ptdoc.db and restart to recreate it.
    // Future: switch to dbContext.Database.Migrate() once EF migrations are added.
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Populate the per-request ITenantContext from the authenticated user's 'clinic_id' claim.
// In production, this claim is set by the authentication handler (e.g. JWT or session token).
// When the claim is absent (unauthenticated/anonymous), ClinicId remains Guid.Empty and tenant
// query filters are bypassed – ensure all sensitive endpoints require authentication.
app.Use(async (context, next) =>
{
    var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
    var clinicIdClaim = context.User.FindFirst("clinic_id")?.Value;
    if (Guid.TryParse(clinicIdClaim, out var clinicId) && clinicId != Guid.Empty)
    {
        tenantContext.SetClinicId(clinicId);
    }
    await next(context);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PTDoc.Client._Imports).Assembly);

app.Run();
