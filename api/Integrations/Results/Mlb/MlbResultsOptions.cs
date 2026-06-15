namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultsOptions
{
    public const string SectionName = "MlbResults";

    public string BaseUrl { get; set; } = "https://statsapi.mlb.com";
}
