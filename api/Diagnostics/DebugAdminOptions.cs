namespace SportsBetting.Api.Diagnostics;

public sealed class DebugAdminOptions
{
    public const string SectionName = "DebugAdmin";

    public bool Enabled { get; set; } = false;
    public bool RequireApiKey { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
}
