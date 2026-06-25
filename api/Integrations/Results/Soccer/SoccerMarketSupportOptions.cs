namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class SoccerPredictionsOptions
{
    public const string SectionName = "SoccerPredictions";

    public bool Enabled { get; set; }
    public int MinimumSampleSize { get; set; } = 5;
    public int LowSampleWarningThreshold { get; set; } = 10;
    public int TopPickMinimumSampleSize { get; set; } = 10;
    public decimal MinimumTopPickHitProbability { get; set; } = 0.55m;
}

public sealed class SoccerGradingOptions
{
    public const string SectionName = "SoccerGrading";

    public bool Enabled { get; set; }
}

public sealed class SoccerTopPicksOptions
{
    public const string SectionName = "SoccerTopPicks";

    public bool Enabled { get; set; }
    public int MinimumTopPickSampleSize { get; set; } = 20;
    public decimal MinimumHitProbability { get; set; } = 0.55m;
    public bool AllowNegativeEv { get; set; } = true;
}
