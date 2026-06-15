namespace SportsBetting.Api.Predictions;

public sealed class PredictionGenerationOptions
{
    public const string SectionName = "PredictionGeneration";

    public decimal OverEdgeThreshold { get; set; } = 999m;
    public decimal UnderEdgeThreshold { get; set; } = 0.75m;
    public string[] EnabledPropTypes { get; set; } =
    [
        "Home Runs",
        "Stolen Bases",
        "Doubles",
        "Singles"
    ];
}
