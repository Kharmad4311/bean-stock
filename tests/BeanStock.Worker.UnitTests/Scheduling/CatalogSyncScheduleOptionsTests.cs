using BeanStock.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BeanStock.Worker.UnitTests.Scheduling;

public sealed class CatalogSyncScheduleOptionsTests
{
    [Fact]
    public void Defaults_describe_a_daily_synchronization()
    {
        var options = Resolve(new());

        options.Enabled.ShouldBeTrue();
        options.Interval.ShouldBe(TimeSpan.FromDays(1));
        options.CheckInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.RetryDelay.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Durations_are_read_in_the_timespan_format()
    {
        var options = Resolve(new()
        {
            ["CatalogSync:Schedule:Interval"] = "00:02:00",
            ["CatalogSync:Schedule:CheckInterval"] = "00:00:15",
        });

        options.Interval.ShouldBe(TimeSpan.FromMinutes(2));
        options.CheckInterval.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData("CatalogSync:Schedule:Interval", "00:00:05")]
    [InlineData("CatalogSync:Schedule:CheckInterval", "00:00:00")]
    [InlineData("CatalogSync:Schedule:CheckInterval", "02:00:00")]
    [InlineData("CatalogSync:Schedule:RetryDelay", "00:00:00")]
    public void Out_of_range_values_are_rejected(string key, string value)
    {
        Should.Throw<OptionsValidationException>(() => Resolve(new() { [key] = value }));
    }

    private static CatalogSyncScheduleOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCatalogSyncScheduling();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CatalogSyncScheduleOptions>>().Value;
    }
}
