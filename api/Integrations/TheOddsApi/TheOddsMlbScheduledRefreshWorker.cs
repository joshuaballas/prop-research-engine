using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsMlbScheduledRefreshWorker(
    IOptions<TheOddsScheduledRefreshOptions> options,
    ITheOddsScheduledRefreshState state,
    ILogger<TheOddsMlbScheduledRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            state.MarkDisabled();
            logger.LogInformation("The Odds API MLB scheduled refresh worker is disabled. No The Odds API calls will be made.");
            return;
        }

        var interval = options.Value.GetInterval();
        logger.LogWarning(
            "The Odds API MLB scheduled refresh worker is enabled. Interval: {Interval}. BookmakerPreset: {BookmakerPreset}. RequireForceLive: {RequireForceLive}.",
            interval,
            options.Value.BookmakerPreset,
            options.Value.RequireForceLive);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                RunGuardedCycle();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB scheduled refresh worker is stopping.");
        }
    }

    private void RunGuardedCycle()
    {
        var startedAt = DateTimeOffset.UtcNow;
        state.MarkStarted(startedAt);

        if (options.Value.RequireForceLive)
        {
            const string reason = "Scheduled live refresh is blocked because TheOddsScheduledRefresh:RequireForceLive is true. Set it false only in explicit local/production ops config after refresh logic is service-wired and credit policy is approved.";
            state.MarkBlocked(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return;
        }

        const string notWiredReason = "Scheduled live refresh execution is intentionally not wired yet. Extract refresh-current into an injectable service before enabling automatic provider calls.";
        state.MarkBlocked(DateTimeOffset.UtcNow, notWiredReason);
        logger.LogWarning("{Reason}", notWiredReason);
    }
}
