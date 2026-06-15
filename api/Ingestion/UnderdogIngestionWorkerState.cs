namespace SportsBetting.Api.Ingestion;

public sealed class UnderdogIngestionWorkerState : IUnderdogIngestionWorkerState
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastSuccessTime;
    private TimeSpan? lastDuration;
    private string lastStatus = "not_started";
    private string? lastErrorMessage;
    private int failureCount;
    private bool isRunning;

    public DateTimeOffset? LastSuccessTime
    {
        get
        {
            lock (syncRoot)
            {
                return lastSuccessTime;
            }
        }
    }

    public TimeSpan? LastDuration
    {
        get
        {
            lock (syncRoot)
            {
                return lastDuration;
            }
        }
    }

    public string LastStatus
    {
        get
        {
            lock (syncRoot)
            {
                return lastStatus;
            }
        }
    }

    public string? LastErrorMessage
    {
        get
        {
            lock (syncRoot)
            {
                return lastErrorMessage;
            }
        }
    }

    public int FailureCount
    {
        get
        {
            lock (syncRoot)
            {
                return failureCount;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (syncRoot)
            {
                return isRunning;
            }
        }
    }

    public void MarkDisabled()
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastStatus = "disabled";
            lastErrorMessage = null;
        }
    }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        lock (syncRoot)
        {
            isRunning = true;
            lastStatus = "running";
        }
    }

    public void MarkSucceeded(DateTimeOffset completedAt, TimeSpan duration)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastSuccessTime = completedAt;
            lastDuration = duration;
            lastStatus = "succeeded";
            lastErrorMessage = null;
        }
    }

    public void MarkFailed(DateTimeOffset completedAt, TimeSpan duration, string errorMessage)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastDuration = duration;
            lastStatus = "failed";
            lastErrorMessage = errorMessage;
            failureCount++;
        }
    }

    public void MarkSkipped(DateTimeOffset skippedAt)
    {
        lock (syncRoot)
        {
            lastStatus = "skipped_overlapping_run";
        }
    }

    public void MarkCanceled(DateTimeOffset completedAt, TimeSpan duration)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastDuration = duration;
            lastStatus = "canceled";
        }
    }
}
