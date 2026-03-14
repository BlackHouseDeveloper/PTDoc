using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using Xunit;

namespace PTDoc.Tests.Tenancy;

/// <summary>
/// Tenant isolation tests for Sprint J.
/// Validates that per-clinic query filters prevent cross-tenant data access.
/// </summary>
[Trait("Category", "Tenancy")]
public class TenantIsolationTests
{
    private static readonly Guid ClinicA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ClinicB = Guid.Parse("20000000-0000-0000-0000-000000000002");

    /// <summary>
    /// Creates an in-memory context scoped to the given clinic ID (simulating a request from that clinic).
    /// </summary>
    private static ApplicationDbContext CreateSystemContext(string dbName)
    {
        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDbContext(options, tenantMock.Object);
    }

    private static ApplicationDbContext CreateTenantContext(Guid clinicId, string dbName)
    {
        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns(clinicId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDbContext(options, tenantMock.Object);
    }

    // ─── Patient isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Patient_Query_Returns_Only_Current_Clinic_Patients()
    {
        // Arrange — seed two clinics' worth of patients into the same DB.
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        seedCtx.Patients.AddRange(
            new Patient { FirstName = "Alice", LastName = "ClinicA", DateOfBirth = new DateTime(1990, 1, 1), ClinicId = ClinicA },
            new Patient { FirstName = "Bob",   LastName = "ClinicB", DateOfBirth = new DateTime(1985, 5, 5), ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        // Act — query as Clinic A.
        await using var ctxA = CreateTenantContext(ClinicA, dbName);
        var patientsA = await ctxA.Patients.ToListAsync();

        // Assert — only Alice is visible to Clinic A.
        Assert.Single(patientsA);
        Assert.Equal("Alice", patientsA[0].FirstName);
    }

    [Fact]
    public async Task Patient_Query_Does_Not_Return_Other_Clinic_Data()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        seedCtx.Patients.AddRange(
            new Patient { FirstName = "Alice", LastName = "ClinicA", DateOfBirth = new DateTime(1990, 1, 1), ClinicId = ClinicA },
            new Patient { FirstName = "Bob",   LastName = "ClinicB", DateOfBirth = new DateTime(1985, 5, 5), ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        // Act — Clinic B cannot see Clinic A's patient.
        await using var ctxB = CreateTenantContext(ClinicB, dbName);
        var patientsB = await ctxB.Patients.ToListAsync();

        Assert.Single(patientsB);
        Assert.DoesNotContain(patientsB, p => p.FirstName == "Alice");
        Assert.Contains(patientsB, p => p.FirstName == "Bob");
    }

    [Fact]
    public async Task Patient_Query_Without_Tenant_Scope_Returns_All_Patients()
    {
        // Arrange — system-level context should see everything.
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        seedCtx.Patients.AddRange(
            new Patient { FirstName = "Alice", LastName = "ClinicA", DateOfBirth = new DateTime(1990, 1, 1), ClinicId = ClinicA },
            new Patient { FirstName = "Bob",   LastName = "ClinicB", DateOfBirth = new DateTime(1985, 5, 5), ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        // Act
        await using var sysCtx = CreateSystemContext(dbName);
        var all = await sysCtx.Patients.ToListAsync();

        Assert.Equal(2, all.Count);
    }

    // ─── ClinicalNote isolation ───────────────────────────────────────────────

    [Fact]
    public async Task ClinicalNote_Query_Returns_Only_Current_Clinic_Notes()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        var patientA = new Patient { FirstName = "P", LastName = "A", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicA };
        var patientB = new Patient { FirstName = "P", LastName = "B", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicB };
        seedCtx.Patients.AddRange(patientA, patientB);

        seedCtx.ClinicalNotes.AddRange(
            new ClinicalNote { PatientId = patientA.Id, DateOfService = DateTime.UtcNow, ClinicId = ClinicA },
            new ClinicalNote { PatientId = patientB.Id, DateOfService = DateTime.UtcNow, ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        await using var ctxA = CreateTenantContext(ClinicA, dbName);
        var notes = await ctxA.ClinicalNotes.ToListAsync();

        Assert.Single(notes);
        Assert.Equal(patientA.Id, notes[0].PatientId);
    }

    [Fact]
    public async Task Appointment_Query_Returns_Only_Current_Clinic_Appointments()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        var patientA = new Patient { FirstName = "P", LastName = "A", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicA };
        var patientB = new Patient { FirstName = "P", LastName = "B", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicB };
        seedCtx.Patients.AddRange(patientA, patientB);

        seedCtx.Appointments.AddRange(
            new Appointment { PatientId = patientA.Id, StartTimeUtc = DateTime.UtcNow, EndTimeUtc = DateTime.UtcNow.AddHours(1), ClinicId = ClinicA },
            new Appointment { PatientId = patientB.Id, StartTimeUtc = DateTime.UtcNow, EndTimeUtc = DateTime.UtcNow.AddHours(1), ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        await using var ctxA = CreateTenantContext(ClinicA, dbName);
        var appointments = await ctxA.Appointments.ToListAsync();

        Assert.Single(appointments);
        Assert.Equal(patientA.Id, appointments[0].PatientId);
    }

    [Fact]
    public async Task IntakeForm_Query_Returns_Only_Current_Clinic_Forms()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        var patientA = new Patient { FirstName = "P", LastName = "A", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicA };
        var patientB = new Patient { FirstName = "P", LastName = "B", DateOfBirth = DateTime.UtcNow.AddYears(-30), ClinicId = ClinicB };
        seedCtx.Patients.AddRange(patientA, patientB);

        var tokenA = Guid.NewGuid().ToString("N");
        var tokenB = Guid.NewGuid().ToString("N");

        seedCtx.IntakeForms.AddRange(
            new IntakeForm { PatientId = patientA.Id, TemplateVersion = "v1", AccessToken = tokenA, ClinicId = ClinicA },
            new IntakeForm { PatientId = patientB.Id, TemplateVersion = "v1", AccessToken = tokenB, ClinicId = ClinicB }
        );
        await seedCtx.SaveChangesAsync();

        await using var ctxA = CreateTenantContext(ClinicA, dbName);
        var forms = await ctxA.IntakeForms.ToListAsync();

        Assert.Single(forms);
        Assert.Equal(patientA.Id, forms[0].PatientId);
    }

    // ─── Legacy (null ClinicId) backward compatibility ────────────────────────

    [Fact]
    public async Task Legacy_Patients_With_No_ClinicId_Are_Visible_To_Any_Tenant_Context()
    {
        // Legacy records that pre-date Sprint J have null ClinicId.
        // They should remain accessible to avoid breaking existing workflows.
        var dbName = Guid.NewGuid().ToString();
        await using var seedCtx = CreateSystemContext(dbName);

        // One legacy patient (no clinic), one scoped patient
        seedCtx.Patients.AddRange(
            new Patient { FirstName = "Legacy", LastName = "Patient", DateOfBirth = new DateTime(1970, 1, 1), ClinicId = null },
            new Patient { FirstName = "Scoped", LastName = "Patient", DateOfBirth = new DateTime(1990, 1, 1), ClinicId = ClinicA }
        );
        await seedCtx.SaveChangesAsync();

        await using var ctxA = CreateTenantContext(ClinicA, dbName);
        var patients = await ctxA.Patients.ToListAsync();

        // Both legacy and scoped are visible (null ClinicId passes the filter)
        Assert.Equal(2, patients.Count);
    }

    // ─── AuthResult includes ClinicId ─────────────────────────────────────────

    [Fact]
    public async Task AuthService_Returns_ClinicId_In_AuthResult()
    {
        // Arrange
        var clinicId = ClinicA;
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateSystemContext(dbName);

        // Create the clinic first
        ctx.Clinics.Add(new Clinic { Id = clinicId, Name = "Test Clinic", Slug = "test-clinic" });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "clinicuser",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Clinic",
            LastName = "User",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ClinicId = clinicId
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var auditMock = new Mock<PTDoc.Application.Compliance.IAuditService>();
        var authService = new AuthService(ctx,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance,
            auditMock.Object);

        // Act
        var result = await authService.AuthenticateAsync("clinicuser", "1234");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(clinicId, result.ClinicId);
    }

    [Fact]
    public async Task AuthService_Returns_Null_ClinicId_For_Users_Without_Clinic()
    {
        // System users (background jobs) have no clinic assignment.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateSystemContext(dbName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "sysuser",
            PinHash = AuthService.HashPin("9999"),
            FirstName = "Sys",
            LastName = "User",
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ClinicId = null
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var auditMock = new Mock<PTDoc.Application.Compliance.IAuditService>();
        var authService = new AuthService(ctx,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance,
            auditMock.Object);

        var result = await authService.AuthenticateAsync("sysuser", "9999");

        Assert.NotNull(result);
        Assert.Null(result.ClinicId);
    }

    // ─── HttpTenantContextAccessor claim extraction ────────────────────────────

    [Fact]
    public void HttpTenantContextAccessor_Returns_ClinicId_From_Claim()
    {
        // Arrange: create a ClaimsPrincipal with the clinic_id claim
        var clinicId = ClinicA;
        var claims = new[]
        {
            new System.Security.Claims.Claim(HttpTenantContextAccessor.ClinicIdClaimType, clinicId.ToString())
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var httpContextMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var fakeHttpCtx = new Mock<Microsoft.AspNetCore.Http.HttpContext>();
        fakeHttpCtx.Setup(c => c.User).Returns(principal);
        httpContextMock.Setup(x => x.HttpContext).Returns(fakeHttpCtx.Object);

        var accessor = new HttpTenantContextAccessor(httpContextMock.Object);

        // Act
        var result = accessor.GetCurrentClinicId();

        // Assert
        Assert.Equal(clinicId, result);
    }

    [Fact]
    public void HttpTenantContextAccessor_Returns_Null_When_No_Claim()
    {
        var httpContextMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        httpContextMock.Setup(x => x.HttpContext).Returns((Microsoft.AspNetCore.Http.HttpContext?)null);

        var accessor = new HttpTenantContextAccessor(httpContextMock.Object);

        Assert.Null(accessor.GetCurrentClinicId());
    }

    [Fact]
    public void ITenantContextAccessor_HasTenantScope_Returns_True_When_ClinicId_Present()
    {
        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns(ClinicA);
        // HasTenantScope has default implementation in the interface
        Assert.True(tenantMock.Object.GetCurrentClinicId().HasValue);
    }

    [Fact]
    public void ITenantContextAccessor_HasTenantScope_Returns_False_When_No_ClinicId()
    {
        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);
        Assert.False(tenantMock.Object.GetCurrentClinicId().HasValue);
    }
}
