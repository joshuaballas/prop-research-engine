using Microsoft.Extensions.Options;
using SportsBetting.Api.Endpoints;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsMlbScheduledRefreshWorker(
    IServiceScopeFactory scopeFactory,
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
                await RunGuardedCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB scheduled refresh worker is stopping.");
        }
    }

    private async Task RunGuardedCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        state.MarkStarted(startedAt);

        if (options.Value.RequireForceLive)
        {
            const string reason = "Scheduled live refresh is blocked because TheOddsScheduledRefresh:RequireForceLive is true. Set it false only in explicit local/production ops config after credit policy is approved.";
            state.MarkBlocked(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return;
        }

        await RunRefreshCycleAsync(cancellationToken);
    }

    private async Task RunRefreshCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var refreshService = scope.ServiceProvider.GetRequiredService<ITheOddsMlbRefreshService>();
            var refreshOptions = options.Value;
            var request = new TheOddsMlbRefreshRequest(
                Date: null,
                DryRun: false,
                ForceLive: true,
                BookmakerPreset: refreshOptions.BookmakerPreset,
                IncludeDfs: null,
                IncludeCandidates: refreshOptions.IncludeCandidates,
                IncludeLineShopping: refreshOptions.IncludeLineShopping,
                IncludeFullDiagnostics: refreshOptions.IncludeFullDiagnostics,
                FreshnessMinutes: refreshOptions.FreshnessMinutes);

            await refreshService.RefreshCurrentAsync(request, httpContext: null, cancellationToken);
            state.MarkCompleted(DateTimeOffset.UtcNow);
            logger.LogInformation(
                "The Odds API MLB scheduled refresh completed. BookmakerPreset: {BookmakerPreset}. IncludeCandidates: {IncludeCandidates}. IncludeLineShopping: {IncludeLineShopping}.",
                refreshOptions.BookmakerPreset,
                refreshOptions.IncludeCandidates,
                refreshOptions.IncludeLineShopping);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB scheduled refresh cycle was canceled during shutdown.");
            throw;
        }
        catch (Exception exception)
        {
            state.MarkFailed(DateTimeOffset.UtcNow, exception.Message);
            logger.LogError(exception, "The Odds API MLB scheduled refresh failed.");
        }
    }
}
