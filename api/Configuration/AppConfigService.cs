using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Configuration;

public interface IAppConfigService
{
    Task<AppConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<AppConfigSnapshot> ReloadAsync(CancellationToken cancellationToken = default);
    T GetValue<T>(string key, T fallback);
}

public interface IAppConfigAuditService
{
    Task<AppConfigAuditReport> BuildAuditAsync(CancellationToken cancellationToken = default);
    Task<AppConfigAuditReport> BuildAuditAsync(bool includeChildren, CancellationToken cancellationToken = default);
}

public sealed class AppConfigService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AppConfigService> logger) : IAppConfigService, IAppConfigAuditService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim reloadLock = new(1, 1);
    private AppConfigSnapshot? cachedSnapshot;

    public T GetValue<T>(string key, T fallback)
        => GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult().GetValue(key, fallback);

    public async Task<AppConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = cachedSnapshot;
        if (snapshot is not null
            && snapshot.CacheLastRefreshedAtUtc.HasValue
            && DateTimeOffset.UtcNow - snapshot.CacheLastRefreshedAtUtc.Value < CacheDuration)
        {
            return snapshot;
        }

        return await ReloadAsync(cancellationToken);
    }

    public async Task<AppConfigSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await reloadLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var rows = Array.Empty<AppConfig>();
            string? loadError = null;
            var loadSucceeded = true;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                rows = await dbContext.AppConfigs
                    .AsNoTracking()
                    .Where(row => row.IsEnabled
                        && (row.Environment == environment.EnvironmentName
                            || row.Environment == AppConfigCatalog.EnvironmentGlobal
                            || row.Environment == AppConfigCatalog.LegacyEnvironmentAll))
                    .ToArrayAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                loadSucceeded = false;
                loadError = $"{exception.GetType().Name}: {exception.Message}";
                logger.LogWarning(exception, "AppConfigs load failed; falling back to appsettings/default values.");
            }

            var rowByKey = rows
                .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(row => string.Equals(row.Environment, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(row => string.Equals(row.Environment, AppConfigCatalog.EnvironmentGlobal, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(row => string.Equals(row.Environment, AppConfigCatalog.LegacyEnvironmentAll, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(row => row.UpdatedAtUtc)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var values = new List<AppConfigEffectiveValue>();
            var issues = new List<AppConfigValidationIssue>();

            foreach (var definition in AppConfigCatalog.Definitions)
            {
                var appsettingsValue = GetConfiguredFallbackValue(definition);
                var fallbackSource = string.IsNullOrWhiteSpace(appsettingsValue) ? "code default" : "appsettings fallback";
                var effectiveValue = string.IsNullOrWhiteSpace(appsettingsValue) ? definition.DefaultValue : appsettingsValue;
                var source = string.IsNullOrWhiteSpace(effectiveValue) ? "missing" : fallbackSource;
                var valid = true;
                string? validationError = null;
                DateTimeOffset? updatedAtUtc = null;
                string? sourceEnvironment = null;
                string? configuredValue = null;

                if (rowByKey.TryGetValue(definition.Key, out var row))
                {
                    updatedAtUtc = row.UpdatedAtUtc;
                    sourceEnvironment = row.Environment;
                    if (AppConfigValueParser.TryParse(row.Value, definition.ValueType, out _, out validationError))
                    {
                        configuredValue = row.Value;
                        effectiveValue = row.Value;
                        source = string.Equals(row.Environment, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase)
                            ? "AppConfigs environment override"
                            : "AppConfigs global/default";
                        valid = true;
                    }
                    else
                    {
                        valid = false;
                        source = string.IsNullOrWhiteSpace(effectiveValue)
                            ? "AppConfigs invalid, missing fallback"
                            : $"AppConfigs invalid, {fallbackSource}";
                        issues.Add(new AppConfigValidationIssue(definition.Key, "AppConfigs", validationError ?? "Invalid value."));
                        logger.LogWarning(
                            "AppConfig key {Key} has invalid {ValueType} value; using fallback source {FallbackSource}.",
                            definition.Key,
                            definition.ValueType,
                            fallbackSource);
                    }
                }
                else if (definition.SeedToAppConfigs && !definition.IsAdvanced && !definition.IsHidden)
                {
                    issues.Add(new AppConfigValidationIssue(definition.Key, source, "AppConfig row is missing; fallback is active."));
                }

                values.Add(new AppConfigEffectiveValue(
                    definition.Key,
                    definition.BootstrapPath,
                    definition.ValueType,
                    definition.Category,
                    definition.Section,
                    definition.DisplayName,
                    definition.ShortDisplayName,
                    definition.HelpText,
                    definition.InputType,
                    definition.Unit,
                    definition.MinValue,
                    definition.MaxValue,
                    definition.SortOrder,
                    definition.IsAdvanced,
                    definition.IsHidden,
                    definition.ImpactLevel,
                    definition.Classification,
                    source,
                    sourceEnvironment,
                    RawOrNull(configuredValue, definition),
                    RawOrNull(effectiveValue, definition),
                    MaskIfNeeded(definition.DefaultValue, definition),
                    MaskIfNeeded(effectiveValue, definition),
                    definition.IsSensitive || definition.MaskInDiagnostics,
                    definition.IsSensitive,
                    definition.IsRuntimeEditable,
                    definition.RequiresRestart,
                    definition.RuntimeRefreshSafe,
                    valid,
                    validationError,
                    updatedAtUtc));
            }

            var snapshot = new AppConfigSnapshot(
                now,
                now,
                environment.EnvironmentName,
                loadSucceeded,
                loadError,
                rows.Count(row => string.Equals(row.Environment, AppConfigCatalog.EnvironmentGlobal, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Environment, AppConfigCatalog.LegacyEnvironmentAll, StringComparison.OrdinalIgnoreCase)),
                rows.Count(row => string.Equals(row.Environment, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase)),
                values,
                issues);
            cachedSnapshot = snapshot;
            return snapshot;
        }
        finally
        {
            reloadLock.Release();
        }
    }

    public async Task<AppConfigAuditReport> BuildAuditAsync(CancellationToken cancellationToken = default)
        => await BuildAuditAsync(includeChildren: false, cancellationToken);

    public async Task<AppConfigAuditReport> BuildAuditAsync(bool includeChildren, CancellationToken cancellationToken = default)
    {
        var appsettingsValues = await ReadJsonFileValuesAsync("appsettings.json", cancellationToken);
        var developmentValues = await ReadJsonFileValuesAsync($"appsettings.{Environments.Development}.json", cancellationToken);
        var discoveredKeys = AppConfigCatalog.Definitions
            .Select(definition => definition.BootstrapPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Concat(appsettingsValues.Keys)
            .Concat(developmentValues.Keys)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => includeChildren ? path! : NormalizeAuditParentKey(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var settings = discoveredKeys
            .Select(key =>
            {
                var definition = AppConfigCatalog.FindByBootstrapPath(key!) ?? InferDefinition(key!);
                var sensitive = definition.IsSensitive || LooksSecretLike(key!);
                return new AppConfigAuditRow(
                    key!,
                    definition.Key,
                    MaskIfNeeded(appsettingsValues.GetValueOrDefault(key!), sensitive),
                    MaskIfNeeded(developmentValues.GetValueOrDefault(key!), sensitive),
                    definition.Classification,
                    definition.Reason,
                    definition.Consumers ?? [],
                    definition.Classification == AppConfigClassifications.MoveToAppConfigs ? definition.Key : null,
                    definition.ValueType,
                    definition.Category,
                    definition.Section,
                    definition.DisplayName,
                    definition.ShortDisplayName,
                    definition.HelpText,
                    definition.InputType,
                    definition.Unit,
                    definition.MinValue,
                    definition.MaxValue,
                    definition.SortOrder,
                    definition.IsAdvanced,
                    definition.IsHidden,
                    definition.ImpactLevel,
                    definition.IsChildAuditItem,
                    MaskIfNeeded(definition.DefaultValue, sensitive),
                    definition.RuntimeRefreshSafe,
                    definition.RequiresRestart,
                    sensitive,
                    sensitive || definition.MaskInDiagnostics);
            })
            .ToArray();

        var parentSettings = settings
            .Where(row => !row.IsChildAuditItem)
            .ToArray();
        var childSettings = settings
            .Where(row => row.IsChildAuditItem)
            .ToArray();
        var counts = parentSettings
            .GroupBy(row => row.Classification)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var childCounts = childSettings
            .GroupBy(row => row.Classification)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCounts = settings
            .GroupBy(row => row.Classification)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new AppConfigAuditReport(
            DateTimeOffset.UtcNow,
            CallsExternalApis: false,
            ReadOnly: true,
            WritesRows: false,
            settings,
            counts,
            childCounts,
            totalCounts,
            settings.Where(row => row.Classification == AppConfigClassifications.RemoveRedundant).ToArray(),
            settings.Where(row => row.Classification == AppConfigClassifications.NeedsDecision).ToArray(),
            settings.Where(row => row.Classification == AppConfigClassifications.KeepInSecretProvider).ToArray());
    }

    private string? GetConfiguredFallbackValue(AppConfigDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.BootstrapPath))
        {
            return null;
        }

        if (definition.ValueType == AppConfigValueTypes.StringList)
        {
            var list = configuration.GetSection(definition.BootstrapPath).Get<string[]>();
            return list is { Length: > 0 }
                ? string.Join(',', list.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
                : configuration[definition.BootstrapPath];
        }

        return configuration[definition.BootstrapPath];
    }

    private AppConfigDefinition InferDefinition(string bootstrapPath)
    {
        if (LooksSecretLike(bootstrapPath))
        {
            return new AppConfigDefinition(
                bootstrapPath.Replace(':', '.'),
                bootstrapPath,
                AppConfigValueTypes.String,
                "Secrets",
                "Secret-like setting inferred from key name.",
                AppConfigClassifications.KeepInSecretProvider,
                "Secret-like setting must remain in user-secrets/environment/secret provider.",
                null,
                IsSensitive: true,
                IsRuntimeEditable: false,
                RequiresRestart: true,
                RuntimeRefreshSafe: false,
                MaskInDiagnostics: true,
                DisplayName: BuildDisplayName(bootstrapPath),
                Section: "Secrets",
                HelpText: "Secret-like setting must remain in user-secrets, environment variables, or another secret provider.",
                InputType: AppConfigInputTypes.AdvancedText,
                IsHidden: true,
                ImpactLevel: AppConfigImpactLevels.Dangerous);
        }

        if (bootstrapPath.StartsWith("Logging", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bootstrapPath, "AllowedHosts", StringComparison.OrdinalIgnoreCase))
        {
            return new AppConfigDefinition(
                bootstrapPath.Replace(':', '.'),
                bootstrapPath,
                AppConfigValueTypes.String,
                "Hosting",
                "Bootstrap host/logging setting inferred from appsettings.",
                AppConfigClassifications.KeepInBootstrapConfig,
                "Bootstrap host/logging setting should stay available before DB config loads.",
                null,
                IsRuntimeEditable: false,
                RequiresRestart: true,
                RuntimeRefreshSafe: false,
                DisplayName: BuildDisplayName(bootstrapPath),
                Section: "Hosting",
                HelpText: "Bootstrap host/logging setting should stay available before DB config loads.",
                InputType: AppConfigInputTypes.AdvancedText,
                ImpactLevel: AppConfigImpactLevels.Medium);
        }

        if (bootstrapPath.Contains(":Targets:", StringComparison.OrdinalIgnoreCase)
            || bootstrapPath.Contains(":HistoricalCompetitions:", StringComparison.OrdinalIgnoreCase)
            || bootstrapPath.StartsWith("MlbPredictionGeneration:", StringComparison.OrdinalIgnoreCase)
            || bootstrapPath.StartsWith("TopPicksModel:", StringComparison.OrdinalIgnoreCase))
        {
            return new AppConfigDefinition(
                bootstrapPath.Replace(':', '.'),
                bootstrapPath,
                AppConfigValueTypes.String,
                "NeedsDecision",
                "Complex collection or model setting inferred from appsettings.",
                AppConfigClassifications.NeedsDecision,
                "Needs a focused migration/validation decision before DB-backed edits.",
                null,
                IsRuntimeEditable: false,
                RequiresRestart: true,
                RuntimeRefreshSafe: false,
                DisplayName: BuildDisplayName(bootstrapPath),
                Section: "Domain / Model Configuration",
                HelpText: "Complex collection or model setting needs a focused migration/validation decision before DB-backed edits.",
                InputType: AppConfigInputTypes.AdvancedText,
                IsChildAuditItem: IsChildAuditPath(bootstrapPath),
                IsAdvanced: true,
                ImpactLevel: AppConfigImpactLevels.Medium);
        }

        return new AppConfigDefinition(
            bootstrapPath.Replace(':', '.'),
            bootstrapPath,
            AppConfigValueTypes.String,
            "Unclassified",
            "Setting was discovered in appsettings but is not yet in the explicit AppConfigs catalog.",
            AppConfigClassifications.NeedsDecision,
            "Needs explicit classification before migration.",
            null,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            DisplayName: BuildDisplayName(bootstrapPath),
            Section: "Unclassified",
            HelpText: "Setting needs explicit classification before it can be considered for DB-backed configuration.",
            InputType: AppConfigInputTypes.AdvancedText,
            IsChildAuditItem: IsChildAuditPath(bootstrapPath),
            IsAdvanced: true,
            ImpactLevel: AppConfigImpactLevels.Medium);
    }

    private static string NormalizeAuditParentKey(string key)
    {
        if (key.StartsWith("MlbPredictionGeneration:", StringComparison.OrdinalIgnoreCase))
        {
            return "MlbPredictionGeneration";
        }

        if (key.StartsWith("TopPicksModel:", StringComparison.OrdinalIgnoreCase))
        {
            return "TopPicksModel";
        }

        return key;
    }

    private static bool IsChildAuditPath(string key)
        => !string.Equals(key, NormalizeAuditParentKey(key), StringComparison.OrdinalIgnoreCase);

    private static string BuildDisplayName(string key)
    {
        var leaf = key.Split(':', '.').LastOrDefault() ?? key;
        var chars = new List<char>(leaf.Length + 8);
        for (var index = 0; index < leaf.Length; index++)
        {
            var current = leaf[index];
            if (index > 0
                && char.IsUpper(current)
                && (char.IsLower(leaf[index - 1]) || index + 1 < leaf.Length && char.IsLower(leaf[index + 1])))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return NormalizeSpaces(new string(chars.ToArray()).Trim());
    }

    private static string NormalizeSpaces(string value)
        => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private async Task<IReadOnlyDictionary<string, string?>> ReadJsonFileValuesAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, fileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(document.RootElement, null, values);
        return values;
    }

    private static void FlattenJson(JsonElement element, string? prefix, IDictionary<string, string?> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}:{property.Name}", values);
                }
                break;
            case JsonValueKind.Array:
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    values[prefix] = element.GetRawText();
                }
                break;
            case JsonValueKind.String:
                values[prefix ?? string.Empty] = element.GetString();
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                values[prefix ?? string.Empty] = element.GetRawText();
                break;
            case JsonValueKind.Null:
                values[prefix ?? string.Empty] = null;
                break;
        }
    }

    private static bool LooksSecretLike(string key)
    {
        return key.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Signing", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Encryption", StringComparison.OrdinalIgnoreCase);
    }

    private static string? MaskIfNeeded(string? value, AppConfigDefinition definition)
        => MaskIfNeeded(value, definition.IsSensitive || definition.MaskInDiagnostics);

    private static string? RawOrNull(string? value, AppConfigDefinition definition)
        => definition.IsSensitive || definition.MaskInDiagnostics ? null : value;

    private static string? MaskIfNeeded(string? value, bool mask)
    {
        if (!mask || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4 ? "****" : $"{value[..2]}****{value[^2..]}";
    }
}

