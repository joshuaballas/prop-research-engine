namespace SportsBetting.Api.Ingestion;

public sealed class PrizePicksIngestionWorkerState : IPrizePicksIngestionWorkerState
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastSuccessfulIngestionTime;
    private TimeSpan? lastIngestionDuration;
    private string lastIngestionStatus = "not_started";
    private string? lastErrorMessage;
    private int failureCount;
    private bool isRunning;

    public DateTimeOffset? LastSuccessfulIngestionTime
    {
        get
        {
            lock (syncRoot)
            {
                return lastSuccessfulIngestionTime;
            }
        }
    }

    public TimeSpan? LastIngestionDuration
    {
        get
        {
            lock (syncRoot)
            {
                return lastIngestionDuration;
            }
        }
    }

    public string LastIngestionStatus
    {
        get
        {
            lock (syncRoot)
            {
                return lastIngestionStatus;
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
            lastIngestionStatus = "disabled";
            lastErrorMessage = null;
        }
    }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        lock (syncRoot)
        {
            isRunning = true;
            lastIngestionStatus = "running";
        }
    }

    public void MarkSucceeded(DateTimeOffset completedAt, TimeSpan duration)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastSuccessfulIngestionTime = completedAt;
            lastIngestionDuration = duration;
            lastIngestionStatus = "succeeded";
            lastErrorMessage = null;
        }
    }

    public void MarkFailed(DateTimeOffset completedAt, TimeSpan duration, string errorMessage)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastIngestionDuration = duration;
            lastIngestionStatus = "failed";
            lastErrorMessage = errorMessage;
            failureCount++;
        }
    }

    public void MarkSkipped(DateTimeOffset skippedAt)
    {
        lock (syncRoot)
        {
            lastIngestionStatus = "skipped_overlapping_run";
        }
    }

    public void MarkCanceled(DateTimeOffset completedAt, TimeSpan duration)
    {
        lock (syncRoot)
        {
            isRunning = false;
            lastIngestionDuration = duration;
            lastIngestionStatus = "canceled";
        }
    }
}
