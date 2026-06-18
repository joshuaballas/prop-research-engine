namespace SportsBetting.Api.Endpoints;

public static class DfsDebugEndpoints
{
    public static IEndpointRouteBuilder MapDfsDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/dfs/provider-expansion-plan", () =>
        {
            var providers = new DfsProviderPlanRow[]
            {
                BuildProvider(
                    "Underdog",
                    "SupportedViaTheOddsApiDfs",
                    actionable: true,
                    "Underdog should be requested through The Odds API region `us_dfs` with bookmaker key `underdog` for customer-facing market data.",
                    "The Odds API us_dfs feed only",
                    blocker: null,
                    antiBotRisk: "Low through The Odds API provider feed.",
                    recommendedNextStep: "Keep direct Underdog disabled except for explicit debug/audit comparisons.",
                    priority: "High",
                    notes: new[]
                    {
                        "Active customer-facing Underdog rows come through The Odds API.",
                        "Actionable for the user."
                    }),
                BuildProvider(
                    "PrizePicks",
                    "SupportedViaTheOddsApiDfs",
                    actionable: true,
                    "PrizePicks should be requested through The Odds API region `us_dfs` with bookmaker key `prizepicks`.",
                    "The Odds API us_dfs feed only",
                    blocker: null,
                    antiBotRisk: "Low through The Odds API provider feed.",
                    recommendedNextStep: "Use `bookmakerPreset=dfsOnly` or include configured DFS bookmakers when intentionally refreshing current markets.",
                    priority: "High",
                    notes: new[]
                    {
                        "Actionable if The Odds API returns current PrizePicks rows.",
                        "DFS odds are indicative and can vary based on user selections."
                    }),
                BuildProvider(
                    "DraftKings Pick6",
                    "CandidateViaTheOddsApiDfs",
                    actionable: true,
                    "Not directly integrated; The Odds API `us_dfs` supports bookmaker key `pick6`.",
                    "The Odds API us_dfs feed",
                    blocker: "Needs live feed validation and backtesting.",
                    antiBotRisk: "Low through The Odds API.",
                    recommendedNextStep: "Enable through ignored local config, run dry-run refresh, then live refresh only when credit spend is intended.",
                    priority: "Medium",
                    notes: new[] { "Actionable DFS/pick'em surface if data quality is sufficient." }),
                BuildProvider(
                    "Betr Picks",
                    "CandidateViaTheOddsApiDfs",
                    actionable: true,
                    "Not directly integrated; The Odds API `us_dfs` supports bookmaker key `betr_us_dfs`.",
                    "The Odds API us_dfs feed",
                    blocker: "Needs live feed validation and backtesting.",
                    antiBotRisk: "Low through The Odds API.",
                    recommendedNextStep: "Enable through ignored local config after PrizePicks/Underdog/Pick6 validation.",
                    priority: "Medium",
                    notes: new[] { "Actionable DFS/pick'em surface if data quality is sufficient." }),
                BuildProvider(
                    "Sleeper Picks",
                    "Candidate",
                    actionable: true,
                    "No active integration yet.",
                    "Unknown; research official/allowed feed, export, or provider aggregator coverage.",
                    blocker: "Data accessibility not researched in this app.",
                    antiBotRisk: "Unknown; avoid scraping/bypass approaches.",
                    recommendedNextStep: "Research data accessibility and terms before implementation.",
                    priority: "Later",
                    notes: new[] { "Actionable DFS/pick'em provider if an allowed stable data source exists." }),
                BuildProvider(
                    "ParlayPlay",
                    "Candidate",
                    actionable: true,
                    "No active integration yet.",
                    "Unknown; research official/allowed feed, export, or provider aggregator coverage.",
                    blocker: "Data accessibility not researched in this app.",
                    antiBotRisk: "Unknown; avoid scraping/bypass approaches.",
                    recommendedNextStep: "Research data accessibility and terms before implementation.",
                    priority: "Later",
                    notes: new[] { "Actionable DFS/pick'em provider if an allowed stable data source exists." }),
                BuildProvider(
                    "Dabble / Other DFS Pick'em",
                    "Candidate",
                    actionable: true,
                    "No active integration yet.",
                    "Unknown; research official/allowed feed, export, or provider aggregator coverage.",
                    blocker: "Data accessibility not researched in this app.",
                    antiBotRisk: "Unknown; avoid scraping/bypass approaches.",
                    recommendedNextStep: "Research one provider at a time after The Odds API us_dfs validation.",
                    priority: "Later",
                    notes: new[] { "Grouped placeholder for future DFS expansion." }),
                BuildProvider(
                    "The Odds API Sportsbooks",
                    "SupportedMarketContext",
                    actionable: false,
                    "Supported for MLB sportsbook line-shopping and market pricing context.",
                    "The Odds API region `us` with sportsbook bookmaker keys.",
                    blocker: "Not directly actionable for this DFS-only user.",
                    antiBotRisk: "Low through The Odds API.",
                    recommendedNextStep: "Keep as consensus/context while surfacing DFS rows as the actionable default.",
                    priority: "Context",
                    notes: new[]
                    {
                        "Useful for market pricing and comparison.",
                        "Not the final action surface for this user."
                    })
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                usesPersistedDatabaseRows = false,
                summary = new
                {
                    actionableSupportedProviders = providers.Count(row => row.Actionable && row.Status.Contains("Supported", StringComparison.OrdinalIgnoreCase)),
                    actionableCandidateProviders = providers.Count(row => row.Actionable && row.Status.Contains("Candidate", StringComparison.OrdinalIgnoreCase)),
                    primaryActionableProvider = "Underdog",
                    dfsOddsNote = "The Odds API DFS odds are indicative and can vary based on user selections.",
                    recommendedNextStep = "Validate The Odds API `us_dfs` rows with bookmakerPreset=dfsOnly before expanding DFS workflows."
                },
                providers
            });
        })
        .WithName("DfsProviderExpansionPlanDebug")
        .WithSummary("Read-only DFS provider expansion and status plan.");

        endpoints.MapGet("/api/debug/dfs/entry-rules", () =>
        {
            return Results.Ok(DfsEntryRulesCatalog.BuildResponse());
        })
        .WithName("DfsEntryRulesDebug")
        .WithSummary("Read-only provider-scoped DFS entry compatibility rules.");

        return endpoints;
    }

    private static DfsProviderPlanRow BuildProvider(
        string provider,
        string status,
        bool actionable,
        string currentIntegration,
        string dataAccessMethod,
        string? blocker,
        string antiBotRisk,
        string recommendedNextStep,
        string priority,
        IReadOnlyList<string> notes)
    {
        return new DfsProviderPlanRow(
            provider,
            status,
            actionable,
            currentIntegration,
            dataAccessMethod,
            blocker,
            antiBotRisk,
            recommendedNextStep,
            priority,
            notes);
    }

    private sealed record DfsProviderPlanRow(
        string Provider,
        string Status,
        bool Actionable,
        string CurrentIntegration,
        string DataAccessMethod,
        string? Blocker,
        string AntiBotRisk,
        string RecommendedNextStep,
        string Priority,
        IReadOnlyList<string> Notes);

}
