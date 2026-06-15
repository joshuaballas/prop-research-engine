namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed record MlbResultImportResult(
    DateOnly Date,
    int RecordsFound,
    int RecordsMapped,
    int RecordsInserted,
    int RecordsUpdated,
    TimeSpan ImportDuration);

public sealed record MlbResultImportStatus(
    DateOnly Date,
    int ImportedResults,
    int DistinctPlayers,
    int DistinctPropTypes);
