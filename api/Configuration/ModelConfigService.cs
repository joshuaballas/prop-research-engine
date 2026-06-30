using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Configuration;

public interface IModelConfigService
{
    Task<ModelConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<ModelConfigSnapshot> ReloadAsync(CancellationToken cancellationToken = default);
    void ClearCache();
}

public sealed class ModelConfigService(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    ILogger<ModelConfigService> logger) : IModelConfigService
{
    public const string TopPicksModelKey = "TopPicksModel";
    public const string MlbPredictionGenerationModelKey = "MlbPredictionGeneration";
    public const string MlbPredictionGenerationEra = "CurrentMlbPredictionGeneration";
    private const string CacheKey = "model-config-snapshot";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<ModelConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out ModelConfigSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        return await ReloadAsync(cancellationToken);
    }

    public async Task<ModelConfigSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sets = await dbContext.ModelConfigSets
                    .AsNoTracking()
                    .Include(row => row.Parameters)
                    .OrderBy(row => row.ModelKey)
                    .ThenBy(row => row.Sport)
                    .ThenBy(row => row.ModelEra)
                    .ToArrayAsync(cancellationToken);

            var snapshot = new ModelConfigSnapshot(DateTimeOffset.UtcNow, true, null, sets, appsettingsFallbackCount: 0);
            cache.Set(CacheKey, snapshot, CacheDuration);
            return snapshot;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Model config DB read failed; runtime will use options/code defaults.");
            return new ModelConfigSnapshot(
                DateTimeOffset.UtcNow,
                loadSucceeded: false,
                loadError: exception.Message,
                modelConfigSets: [],
                appsettingsFallbackCount: 2);
        }
    }

    public void ClearCache() => cache.Remove(CacheKey);
}

