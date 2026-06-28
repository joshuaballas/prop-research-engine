namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsScheduledRefreshState
{
    TheOddsScheduledRefreshStateSnapshot GetSnapshot();
    void MarkDisabled();
    void MarkWaiting(DateTimeOffset observedAt, DateTimeOffset nextWakeAt);
    void MarkStarted(DateTimeOffset startedAt, DateTimeOffset? scheduledDueAt = null);
    void MarkBlocked(DateTimeOffset completedAt, string reason);
    void MarkSkipped(DateTimeOffset completedAt, string reason);
    void MarkPlan(TheOddsRefreshPlanResult plan);
    void MarkCompleted(DateTimeOffset completedAt);
    void MarkFailed(DateTimeOffset completedAt, string error);
}

public sealed class TheOddsScheduledRefreshState : ITheOddsScheduledRefreshState
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastRunStartedAtUtc;
    private DateTimeOffset? lastRunScheduledDueUtc;
    private DateTimeOffset? lastRunCompletedAtUtc;
    private DateTimeOffset? lastSuccessfulRefreshCompletedAtUtc;
    private bool? lastRunSucceeded;
    private bool lastRunSkipped;
    private bool lastRunBlocked;
    private string? lastRunError;
    private string status = "disabled";
    private bool workerRunning;
    private DateTimeOffset? lastWakeAtUtc;
    private DateTimeOffset? nextWakeEstimateUtc;
    private DateTimeOffset? lastPlanGeneratedAtUtc;
    private bool? lastPlanShouldRefresh;
    private string? lastPlanSkipReason;
    private int? lastPlanEstimatedApiCalls;
    private bool? lastPlanBudgetAllowed;
    private bool? lastPlanProviderAvailable;
    private DateTimeOffset? lastSkippedAtUtc;
    private string? lastSkipReason;

    public TheOddsScheduledRefreshStateSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new TheOddsScheduledRefreshStateSnapshot(
                lastRunStartedAtUtc,
                lastRunScheduledDueUtc,
                lastRunCompletedAtUtc,
                lastSuccessfulRefreshCompletedAtUtc,
                lastRunSucceeded,
                lastRunSkipped,
                lastRunBlocked,
                lastRunError,
                status,
                workerRunning,
                lastWakeAtUtc,
                nextWakeEstimateUtc,
                lastPlanGeneratedAtUtc,
                lastPlanShouldRefresh,
                lastPlanSkipReason,
                lastPlanEstimatedApiCalls,
                lastPlanBudgetAllowed,
                lastPlanProviderAvailable,
                lastSkippedAtUtc,
                lastSkipReason);
        }
    }

    public void MarkDisabled()
    {
        lock (syncRoot)
        {
            status = "disabled";
            workerRunning = false;
            nextWakeEstimateUtc = null;
            lastRunSkipped = false;
            lastRunBlocked = false;
            lastRunError = null;
        }
    }

    public void MarkWaiting(DateTimeOffset observedAt, DateTimeOffset nextWakeAt)
    {
        lock (syncRoot)
        {
            status = "waiting";
            workerRunning = true;
            lastWakeAtUtc = observedAt;
            nextWakeEstimateUtc = nextWakeAt;
            lastRunBlocked = false;
            lastRunError = null;
        }
    }

    public void MarkStarted(DateTimeOffset startedAt, DateTimeOffset? scheduledDueAt = null)
    {
        lock (syncRoot)
        {
            status = "running";
            workerRunning = true;
            lastWakeAtUtc = startedAt;
            nextWakeEstimateUtc = null;
            lastRunStartedAtUtc = startedAt;
            lastRunScheduledDueUtc = scheduledDueAt;
            lastRunCompletedAtUtc = null;
            lastRunSucceeded = null;
            lastRunSkipped = false;
            lastRunBlocked = false;
            lastRunError = null;
        }
    }

    public void MarkBlocked(DateTimeOffset completedAt, string reason)
    {
        lock (syncRoot)
        {
            status = "blocked";
            lastRunCompletedAtUtc = completedAt;
            lastRunSucceeded = false;
            lastRunSkipped = false;
            lastRunBlocked = true;
            lastRunError = reason;
        }
    }

    public void MarkSkipped(DateTimeOffset completedAt, string reason)
    {
        lock (syncRoot)
        {
            status = "skipped";
            lastRunCompletedAtUtc = completedAt;
            lastRunSucceeded = false;
            lastRunSkipped = true;
            lastRunBlocked = false;
            lastRunError = null;
            lastSkippedAtUtc = completedAt;
            lastSkipReason = reason;
        }
    }

    public void MarkPlan(TheOddsRefreshPlanResult plan)
    {
        lock (syncRoot)
        {
            lastPlanGeneratedAtUtc = plan.GeneratedAtUtc;
            lastPlanShouldRefresh = plan.ShouldRefresh;
            lastPlanSkipReason = plan.SkipReason;
            lastPlanEstimatedApiCalls = plan.EstimatedApiCalls;
            lastPlanBudgetAllowed = plan.Budget.BudgetAllowed;
            lastPlanProviderAvailable = plan.ProviderAvailable;
            var latestPlanSuccessAt = plan.Targets
                .Where(target => target.Enabled && target.LatestSuccessfulRefreshCompletedAtUtc.HasValue)
                .Select(target => target.LatestSuccessfulRefreshCompletedAtUtc)
                .Max() ?? plan.LastSuccessfulRefreshAtUtc;
            if (latestPlanSuccessAt.HasValue &&
                (!lastSuccessfulRefreshCompletedAtUtc.HasValue || latestPlanSuccessAt.Value > lastSuccessfulRefreshCompletedAtUtc.Value))
            {
                lastSuccessfulRefreshCompletedAtUtc = latestPlanSuccessAt.Value;
            }
        }
    }

    public void MarkCompleted(DateTimeOffset completedAt)
    {
        lock (syncRoot)
        {
            status = "completed";
            lastRunCompletedAtUtc = completedAt;
            lastSuccessfulRefreshCompletedAtUtc = completedAt;
            lastRunSucceeded = true;
            lastRunSkipped = false;
            lastRunBlocked = false;
            lastRunError = null;
        }
    }

    public void MarkFailed(DateTimeOffset completedAt, string error)
    {
        lock (syncRoot)
        {
            status = "failed";
            lastRunCompletedAtUtc = completedAt;
            lastRunSucceeded = false;
            lastRunSkipped = false;
            lastRunBlocked = false;
            lastRunError = error;
        }
    }
}

public sealed record TheOddsScheduledRefreshStateSnapshot(
    DateTimeOffset? LastRunStartedAtUtc,
    DateTimeOffset? LastRunScheduledDueUtc,
    DateTimeOffset? LastRunCompletedAtUtc,
    DateTimeOffset? LastSuccessfulRefreshCompletedAtUtc,
    bool? LastRunSucceeded,
    bool LastRunSkipped,
    bool LastRunBlocked,
    string? LastRunError,
    string Status,
    bool WorkerRunning,
    DateTimeOffset? LastWakeAtUtc,
    DateTimeOffset? NextWakeEstimateUtc,
    DateTimeOffset? LastPlanGeneratedAtUtc,
    bool? LastPlanShouldRefresh,
    string? LastPlanSkipReason,
    int? LastPlanEstimatedApiCalls,
    bool? LastPlanBudgetAllowed,
    bool? LastPlanProviderAvailable,
    DateTimeOffset? LastSkippedAtUtc,
    string? LastSkipReason);
