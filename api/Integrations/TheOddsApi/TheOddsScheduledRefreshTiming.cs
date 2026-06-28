namespace SportsBetting.Api.Integrations.TheOddsApi;

public static class TheOddsScheduledRefreshTiming
{
    public static TheOddsScheduledRefreshTimingSnapshot Resolve(
        TheOddsScheduledRefreshOptions options,
        TheOddsRefreshPlanResult plan,
        TheOddsScheduledRefreshStateSnapshot snapshot)
    {
        var generatedAtUtc = plan.GeneratedAtUtc;
        var nextRefreshDueUtc = options.Enabled
            ? plan.Targets
                .Where(target => target.Enabled)
                .Select(target => (DateTimeOffset?)target.NextEligibleRefreshUtc)
                .Min() ?? plan.NextEligibleRefreshUtc
            : (DateTimeOffset?)null;

        var isRefreshInProgress = string.Equals(snapshot.Status, "running", StringComparison.OrdinalIgnoreCase)
            && snapshot.LastRunStartedAtUtc.HasValue
            && !snapshot.LastRunCompletedAtUtc.HasValue;
        var nextRefreshStartEstimateUtc = ResolveNextRefreshStartEstimateUtc(
            options,
            snapshot,
            nextRefreshDueUtc,
            isRefreshInProgress);
        var expectedRefreshDuration = ResolveExpectedRefreshDuration(options, plan);
        var postRefreshReadBuffer = options.GetPostRefreshReadBuffer();
        var nextHeaderRefreshDueUtc = ResolveNextHeaderRefreshDueUtc(options, plan, nextRefreshDueUtc);
        var nextHeaderSyncTargetUtc = nextHeaderRefreshDueUtc?.Add(postRefreshReadBuffer);
        var nextDataAvailableEstimateUtc = nextHeaderSyncTargetUtc;
        var conservativeDataAvailableEstimateUtc = nextRefreshStartEstimateUtc?.Add(expectedRefreshDuration).Add(postRefreshReadBuffer);
        var isOverdue = options.Enabled
            && !isRefreshInProgress
            && nextDataAvailableEstimateUtc.HasValue
            && generatedAtUtc > nextDataAvailableEstimateUtc.Value;

        return new TheOddsScheduledRefreshTimingSnapshot(
            nextRefreshDueUtc,
            nextRefreshStartEstimateUtc,
            nextHeaderSyncTargetUtc,
            nextDataAvailableEstimateUtc,
            conservativeDataAvailableEstimateUtc,
            NextRunEstimateUtc: nextRefreshStartEstimateUtc,
            IsRefreshInProgress: isRefreshInProgress,
            IsOverdue: isOverdue,
            SecondsUntilNextRefreshStart: SecondsUntil(generatedAtUtc, nextRefreshStartEstimateUtc),
            SecondsUntilNextDataAvailable: SecondsUntil(generatedAtUtc, nextDataAvailableEstimateUtc),
            SecondsUntilConservativeDataAvailable: SecondsUntil(generatedAtUtc, conservativeDataAvailableEstimateUtc),
            ExpectedRefreshDurationSeconds: (int)Math.Ceiling(expectedRefreshDuration.TotalSeconds),
            PostRefreshReadBufferSeconds: (int)postRefreshReadBuffer.TotalSeconds,
            EstimateBasis: BuildEstimateBasis(snapshot, nextRefreshDueUtc, nextRefreshStartEstimateUtc));
    }

    private static DateTimeOffset? ResolveNextRefreshStartEstimateUtc(
        TheOddsScheduledRefreshOptions options,
        TheOddsScheduledRefreshStateSnapshot snapshot,
        DateTimeOffset? nextRefreshDueUtc,
        bool isRefreshInProgress)
    {
        if (!options.Enabled)
        {
            return null;
        }

        if (isRefreshInProgress)
        {
            return snapshot.LastRunStartedAtUtc;
        }

        var nextWakeEstimateUtc = snapshot.WorkerRunning ? snapshot.NextWakeEstimateUtc : null;
        if (nextRefreshDueUtc.HasValue && nextWakeEstimateUtc.HasValue)
        {
            return nextWakeEstimateUtc.Value > nextRefreshDueUtc.Value
                ? nextWakeEstimateUtc.Value
                : nextRefreshDueUtc.Value;
        }

        return nextWakeEstimateUtc ?? nextRefreshDueUtc;
    }