public sealed class ModelConfigSnapshot(
    DateTimeOffset generatedAtUtc,
    bool loadSucceeded,
    string? loadError,
    IReadOnlyList<ModelConfigSet> modelConfigSets,
    int appsettingsFallbackCount)
{
    private int fallbackCount = appsettingsFallbackCount;

    public DateTimeOffset GeneratedAtUtc { get; } = generatedAtUtc;
    public bool LoadSucceeded { get; } = loadSucceeded;
    public string? LoadError { get; } = loadError;
    public IReadOnlyList<ModelConfigSet> ModelConfigSets { get; } = modelConfigSets;
    public int AppsettingsFallbackCount => fallbackCount;

    public ModelConfigSet? FindActive(string modelKey, string? sport = null)
    {
        var matches = ModelConfigSets
            .Where(row => row.IsActive
                && string.Equals(row.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(sport)
                    || string.Equals(row.Sport, sport, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Sport, "All", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(row => !string.Equals(row.Sport, "All", StringComparison.OrdinalIgnoreCase))
            .ThenBy(row => row.ModelEra, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.FirstOrDefault();
    }

    internal void RegisterAppsettingsFallback() => Interlocked.Increment(ref fallbackCount);
}

public sealed class ModelConfigOptionsOverrides(IModelConfigService modelConfigService) :
    IConfigureOptions<TopPicksModelOptions>,
    IConfigureOptions<PredictionGenerationOptions>
{
    public void Configure(TopPicksModelOptions options)
    {
        var snapshot = modelConfigService.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
        var set = snapshot.FindActive(ModelConfigService.TopPicksModelKey);
        if (set is null)
        {
            snapshot.RegisterAppsettingsFallback();
            return;
        }

        ApplyString(set, nameof(TopPicksModelOptions.ActiveModel), value => options.ActiveModel = value);
        ApplyString(set, nameof(TopPicksModelOptions.CurrentModelEra), value => options.CurrentModelEra = value);
        ApplyBoolean(set, nameof(TopPicksModelOptions.EnableDebugBreakdown), value => options.EnableDebugBreakdown = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.MlbMinimumHitChance), value => options.MlbMinimumHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.MlbMinimumReliability), value => options.MlbMinimumReliability = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SoccerWorldCupMinimumHitChance), value => options.SoccerWorldCupMinimumHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SoccerWorldCupMinimumReliability), value => options.SoccerWorldCupMinimumReliability = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.MinimumEffectiveSampleSize), value => options.MinimumEffectiveSampleSize = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.MarketCalibrationGapThreshold), value => options.MarketCalibrationGapThreshold = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.MarketCalibrationShrinkRate), value => options.MarketCalibrationShrinkRate = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.RecentModifierMin), value => options.RecentModifierMin = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.RecentModifierMax), value => options.RecentModifierMax = value);
        ApplyInteger(set, nameof(TopPicksModelOptions.SportsbookBalancedMaxFavoriteAmericanOdds), value => options.SportsbookBalancedMaxFavoriteAmericanOdds = value);
        ApplyInteger(set, nameof(TopPicksModelOptions.SportsbookBalancedMaxUnderdogAmericanOdds), value => options.SportsbookBalancedMaxUnderdogAmericanOdds = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SportsbookBalancedTargetMinHitChance), value => options.SportsbookBalancedTargetMinHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SportsbookBalancedPublicMinimumHitChance), value => options.SportsbookBalancedPublicMinimumHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SportsbookBalancedTargetMaxHitChance), value => options.SportsbookBalancedTargetMaxHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.SportsbookBalancedSuspiciousMarketGap), value => options.SportsbookBalancedSuspiciousMarketGap = value);
        ApplyInteger(set, nameof(TopPicksModelOptions.SportsbookHeavyFavoritePenaltyStartsAt), value => options.SportsbookHeavyFavoritePenaltyStartsAt = value);
        ApplyBoolean(set, nameof(TopPicksModelOptions.SportsbookExtremeFavoriteIneligible), value => options.SportsbookExtremeFavoriteIneligible = value);
        ApplyBoolean(set, nameof(TopPicksModelOptions.EnableSelectedSideHistoryConsistencyGuard), value => options.EnableSelectedSideHistoryConsistencyGuard = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.ConsistencyGuardHighHitChance), value => options.ConsistencyGuardHighHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.ConsistencyGuardExtremeHitChance), value => options.ConsistencyGuardExtremeHitChance = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.ConsistencyGuardWeakRecent10HitRate), value => options.ConsistencyGuardWeakRecent10HitRate = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.ConsistencyGuardExtremeWeakRecent10HitRate), value => options.ConsistencyGuardExtremeWeakRecent10HitRate = value);
        ApplyDecimal(set, nameof(TopPicksModelOptions.ConsistencyGuardHighHitChanceGap), value => options.ConsistencyGuardHighHitChanceGap = value);
    }

    public void Configure(PredictionGenerationOptions options)
    {
        var snapshot = modelConfigService.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
        var set = snapshot.FindActive(ModelConfigService.MlbPredictionGenerationModelKey, "MLB");
        if (set is null)
        {
            snapshot.RegisterAppsettingsFallback();
            return;
        }

        ApplyDecimal(set, nameof(PredictionGenerationOptions.OverProjectionEdgeThreshold), value => options.OverProjectionEdgeThreshold = value);
        ApplyDecimal(set, nameof(PredictionGenerationOptions.UnderProjectionEdgeThreshold), value => options.UnderProjectionEdgeThreshold = value);
        ApplyDecimal(set, nameof(PredictionGenerationOptions.MinimumPickHitProbability), value => options.MinimumPickHitProbability = value);
        ApplyDecimal(set, nameof(PredictionGenerationOptions.StrongPickHitProbability), value => options.StrongPickHitProbability = value);
        ApplyDecimal(set, nameof(PredictionGenerationOptions.ResearchOnlyBelowHitProbability), value => options.ResearchOnlyBelowHitProbability = value);

        var enabledPropTypes = set.Parameters
            .Where(row => row.ParameterKey.StartsWith("EnabledPropType.", StringComparison.OrdinalIgnoreCase)
                && AppConfigValueParser.TryParse(row.Value, AppConfigValueTypes.Boolean, out var parsed, out _)
                && parsed is true)
            .OrderBy(row => row.SortOrder)
            .Select(row => row.ParameterKey["EnabledPropType.".Length..])
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (enabledPropTypes.Length > 0)
        {
            options.EnabledPropTypes = enabledPropTypes;
        }
    }

    private static void ApplyString(ModelConfigSet set, string key, Action<string> apply)
    {
        var parameter = FindParameter(set, key);
        if (parameter is not null)
        {
            apply(parameter.Value);
        }
    }

    private static void ApplyBoolean(ModelConfigSet set, string key, Action<bool> apply)
    {
        if (TryParseParameter<bool>(set, key, out var value))
        {
            apply(value);
        }
    }

    private static void ApplyInteger(ModelConfigSet set, string key, Action<int> apply)
    {
        if (TryParseParameter<int>(set, key, out var value))
        {
            apply(value);
        }
    }

    private static void ApplyDecimal(ModelConfigSet set, string key, Action<decimal> apply)
    {
        if (TryParseParameter<decimal>(set, key, out var value))
        {
            apply(value);
        }
    }

    private static bool TryParseParameter<T>(ModelConfigSet set, string key, out T value)
    {
        value = default!;
        var parameter = FindParameter(set, key);
        if (parameter is null
            || !AppConfigValueParser.TryParse(parameter.Value, typeof(T), out var parsed, out _)
            || parsed is not T typed)
        {
            return false;
        }

        value = typed;
        return true;
    }

    private static ModelConfigParameter? FindParameter(ModelConfigSet set, string key)
        => set.Parameters.FirstOrDefault(row => string.Equals(row.ParameterKey, key, StringComparison.OrdinalIgnoreCase));
}

