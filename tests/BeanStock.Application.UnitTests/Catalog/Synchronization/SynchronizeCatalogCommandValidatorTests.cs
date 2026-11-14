using BeanStock.Application.Catalog.Synchronization;
using BeanStock.Domain.Catalog.Synchronization;

namespace BeanStock.Application.UnitTests.Catalog.Synchronization;

public sealed class SynchronizeCatalogCommandValidatorTests
{
    private readonly SynchronizeCatalogCommandValidator _validator = new();

    [Fact]
    public void Manual_command_is_valid()
    {
        _validator.Validate(SynchronizeCatalogCommand.Manual()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Scheduled_command_with_a_positive_interval_is_valid()
    {
        _validator.Validate(SynchronizeCatalogCommand.Scheduled(TimeSpan.FromDays(1))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Scheduled_command_without_interval_is_invalid()
    {
        _validator.Validate(new SynchronizeCatalogCommand(CatalogSyncTrigger.Scheduled, null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Scheduled_command_with_a_zero_interval_is_invalid()
    {
        _validator.Validate(SynchronizeCatalogCommand.Scheduled(TimeSpan.Zero)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Manual_command_with_an_interval_is_invalid()
    {
        _validator.Validate(new SynchronizeCatalogCommand(CatalogSyncTrigger.Manual, TimeSpan.FromHours(1))).IsValid.ShouldBeFalse();
    }
}