public static class AppConfigValueParser
{
    public static bool TryParse(string? value, string valueType, out object? parsed, out string? error)
    {
        parsed = null;
        error = null;

        try
        {
            switch (valueType)
            {
                case AppConfigValueTypes.Boolean:
                    if (bool.TryParse(value, out var boolean))
                    {
                        parsed = boolean;
                        return true;
                    }

                    error = "Expected Boolean value.";
                    return false;

                case AppConfigValueTypes.Integer:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        parsed = integer;
                        return true;
                    }

                    error = "Expected Integer value.";
                    return false;

                case AppConfigValueTypes.Decimal:
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    {
                        parsed = number;
                        return true;
                    }

                    error = "Expected Decimal value.";
                    return false;

                case AppConfigValueTypes.StringList:
                    parsed = ParseStringList(value);
                    return true;

                case AppConfigValueTypes.Json:
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        parsed = null;
                        return true;
                    }

                    using (JsonDocument.Parse(value))
                    {
                        parsed = value;
                        return true;
                    }

                case AppConfigValueTypes.String:
                default:
                    parsed = value ?? string.Empty;
                    return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool TryParse(string? value, Type targetType, out object? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (targetType == typeof(string))
        {
            parsed = value ?? string.Empty;
            return true;
        }

        if (targetType == typeof(bool))
        {
            return TryParse(value, AppConfigValueTypes.Boolean, out parsed, out error);
        }

        if (targetType == typeof(int))
        {
            return TryParse(value, AppConfigValueTypes.Integer, out parsed, out error);
        }

        if (targetType == typeof(decimal))
        {
            return TryParse(value, AppConfigValueTypes.Decimal, out parsed, out error);
        }

        if (targetType == typeof(string[]))
        {
            parsed = ParseStringList(value);
            return true;
        }

        if (targetType == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan))
            {
                parsed = timeSpan;
                return true;
            }

            error = "Expected TimeSpan value.";
            return false;
        }