public sealed record ModelConfigSeedResult(
    int SetsInserted,
    int ParametersInserted,
    int MetadataUpdated,
    DateTimeOffset SeededAtUtc);

public static class ModelConfigSeeder
{
    public static async Task<ModelConfigSeedResult> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var topPicksOptions = new TopPicksModelOptions();
        var predictionOptions = new PredictionGenerationOptions();
        var now = DateTimeOffset.UtcNow;
        var setsInserted = 0;
        var parametersInserted = 0;
        var metadataUpdated = 0;

        var topPicksSet = await UpsertSetAsync(
            dbContext,
            ModelConfigService.TopPicksModelKey,
            topPicksOptions.CurrentModelEra,
            "All",
            isActive: true,
            "Current public Top Picks model configuration.",
            now,
            cancellationToken);
        setsInserted += topPicksSet.Inserted;
        metadataUpdated += topPicksSet.MetadataUpdated;
        var topPicksParameters = BuildTopPicksParameters(topPicksSet.SetId, topPicksOptions, now).ToArray();
        var topPicksParameterResult = await UpsertParametersAsync(dbContext, topPicksParameters, now, cancellationToken);
        parametersInserted += topPicksParameterResult.Inserted;
        metadataUpdated += topPicksParameterResult.MetadataUpdated;

        var predictionSet = await UpsertSetAsync(
            dbContext,
            ModelConfigService.MlbPredictionGenerationModelKey,
            ModelConfigService.MlbPredictionGenerationEra,
            "MLB",
            isActive: true,
            "Current MLB prediction generation thresholds and enabled prop types.",
            now,
            cancellationToken);
        setsInserted += predictionSet.Inserted;
        metadataUpdated += predictionSet.MetadataUpdated;
        var predictionParameters = BuildPredictionGenerationParameters(predictionSet.SetId, predictionOptions, now).ToArray();
        var predictionParameterResult = await UpsertParametersAsync(dbContext, predictionParameters, now, cancellationToken);
        parametersInserted += predictionParameterResult.Inserted;
        metadataUpdated += predictionParameterResult.MetadataUpdated;

        await dbContext.SaveChangesAsync(cancellationToken);
        scope.ServiceProvider.GetService<IModelConfigService>()?.ClearCache();

