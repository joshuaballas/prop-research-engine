namespace SportsBetting.Api.Integrations.TheOddsApi;

public static class TheOddsScheduledRefreshTiming
{
    public static TheOddsScheduledRefreshTimingSnapshot Resolve(
        TheOddsScheduledRefreshOptions options,
        TheOddsRefreshPlanResult plan,
        TheOddsScheduledRefreshStateSnapshot snapshot)
    {
        var nowUtc = plan.GeneratedAtUtc;
        var interval = options.GetInterval();
        var isRefreshInProgress = string.Equals(snapshot.Status, "running", StringComparison.OrdinalIgnoreCase)
            && snapshot.LastRunStartedAtUtc.HasValue
            && !snapshot.LastRunCompletedAtUtc.HasValue;
        var latestSuccessfulRefreshCompletedAtUtc = Max(
            snapshot.LastSuccessfulRefreshCompletedAtUtc,
            ResolveLatestSuccessfulRefreshCompletedAtUtc(plan));
        var lastRunDurationSeconds = SecondsBetween(snapshot.LastRunStartedAtUtc, snapshot.LastRunCompletedAtUtc);
        var currentRunElapsedSeconds = isRefreshInProgress
            ? SecondsBetween(snapshot.LastRunStartedAtUtc, nowUtc)
            : null;
        var anchorUtc = Max(latestSuccessfulRefreshCompletedAtUtc, snapshot.LastRunCompletedAtUtc);
        var nextRefreshAtUtc = ResolveNextRefreshAtUtc(options.Enabled, anchorUtc, interval, nowUtc);
        var postRefreshReadBuffer = options.GetPostRefreshReadBuffer();
        var expectedRefreshDuration = options.GetExpectedRefreshDuration();
        var currentRunScheduledDueUtc = isRefreshInProgress
            ? ResolveCurrentRunScheduledDueUtc(snapshot.LastRunStartedAtUtc, snapshot.LastRunScheduledDueUtc, interval)
            : null;
        var currentRunEstimatedCompletionUtc = isRefreshInProgress
            ? snapshot.LastRunStartedAtUtc?.Add(expectedRefreshDuration)
            : null;
        var currentRunEstimatedRemainingSeconds = isRefreshInProgress
            ? NonNegativeSecondsUntil(nowUtc, currentRunEstimatedCompletionUtc)
            : null;
        var activeRunDataAvailableTargetUtc = isRefreshInProgress
            ? currentRunEstimatedCompletionUtc?.Add(postRefreshReadBuffer)
            : null;
        var nextHeaderSyncTargetUtc = activeRunDataAvailableTargetUtc ?? nextRefreshAtUtc?.Add(postRefreshReadBuffer);
        var secondsUntilNextRefresh = SecondsUntil(nowUtc, nextRefreshAtUtc);
        var secondsUntilNextDataAvailable = SecondsUntil(nowUtc, nextHeaderSyncTargetUtc);
        var isOverdue = options.Enabled
            && !isRefreshInProgress
            && nextHeaderSyncTargetUtc.HasValue
            && nextHeaderSyncTargetUtc.Value <= nowUtc;

        return new TheOddsScheduledRefreshTimingSnapshot(
            NextRefreshAtUtc: nextRefreshAtUtc,
            SecondsUntilNextRefresh: secondsUntilNextRefresh,
            LastSuccessfulRefreshCompletedAtUtc: latestSuccessfulRefreshCompletedAtUtc,
            IsRefreshInProgress: isRefreshInProgress,
            IsOverdue: isOverdue,
            CurrentRunStartedAtUtc: isRefreshInProgress ? snapshot.LastRunStartedAtUtc : null,
            CurrentRunElapsedSeconds: currentRunElapsedSeconds,
            LastRunDurationSeconds: lastRunDurationSeconds,
            LastError: snapshot.LastRunError,
            IntervalMinutes: options.IntervalMinutes,
            EffectiveIntervalMinutes: (int)interval.TotalMinutes,
            PostRefreshReadBufferSeconds: (int)postRefreshReadBuffer.TotalSeconds,
            ExpectedRefreshDurationSeconds: (int)Math.Ceiling(options.GetExpectedRefreshDuration().TotalSeconds),
            NextRefreshDueUtc: nextRefreshAtUtc,
            NextRefreshStartEstimateUtc: nextRefreshAtUtc,
            NextHeaderSyncTargetUtc: nextHeaderSyncTargetUtc,
            NextDataAvailableEstimateUtc: nextHeaderSyncTargetUtc,
            ConservativeDataAvailableEstimateUtc: nextHeaderSyncTargetUtc,
            NextRunEstimateUtc: nextRefreshAtUtc,
            WorkerNextWakeUtc: nextRefreshAtUtc,
            CurrentRunScheduledDueUtc: currentRunScheduledDueUtc,
            CurrentRunEstimatedRemainingSeconds: currentRunEstimatedRemainingSeconds,
            ActiveRunDataAvailableTargetUtc: activeRunDataAvailableTargetUtc,
            NextCycleRefreshDueUtc: null,
            NextCycleDataAvailableEstimateUtc: null,
            DataAvailableStatus: isRefreshInProgress ? "RefreshInProgress" : "Waiting",
            LastRunScheduledDueUtc: null,
            LastRunStartedDelaySeconds: null,
            SecondsUntilNextRefreshDue: secondsUntilNextRefresh,
            SecondsUntilNextRefreshStart: secondsUntilNextRefresh,
            SecondsUntilNextDataAvailable: secondsUntilNextDataAvailable,
            SecondsUntilConservativeDataAvailable: secondsUntilNextDataAvailable,
            EstimateBasis: "SimpleCompletionInterval");
    }

