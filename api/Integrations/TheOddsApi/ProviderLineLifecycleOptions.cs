namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class ProviderLineLifecycleOptions
{
    public const string SectionName = "ProviderLineLifecycle";

    public int ActiveLineMaxAgeMinutes { get; set; } = 15;
    public int PulledAfterSuccessfulMissingRefreshes { get; set; } = 2;
    public int ExpireAfterHours { get; set; } = 12;
    public bool RecordSeenUnchangedEvents { get; set; }
}
