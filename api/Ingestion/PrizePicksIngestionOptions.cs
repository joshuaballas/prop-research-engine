namespace SportsBetting.Api.Ingestion;

public sealed class PrizePicksIngestionOptions
{
    public const string SectionName = "PrizePicksIngestion";

    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 5;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
    }
}