    private static TimeSpan ResolveExpectedRefreshDuration(
        TheOddsScheduledRefreshOptions options,
        TheOddsRefreshPlanResult plan)
    {
        var configuredDuration = options.GetExpectedRefreshDuration();
        var observedMaxDuration = plan.Targets
            .Where(target => target.Enabled
                && target.LatestRunSucceeded == true
                && target.LatestRunStartedAtUtc.HasValue
                && target.LatestRunCompletedAtUtc.HasValue
                && target.LatestRunCompletedAtUtc.Value > target.LatestRunStartedAtUtc.Value)
            .Select(target => target.LatestRunCompletedAtUtc!.Value - target.LatestRunStartedAtUtc!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        return observedMaxDuration > configuredDuration
            ? observedMaxDuration
            : configuredDuration;
    }

    private static DateTimeOffset? ResolveNextHeaderRefreshDueUtc(
        TheOddsScheduledRefreshOptions options,
        TheOddsRefreshPlanResult plan,
        DateTimeOffset? fallbackNextRefreshDueUtc)
    {
        if (!options.Enabled)
        {
            return null;
        }

        var latestSuccessfulRefreshCompletedAtUtc = plan.Targets
            .Where(target => target.Enabled && target.LatestSuccessfulRefreshCompletedAtUtc.HasValue)
            .Select(target => target.LatestSuccessfulRefreshCompletedAtUtc)
            .Max() ?? plan.LastSuccessfulRefreshAtUtc;

        return latestSuccessfulRefreshCompletedAtUtc.HasValue
            ? latestSuccessfulRefreshCompletedAtUtc.Value.Add(options.GetInterval())
            : fallbackNextRefreshDueUtc;
    }

    private static int? SecondsUntil(DateTimeOffset generatedAtUtc, DateTimeOffset? targetUtc)
    {
        if (!targetUtc.HasValue)
        {
            return null;
        }

        return (int)Math.Ceiling((targetUtc.Value - generatedAtUtc).TotalSeconds);
    }

    private static string BuildEstimateBasis(
        TheOddsScheduledRefreshStateSnapshot snapshot,
        DateTimeOffset? nextRefreshDueUtc,
        DateTimeOffset? nextRefreshStartEstimateUtc)
    {
        if (string.Equals(snapshot.Status, "running", StringComparison.OrdinalIgnoreCase)
            && snapshot.LastRunStartedAtUtc.HasValue
            && !snapshot.LastRunCompletedAtUtc.HasValue)
        {
            return "ActiveRunStart";
        }

        if (snapshot.WorkerRunning
            && snapshot.NextWakeEstimateUtc.HasValue
            && nextRefreshStartEstimateUtc == snapshot.NextWakeEstimateUtc
            && (!nextRefreshDueUtc.HasValue || snapshot.NextWakeEstimateUtc.Value > nextRefreshDueUtc.Value))
        {
            return "WorkerWakeEstimate";
        }

        return "RefreshDueEstimate";
    }
}

public sealed record TheOddsScheduledRefreshTimingSnapshot(
    DateTimeOffset? NextRefreshDueUtc,
    DateTimeOffset? NextRefreshStartEstimateUtc,
    DateTimeOffset? NextHeaderSyncTargetUtc,
    DateTimeOffset? NextDataAvailableEstimateUtc,
    DateTimeOffset? ConservativeDataAvailableEstimateUtc,
    DateTimeOffset? NextRunEstimateUtc,
    bool IsRefreshInProgress,
    bool IsOverdue,
    int? SecondsUntilNextRefreshStart,
    int? SecondsUntilNextDataAvailable,
    int? SecondsUntilConservativeDataAvailable,
    int ExpectedRefreshDurationSeconds,
    int PostRefreshReadBufferSeconds,
    string EstimateBasis);
