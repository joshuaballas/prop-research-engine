using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public interface ISoccerWorldCupPredictionService
{
    Task<SoccerWorldCupPredictionBatch> BuildPredictionsAsync(
        IReadOnlyCollection<SoccerWorldCupPredictionInput> inputs,
        bool requireEnabled,
        CancellationToken cancellationToken);

    Task<SoccerWorldCupGradingCoverage> BuildGradingCoverageAsync(
        IReadOnlyCollection<SoccerWorldCupPredictionInput> inputs,
        bool requireEnabled,
        CancellationToken cancellationToken);
}

public sealed class SoccerWorldCupPredictionService(
    ApplicationDbContext dbContext,
    IOptions<SoccerPredictionsOptions> predictionOptions,
    IOptions<SoccerGradingOptions> gradingOptions) : ISoccerWorldCupPredictionService
{
    public const string ApiSportsFootballProviderKey = "api-sports-football";
    public const string ApiSportsFootballResultSource = "api-sports-football";

    private static readonly IReadOnlyDictionary<string, string> SupportedStatsByDisplayName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Goals"] = "GOALS",
            ["Assists"] = "ASSISTS",
            ["Shots"] = "SHOTS",
            ["Shots On Target"] = "SHOTS_ON_TARGET",
            ["ShotsOnTarget"] = "SHOTS_ON_TARGET"
        };

    public async Task<SoccerWorldCupPredictionBatch> BuildPredictionsAsync(
        IReadOnlyCollection<SoccerWorldCupPredictionInput> inputs,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var options = predictionOptions.Value;
        var inputRows = inputs.ToArray();
        if (requireEnabled && !options.Enabled)
        {
            return SoccerWorldCupPredictionBatch.Disabled(inputRows.Length, "SoccerPredictions:Enabled is false.");
        }

        var context = await LoadPredictionContextAsync(inputRows, cancellationToken);
        var predictions = new Dictionary<SoccerWorldCupPredictionKey, SoccerWorldCupPrediction>(inputRows.Length);
        foreach (var input in inputRows)
        {
            var key = SoccerWorldCupPredictionKey.From(input);
            predictions[key] = BuildPrediction(input, context, options);
        }

        return new SoccerWorldCupPredictionBatch(
            Enabled: options.Enabled || !requireEnabled,
            InputsEvaluated: inputRows.Length,
            SupportedPropInputs: predictions.Values.Count(row => row.IsSupportedPropType),
            RowsWithApprovedLink: predictions.Values.Count(row => row.HasApprovedProviderLink),
            RowsWithHistory: predictions.Values.Count(row => row.HasHistoricalRows),
            RowsWithModelProbability: predictions.Values.Count(row => row.ModelProbabilityAvailable),
            RowsGradeable: predictions.Values.Count(row => row.ResultGradingSupported),
            TopPickEligibleRows: predictions.Values.Count(row => row.TopPickEligible),
            BlockedReasons: predictions.Values
                .Where(row => !row.ModelProbabilityAvailable)
                .Select(row => row.UnavailableReason ?? "Unknown")
                .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            PredictionsByKey: predictions);
    }

    public async Task<SoccerWorldCupGradingCoverage> BuildGradingCoverageAsync(
        IReadOnlyCollection<SoccerWorldCupPredictionInput> inputs,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var options = gradingOptions.Value;
        var inputRows = inputs.ToArray();
        if (requireEnabled && !options.Enabled)
        {
            return SoccerWorldCupGradingCoverage.Disabled(inputRows.Length, "SoccerGrading:Enabled is false.");
        }

        var context = await LoadPredictionContextAsync(inputRows, cancellationToken);
        var rows = inputRows.Select(input => BuildPrediction(input, context, predictionOptions.Value)).ToArray();
        return new SoccerWorldCupGradingCoverage(
            Enabled: options.Enabled || !requireEnabled,
            InputsEvaluated: inputRows.Length,
            SupportedPropInputs: rows.Count(row => row.IsSupportedPropType),
            RowsWithApprovedLink: rows.Count(row => row.HasApprovedProviderLink),
            RowsWithSameDateResult: rows.Count(row => row.SameDateResult.HasValue),
            RowsGradeable: rows.Count(row => row.ResultGradingSupported),
            UnsupportedPropRows: rows.Count(row => !row.IsSupportedPropType),
            MissingLinkRows: rows.Count(row => row.IsSupportedPropType && !row.HasApprovedProviderLink),
            MissingResultRows: rows.Count(row => row.IsSupportedPropType && row.HasApprovedProviderLink && !row.SameDateResult.HasValue),
            Samples: rows
                .Where(row => row.ResultGradingSupported || !row.IsSupportedPropType || row.HasApprovedProviderLink)
                .Take(25)
                .Select(row => new SoccerWorldCupGradingCoverageSample(
                    row.PlayerName,
                    row.PropType,
                    row.Line,
                    row.SelectedSide,
                    row.ProviderPlayerId,
                    row.SameDateResult,
                    row.ResultGradingSupported,
                    row.UnavailableReason))
                .ToArray());
    }

    private SoccerWorldCupPrediction BuildPrediction(
        SoccerWorldCupPredictionInput input,
        SoccerWorldCupPredictionContext context,
        SoccerPredictionsOptions options)
    {
        var key = SoccerWorldCupPredictionKey.From(input);
        var statKey = ResolveSupportedStatKey(input.PropType, input.MarketKey);
        if (statKey is null)
        {
            return SoccerWorldCupPrediction.Unavailable(input, key, null, "UnsupportedSoccerPropType");
        }

        if (!context.ProviderPlayerIdsByPlatformPlayerId.TryGetValue(input.PlayerId, out var providerPlayerIds) ||
            providerPlayerIds.Length == 0)
        {
            return SoccerWorldCupPrediction.Unavailable(input, key, statKey, "NoApprovedApiSportsProviderEntityLink");
        }

        var providerPlayerId = providerPlayerIds[0];
        var historyRows = providerPlayerIds
            .SelectMany(providerId => context.HistoryRowsByProviderPlayerId.GetValueOrDefault(providerId) ?? [])
            .Where(row => string.Equals(row.StatKey, statKey, StringComparison.OrdinalIgnoreCase) && row.EventDate < input.EventDate)
            .OrderByDescending(row => row.EventDate)
            .ToArray();
        if (historyRows.Length == 0)
        {
            return SoccerWorldCupPrediction.Unavailable(input, key, statKey, "ApprovedLinkExistsButNoSupportedHistoricalRows", providerPlayerId);
        }

        var sampleSize = historyRows.Length;
        var overWins = historyRows.Count(row => row.Value > input.Line);
        var underWins = historyRows.Count(row => row.Value < input.Line);
        var pushes = historyRows.Count(row => row.Value == input.Line);
        var overProbability = decimal.Round((decimal)overWins / sampleSize, 4);
        var underProbability = decimal.Round((decimal)underWins / sampleSize, 4);
        var pushProbability = decimal.Round((decimal)pushes / sampleSize, 4);
        var average = decimal.Round(historyRows.Average(row => row.Value), 3);
        var last5Average = decimal.Round(historyRows.Take(5).Average(row => row.Value), 3);
        var last10Average = decimal.Round(historyRows.Take(10).Average(row => row.Value), 3);
        var hasEnoughSample = sampleSize >= Math.Max(1, options.MinimumSampleSize);
        var selectedSide = ChooseSelectedSide(input, overProbability, underProbability);
        var selectedProbability = selectedSide == "Over" ? overProbability : selectedSide == "Under" ? underProbability : (decimal?)null;
        var probabilityEdge = selectedProbability.HasValue
            ? selectedProbability.Value - 0.5m
            : (decimal?)null;
        var projectionEdge = selectedSide == "Over"
            ? average - input.Line
            : selectedSide == "Under"
                ? input.Line - average
                : (decimal?)null;
        var overEstimatedEv = input.PriceOverDecimal.HasValue
            ? decimal.Round((overProbability * input.PriceOverDecimal.Value) - 1m, 4)
            : (decimal?)null;
        var underEstimatedEv = input.PriceUnderDecimal.HasValue
            ? decimal.Round((underProbability * input.PriceUnderDecimal.Value) - 1m, 4)
            : (decimal?)null;
        var sameDateResult = context.HistoryRowsByProviderPlayerId.GetValueOrDefault(providerPlayerId)?
            .FirstOrDefault(row => string.Equals(row.StatKey, statKey, StringComparison.OrdinalIgnoreCase)
                && row.EventDate == input.EventDate)
            ?.Value;

        return new SoccerWorldCupPrediction(
            key,
            input.PlayerName,
            input.PropType,
            input.Line,
            statKey,
            providerPlayerId,
            IsSupportedPropType: true,
            HasApprovedProviderLink: true,
            HasHistoricalRows: true,
            ModelProbabilityAvailable: hasEnoughSample,
            ResultGradingSupported: gradingOptions.Value.Enabled && sameDateResult.HasValue,
            TopPickEligible: hasEnoughSample
                && sampleSize >= Math.Max(options.MinimumSampleSize, options.TopPickMinimumSampleSize)
                && selectedProbability >= options.MinimumTopPickHitProbability,
            ModelProbabilityOver: hasEnoughSample ? overProbability : null,
            ModelProbabilityUnder: hasEnoughSample ? underProbability : null,
            ModelProbabilityPush: hasEnoughSample ? pushProbability : null,
            ModelProjection: average,
            ProjectionEdge: hasEnoughSample ? projectionEdge : null,
            SelectedSide: selectedSide,
            SelectedSideProbability: hasEnoughSample ? selectedProbability : null,
            SelectedProbabilityEdge: hasEnoughSample ? probabilityEdge : null,
            OverEstimatedEv: hasEnoughSample ? overEstimatedEv : null,
            UnderEstimatedEv: hasEnoughSample ? underEstimatedEv : null,
            HistoricalSampleSize: sampleSize,
            Last5Average: last5Average,
            Last10Average: last10Average,
            SameDateResult: sameDateResult,
            SampleWarning: sampleSize < options.LowSampleWarningThreshold ? $"Low sample: {sampleSize} historical matches." : null,
            UnavailableReason: hasEnoughSample ? null : "InsufficientHistoricalSample",
            ProbabilityMethod: "SoccerHistoricalEmpiricalHitRate");
    }

    private static string ChooseSelectedSide(SoccerWorldCupPredictionInput input, decimal overProbability, decimal underProbability)
    {
        if (input.OverAvailable && input.UnderAvailable)
        {
            return overProbability >= underProbability ? "Over" : "Under";
        }

        if (input.OverAvailable)
        {
            return "Over";
        }

        return input.UnderAvailable ? "Under" : "None";
    }

    private async Task<SoccerWorldCupPredictionContext> LoadPredictionContextAsync(
        IReadOnlyCollection<SoccerWorldCupPredictionInput> inputs,
        CancellationToken cancellationToken)
    {
        var playerIds = inputs.Select(input => input.PlayerId).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (playerIds.Length == 0)
        {
            return SoccerWorldCupPredictionContext.Empty;
        }

        var links = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == ApiSportsFootballProviderKey
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && playerIds.Contains(link.CanonicalEntityId.Value))
            .Select(link => new { PlayerId = link.CanonicalEntityId!.Value, ProviderPlayerId = link.ProviderEntityId })
            .ToArrayAsync(cancellationToken);

        var providerPlayerIds = links
            .Select(link => link.ProviderPlayerId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (providerPlayerIds.Length == 0)
        {
            return new SoccerWorldCupPredictionContext(
                links.GroupBy(link => link.PlayerId).ToDictionary(group => group.Key, _ => Array.Empty<string>()),
                new Dictionary<string, SoccerWorldCupHistoryRow[]>(StringComparer.OrdinalIgnoreCase));
        }

        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == ApiSportsFootballProviderKey
                && providerPlayerIds.Contains(reference.ProviderPlayerId))
            .Select(reference => new { reference.PlayerId, reference.ProviderPlayerId })
            .ToArrayAsync(cancellationToken);
        var providerIdByHistoricalPlayerId = refs
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(group => group.Key, group => group.First().ProviderPlayerId);
        var historicalPlayerIds = providerIdByHistoricalPlayerId.Keys.ToArray();
        var supportedStatKeys = SupportedStatsByDisplayName.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var historyRows = historicalPlayerIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.PropType)
                .Where(result => historicalPlayerIds.Contains(result.PlayerId)
                    && result.ExternalSource == ApiSportsFootballResultSource
                    && result.PropType != null
                    && supportedStatKeys.Contains(result.PropType.StatKey))
                .Select(result => new SoccerWorldCupHistoryRow(
                    providerIdByHistoricalPlayerId[result.PlayerId],
                    result.EventDate,
                    result.ActualResultValue,
                    result.PropType!.StatKey))
                .ToArrayAsync(cancellationToken);

        return new SoccerWorldCupPredictionContext(
            links.GroupBy(link => link.PlayerId).ToDictionary(
                group => group.Key,
                group => group
                    .Select(link => link.ProviderPlayerId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order()
                    .ToArray()),
            historyRows
                .GroupBy(row => row.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    public static string? ResolveSupportedStatKey(string? propType, string? marketKey)
    {
        if (!string.IsNullOrWhiteSpace(propType) && SupportedStatsByDisplayName.TryGetValue(propType, out var statKey))
        {
            return statKey;
        }

        return marketKey?.Trim().ToLowerInvariant() switch
        {
            "player_goals" => "GOALS",
            "player_assists" => "ASSISTS",
            "player_shots" => "SHOTS",
            "player_shots_on_target" => "SHOTS_ON_TARGET",
            _ => null
        };
    }
}

public sealed record SoccerWorldCupPredictionInput(
    Guid PlayerId,
    Guid PropTypeId,
    string PlayerName,
    string PropType,
    string? MarketKey,
    decimal Line,
    bool OverAvailable,
    bool UnderAvailable,
    DateOnly EventDate,
    int? PriceOver,
    decimal? PriceOverDecimal,
    int? PriceUnder,
    decimal? PriceUnderDecimal);

public readonly record struct SoccerWorldCupPredictionKey(Guid PlayerId, Guid PropTypeId, decimal Line)
{
    public static SoccerWorldCupPredictionKey From(SoccerWorldCupPredictionInput input)
    {
        return new SoccerWorldCupPredictionKey(input.PlayerId, input.PropTypeId, input.Line);
    }
}

public sealed record SoccerWorldCupPredictionBatch(
    bool Enabled,
    int InputsEvaluated,
    int SupportedPropInputs,
    int RowsWithApprovedLink,
    int RowsWithHistory,
    int RowsWithModelProbability,
    int RowsGradeable,
    int TopPickEligibleRows,
    IReadOnlyDictionary<string, int> BlockedReasons,
    IReadOnlyDictionary<SoccerWorldCupPredictionKey, SoccerWorldCupPrediction> PredictionsByKey)
{
    public static SoccerWorldCupPredictionBatch Disabled(int inputsEvaluated, string reason)
    {
        return new SoccerWorldCupPredictionBatch(
            Enabled: false,
            InputsEvaluated: inputsEvaluated,
            SupportedPropInputs: 0,
            RowsWithApprovedLink: 0,
            RowsWithHistory: 0,
            RowsWithModelProbability: 0,
            RowsGradeable: 0,
            TopPickEligibleRows: 0,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [reason] = inputsEvaluated },
            new Dictionary<SoccerWorldCupPredictionKey, SoccerWorldCupPrediction>());
    }
}

public sealed record SoccerWorldCupPrediction(
    SoccerWorldCupPredictionKey Key,
    string PlayerName,
    string PropType,
    decimal Line,
    string? StatKey,
    string? ProviderPlayerId,
    bool IsSupportedPropType,
    bool HasApprovedProviderLink,
    bool HasHistoricalRows,
    bool ModelProbabilityAvailable,
    bool ResultGradingSupported,
    bool TopPickEligible,
    decimal? ModelProbabilityOver,
    decimal? ModelProbabilityUnder,
    decimal? ModelProbabilityPush,
    decimal? ModelProjection,
    decimal? ProjectionEdge,
    string SelectedSide,
    decimal? SelectedSideProbability,
    decimal? SelectedProbabilityEdge,
    decimal? OverEstimatedEv,
    decimal? UnderEstimatedEv,
    int HistoricalSampleSize,
    decimal? Last5Average,
    decimal? Last10Average,
    decimal? SameDateResult,
    string? SampleWarning,
    string? UnavailableReason,
    string ProbabilityMethod)
{
    public static SoccerWorldCupPrediction Unavailable(
        SoccerWorldCupPredictionInput input,
        SoccerWorldCupPredictionKey key,
        string? statKey,
        string reason,
        string? providerPlayerId = null)
    {
        return new SoccerWorldCupPrediction(
            key,
            input.PlayerName,
            input.PropType,
            input.Line,
            statKey,
            providerPlayerId,
            IsSupportedPropType: statKey is not null,
            HasApprovedProviderLink: providerPlayerId is not null,
            HasHistoricalRows: false,
            ModelProbabilityAvailable: false,
            ResultGradingSupported: false,
            TopPickEligible: false,
            ModelProbabilityOver: null,
            ModelProbabilityUnder: null,
            ModelProbabilityPush: null,
            ModelProjection: null,
            ProjectionEdge: null,
            SelectedSide: "None",
            SelectedSideProbability: null,
            SelectedProbabilityEdge: null,
            OverEstimatedEv: null,
            UnderEstimatedEv: null,
            HistoricalSampleSize: 0,
            Last5Average: null,
            Last10Average: null,
            SameDateResult: null,
            SampleWarning: null,
            UnavailableReason: reason,
            ProbabilityMethod: "Unavailable");
    }
}

public sealed record SoccerWorldCupGradingCoverage(
    bool Enabled,
    int InputsEvaluated,
    int SupportedPropInputs,
    int RowsWithApprovedLink,
    int RowsWithSameDateResult,
    int RowsGradeable,
    int UnsupportedPropRows,
    int MissingLinkRows,
    int MissingResultRows,
    IReadOnlyList<SoccerWorldCupGradingCoverageSample> Samples)
{
    public static SoccerWorldCupGradingCoverage Disabled(int inputsEvaluated, string reason)
    {
        return new SoccerWorldCupGradingCoverage(
            Enabled: false,
            InputsEvaluated: inputsEvaluated,
            SupportedPropInputs: 0,
            RowsWithApprovedLink: 0,
            RowsWithSameDateResult: 0,
            RowsGradeable: 0,
            UnsupportedPropRows: 0,
            MissingLinkRows: 0,
            MissingResultRows: 0,
            Samples: [new SoccerWorldCupGradingCoverageSample("", "", 0, "None", null, null, false, reason)]);
    }
}

public sealed record SoccerWorldCupGradingCoverageSample(
    string PlayerName,
    string PropType,
    decimal Line,
    string SelectedSide,
    string? ProviderPlayerId,
    decimal? SameDateResult,
    bool ResultGradingSupported,
    string? BlockReason);

internal sealed record SoccerWorldCupPredictionContext(
    IReadOnlyDictionary<Guid, string[]> ProviderPlayerIdsByPlatformPlayerId,
    IReadOnlyDictionary<string, SoccerWorldCupHistoryRow[]> HistoryRowsByProviderPlayerId)
{
    public static SoccerWorldCupPredictionContext Empty { get; } = new(
        new Dictionary<Guid, string[]>(),
        new Dictionary<string, SoccerWorldCupHistoryRow[]>(StringComparer.OrdinalIgnoreCase));
}

internal sealed record SoccerWorldCupHistoryRow(
    string ProviderPlayerId,
    DateOnly EventDate,
    decimal Value,
    string StatKey);
