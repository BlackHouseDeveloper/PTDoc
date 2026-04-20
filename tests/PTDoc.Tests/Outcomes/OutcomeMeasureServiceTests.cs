using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Outcomes;

[Trait("Category", "CoreCi")]
public sealed class OutcomeMeasureServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ITenantContextAccessor> _tenantContext = new();
    private readonly IOutcomeMeasureRegistry _registry = new OutcomeMeasureRegistry();

    public OutcomeMeasureServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OutcomeMeasureService_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _tenantContext.Setup(accessor => accessor.GetCurrentClinicId()).Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task RecordResultAsync_PersistsSelectableQuickDash()
    {
        var service = new OutcomeMeasureService(_context, _registry, _tenantContext.Object);

        var result = await service.RecordResultAsync(
            Guid.NewGuid(),
            OutcomeMeasureType.QuickDASH,
            32,
            Guid.NewGuid());

        Assert.Equal(OutcomeMeasureType.QuickDASH, result.MeasureType);
        Assert.Equal(32, result.Score);
        Assert.Single(_context.OutcomeMeasureResults);
    }

    [Fact]
    public async Task RecordResultAsync_RejectsHistoricalOnlyVas()
    {
        var service = new OutcomeMeasureService(_context, _registry, _tenantContext.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordResultAsync(
            Guid.NewGuid(),
            OutcomeMeasureType.VAS,
            5,
            Guid.NewGuid()));

        Assert.Contains("VAS", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_context.OutcomeMeasureResults);
    }

    [Fact]
    public async Task RecordResultAsync_RejectsUnknownOutcomeMeasureType()
    {
        var service = new OutcomeMeasureService(_context, _registry, _tenantContext.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordResultAsync(
            Guid.NewGuid(),
            (OutcomeMeasureType)999,
            5,
            Guid.NewGuid()));

        Assert.Contains("not recognized", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_context.OutcomeMeasureResults);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
