namespace SportsBetting.Api.Predictions;

public interface IPlayerIdentityAliasService
{
    Task<IReadOnlyCollection<Guid>> GetEquivalentPlayerIdsAsync(
        Guid playerId,
        string leagueCode,
        CancellationToken cancellationToken);
}