        error = $"Unsupported target type {targetType.Name}.";
        return false;
    }

    private static string[] ParseStringList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            return JsonSerializer.Deserialize<string[]>(trimmed) ?? [];
        }

        return trimmed
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}

public static class AppConfigSeeder
{
    public static async Task<AppConfigSeedResult> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var metadataUpdated = 0;

        foreach (var definition in AppConfigCatalog.Definitions.Where(row => row.SeedToAppConfigs && !row.IsSensitive))
        {
            var existing = await dbContext.AppConfigs.SingleOrDefaultAsync(
                row => row.Key == definition.Key && row.Environment == AppConfigCatalog.EnvironmentGlobal,
                cancellationToken);
            var seedValue = GetSeedValue(configuration, definition);

            if (string.IsNullOrWhiteSpace(seedValue) && definition.DefaultValue is null)
            {
                continue;
            }

            if (existing is null)
            {
                dbContext.AppConfigs.Add(new AppConfig
                {
                    Id = Guid.NewGuid(),
                    Key = definition.Key,
                    Value = seedValue ?? definition.DefaultValue ?? string.Empty,
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    Description = definition.Description,
                    DefaultValue = definition.DefaultValue,
                    IsSensitive = definition.IsSensitive,
                    IsRuntimeEditable = definition.IsRuntimeEditable,
                    RequiresRestart = definition.RequiresRestart,
                    ValidationJson = definition.ValidationJson,
                    Environment = AppConfigCatalog.EnvironmentGlobal,
                    IsEnabled = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    UpdatedBy = "AppConfigSeeder"
                });
                inserted++;
                continue;
            }

            var changed = false;
            if (!string.Equals(existing.ValueType, definition.ValueType, StringComparison.Ordinal))
            {
                existing.ValueType = definition.ValueType;
                changed = true;
            }

            if (!string.Equals(existing.Category, definition.Category, StringComparison.Ordinal))
            {
                existing.Category = definition.Category;
                changed = true;
            }

            changed |= SetIfDifferent(existing.Description, definition.Description, value => existing.Description = value);
            changed |= SetIfDifferent(existing.DefaultValue, definition.DefaultValue, value => existing.DefaultValue = value);
            changed |= SetIfDifferent(existing.ValidationJson, definition.ValidationJson, value => existing.ValidationJson = value);

            if (existing.IsSensitive != definition.IsSensitive)
            {
                existing.IsSensitive = definition.IsSensitive;
                changed = true;
            }

            if (existing.IsRuntimeEditable != definition.IsRuntimeEditable)
            {
                existing.IsRuntimeEditable = definition.IsRuntimeEditable;
                changed = true;
            }

            if (existing.RequiresRestart != definition.RequiresRestart)
            {
                existing.RequiresRestart = definition.RequiresRestart;
                changed = true;
            }

            if (changed)
            {
                existing.UpdatedAtUtc = now;
                existing.UpdatedBy = "AppConfigSeeder";
                metadataUpdated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AppConfigSeedResult(inserted, metadataUpdated);
    }

    private static string? GetSeedValue(IConfiguration configuration, AppConfigDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.BootstrapPath))
        {
            return definition.DefaultValue;
        }

        if (definition.ValueType == AppConfigValueTypes.StringList)
        {
            var values = configuration.GetSection(definition.BootstrapPath).Get<string[]>();
            return values is { Length: > 0 }
                ? string.Join(',', values)
                : configuration[definition.BootstrapPath] ?? definition.DefaultValue;
        }

        return configuration[definition.BootstrapPath] ?? definition.DefaultValue;
    }

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

public sealed record AppConfigSeedResult(int Inserted, int MetadataUpdated);
