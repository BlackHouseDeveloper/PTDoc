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

builder.Services.AddDbContext<PTDocDbContext>(options =>
{
    // For SQLite (local development, offline-first)
    options.UseSqlite(connectionString);
    
    // For future SQL Server/Azure SQL migration, uncomment and use:
    // options.UseSqlServer(connectionString);
});

// Register application services
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<ISOAPNoteService, SOAPNoteService>();
builder.Services.AddScoped<IInsuranceService, InsuranceService>();
builder.Services.AddScoped<IAppStateService, AppStateService>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PTDocDbContext>();
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PTDoc.Client._Imports).Assembly);

app.Run();
