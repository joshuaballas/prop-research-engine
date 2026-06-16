namespace SportsBetting.Api.Endpoints;

public static class DfsEntryRulesCatalog
{
    public static object BuildResponse()
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
                    "Current PrizePicks rows should come through The Odds API us_dfs."
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

        return new
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
        };
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