    private static DateTimeOffset? ResolveNextRefreshAtUtc(
        bool enabled,
        DateTimeOffset? anchorUtc,
        TimeSpan interval,
        DateTimeOffset nowUtc)
    {
        if (!enabled)
        {
            return null;
        }

        if (!anchorUtc.HasValue)
        {
            return nowUtc;
        }

        return anchorUtc.Value.Add(interval);
    }

    private static DateTimeOffset? ResolveCurrentRunScheduledDueUtc(
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? scheduledDueUtc,
        TimeSpan interval)
    {
        if (!startedAtUtc.HasValue)
        {
            return null;
        }

        if (!scheduledDueUtc.HasValue)
        {
            return startedAtUtc;
        }

        var tolerance = interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : interval;
        return scheduledDueUtc.Value >= startedAtUtc.Value.Subtract(tolerance)
            && scheduledDueUtc.Value <= startedAtUtc.Value.Add(tolerance)
            ? scheduledDueUtc
            : startedAtUtc;
    }

    private static DateTimeOffset? ResolveLatestSuccessfulRefreshCompletedAtUtc(TheOddsRefreshPlanResult plan)
    {
        return plan.Targets
            .Where(target => target.Enabled && target.LatestSuccessfulRefreshCompletedAtUtc.HasValue)
            .Select(target => target.LatestSuccessfulRefreshCompletedAtUtc)
            .Max() ?? plan.LastSuccessfulRefreshAtUtc;
    }

    private static int? SecondsUntil(DateTimeOffset generatedAtUtc, DateTimeOffset? targetUtc)
    {
        return targetUtc.HasValue
            ? (int)Math.Ceiling((targetUtc.Value - generatedAtUtc).TotalSeconds)
            : null;
    }

    private static int? NonNegativeSecondsUntil(DateTimeOffset generatedAtUtc, DateTimeOffset? targetUtc)
    {
        var seconds = SecondsUntil(generatedAtUtc, targetUtc);
        return seconds.HasValue ? Math.Max(0, seconds.Value) : null;
    }

    private static decimal? SecondsBetween(DateTimeOffset? startUtc, DateTimeOffset? endUtc)
    {
        if (!startUtc.HasValue || !endUtc.HasValue)
        {
            return null;
        }

        return Math.Round((decimal)(endUtc.Value - startUtc.Value).TotalSeconds, 3);
    }

    private static DateTimeOffset? Max(params DateTimeOffset?[] values)
    {
        var present = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return present.Length == 0 ? null : present.Max();
    }
}

public sealed record TheOddsScheduledRefreshTimingSnapshot(
    DateTimeOffset? NextRefreshAtUtc,
    int? SecondsUntilNextRefresh,
    DateTimeOffset? LastSuccessfulRefreshCompletedAtUtc,
    bool IsRefreshInProgress,
    bool IsOverdue,
    DateTimeOffset? CurrentRunStartedAtUtc,
    decimal? CurrentRunElapsedSeconds,
    decimal? LastRunDurationSeconds,
    string? LastError,
    int IntervalMinutes,
    int EffectiveIntervalMinutes,
    int PostRefreshReadBufferSeconds,
    int ExpectedRefreshDurationSeconds,
    DateTimeOffset? NextRefreshDueUtc,
    DateTimeOffset? NextRefreshStartEstimateUtc,
    DateTimeOffset? NextHeaderSyncTargetUtc,
    DateTimeOffset? NextDataAvailableEstimateUtc,
    DateTimeOffset? ConservativeDataAvailableEstimateUtc,
    DateTimeOffset? NextRunEstimateUtc,
    DateTimeOffset? WorkerNextWakeUtc,
    DateTimeOffset? CurrentRunScheduledDueUtc,
    int? CurrentRunEstimatedRemainingSeconds,
    DateTimeOffset? ActiveRunDataAvailableTargetUtc,
    DateTimeOffset? NextCycleRefreshDueUtc,
    DateTimeOffset? NextCycleDataAvailableEstimateUtc,
    string DataAvailableStatus,
    DateTimeOffset? LastRunScheduledDueUtc,
    decimal? LastRunStartedDelaySeconds,
    int? SecondsUntilNextRefreshDue,
    int? SecondsUntilNextRefreshStart,
    int? SecondsUntilNextDataAvailable,
    int? SecondsUntilConservativeDataAvailable,
    string EstimateBasis);
