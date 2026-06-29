using SportsBetting.Api.Configuration;

namespace SportsBetting.Api.Endpoints;

public static class AppConfigDebugEndpoints
{
    public static IEndpointRouteBuilder MapAppConfigDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/app-configs/status", async (
            IAppConfigService appConfigService,
            IAppConfigAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await appConfigService.GetSnapshotAsync(cancellationToken);
            var audit = await auditService.BuildAuditAsync(includeChildren: false, cancellationToken);
            return Results.Ok(new
            {
                generatedAtUtc = snapshot.GeneratedAtUtc,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                environment = snapshot.Environment,
                cacheLastRefreshedAtUtc = snapshot.CacheLastRefreshedAtUtc,
                snapshot.LoadSucceeded,
                snapshot.LoadError,
                totalKnownConfigKeys = snapshot.Values.Count,
                scalarAppConfigsCount = snapshot.Values.Count(row => row.Classification == AppConfigClassifications.MoveToAppConfigs),
                appConfigsBackedCount = snapshot.Values.Count(row => row.Classification == AppConfigClassifications.MoveToAppConfigs
                    && row.Source.StartsWith("AppConfigs", StringComparison.OrdinalIgnoreCase)),
                globalRowCount = snapshot.GlobalRowCount,
                environmentOverrideRowCount = snapshot.EnvironmentOverrideRowCount,
                fallbackCount = snapshot.Values.Count(row => row.Source.Contains("fallback", StringComparison.OrdinalIgnoreCase)),
                missingCount = snapshot.Values.Count(row => row.Source == "missing"),
                invalidCount = snapshot.ValidationIssues.Count(row => row.Source == "AppConfigs"),
                validationIssueCount = snapshot.ValidationIssues.Count,
                moveToDomainTableCount = audit.CountsByClassification.GetValueOrDefault(AppConfigClassifications.MoveToDomainTable),
                removeRedundantCount = audit.CountsByClassification.GetValueOrDefault(AppConfigClassifications.RemoveRedundant),
                needsDecisionCount = audit.CountsByClassification.GetValueOrDefault(AppConfigClassifications.NeedsDecision),
                advancedCount = snapshot.Values.Count(row => row.IsAdvanced),
                hiddenCount = snapshot.Values.Count(row => row.IsHidden),
                defaultEffectiveValueCount = snapshot.Values.Count(row => row.Classification == AppConfigClassifications.MoveToAppConfigs
                    && !row.IsHidden
                    && !row.IsAdvanced
                    && !row.IsSensitive),
                countsBySource = snapshot.Values
                    .GroupBy(row => row.Source)
                    .OrderBy(group => group.Key)
                    .Select(group => new { source = group.Key, count = group.Count() })
                    .ToArray(),
                countsByClassification = audit.CountsByClassification
                    .OrderBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new { classification = row.Key, count = row.Value })
                    .ToArray(),
                validationIssues = snapshot.ValidationIssues
            });
        })
        .WithName("AppConfigsStatusDebug")
        .WithSummary("Read-only status for DB-backed AppConfigs cache and validation.");

        endpoints.MapGet("/api/debug/app-configs", async (
            bool? includeValues,
            bool? includeAuditCandidates,
            bool? includeHidden,
            bool? includeAdvanced,
            bool? includeSecrets,
            string? classification,
            string? category,
            IAppConfigService appConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await appConfigService.GetSnapshotAsync(cancellationToken);
            var values = snapshot.Values.AsEnumerable();
            var showAuditCandidates = includeAuditCandidates == true || !string.IsNullOrWhiteSpace(classification);
            if (!showAuditCandidates)
            {
                values = values.Where(row => row.Classification == AppConfigClassifications.MoveToAppConfigs);
            }

            if (includeHidden != true)
            {
                values = values.Where(row => !row.IsHidden);
            }

            if (includeAdvanced != true)
            {
                values = values.Where(row => !row.IsAdvanced);
            }

            if (includeSecrets != true)
            {
                values = values.Where(row => !row.IsSensitive
                    && row.Classification != AppConfigClassifications.KeepInSecretProvider);
            }

            if (!string.IsNullOrWhiteSpace(classification))
            {
                values = values.Where(row => string.Equals(row.Classification, classification.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                values = values.Where(row => string.Equals(row.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            _ = includeValues;
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                environment = snapshot.Environment,
                cacheLastRefreshedAtUtc = snapshot.CacheLastRefreshedAtUtc,
                includeAuditCandidates = showAuditCandidates,
                includeHidden = includeHidden == true,
                includeAdvanced = includeAdvanced == true,
                includeSecrets = includeSecrets == true,
                values = values
                    .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.SortOrder)
                    .ThenBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new
                    {
                        row.Key,
                        row.BootstrapPath,
                        row.ValueType,
                        row.Category,
                        row.Section,
                        row.DisplayName,
                        row.ShortDisplayName,
                        row.HelpText,
                        row.InputType,
                        row.Unit,
                        row.MinValue,
                        row.MaxValue,
                        row.SortOrder,
                        row.IsAdvanced,
                        row.IsHidden,
                        row.ImpactLevel,
                        row.Classification,
                        row.Source,
                        row.SourceEnvironment,
                        row.ConfiguredValue,
                        row.EffectiveValue,
                        row.DefaultValue,
                        row.DisplayValue,
                        row.IsValueMasked,
                        row.IsSensitive,
                        row.IsRuntimeEditable,
                        row.RequiresRestart,
                        row.RuntimeRefreshSafe,
                        row.IsValid,
                        row.ValidationError,
                        row.UpdatedAtUtc
                    })
                    .ToArray()
            });
        })
        .WithName("AppConfigsEffectiveValuesDebug")
        .WithSummary("Read-only effective DB/appsettings/default AppConfigs values with sensitive values masked or omitted.");

        endpoints.MapGet("/api/debug/app-configs/audit", async (
            bool? includeChildren,
            IAppConfigAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var report = await auditService.BuildAuditAsync(includeChildren == true, cancellationToken);
            return Results.Ok(report);
        })
        .WithName("AppConfigsAuditDebug")
        .WithSummary("Read-only audit/classification of appsettings and AppConfigs migration candidates.");

        return endpoints;
    }
}
