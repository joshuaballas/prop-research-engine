namespace SportsBetting.Api.Ingestion.Players;

public sealed class PlayerMergeExecutionState : IPlayerMergeExecutionState
{
    private readonly Lock gate = new();
    private PlayerMergeExecutionStatusSnapshot snapshot = new(
        GroupsProcessed: 0,
        GroupsSucceeded: 0,
        GroupsFailed: 0,
        PlayersMerged: 0,
        PropsReassigned: 0,
        SnapshotsAffected: 0,
        MappingsReassigned: 0,
        ResultsReassigned: 0,
        StartedAt: null,
        CompletedAt: null);

    public void Update(PlayerMergeExecutionStatusSnapshot snapshot)
    {
        lock (gate)
        {
            this.snapshot = snapshot;
        }
    }

    public PlayerMergeExecutionStatusSnapshot Snapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }
}