        return new ModelConfigSeedResult(setsInserted, parametersInserted, metadataUpdated, now);
    }

    private static async Task<(Guid SetId, int Inserted, int MetadataUpdated)> UpsertSetAsync(
        ApplicationDbContext dbContext,
        string modelKey,
        string modelEra,
        string sport,
        bool isActive,
        string description,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ModelConfigSets.SingleOrDefaultAsync(
            row => row.ModelKey == modelKey && row.ModelEra == modelEra && row.Sport == sport,
            cancellationToken);
        if (existing is null)
        {
            var row = new ModelConfigSet
            {
                Id = Guid.NewGuid(),
                ModelKey = modelKey,
                ModelEra = modelEra,
                Sport = sport,
                IsActive = isActive,
                Description = description,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.ModelConfigSets.Add(row);
            return (row.Id, 1, 0);
        }

        var changed = false;
        if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
        {
            existing.Description = description;
            changed = true;
        }
        if (changed)
        {
            existing.UpdatedAtUtc = now;
            return (existing.Id, 0, 1);
        }

        return (existing.Id, 0, 0);
    }

    private static async Task<(int Inserted, int MetadataUpdated)> UpsertParametersAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ModelConfigParameter> seeds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var metadataUpdated = 0;
        foreach (var seed in seeds)
        {
            var existing = await dbContext.ModelConfigParameters.SingleOrDefaultAsync(
                row => row.ModelConfigSetId == seed.ModelConfigSetId && row.ParameterKey == seed.ParameterKey,
                cancellationToken);
            if (existing is null)
            {
                dbContext.ModelConfigParameters.Add(seed);
                inserted++;
                continue;
            }

            var changed = false;
            if (!string.Equals(existing.ValueType, seed.ValueType, StringComparison.Ordinal))
            {
                existing.ValueType = seed.ValueType;
                changed = true;
            }

            if (!string.Equals(existing.DisplayName, seed.DisplayName, StringComparison.Ordinal))
            {
                existing.DisplayName = seed.DisplayName;
                changed = true;
            }
            changed |= SetIfDifferent(existing.Description, seed.Description, value => existing.Description = value);
            changed |= SetIfDifferent(existing.Unit, seed.Unit, value => existing.Unit = value);
            if (existing.SortOrder != seed.SortOrder)
            {
                existing.SortOrder = seed.SortOrder;
                changed = true;
            }

            if (changed)
            {
                existing.UpdatedAtUtc = now;
                metadataUpdated++;
            }
        }

        return (inserted, metadataUpdated);
    }

    private static IEnumerable<ModelConfigParameter> BuildTopPicksParameters(
        Guid setId,
        TopPicksModelOptions options,
        DateTimeOffset now)
    {
        var sortOrder = 100;
        yield return Parameter(setId, nameof(options.ActiveModel), AppConfigValueTypes.String, options.ActiveModel, "Active model", null, sortOrder++, now);
        yield return Parameter(setId, nameof(options.CurrentModelEra), AppConfigValueTypes.String, options.CurrentModelEra, "Current model era", null, sortOrder++, now);
        yield return Parameter(setId, nameof(options.EnableDebugBreakdown), AppConfigValueTypes.Boolean, options.EnableDebugBreakdown, "Enable debug breakdown", null, sortOrder++, now);
        yield return Parameter(setId, nameof(options.MlbMinimumHitChance), AppConfigValueTypes.Decimal, options.MlbMinimumHitChance, "MLB minimum hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.MlbMinimumReliability), AppConfigValueTypes.Decimal, options.MlbMinimumReliability, "MLB minimum reliability", "score", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SoccerWorldCupMinimumHitChance), AppConfigValueTypes.Decimal, options.SoccerWorldCupMinimumHitChance, "Soccer World Cup minimum hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SoccerWorldCupMinimumReliability), AppConfigValueTypes.Decimal, options.SoccerWorldCupMinimumReliability, "Soccer World Cup minimum reliability", "score", sortOrder++, now);
        yield return Parameter(setId, nameof(options.MinimumEffectiveSampleSize), AppConfigValueTypes.Decimal, options.MinimumEffectiveSampleSize, "Minimum effective sample size", "count", sortOrder++, now);
        yield return Parameter(setId, nameof(options.MarketCalibrationGapThreshold), AppConfigValueTypes.Decimal, options.MarketCalibrationGapThreshold, "Market calibration gap threshold", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.MarketCalibrationShrinkRate), AppConfigValueTypes.Decimal, options.MarketCalibrationShrinkRate, "Market calibration shrink rate", "rate", sortOrder++, now);
        yield return Parameter(setId, nameof(options.RecentModifierMin), AppConfigValueTypes.Decimal, options.RecentModifierMin, "Recent modifier minimum", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.RecentModifierMax), AppConfigValueTypes.Decimal, options.RecentModifierMax, "Recent modifier maximum", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedMaxFavoriteAmericanOdds), AppConfigValueTypes.Integer, options.SportsbookBalancedMaxFavoriteAmericanOdds, "Balanced max favorite American odds", "american odds", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedMaxUnderdogAmericanOdds), AppConfigValueTypes.Integer, options.SportsbookBalancedMaxUnderdogAmericanOdds, "Balanced max underdog American odds", "american odds", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedTargetMinHitChance), AppConfigValueTypes.Decimal, options.SportsbookBalancedTargetMinHitChance, "Balanced target minimum hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedPublicMinimumHitChance), AppConfigValueTypes.Decimal, options.SportsbookBalancedPublicMinimumHitChance, "Balanced public minimum hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedTargetMaxHitChance), AppConfigValueTypes.Decimal, options.SportsbookBalancedTargetMaxHitChance, "Balanced target maximum hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookBalancedSuspiciousMarketGap), AppConfigValueTypes.Decimal, options.SportsbookBalancedSuspiciousMarketGap, "Balanced suspicious market gap", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookHeavyFavoritePenaltyStartsAt), AppConfigValueTypes.Integer, options.SportsbookHeavyFavoritePenaltyStartsAt, "Sportsbook heavy favorite penalty starts at", "american odds", sortOrder++, now);
        yield return Parameter(setId, nameof(options.SportsbookExtremeFavoriteIneligible), AppConfigValueTypes.Boolean, options.SportsbookExtremeFavoriteIneligible, "Sportsbook extreme favorite ineligible", null, sortOrder++, now);
        yield return Parameter(setId, nameof(options.EnableSelectedSideHistoryConsistencyGuard), AppConfigValueTypes.Boolean, options.EnableSelectedSideHistoryConsistencyGuard, "Enable selected-side history consistency guard", null, sortOrder++, now);
        yield return Parameter(setId, nameof(options.ConsistencyGuardHighHitChance), AppConfigValueTypes.Decimal, options.ConsistencyGuardHighHitChance, "Consistency guard high hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.ConsistencyGuardExtremeHitChance), AppConfigValueTypes.Decimal, options.ConsistencyGuardExtremeHitChance, "Consistency guard extreme hit chance", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.ConsistencyGuardWeakRecent10HitRate), AppConfigValueTypes.Decimal, options.ConsistencyGuardWeakRecent10HitRate, "Consistency guard weak recent 10 hit rate", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.ConsistencyGuardExtremeWeakRecent10HitRate), AppConfigValueTypes.Decimal, options.ConsistencyGuardExtremeWeakRecent10HitRate, "Consistency guard extreme weak recent 10 hit rate", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.ConsistencyGuardHighHitChanceGap), AppConfigValueTypes.Decimal, options.ConsistencyGuardHighHitChanceGap, "Consistency guard high hit chance gap", "probability", sortOrder++, now);
    }

    private static IEnumerable<ModelConfigParameter> BuildPredictionGenerationParameters(
        Guid setId,
        PredictionGenerationOptions options,
        DateTimeOffset now)
    {
        var sortOrder = 100;
        yield return Parameter(setId, nameof(options.OverProjectionEdgeThreshold), AppConfigValueTypes.Decimal, options.OverProjectionEdgeThreshold, "Over projection edge threshold", "count", sortOrder++, now);
        yield return Parameter(setId, nameof(options.UnderProjectionEdgeThreshold), AppConfigValueTypes.Decimal, options.UnderProjectionEdgeThreshold, "Under projection edge threshold", "count", sortOrder++, now);
        yield return Parameter(setId, nameof(options.MinimumPickHitProbability), AppConfigValueTypes.Decimal, options.MinimumPickHitProbability, "Minimum pick hit probability", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.StrongPickHitProbability), AppConfigValueTypes.Decimal, options.StrongPickHitProbability, "Strong pick hit probability", "probability", sortOrder++, now);
        yield return Parameter(setId, nameof(options.ResearchOnlyBelowHitProbability), AppConfigValueTypes.Decimal, options.ResearchOnlyBelowHitProbability, "Research-only below hit probability", "probability", sortOrder++, now);

        foreach (var propType in options.EnabledPropTypes.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return Parameter(setId, $"EnabledPropType.{propType}", AppConfigValueTypes.Boolean, true, $"Enable {propType}", null, sortOrder++, now);
        }
    }

    private static ModelConfigParameter Parameter(
        Guid setId,
        string key,
        string valueType,
        object value,
        string displayName,
        string? unit,
        int sortOrder,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            ModelConfigSetId = setId,
            ParameterKey = key,
            ValueType = valueType,
            Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            DisplayName = displayName,
            Description = displayName,
            Unit = unit,
            SortOrder = sortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static bool SetIfDifferent(string? current, string? next, Action<string?> assign)
    {
        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return false;
        }

        assign(next);
        return true;
    }
}
