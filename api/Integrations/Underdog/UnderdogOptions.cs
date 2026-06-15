namespace SportsBetting.Api.Integrations.Underdog;

public sealed class UnderdogOptions
{
    public const string SectionName = "Underdog";

    public string OverUnderLinesUrl { get; set; } = "https://api.underdogfantasy.com/beta/v5/over_under_lines";
}
