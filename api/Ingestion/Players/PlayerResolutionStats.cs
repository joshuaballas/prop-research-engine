namespace SportsBetting.Api.Ingestion.Players;

public sealed class PlayerResolutionStats : IPlayerResolutionStats
{
    private const int CreatedPlayerHistoryLimit = 500;
    private readonly Lock gate = new();
    private readonly Queue<PlayerCreatedResolutionDiagnostic> createdPlayers = new();
    private long resolvedByExternalRef;
    private long resolvedByLegacyRef;
    private long resolvedByFallback;
    private long newlyCreated;

    public void Record(PlayerResolutionMethod method)
    {
        switch (method)
        {
            case PlayerResolutionMethod.ExternalRef:
                Interlocked.Increment(ref resolvedByExternalRef);
                break;
            case PlayerResolutionMethod.LegacyRef:
                Interlocked.Increment(ref resolvedByLegacyRef);
                break;
            case PlayerResolutionMethod.Fallback:
                Interlocked.Increment(ref resolvedByFallback);
                break;
            case PlayerResolutionMethod.Created:
                Interlocked.Increment(ref newlyCreated);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown player resolution method.");
        }
    }

    public void RecordCreated(PlayerCreatedResolutionDiagnostic createdPlayer)
    {
        lock (gate)
        {
            createdPlayers.Enqueue(createdPlayer);
            while (createdPlayers.Count > CreatedPlayerHistoryLimit)
            {
                createdPlayers.Dequeue();
            }
        }
    }

    public PlayerResolutionStatsSnapshot Snapshot()
    {
        return new PlayerResolutionStatsSnapshot(
            Interlocked.Read(ref resolvedByExternalRef),
            Interlocked.Read(ref resolvedByLegacyRef),
            Interlocked.Read(ref resolvedByFallback),
            Interlocked.Read(ref newlyCreated));
    }

    public IReadOnlyCollection<PlayerCreatedResolutionDiagnostic> CreatedPlayers()
    {
        lock (gate)
        {
            return createdPlayers
                .OrderByDescending(player => player.CreatedAt)
                .ThenBy(player => player.DisplayName)
                .ToArray();
        }
    }
}
