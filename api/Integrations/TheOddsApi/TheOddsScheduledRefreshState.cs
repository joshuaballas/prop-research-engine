namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsScheduledRefreshState
{
    TheOddsScheduledRefreshStateSnapshot GetSnapshot();
    void MarkDisabled();
    void MarkStarted(DateTimeOffset startedAt);
    void MarkBlocked(DateTimeOffset completedAt, string reason);
    void MarkCompleted(DateTimeOffset completedAt);
    void MarkFailed(DateTimeOffset completedAt, string error);
}

public sealed class TheOddsScheduledRefreshState : ITheOddsScheduledRefreshState
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastRunStartedAtUtc;
    private DateTimeOffset? lastRunCompletedAtUtc;
    private bool? lastRunSucceeded;
    private bool lastRunBlocked;
    private string? lastRunError;
    private string status = "disabled";

    public TheOddsScheduledRefreshStateSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new TheOddsScheduledRefreshStateSnapshot(
                lastRunStartedAtUtc,
                lastRunCompletedAtUtc,
                lastRunSucceeded,
                lastRunBlocked,
                lastRunError,
                status);
        }
    }

    public void MarkDisabled()
    {
        lock (syncRoot)
        {
            status = "disabled";
            lastRunBlocked = false;
            lastRunError = null;
        }
    }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        lock (syncRoot)
        {
            status = "running";
            lastRunStartedAtUtc = startedAt;
            lastRunCompletedAtUtc = null;
            lastRunSucceeded = null;
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
            lastRunBlocked = true;
            lastRunError = reason;
        }
    }

    public void MarkCompleted(DateTimeOffset completedAt)
    {
        lock (syncRoot)
        {
            status = "completed";
            lastRunCompletedAtUtc = completedAt;
            lastRunSucceeded = true;
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
            lastRunBlocked = false;
            lastRunError = error;
        }
    }
}

public sealed record TheOddsScheduledRefreshStateSnapshot(
    DateTimeOffset? LastRunStartedAtUtc,
    DateTimeOffset? LastRunCompletedAtUtc,
    bool? LastRunSucceeded,
    bool LastRunBlocked,
    string? LastRunError,
    string Status);
