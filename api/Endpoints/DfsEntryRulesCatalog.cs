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
                payoutModelType: "FixedMultiplierTableWithPartialHitRows",
                hasPartialHitPayouts: true,
                partialHitPayoutsModeled: false,
                payoutRulesNotes: "Standard Player Pick all-correct top-payout rows are configured for Power and Flex. Flex partial-hit rows, protected plays, promos, special projections, and lineup-specific variations are not modeled.",
                sourceUrl: "https://help.prizepicks.com/hc/en-us/articles/34718473789979-Payouts",
                lastVerifiedAtUtc: new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero),
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
                payoutModelType: "UnknownOrUnavailable",
                hasPartialHitPayouts: false,
                partialHitPayoutsModeled: false,
                payoutRulesNotes: "No official fixed Underdog Pick'em payout multiplier table was verified in this pass. Keep exact payout unavailable until official provider rules expose provider, entry type, leg count, projection modifier, and partial-hit payout terms.",
                sourceUrl: "https://app.underdogfantasy.com/rules",
                lastVerifiedAtUtc: null,
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
                payoutModelType: "UnknownOrUnavailable",
                hasPartialHitPayouts: false,
                partialHitPayoutsModeled: false,
                payoutRulesNotes: "The accessible official Pick6 page confirms a Pick6 product flow and minimum two picks, but no official fixed payout multiplier table was verified. Keep exact payout unavailable until official payout rules are modeled.",
                sourceUrl: "https://pick6.draftkings.com/",
                lastVerifiedAtUtc: null,
                payoutMultipliers: Array.Empty<DfsEntryPayoutMultiplierRow>()),
            BuildEntryRule(
                "Betr Picks",
                "DFS",
                actionableForUser: true,
                "BetrPicks",
                minLegs: 2,
                maxLegs: 10,
                allowedLegCounts: new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                entryTypes: new[] { "Perfect Play", "Flex Play" },
                supportsFlex: null,
                supportsPower: null,
                supportsDemonsGoblinsOrAltLines: "Unknown",
                sameGameAllowed: "Unknown",
                notes: new[]
                {
                    "Betr Picks entries should use Betr Picks legs only.",
                    "Betr Picks House Rules describe 2-10 player lineups and Perfect Play/Flex Play formats.",
                    "The prize is posted before entry, and Edge Combos or other non-standard conditions can change the multiplier."
                },
                sourceConfidence: "NeedsVerification",
                payoutModelType: "VariableMultiplier",
                hasPartialHitPayouts: true,
                partialHitPayoutsModeled: false,
                payoutRulesNotes: "Betr Picks House Rules say the entry fee and total prize are posted before entry and note non-standard payouts such as Edge Combos. No official fixed payout multiplier table was verified, so exact payout remains unavailable.",
                sourceUrl: "https://help.betr.app/en/articles/9461972-betr-picks-house-rules",
                lastVerifiedAtUtc: null,
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
                row.PayoutModelType,
                row.HasPartialHitPayouts,
                row.PartialHitPayoutsModeled,
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
        string payoutModelType,
        bool hasPartialHitPayouts,
        bool partialHitPayoutsModeled,
        string payoutRulesNotes,
        string sourceUrl,
        DateTimeOffset? lastVerifiedAtUtc,
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
            payoutModelType,
            hasPartialHitPayouts,
            partialHitPayoutsModeled,
            payoutRulesNotes,
            sourceUrl,
            lastVerifiedAtUtc,
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
        string PayoutModelType,
        bool HasPartialHitPayouts,
        bool PartialHitPayoutsModeled,
        string PayoutRulesNotes,
        string SourceUrl,
        DateTimeOffset? LastVerifiedAtUtc,
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
