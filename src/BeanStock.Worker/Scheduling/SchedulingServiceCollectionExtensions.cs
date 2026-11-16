namespace BeanStock.Worker.Scheduling;

internal static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the automatic catalog synchronization: options (validated at startup and on reload),
    /// the job and the scheduler.
    /// </summary>
    public static IServiceCollection AddCatalogSyncScheduling(this IServiceCollection services)
    {
        services.AddOptions<CatalogSyncScheduleOptions>()
            .BindConfiguration(CatalogSyncScheduleOptions.SectionName)
            .Validate(
                options => options.Interval >= CatalogSyncScheduleOptions.MinimumInterval,
                $"{CatalogSyncScheduleOptions.SectionName}:Interval must be at least {CatalogSyncScheduleOptions.MinimumInterval}.")
            .Validate(
                options => options.CheckInterval >= CatalogSyncScheduleOptions.MinimumCheckInterval
                    && options.CheckInterval <= CatalogSyncScheduleOptions.MaximumCheckInterval,
                $"{CatalogSyncScheduleOptions.SectionName}:CheckInterval must be between "
                    + $"{CatalogSyncScheduleOptions.MinimumCheckInterval} and {CatalogSyncScheduleOptions.MaximumCheckInterval}.")
            .Validate(
                options => options.RetryDelay > TimeSpan.Zero,
                $"{CatalogSyncScheduleOptions.SectionName}:RetryDelay must be positive.")
            .ValidateOnStart();

        services.AddSingleton<ICatalogSyncJob, CatalogSyncJob>();
        services.AddHostedService<CatalogSyncScheduler>();

        return services;
    }
}
