namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultImportWorkerOptions
{
    public const string SectionName = "MlbResultImportWorker";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int MaxBackfillDatesPerRun { get; set; } = 14;
    public int RepairLookbackDays { get; set; } = 7;
    public int MinimumCompleteImportedResults { get; set; } = 1000;
    public int MinimumCompleteDistinctPlayers { get; set; } = 100;
    public int MinimumCompleteDistinctPropTypes { get; set; } = 12;
}
