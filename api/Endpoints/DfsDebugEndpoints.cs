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
                    "Supported",
                    actionable: true,
                    "Direct Underdog ingestion works today; The Odds API `us_dfs` can also provide Underdog market context when configured.",
                    "Direct Underdog API plus optional The Odds API us_dfs feed",
                    blocker: null,
                    antiBotRisk: "Low for current direct integration; The Odds API handles provider access for us_dfs.",
                    recommendedNextStep: "Keep direct Underdog running. Use The Odds API DFS rows as comparison/context if enabled.",
                    priority: "High",
                    notes: new[]
                    {
                        "Current working DFS provider.",
                        "Actionable for the user."
                    }),
                BuildProvider(
                    "PrizePicks",
                    "SupportedViaTheOddsApiDfs",
                    actionable: true,
                    "Direct PrizePicks scraping/API access remains retired, but PrizePicks can be requested through The Odds API region `us_dfs` with bookmaker key `prizepicks`.",
                    "The Odds API us_dfs feed only; no direct scraper",
                    blocker: "Direct attempts previously hit DataDome/403.",
                    antiBotRisk: "Do not bypass DataDome. The Odds API provider feed is the safe supported path.",
                    recommendedNextStep: "Use `includeDfs=true` with configured DFS bookmakers. Do not re-enable old PrizePicks scraping/browser automation.",
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
                    recommendedNextStep = "Validate The Odds API `us_dfs` rows with includeDfs=true. Keep old PrizePicks direct scraping retired."
                },
                providers
            });
        })
        .WithName("DfsProviderExpansionPlanDebug")
        .WithSummary("Read-only DFS provider expansion and status plan.");

        endpoints.MapGet("/api/debug/dfs/entry-rules", () =>
        {
            var rules = new DfsEntryRuleRow[]
            {
                BuildEntryRule(
                    "PrizePicks",
                    "DFS",
                    actionableForUser: true,
                    "PrizePicks",
                    minLegs: 2,
                    maxLegs: 6,
                    allowedLegCounts: new[] { 2, 3, 4, 5, 6 },
                    entryTypes: new[] { "Power", "Flex" },
                    supportsFlex: true,
                    supportsPower: true,
                    supportsDemonsGoblinsOrAltLines: "Unknown",
                    sameGameAllowed: "Unknown",
                    notes: new[]
                    {
                        "PrizePicks entries should use PrizePicks legs only.",
                        "PrizePicks direct scraping remains retired; current PrizePicks rows should come through The Odds API us_dfs."
                    },
                    sourceConfidence: "NeedsVerification"),
                BuildEntryRule(
                    "Underdog",
                    "DFS",
                    actionableForUser: true,
                    "Underdog",
                    minLegs: 2,
                    maxLegs: 5,
                    allowedLegCounts: new[] { 2, 3, 4, 5 },
                    entryTypes: new[] { "PickEm" },
                    supportsFlex: false,
                    supportsPower: false,
                    supportsDemonsGoblinsOrAltLines: "Unknown",
                    sameGameAllowed: "Unknown",
                    notes: new[]
                    {
                        "Underdog entries should use Underdog legs only.",
                        "Direct Underdog ingestion and The Odds API us_dfs Underdog rows are separate data paths."
                    },
                    sourceConfidence: "NeedsVerification"),
                BuildEntryRule(
                    "DraftKings Pick6",
                    "DFS",
                    actionableForUser: true,
                    "DraftKingsPick6",
                    minLegs: null,
                    maxLegs: null,
                    allowedLegCounts: Array.Empty<int>(),
                    entryTypes: new[] { "Pick6" },
                    supportsFlex: null,
                    supportsPower: null,
                    supportsDemonsGoblinsOrAltLines: "Unknown",
                    sameGameAllowed: "Unknown",
                    notes: new[] { "DraftKings Pick6 entries should use DraftKings Pick6 legs only." },
                    sourceConfidence: "NeedsVerification"),
                BuildEntryRule(
                    "Betr Picks",
                    "DFS",
                    actionableForUser: true,
                    "BetrPicks",
                    minLegs: null,
                    maxLegs: null,
                    allowedLegCounts: Array.Empty<int>(),
                    entryTypes: new[] { "Picks" },
                    supportsFlex: null,
                    supportsPower: null,
                    supportsDemonsGoblinsOrAltLines: "Unknown",
                    sameGameAllowed: "Unknown",
                    notes: new[] { "Betr Picks entries should use Betr Picks legs only." },
                    sourceConfidence: "NeedsVerification"),
                BuildEntryRule(
                    "Sportsbook context providers",
                    "Sportsbook",
                    actionableForUser: false,
                    "BookmakerName",
                    minLegs: null,
                    maxLegs: null,
                    allowedLegCounts: Array.Empty<int>(),
                    entryTypes: Array.Empty<string>(),
                    supportsFlex: false,
                    supportsPower: false,
                    supportsDemonsGoblinsOrAltLines: "Unknown",
                    sameGameAllowed: "Unknown",
                    notes: new[]
                    {
                        "Sportsbook rows are market context for this DFS-only user.",
                        "If sportsbook parlay support is ever added, entries must remain scoped to one sportsbook/bookmaker."
                    },
                    sourceConfidence: "Assumed")
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                summary = new
                {
                    providerScopedEntriesRequired = true,
                    crossProviderMixingAllowed = false,
                    actionableProviderCategories = new[] { "DFS" },
                    contextProviderCategories = new[] { "Sportsbook" },
                    warning = "Entry rules marked NeedsVerification must be confirmed before production use."
                },
                rules
            });
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

    private static DfsEntryRuleRow BuildEntryRule(
        string provider,
        string providerCategory,
        bool actionableForUser,
        string compatibilityGroup,
        int? minLegs,
        int? maxLegs,
        IReadOnlyList<int> allowedLegCounts,
        IReadOnlyList<string> entryTypes,
        bool? supportsFlex,
        bool? supportsPower,
        string supportsDemonsGoblinsOrAltLines,
        string sameGameAllowed,
        IReadOnlyList<string> notes,
        string sourceConfidence)
    {
        return new DfsEntryRuleRow(
            provider,
            providerCategory,
            actionableForUser,
            compatibilityGroup,
            minLegs,
            maxLegs,
            allowedLegCounts,
            entryTypes,
            supportsFlex,
            supportsPower,
            supportsDemonsGoblinsOrAltLines,
            sameGameAllowed,
            notes,
            sourceConfidence);
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

    private sealed record DfsEntryRuleRow(
        string Provider,
        string ProviderCategory,
        bool ActionableForUser,
        string CompatibilityGroup,
        int? MinLegs,
        int? MaxLegs,
        IReadOnlyList<int> AllowedLegCounts,
        IReadOnlyList<string> EntryTypes,
        bool? SupportsFlex,
        bool? SupportsPower,
        string SupportsDemonsGoblinsOrAltLines,
        string SameGameAllowed,
        IReadOnlyList<string> Notes,
        string SourceConfidence);
}
