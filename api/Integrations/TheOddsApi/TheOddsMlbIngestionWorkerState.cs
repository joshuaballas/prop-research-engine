namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsMlbIngestionWorkerState
{
    TheOddsMlbIngestionWorkerStateSnapshot GetSnapshot();
    int GetEstimatedCreditsUsedToday(DateOnly utcDate);
    void MarkDisabled(IReadOnlyList<string> skippedReasons);
    void MarkStarted(DateTimeOffset startedAt, bool dryRun);
    void MarkCompleted(
        DateTimeOffset completedAt,
        string status,
        bool dryRun,
        IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> plannedEvents,
        int candidateEvents,
        int estimatedCredits,
        IReadOnlyList<string> skippedReasons,
        bool consumeEstimatedCredits);
    void MarkFailed(DateTimeOffset completedAt, bool dryRun, string error);
    void MarkManualDryRun(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> plannedEvents,
        int candidateEvents,
        int estimatedCredits,
        IReadOnlyList<string> skippedReasons);
}

public sealed class TheOddsMlbIngestionWorkerState : ITheOddsMlbIngestionWorkerState
{
    private readonly object syncRoot = new();
    private DateOnly estimatedCreditsDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private int estimatedCreditsUsedToday;
    private DateTimeOffset? lastRunStartedAt;
    private DateTimeOffset? lastRunCompletedAt;
    private string lastRunStatus = "not_started";
    private bool lastRunDryRun = true;
    private int lastRunCandidateEvents;
    private int lastRunEstimatedCredits;
    private IReadOnlyList<string> lastRunSkippedReasons = [];
    private IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> lastRunPlannedEvents = [];
    private string? lastError;
    private DateTimeOffset? lastManualDryRunStartedAt;
    private DateTimeOffset? lastManualDryRunCompletedAt;
    private int lastManualDryRunCandidateEvents;
    private int lastManualDryRunEstimatedCredits;
    private IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> lastManualDryRunPlannedEvents = [];
    private IReadOnlyList<string> lastManualDryRunSkippedReasons = [];

    public TheOddsMlbIngestionWorkerStateSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            RefreshCreditWindow(DateOnly.FromDateTime(DateTime.UtcNow));
            return new TheOddsMlbIngestionWorkerStateSnapshot(
                estimatedCreditsUsedToday,
                lastRunStartedAt,
                lastRunCompletedAt,
                lastRunStatus,
                lastRunDryRun,
                lastRunCandidateEvents,
                lastRunEstimatedCredits,
                lastRunSkippedReasons,
                lastRunPlannedEvents,
                lastError,
                lastManualDryRunStartedAt,
                lastManualDryRunCompletedAt,
                lastManualDryRunCandidateEvents,
                lastManualDryRunEstimatedCredits,
                lastManualDryRunPlannedEvents,
                lastManualDryRunSkippedReasons);
        }
    }

    public int GetEstimatedCreditsUsedToday(DateOnly utcDate)
    {
        lock (syncRoot)
        {
            RefreshCreditWindow(utcDate);
            return estimatedCreditsUsedToday;
        }
    }

    public void MarkDisabled(IReadOnlyList<string> skippedReasons)
    {
        lock (syncRoot)
        {
            lastRunStatus = "disabled";
            lastRunDryRun = true;
            lastRunCandidateEvents = 0;
            lastRunEstimatedCredits = 0;
            lastRunSkippedReasons = skippedReasons;
            lastRunPlannedEvents = [];
            lastError = null;
        }
    }

    public void MarkStarted(DateTimeOffset startedAt, bool dryRun)
    {
        lock (syncRoot)
        {
            lastRunStartedAt = startedAt;
            lastRunCompletedAt = null;
            lastRunStatus = "running";
            lastRunDryRun = dryRun;
            lastError = null;
        }
    }

    public void MarkCompleted(
        DateTimeOffset completedAt,
        string status,
        bool dryRun,
        IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> plannedEvents,
        int candidateEvents,
        int estimatedCredits,
        IReadOnlyList<string> skippedReasons,
        bool consumeEstimatedCredits)
    {
        lock (syncRoot)
        {
            RefreshCreditWindow(DateOnly.FromDateTime(completedAt.UtcDateTime));
            lastRunCompletedAt = completedAt;
            lastRunStatus = status;
            lastRunDryRun = dryRun;
            lastRunCandidateEvents = candidateEvents;
            lastRunEstimatedCredits = estimatedCredits;
            lastRunSkippedReasons = skippedReasons;
            lastRunPlannedEvents = plannedEvents;
            lastError = null;

            if (consumeEstimatedCredits)
            {
                estimatedCreditsUsedToday += estimatedCredits;
            }
        }
    }

    public void MarkFailed(DateTimeOffset completedAt, bool dryRun, string error)
    {
        lock (syncRoot)
        {
            lastRunCompletedAt = completedAt;
            lastRunStatus = "failed";
            lastRunDryRun = dryRun;
            lastError = error;
        }
    }

    public void MarkManualDryRun(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> plannedEvents,
        int candidateEvents,
        int estimatedCredits,
        IReadOnlyList<string> skippedReasons)
    {
        lock (syncRoot)
        {
            RefreshCreditWindow(DateOnly.FromDateTime(completedAt.UtcDateTime));
            lastManualDryRunStartedAt = startedAt;
            lastManualDryRunCompletedAt = completedAt;
            lastManualDryRunCandidateEvents = candidateEvents;
            lastManualDryRunEstimatedCredits = estimatedCredits;
            lastManualDryRunPlannedEvents = plannedEvents;
            lastManualDryRunSkippedReasons = skippedReasons;
        }
    }

    private void RefreshCreditWindow(DateOnly utcDate)
    {
        if (estimatedCreditsDate == utcDate)
        {
            return;
        }

        estimatedCreditsDate = utcDate;
        estimatedCreditsUsedToday = 0;
    }
}

public sealed record TheOddsMlbIngestionWorkerStateSnapshot(
    int EstimatedCreditsUsedToday,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    string LastRunStatus,
    bool LastRunDryRun,
    int LastRunCandidateEvents,
    int LastRunEstimatedCredits,
    IReadOnlyList<string> LastRunSkippedReasons,
    IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> LastRunPlannedEvents,
    string? LastError,
    DateTimeOffset? LastManualDryRunStartedAt,
    DateTimeOffset? LastManualDryRunCompletedAt,
    int LastManualDryRunCandidateEvents,
    int LastManualDryRunEstimatedCredits,
    IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> LastManualDryRunPlannedEvents,
    IReadOnlyList<string> LastManualDryRunSkippedReasons);

public sealed record TheOddsMlbIngestionWorkerPlannedEvent(
    string EventId,
    string EventName,
    DateTimeOffset? EventStartTime,
    int PlatformPropCount,
    bool CoverageOpportunity,
    string Status,
    IReadOnlyList<string> Reasons);
