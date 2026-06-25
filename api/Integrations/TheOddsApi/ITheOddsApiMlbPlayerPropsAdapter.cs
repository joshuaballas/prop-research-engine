namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsApiMlbPlayerPropsAdapter
{
    Task<TheOddsApiPlayerPropsSpikeResult> FetchSpikeAsync(
        string? eventId,
        string? markets,
        string? bookmakers,
        string? regions,
        string? oddsFormat,
        CancellationToken cancellationToken,
        string? sportKey = null);
}
