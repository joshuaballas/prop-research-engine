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
                    "Current PrizePicks rows should come through The Odds API us_dfs.",
                    "Standard Player Pick payout rates can vary by lineup submission, promotions, pick combinations, special projections, and other pre-established factors disclosed by PrizePicks before submission."
                },
                sourceConfidence: "NeedsVerification",
                payoutMultipliers: BuildPrizePicksStandardPlayerPickMultipliers()),
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
                sourceConfidence: "NeedsVerification",
                payoutMultipliers: Array.Empty<DfsEntryPayoutMultiplierRow>()),
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
                sourceConfidence: "NeedsVerification",
                payoutMultipliers: Array.Empty<DfsEntryPayoutMultiplierRow>()),
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
                sourceConfidence: "NeedsVerification",
                payoutMultipliers: Array.Empty<DfsEntryPayoutMultiplierRow>()),
        };
        var entryBuildableProviders = rules
            .Where(row => row.ActionableForUser && row.ProviderCategory == "DFS")
            .Select(row => new
            {
                row.Provider,
                row.ProviderCategory,
                row.CompatibilityGroup,
                row.MinLegs,
                row.MaxLegs,
                row.AllowedLegCounts,
                row.EntryTypes,
                row.SourceConfidence,
                hasConfiguredPayoutMultipliers = row.PayoutMultipliers.Count > 0
            })
            .ToArray();
        var contextOnlyProviders = new[]
        {
            new
            {
                ProviderCategory = "Sportsbook",
                CompatibilityGroup = "BookmakerName",
                AppliesTo = "All sportsbook/bookmaker rows",
                ActionableForUser = false,
                Reason = "Sportsbook lines are comparison context only and are not Entry Builder legs."
            }
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
                warning = "Entry rules are maintained only for explicitly supported DFS pick'em providers. Payout multiplier tables are optional configured data; final terms must be confirmed in the provider app."
            },
            entryBuildableProviders,
            contextOnlyProviders,
            unsupportedProviders = Array.Empty<object>(),
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
        string sourceConfidence,
        IReadOnlyList<DfsEntryPayoutMultiplierRow> payoutMultipliers)
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
            sourceConfidence,
            payoutMultipliers);
    }

    private static IReadOnlyList<DfsEntryPayoutMultiplierRow> BuildPrizePicksStandardPlayerPickMultipliers()
    {
        var verifiedAtUtc = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero);
        const string confidence = "Known";
        var notes = new[]
        {
            "Standard Player Pick payout rate from PrizePicks Help Center Payouts page, last updated Apr 23, 2026.",
            "This row represents the all-correct top payout for the selected entry type and leg count.",
            "Actual payout can vary by lineup submission, promotions, pick combinations, special projections, ties, DNP/Reboot handling, and other factors disclosed by PrizePicks before submission.",
            "Flex Play partial-hit payouts are not modeled in Entry Builder estimated payout."
        };

        return new[]
        {
            new DfsEntryPayoutMultiplierRow("Power", 2, 3m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Power", 3, 6m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Power", 4, 10m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Power", 5, 20m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Power", 6, 37.5m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Flex", 3, 3m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Flex", 4, 6m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Flex", 5, 10m, "StandardOnly", confidence, verifiedAtUtc, notes),
            new DfsEntryPayoutMultiplierRow("Flex", 6, 25m, "StandardOnly", confidence, verifiedAtUtc, notes)
        };
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
        string SourceConfidence,
        IReadOnlyList<DfsEntryPayoutMultiplierRow> PayoutMultipliers);

    private sealed record DfsEntryPayoutMultiplierRow(
        string EntryType,
        int LegCount,
        decimal Multiplier,
        string AppliesToProjectionType,
        string SourceConfidence,
        DateTimeOffset? LastVerifiedAtUtc,
        IReadOnlyList<string> Notes);
}
