namespace SportsBetting.Api.Ingestion;

public interface IPrizePicksIngestionWorkerState
{
    DateTimeOffset? LastSuccessfulIngestionTime { get; }
    TimeSpan? LastIngestionDuration { get; }
    string LastIngestionStatus { get; }
    string? LastErrorMessage { get; }
    int FailureCount { get; }
    bool IsRunning { get; }

    void MarkDisabled();
    void MarkStarted(DateTimeOffset startedAt);
    void MarkSucceeded(DateTimeOffset completedAt, TimeSpan duration);
    void MarkFailed(DateTimeOffset completedAt, TimeSpan duration, string errorMessage);
    void MarkSkipped(DateTimeOffset skippedAt);
    void MarkCanceled(DateTimeOffset completedAt, TimeSpan duration);
}
