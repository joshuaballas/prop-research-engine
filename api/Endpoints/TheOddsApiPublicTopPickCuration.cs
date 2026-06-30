using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SportsBetting.Api.Data;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static partial class TheOddsApiDebugEndpoints
{
    internal static PublicTopPickCurationResult CuratePublicTopPickRows(
        IReadOnlyCollection<JsonObject> rows,
        int requestedTake,
        string? providerCategory,
        TopPicksModelOptions options)
    {
        var preDedupeCount = rows.Count;
        var providerCategoryFiltered = !string.IsNullOrWhiteSpace(providerCategory)
            && !string.Equals(providerCategory, "All", StringComparison.OrdinalIgnoreCase);

        var opportunityGroups = rows
            .GroupBy(row => BuildPublicTopPickOpportunityKey(row, providerCategoryFiltered), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedRows = group
                    .OrderByDescending(GetPublicTopPickEstimatedEv)
                    .ThenBy(GetPublicTopPickImpliedProbabilityForSort)
                    .ThenByDescending(row => GetDecimal(row, "bestDecimalPrice", "BestDecimalPrice") ?? decimal.MinValue)
                    .ThenByDescending(row => GetBool(row, "playablePriceEligible", "PlayablePriceEligible") == true)
                    .ThenByDescending(row => GetBool(row, "entryActionableForUser", "EntryActionableForUser") == true)
                    .ThenByDescending(row => GetDecimal(row, "topPickScore", "TopPickScore", "rankingScore", "RankingScore") ?? decimal.MinValue)
                    .ThenByDescending(row => GetDecimal(row, "selectedSideProbability", "SelectedSideProbability", "bestProbability", "BestProbability") ?? decimal.MinValue)
                    .ThenBy(row => GetString(row, "providerCategory", "ProviderCategory") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => GetString(row, "bestBookmaker", "BestBookmaker", "bookmaker", "Bookmaker", "provider", "Provider") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => GetString(row, "playerName", "PlayerName", "player", "Player") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var representative = orderedRows[0].DeepClone().AsObject();
                representative["propFamily"] = BuildPublicTopPickPropFamily(representative);
                representative["opportunityKey"] = group.Key;
                var providerOptions = BuildPublicTopPickProviderOptions(orderedRows);
                var effectiveProviderFanoutCount = Math.Max(
                    orderedRows.Length,
                    Math.Max(providerOptions.Count, GetInt(representative, "providerFanoutCount", "ProviderFanoutCount") ?? 0));
                representative["providerFanoutCount"] = effectiveProviderFanoutCount;
                representative["alternateProviderCount"] = Math.Max(0, effectiveProviderFanoutCount - 1);
                representative["bestPriceBookmaker"] = GetString(representative, "bestPriceBookmaker", "BestPriceBookmaker")
                    ?? GetString(representative, "bestBookmaker", "BestBookmaker")
                    ?? GetString(representative, "bookmaker", "Bookmaker")
                    ?? GetString(representative, "provider", "Provider");
                representative["providerOptions"] = providerOptions;

                return new PublicTopPickOpportunity(group.Key, representative, orderedRows.Length, effectiveProviderFanoutCount);
            })
            .OrderByDescending(group => GetPublicTopPickEstimatedEv(group.Row))
            .ThenBy(group => GetPublicTopPickImpliedProbabilityForSort(group.Row))
            .ThenByDescending(group => GetDecimal(group.Row, "bestDecimalPrice", "BestDecimalPrice") ?? decimal.MinValue)
            .ThenByDescending(group => GetBool(group.Row, "playablePriceEligible", "PlayablePriceEligible") == true)
            .ThenByDescending(group => GetBool(group.Row, "entryActionableForUser", "EntryActionableForUser") == true)
            .ThenByDescending(group => GetDecimal(group.Row, "topPickScore", "TopPickScore", "rankingScore", "RankingScore") ?? decimal.MinValue)
            .ThenByDescending(group => GetDecimal(group.Row, "selectedSideProbability", "SelectedSideProbability", "bestProbability", "BestProbability") ?? decimal.MinValue)
            .ThenBy(group => GetString(group.Row, "sport", "Sport") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => GetString(group.Row, "eventId", "EventId", "eventName", "EventName") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => GetString(group.Row, "playerName", "PlayerName", "player", "Player") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => GetString(group.Row, "propTypeId", "PropTypeId", "normalizedPropType", "NormalizedPropType", "propType", "PropType") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => GetDecimal(group.Row, "line", "Line") ?? decimal.Zero)
            .ThenBy(group => GetString(group.Row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var suppressionCounts = new PublicTopPickSuppressionCounts(
            ProviderFanout: opportunityGroups.Sum(group => Math.Max(0, group.InputRowCount - 1)),
            PlayerExposure: 0,
            PlayerPropFamilyExposure: 0,
            GameExposure: 0,
            TeamExposure: 0,
            PropTypeExposure: 0,
            SlateCap: 0);

        var selectedRows = new List<JsonObject>();
        var selectedBySport = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPlayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPlayerPropFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByGame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByTeam = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPropType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var suppressedRows = new List<PublicTopPickSuppressedRow>();
        var maxRequested = Math.Clamp(requestedTake, 1, 500);
        var maxPerSport = PositiveOrUnlimited(options.PublicMaxTopPicksPerSlateSport);
        var maxPerPlayer = PositiveOrUnlimited(options.PublicMaxTopPicksPerPlayer);
        var maxPerPlayerPropFamily = PositiveOrUnlimited(options.PublicMaxTopPicksPerPlayerPropFamily);
        var maxPerGame = PositiveOrUnlimited(options.PublicMaxTopPicksPerGameEvent);
        var maxPerTeam = PositiveOrUnlimited(options.PublicMaxTopPicksPerTeam);
        var maxPerPropType = PositiveOrUnlimited(options.PublicMaxTopPicksPerPropType);

        foreach (var opportunity in opportunityGroups)
        {
            var row = opportunity.Row;
            var sportKey = BuildPublicTopPickSportKey(row);
            var playerKey = BuildPublicTopPickPlayerKey(row);
            var playerPropFamilyKey = $"{playerKey}|{BuildPublicTopPickPropFamily(row)}";
            var gameKey = $"{sportKey}|{BuildPublicTopPickGameKey(row)}";
            var teamKey = $"{sportKey}|{BuildPublicTopPickTeamKey(row)}";
            var propTypeKey = $"{sportKey}|{BuildPublicTopPickPropTypeKey(row)}";

            string? reason = null;
            if (selectedRows.Count >= maxRequested)
            {
                reason = "SlateCap";
                suppressionCounts = suppressionCounts with { SlateCap = suppressionCounts.SlateCap + 1 };
            }
            else if (GetCount(selectedBySport, sportKey) >= maxPerSport)
            {
                reason = "SlateSportCap";
                suppressionCounts = suppressionCounts with { SlateCap = suppressionCounts.SlateCap + 1 };
            }
            else if (GetCount(selectedByPlayer, playerKey) >= maxPerPlayer)
            {
                reason = "PlayerExposureCap";
                suppressionCounts = suppressionCounts with { PlayerExposure = suppressionCounts.PlayerExposure + 1 };
            }
            else if (GetCount(selectedByPlayerPropFamily, playerPropFamilyKey) >= maxPerPlayerPropFamily)
            {
                reason = "PlayerPropFamilyExposureCap";
                suppressionCounts = suppressionCounts with { PlayerPropFamilyExposure = suppressionCounts.PlayerPropFamilyExposure + 1 };
            }
            else if (GetCount(selectedByGame, gameKey) >= maxPerGame)
            {
                reason = "GameExposureCap";
                suppressionCounts = suppressionCounts with { GameExposure = suppressionCounts.GameExposure + 1 };
            }
            else if (GetCount(selectedByTeam, teamKey) >= maxPerTeam)
            {
                reason = "TeamExposureCap";
                suppressionCounts = suppressionCounts with { TeamExposure = suppressionCounts.TeamExposure + 1 };
            }
            else if (GetCount(selectedByPropType, propTypeKey) >= maxPerPropType)
            {
                reason = "PropTypeExposureCap";
                suppressionCounts = suppressionCounts with { PropTypeExposure = suppressionCounts.PropTypeExposure + 1 };
            }

            if (reason is not null)
            {
                suppressedRows.Add(new PublicTopPickSuppressedRow(
                    opportunity.OpportunityKey,
                    reason,
                    GetString(row, "sport", "Sport"),
                    GetString(row, "playerName", "PlayerName", "player", "Player"),
                    GetString(row, "normalizedPropType", "NormalizedPropType", "propType", "PropType"),
                    GetString(row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side"),
                    GetDecimal(row, "line", "Line"),
                    GetString(row, "eventId", "EventId", "eventName", "EventName")));
                continue;
            }

            selectedRows.Add(row);
            Increment(selectedBySport, sportKey);
            Increment(selectedByPlayer, playerKey);
            Increment(selectedByPlayerPropFamily, playerPropFamilyKey);
            Increment(selectedByGame, gameKey);
            Increment(selectedByTeam, teamKey);
            Increment(selectedByPropType, propTypeKey);
        }

        var resultRows = selectedRows
            .Select((row, index) =>
            {
                row["curationRank"] = index + 1;
                return row;
            })
            .ToArray();
        var diagnostics = new PublicTopPickCurationDiagnostics(
            PreDedupeCandidateCount: preDedupeCount,
            PostOpportunityDedupeCount: opportunityGroups.Length,
            ReturnedTopPickCount: resultRows.Length,
            SuppressedProviderFanoutCount: suppressionCounts.ProviderFanout,
            SuppressedByPlayerExposureCount: suppressionCounts.PlayerExposure,
            SuppressedByPlayerPropFamilyExposureCount: suppressionCounts.PlayerPropFamilyExposure,
            SuppressedByGameExposureCount: suppressionCounts.GameExposure,
            SuppressedByTeamExposureCount: suppressionCounts.TeamExposure,
            SuppressedByPropTypeExposureCount: suppressionCounts.PropTypeExposure,
            SuppressedBySlateCapCount: suppressionCounts.SlateCap,
            RequestedTake: maxRequested,
            MaxPerSlateSport: options.PublicMaxTopPicksPerSlateSport,
            MaxPerPlayer: options.PublicMaxTopPicksPerPlayer,
            MaxPerPlayerPropFamily: options.PublicMaxTopPicksPerPlayerPropFamily,
            MaxPerGameEvent: options.PublicMaxTopPicksPerGameEvent,
            MaxPerTeam: options.PublicMaxTopPicksPerTeam,
            MaxPerPropType: options.PublicMaxTopPicksPerPropType,
            OpportunityKeyFields: providerCategoryFiltered
                ? "sport|competition|event|player|propType|line|selectedSide|providerCategory"
                : "sport|competition|event|player|propType|line|selectedSide",
            ProviderFanoutOpportunityCount: opportunityGroups.Count(group => group.EffectiveProviderFanoutCount > 1),
            MaxProviderFanoutCount: opportunityGroups.Length == 0 ? 0 : opportunityGroups.Max(group => group.EffectiveProviderFanoutCount),
            RowsWithProviderOptionsCount: resultRows.Count(row => GetProviderOptionCount(row) > 1),
            SampleProviderFanoutOpportunities: opportunityGroups
                .Where(group => group.EffectiveProviderFanoutCount > 1)
                .Take(10)
                .Select(group => new
                {
                    opportunityKey = group.OpportunityKey,
                    providerFanoutCount = group.EffectiveProviderFanoutCount,
                    player = GetString(group.Row, "playerName", "PlayerName", "player", "Player"),
                    propType = GetString(group.Row, "normalizedPropType", "NormalizedPropType", "propType", "PropType"),
                    propFamily = GetString(group.Row, "propFamily", "PropFamily"),
                    side = GetString(group.Row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side"),
                    line = GetDecimal(group.Row, "line", "Line"),
                    representativeBookmaker = GetString(group.Row, "bestPriceBookmaker", "BestPriceBookmaker", "bestBookmaker", "BestBookmaker", "bookmaker", "Bookmaker", "provider", "Provider")
                })
                .ToArray(),
            SuppressionReasonBreakdown: BuildSuppressionReasonBreakdown(suppressedRows),
            SuppressedRows: suppressedRows.Take(50).ToArray());

        return new PublicTopPickCurationResult(resultRows, diagnostics);
    }

    private static JsonArray BuildPublicTopPickProviderOptions(IReadOnlyList<JsonObject> rows)
    {
        var options = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (TryGetProviderOptions(row, out var existingOptions) && existingOptions.Count > 0)
            {
                foreach (var existingOption in existingOptions)
                {
                    if (existingOption is not JsonObject existingObject)
                    {
                        continue;
                    }

                    AddProviderOption(options, seen, existingObject);
                }

                continue;
            }

            var option = new JsonObject
            {
                ["provider"] = GetString(row, "provider", "Provider"),
                ["bookmaker"] = GetString(row, "bookmaker", "Bookmaker"),
                ["bestBookmaker"] = GetString(row, "bestBookmaker", "BestBookmaker"),
                ["bestPriceBookmaker"] = GetString(row, "bestPriceBookmaker", "BestPriceBookmaker")
                    ?? GetString(row, "bestBookmaker", "BestBookmaker")
                    ?? GetString(row, "bookmaker", "Bookmaker")
                    ?? GetString(row, "provider", "Provider"),
                ["providerCategory"] = GetString(row, "providerCategory", "ProviderCategory"),
                ["entryProvider"] = GetString(row, "entryProvider", "EntryProvider"),
                ["entryActionableForUser"] = GetBool(row, "entryActionableForUser", "EntryActionableForUser"),
                ["playablePriceEligible"] = GetBool(row, "playablePriceEligible", "PlayablePriceEligible"),
                ["bestAmericanPrice"] = GetDecimal(row, "bestAmericanPrice", "BestAmericanPrice"),
                ["bestDecimalPrice"] = GetDecimal(row, "bestDecimalPrice", "BestDecimalPrice"),
                ["bestImpliedProbability"] = GetDecimal(row, "bestImpliedProbability", "BestImpliedProbability"),
                ["selectedSideProbability"] = GetDecimal(row, "selectedSideProbability", "SelectedSideProbability", "bestProbability", "BestProbability"),
                ["bestProbabilityEdge"] = GetDecimal(row, "bestProbabilityEdge", "BestProbabilityEdge"),
                ["displayExpectedValue"] = GetDecimal(row, "displayExpectedValue", "DisplayExpectedValue", "bestEstimatedEv", "BestEstimatedEv")
            };
            AddProviderOption(options, seen, option);
        }

        return options;
    }

    private static void AddProviderOption(JsonArray options, HashSet<string> seen, JsonObject option)
    {
        var key = string.Join('|',
            RecommendationLedgerWriter.NormalizeKey(GetString(option, "provider", "Provider")),
            RecommendationLedgerWriter.NormalizeKey(GetString(option, "bookmaker", "Bookmaker")),
            RecommendationLedgerWriter.NormalizeKey(GetString(option, "bestBookmaker", "BestBookmaker")),
            RecommendationLedgerWriter.NormalizeKey(GetString(option, "bestPriceBookmaker", "BestPriceBookmaker")),
            RecommendationLedgerWriter.NormalizeKey(GetString(option, "providerCategory", "ProviderCategory")),
            (GetDecimal(option, "bestAmericanPrice", "BestAmericanPrice") ?? decimal.MinValue).ToString("0.####", CultureInfo.InvariantCulture),
            (GetDecimal(option, "bestDecimalPrice", "BestDecimalPrice") ?? decimal.MinValue).ToString("0.####", CultureInfo.InvariantCulture));

        if (seen.Add(key))
        {
            options.Add(option.DeepClone());
        }
    }

    private static bool TryGetProviderOptions(JsonObject row, out JsonArray providerOptions)
    {
        providerOptions = [];
        if (row.TryGetPropertyValue("providerOptions", out var value) && value is JsonArray camelOptions)
        {
            providerOptions = camelOptions;
            return true;
        }

        if (row.TryGetPropertyValue("ProviderOptions", out value) && value is JsonArray pascalOptions)
        {
            providerOptions = pascalOptions;
            return true;
        }

        return false;
    }

    private static object[] BuildSuppressionReasonBreakdown(IReadOnlyCollection<PublicTopPickSuppressedRow> rows) =>
        rows
            .GroupBy(row => row.Reason, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { reason = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.reason)
            .ToArray();

    private static string BuildPublicTopPickOpportunityKey(JsonObject row, bool includeProviderCategory)
    {
        var values = new List<string>
        {
            BuildPublicTopPickSportKey(row),
            RecommendationLedgerWriter.NormalizeKey(GetString(row, "competition", "Competition") ?? GetString(row, "sport", "Sport")),
            BuildPublicTopPickGameKey(row),
            BuildPublicTopPickPlayerKey(row),
            BuildPublicTopPickPropTypeKey(row),
            (GetDecimal(row, "line", "Line") ?? decimal.Zero).ToString("0.####", CultureInfo.InvariantCulture),
            RecommendationLedgerWriter.NormalizeKey(GetString(row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side"))
        };

        if (includeProviderCategory)
        {
            values.Add(RecommendationLedgerWriter.NormalizeKey(GetString(row, "providerCategory", "ProviderCategory")));
        }

        return string.Join('|', values);
    }

    private static string BuildPublicTopPickSportKey(JsonObject row) =>
        RecommendationLedgerWriter.NormalizeKey(GetString(row, "sport", "Sport") ?? "Unknown");

    private static string BuildPublicTopPickGameKey(JsonObject row) =>
        RecommendationLedgerWriter.NormalizeKey(
            GetPublicTopPickScalarString(row, "eventId", "EventId")
            ?? GetString(row, "eventName", "EventName", "event", "Event")
            ?? GetPublicTopPickScalarString(row, "eventStartTimeUtc", "EventStartTimeUtc", "eventStartTime", "EventStartTime")
            ?? "Unknown");

    private static string BuildPublicTopPickPlayerKey(JsonObject row) =>
        RecommendationLedgerWriter.NormalizeKey(
            GetPublicTopPickScalarString(row, "canonicalPlayerId", "CanonicalPlayerId")
            ?? GetPublicTopPickScalarString(row, "playerId", "PlayerId")
            ?? GetString(row, "playerName", "PlayerName", "player", "Player")
            ?? "Unknown");

    private static string BuildPublicTopPickPropTypeKey(JsonObject row) =>
        RecommendationLedgerWriter.NormalizeKey(
            GetPublicTopPickScalarString(row, "propTypeId", "PropTypeId")
            ?? GetString(row, "normalizedPropType", "NormalizedPropType")
            ?? GetString(row, "propType", "PropType")
            ?? "Unknown");

    private static string? GetPublicTopPickScalarString(JsonObject row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value.GetValueKind() == JsonValueKind.String)
            {
                try
                {
                    return value.GetValue<string>();
                }
                catch (InvalidOperationException)
                {
                    return value.ToJsonString().Trim('"');
                }
            }

            if (value.AsValue().TryGetValue<Guid>(out var guid))
            {
                return guid.ToString();
            }

            if (value.AsValue().TryGetValue<string>(out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static string BuildPublicTopPickPropFamily(JsonObject row)
    {
        var normalized = BuildPublicTopPickPropTypeKey(row);
        return normalized switch
        {
            "totalbases" or "hits" or "singles" or "doubles" or "triples" or "homeruns" => "BattingContactPower",
            "runs" or "runsscored" or "rbi" or "runsbattedin" => "BattingRunProduction",
            "walks" or "batterwalks" or "batterstrikeouts" => "BattingDiscipline",
            "pitcherstrikeouts" or "strikeouts" => "PitchingStrikeouts",
            "pitcherouts" or "outsrecorded" or "earnedruns" or "hitsallowed" or "walksallowed" or "pitcherhitsallowed" or "pitcherwalks" => "PitchingRunPrevention",
            "goals" or "assists" => "SoccerScoringCreation",
            "shots" or "shotsontarget" => "SoccerShooting",
            _ => normalized.Length == 0 ? "Unknown" : normalized
        };
    }

    private static int? GetInt(JsonObject row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value.AsValue().TryGetValue<int>(out var intValue))
            {
                return intValue;
            }
        }

        return null;
    }

    private static int GetProviderOptionCount(JsonObject row)
    {
        if (TryGetProviderOptions(row, out var providerOptions) && providerOptions.Count > 0)
        {
            return providerOptions.Count;
        }

        return GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0;
    }

    private static string BuildPublicTopPickTeamKey(JsonObject row) =>
        RecommendationLedgerWriter.NormalizeKey(
            GetString(row, "teamName", "TeamName", "team", "Team", "playerTeam", "PlayerTeam", "homeTeam", "HomeTeam", "awayTeam", "AwayTeam")
            ?? "Unknown");

    private static decimal GetPublicTopPickEstimatedEv(JsonObject row) =>
        GetDecimal(row, "displayExpectedValue", "DisplayExpectedValue", "bestEstimatedEv", "BestEstimatedEv") ?? decimal.MinValue;

    private static decimal GetPublicTopPickImpliedProbabilityForSort(JsonObject row) =>
        GetDecimal(row, "bestImpliedProbability", "BestImpliedProbability")
        ?? ConvertAmericanOddsToImpliedProbability(GetDecimal(row, "bestAmericanPrice", "BestAmericanPrice"))
        ?? decimal.MaxValue;

    private static decimal? ConvertAmericanOddsToImpliedProbability(decimal? americanOdds)
    {
        if (!americanOdds.HasValue || americanOdds.Value == 0m)
        {
            return null;
        }

        return americanOdds.Value < 0m
            ? Math.Abs(americanOdds.Value) / (Math.Abs(americanOdds.Value) + 100m)
            : 100m / (americanOdds.Value + 100m);
    }

    private static int PositiveOrUnlimited(int value) => value > 0 ? value : int.MaxValue;

    private static int GetCount(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var count) ? count : 0;

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = GetCount(counts, key) + 1;

    internal sealed record PublicTopPickCurationResult(
        JsonObject[] Rows,
        PublicTopPickCurationDiagnostics Diagnostics);

    internal sealed record PublicTopPickCurationDiagnostics(
        int PreDedupeCandidateCount,
        int PostOpportunityDedupeCount,
        int ReturnedTopPickCount,
        int SuppressedProviderFanoutCount,
        int SuppressedByPlayerExposureCount,
        int SuppressedByPlayerPropFamilyExposureCount,
        int SuppressedByGameExposureCount,
        int SuppressedByTeamExposureCount,
        int SuppressedByPropTypeExposureCount,
        int SuppressedBySlateCapCount,
        int RequestedTake,
        int MaxPerSlateSport,
        int MaxPerPlayer,
        int MaxPerPlayerPropFamily,
        int MaxPerGameEvent,
        int MaxPerTeam,
        int MaxPerPropType,
        string OpportunityKeyFields,
        int ProviderFanoutOpportunityCount,
        int MaxProviderFanoutCount,
        int RowsWithProviderOptionsCount,
        object[] SampleProviderFanoutOpportunities,
        object[] SuppressionReasonBreakdown,
        PublicTopPickSuppressedRow[] SuppressedRows);

    private sealed record PublicTopPickOpportunity(
        string OpportunityKey,
        JsonObject Row,
        int InputRowCount,
        int EffectiveProviderFanoutCount);

    internal sealed record PublicTopPickSuppressedRow(
        string OpportunityKey,
        string Reason,
        string? Sport,
        string? Player,
        string? PropType,
        string? Side,
        decimal? Line,
        string? EventId);

    private sealed record PublicTopPickSuppressionCounts(
        int ProviderFanout,
        int PlayerExposure,
        int PlayerPropFamilyExposure,
        int GameExposure,
        int TeamExposure,
        int PropTypeExposure,
        int SlateCap);
}
