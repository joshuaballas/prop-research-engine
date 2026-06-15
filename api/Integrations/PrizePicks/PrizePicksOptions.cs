namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed class PrizePicksOptions
{
    public const string SectionName = "PrizePicks";

    // Keep this endpoint unfiltered by league. League configuration gates later analysis,
    // optimizer, and display workflows; it must not reduce ingestion coverage.
    public string ProjectionsUrl { get; set; } = "https://api.prizepicks.com/projections?per_page=250&single_stat=true";
}
