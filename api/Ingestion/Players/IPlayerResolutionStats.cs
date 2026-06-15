namespace SportsBetting.Api.Ingestion.Players;

public interface IPlayerResolutionStats
{
    void Record(PlayerResolutionMethod method);

    void RecordCreated(PlayerCreatedResolutionDiagnostic createdPlayer);

    PlayerResolutionStatsSnapshot Snapshot();

    IReadOnlyCollection<PlayerCreatedResolutionDiagnostic> CreatedPlayers();
}

public sealed record PlayerResolutionStatsSnapshot(
    long ResolvedByExternalRef,
    long ResolvedByLegacyRef,
    long ResolvedByFallback,
    long NewlyCreated);

public sealed record PlayerCreatedResolutionDiagnostic(
    Guid PlayerId,
    string DisplayName,
    string LeagueCode,
    string Provider,
    string? ProviderPlayerId,
    DateTimeOffset CreatedAt,
    string CreationReason);
