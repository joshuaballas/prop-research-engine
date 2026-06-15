namespace SportsBetting.Api.Ingestion;

public sealed class UnderdogIngestionOptions
{
    public const string SectionName = "UnderdogIngestion";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
    }
}
