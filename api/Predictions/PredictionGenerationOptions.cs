namespace SportsBetting.Api.Predictions;

public sealed class PredictionGenerationOptions
{
    public const string SectionName = "MlbPredictionGeneration";
    public const string LegacySectionName = "PredictionGeneration";

    public decimal OverProjectionEdgeThreshold { get; set; } = 0.75m;
    public decimal UnderProjectionEdgeThreshold { get; set; } = 0.75m;
    public decimal MinimumPickHitProbability { get; set; } = 0.55m;
    public decimal StrongPickHitProbability { get; set; } = 0.60m;
    public decimal ResearchOnlyBelowHitProbability { get; set; } = 0.50m;
    public string[] EnabledPropTypes { get; set; } =
    [
        "Home Runs",
        "Stolen Bases",
        "Doubles",
        "Singles"
    ];

    public decimal OverEdgeThreshold
    {
        get => OverProjectionEdgeThreshold;
        set => OverProjectionEdgeThreshold = value;
    }

    public decimal UnderEdgeThreshold
    {
        get => UnderProjectionEdgeThreshold;
        set => UnderProjectionEdgeThreshold = value;
    }
}
