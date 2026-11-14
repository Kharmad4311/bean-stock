using System.Collections.Concurrent;
using BeanStock.Worker.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeanStock.Worker.UnitTests.Scheduling;

/// <remarks>
/// Time is simulated. The scheduler arms its next delay asynchronously, so tests never assume it is armed
/// at a given instant: "not yet" assertions advance less than the expected delay, and "eventually" assertions
/// advance in small steps until the expected check happens.
/// </remarks>
public sealed class CatalogSyncSchedulerTests : IDisposable
{
    private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(15);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 3, 0, 0, TimeSpan.Zero));
    private readonly RecordingJob _job = new();
    private readonly MutableOptionsMonitor _options = new(new CatalogSyncScheduleOptions
    {
        Interval = TimeSpan.FromDays(1),
        CheckInterval = _checkInterval,
        RetryDelay = _retryDelay,
    });

    private readonly CatalogSyncScheduler _scheduler;

    public CatalogSyncSchedulerTests()
    {
        _scheduler = new CatalogSyncScheduler(_job, _options, _time, NullLogger<CatalogSyncScheduler>.Instance);
    }

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public async Task First_check_happens_at_startup_with_the_configured_interval()
    {
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);

        await WaitForChecksAsync(1);

        _job.Intervals.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromDays(1));
        await StopAsync();
    }

    [Fact]
    public async Task Next_check_waits_for_the_check_interval()
    {
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);

        await AdvanceAsync(_checkInterval - TimeSpan.FromSeconds(1));
        _job.Intervals.Length.ShouldBe(1);

        await AdvanceUntilChecksAsync(2);
        await StopAsync();
    }

    [Fact]
    public async Task Failed_check_waits_for_the_retry_delay()
    {
        _job.NextResults.Enqueue(false);
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);

        await AdvanceAsync(_retryDelay - TimeSpan.FromSeconds(1));
        _job.Intervals.Length.ShouldBe(1);

        await AdvanceUntilChecksAsync(2);
        await StopAsync();
    }

    [Fact]
    public async Task Exception_in_a_check_does_not_stop_the_schedule()
    {
        _job.NextFailure = new InvalidOperationException("Database unreachable.");
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);

        await AdvanceUntilChecksAsync(2);
        await StopAsync();
    }

    [Fact]
    public async Task Disabled_schedule_does_not_check_until_enabled_again()
    {
        _options.Set(new CatalogSyncScheduleOptions { Enabled = false, CheckInterval = _checkInterval });
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);

        await AdvanceAsync(_checkInterval * 3);
        _job.Intervals.ShouldBeEmpty();

        _options.Set(new CatalogSyncScheduleOptions { Enabled = true, Interval = TimeSpan.FromHours(6), CheckInterval = _checkInterval });
        await AdvanceUntilChecksAsync(1);
        _job.Intervals.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromHours(6));
        await StopAsync();
    }

    [Fact]
    public async Task Interval_changed_in_configuration_applies_at_the_next_check()
    {
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);

        _options.Set(new CatalogSyncScheduleOptions { Interval = TimeSpan.FromMinutes(2), CheckInterval = _checkInterval });
        await AdvanceUntilChecksAsync(2);

        _job.Intervals.Last().ShouldBe(TimeSpan.FromMinutes(2));
        await StopAsync();
    }

    [Fact]
    public async Task Invalid_configuration_change_keeps_the_last_valid_options()
    {
        await _scheduler.StartAsync(TestContext.Current.CancellationToken);
        await WaitForChecksAsync(1);

        _options.FailValidation = true;
        await AdvanceUntilChecksAsync(2);

        _job.Intervals.Last().ShouldBe(TimeSpan.FromDays(1));
        await StopAsync();
    }

    private async Task StopAsync() => await _scheduler.StopAsync(TestContext.Current.CancellationToken);

    private async Task WaitForChecksAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_job.Intervals.Length < expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {expected} check(s), got {_job.Intervals.Length}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Advances simulated time, leaving the scheduler a moment of real time to react.</summary>
    private async Task AdvanceAsync(TimeSpan duration)
    {
        _time.Advance(duration);
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    private async Task AdvanceUntilChecksAsync(int expected)
    {
        // Steps of 10 s (a sixth of the check interval): precise enough, and fast in real time.
        for (var step = 0; step < 500 && _job.Intervals.Length < expected; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        await WaitForChecksAsync(expected);
    }

    private sealed class RecordingJob : ICatalogSyncJob
    {
        private readonly ConcurrentQueue<TimeSpan> _intervals = new();

        // Snapshot: the scheduler records checks on another thread.
        public TimeSpan[] Intervals => _intervals.ToArray();

        public ConcurrentQueue<bool> NextResults { get; } = new();

        public Exception? NextFailure { get; set; }

        public Task<bool> RunAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            _intervals.Enqueue(interval);

            if (NextFailure is { } failure)
            {
                NextFailure = null;
                return Task.FromException<bool>(failure);
            }

            return Task.FromResult(!NextResults.TryDequeue(out var result) || result);
        }
    }

    private sealed class MutableOptionsMonitor(CatalogSyncScheduleOptions initial) : IOptionsMonitor<CatalogSyncScheduleOptions>
    {
        private CatalogSyncScheduleOptions _current = initial;

        public bool FailValidation { get; set; }

        public CatalogSyncScheduleOptions CurrentValue => FailValidation
            ? throw new OptionsValidationException(Options.DefaultName, typeof(CatalogSyncScheduleOptions), ["Invalid interval."])
            : _current;

        public void Set(CatalogSyncScheduleOptions options) => _current = options;

        public CatalogSyncScheduleOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<CatalogSyncScheduleOptions, string?> listener) => null;
    }
}
