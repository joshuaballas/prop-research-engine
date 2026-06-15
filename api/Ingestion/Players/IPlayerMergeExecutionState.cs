namespace SportsBetting.Api.Ingestion.Players;

public interface IPlayerMergeExecutionState
{
    void Update(PlayerMergeExecutionStatusSnapshot snapshot);

    PlayerMergeExecutionStatusSnapshot Snapshot();
}

public sealed record PlayerMergeExecutionStatusSnapshot(
    int GroupsProcessed,
    int GroupsSucceeded,
    int GroupsFailed,
    int PlayersMerged,
    int PropsReassigned,
    int SnapshotsAffected,
    int MappingsReassigned,
    int ResultsReassigned,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
