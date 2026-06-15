namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultImportWorkerOptions
{
    public const string SectionName = "MlbResultImportWorker";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int MaxBackfillDatesPerRun { get; set; } = 14;
}
