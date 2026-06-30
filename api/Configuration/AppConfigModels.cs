using System.Text.Json.Serialization;

namespace SportsBetting.Api.Configuration;

public static class AppConfigClassifications
{
    public const string KeepInBootstrapConfig = "KeepInBootstrapConfig";
    public const string KeepInSecretProvider = "KeepInSecretProvider";
    public const string MoveToAppConfigs = "MoveToAppConfigs";
    public const string MoveToDomainTable = "MoveToDomainTable";
    public const string MovedToDomainTable = "MovedToDomainTable";
    public const string RemoveRedundant = "RemoveRedundant";
    public const string NeedsDecision = "NeedsDecision";
}

public static class AppConfigValueTypes
{
    public const string Boolean = "Boolean";
    public const string Integer = "Integer";
    public const string Decimal = "Decimal";
    public const string String = "String";
    public const string StringList = "StringList";
    public const string Json = "Json";
}

public static class AppConfigInputTypes
{
    public const string Boolean = "Boolean";
    public const string Number = "Number";
    public const string Duration = "Duration";
    public const string Select = "Select";
    public const string MultiSelect = "MultiSelect";
    public const string Text = "Text";
    public const string AdvancedText = "AdvancedText";
}

public static class AppConfigImpactLevels
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Dangerous = "Dangerous";
}

public sealed record AppConfigDefinition(
    string Key,
    string? BootstrapPath,
    string ValueType,
    string Category,
    string Description,
    string Classification,
    string Reason,
    string? DefaultValue,
    bool SeedToAppConfigs = false,
    bool IsSensitive = false,
    bool IsRuntimeEditable = true,
    bool RequiresRestart = false,
    bool RuntimeRefreshSafe = true,
    bool MaskInDiagnostics = false,
    string? ValidationJson = null,
    IReadOnlyList<string>? Consumers = null,
    string DisplayName = "",
    string Section = "",
    string HelpText = "",
    string InputType = AppConfigInputTypes.Text,
    string? Unit = null,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    int SortOrder = 1000,
    bool IsAdvanced = false,
    bool IsHidden = false,
    string ImpactLevel = AppConfigImpactLevels.Low,
    bool IsChildAuditItem = false,
    string ShortDisplayName = "");

public sealed record AppConfigEffectiveValue(
    string Key,
    string? BootstrapPath,
    string ValueType,
    string Category,
    string Section,
    string DisplayName,
    string ShortDisplayName,
    string HelpText,
    string InputType,
    string? Unit,
    decimal? MinValue,
    decimal? MaxValue,
    int SortOrder,
    bool IsAdvanced,
    bool IsHidden,
    string ImpactLevel,
    string Classification,
    string Source,
    string? SourceEnvironment,
    string? ConfiguredValue,
    string? EffectiveValue,
    string? DefaultValue,
    string? DisplayValue,
    bool IsValueMasked,
    bool IsSensitive,
    bool IsRuntimeEditable,
    bool RequiresRestart,
    bool RuntimeRefreshSafe,
    bool IsValid,
    string? ValidationError,
    DateTimeOffset? UpdatedAtUtc);

public sealed record AppConfigSnapshot(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? CacheLastRefreshedAtUtc,
    string Environment,
    bool LoadSucceeded,
    string? LoadError,
    int GlobalRowCount,
    int EnvironmentOverrideRowCount,
    IReadOnlyList<AppConfigEffectiveValue> Values,
    IReadOnlyList<AppConfigValidationIssue> ValidationIssues)
{
    private IReadOnlyDictionary<string, AppConfigEffectiveValue>? valueByKey;

    [JsonIgnore]
    public IReadOnlyDictionary<string, AppConfigEffectiveValue> ValueByKey =>
        valueByKey ??= Values.ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);

    public T GetValue<T>(string key, T fallback)
    {
        if (!ValueByKey.TryGetValue(key, out var value)
            || !value.IsValid
            || string.IsNullOrWhiteSpace(value.EffectiveValue))
        {
            return fallback;
        }

        return AppConfigValueParser.TryParse(value.EffectiveValue, typeof(T), out var parsed, out _)
            ? (T)parsed!
            : fallback;
    }
}

public sealed record AppConfigValidationIssue(
    string Key,
    string Source,
    string Message);

public sealed record AppConfigAuditRow(
    string Key,
    string? AppConfigKey,
    string? AppsettingsValue,
    string? DevelopmentValue,
    string Classification,
    string Reason,
    IReadOnlyList<string> Consumers,
    string? ProposedAppConfigKey,
    string ValueType,
    string Category,
    string Section,
    string DisplayName,
    string ShortDisplayName,
    string HelpText,
    string InputType,
    string? Unit,
    decimal? MinValue,
    decimal? MaxValue,
    int SortOrder,
    bool IsAdvanced,
    bool IsHidden,
    string ImpactLevel,
    bool IsChildAuditItem,
    string? DefaultValue,
    bool RuntimeRefreshSafe,
    bool RequiresRestart,
    bool SecretOrEncryptionRequired,
    bool MaskedInDiagnostics);

public sealed record AppConfigAuditReport(
    DateTimeOffset GeneratedAtUtc,
    bool CallsExternalApis,
    bool ReadOnly,
    bool WritesRows,
    IReadOnlyList<AppConfigAuditRow> Settings,
    IReadOnlyDictionary<string, int> CountsByClassification,
    IReadOnlyDictionary<string, int> ChildCountsByClassification,
    IReadOnlyDictionary<string, int> TotalCountsByClassification,
    IReadOnlyList<AppConfigAuditRow> RedundantSettings,
    IReadOnlyList<AppConfigAuditRow> NeedsDecisionSettings,
    IReadOnlyList<AppConfigAuditRow> SecretProviderSettings);
