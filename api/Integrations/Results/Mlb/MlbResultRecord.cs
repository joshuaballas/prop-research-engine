using SportsBetting.Api.Integrations.Results;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed record MlbResultRecord(
    ResultRecord ResultRecord,
    string PlayerName,
    string MlbPlayerId,
    string GameId,
    string StatName,
    decimal StatValue,
    bool IsMapped);
