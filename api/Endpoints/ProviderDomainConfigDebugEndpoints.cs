using SportsBetting.Api.Configuration;

namespace SportsBetting.Api.Endpoints;

public static class ProviderDomainConfigDebugEndpoints
{
    public static IEndpointRouteBuilder MapProviderDomainConfigDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/provider-domain-config/status", async (
            IProviderDomainConfigService providerDomainConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await providerDomainConfigService.GetSnapshotAsync(cancellationToken);
            var warnings = BuildWarnings(snapshot);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                snapshot.LoadSucceeded,
                snapshot.LoadError,
                providerCompetitionCount = snapshot.ProviderCompetitions.Count,
                enabledProviderCompetitionCount = snapshot.ProviderCompetitions.Count(row => row.IsEnabled),
                providerMarketCount = snapshot.ProviderMarkets.Count,
                enabledProviderMarketCount = snapshot.ProviderMarkets.Count(row => row.IsEnabled),
                providerBookmakerCount = snapshot.ProviderBookmakers.Count,
                enabledProviderBookmakerCount = snapshot.ProviderBookmakers.Count(row => row.IsEnabled),
                providerRefreshTargetCount = snapshot.ProviderRefreshTargets.Count,
                enabledProviderRefreshTargetCount = snapshot.ProviderRefreshTargets.Count(row => row.IsEnabled),
                snapshot.AppsettingsFallbackCount,
                staleOrMissingDomainConfigWarnings = warnings,
                lastSeededAtUtc = ResolveLastSeededAt(snapshot),
                countsByProvider = snapshot.ProviderCompetitions
                    .Select(row => row.ProviderKey)
                    .Concat(snapshot.ProviderMarkets.Select(row => row.ProviderKey))
                    .Concat(snapshot.ProviderBookmakers.Select(row => row.ProviderKey))
                    .Concat(snapshot.ProviderRefreshTargets.Select(row => row.ProviderKey))
                    .GroupBy(row => row, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { providerKey = group.Key, count = group.Count() })
                    .ToArray()
            });
        })
        .WithName("ProviderDomainConfigStatusDebug")
        .WithSummary("Read-only compact status for normalized provider/domain configuration tables.");

        endpoints.MapGet("/api/debug/provider-domain-config/competitions", async (
            bool? includeDisabled,
            IProviderDomainConfigService providerDomainConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await providerDomainConfigService.GetSnapshotAsync(cancellationToken);
            var rows = snapshot.ProviderCompetitions
                .Where(row => includeDisabled == true || row.IsEnabled)
                .OrderBy(row => row.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.CompetitionKey, StringComparer.OrdinalIgnoreCase)
                .Select(row => new
                {
                    row.ProviderKey,
                    row.Sport,
                    row.CompetitionKey,
                    row.DisplayName,
                    row.Country,
                    row.CompetitionType,
                    row.ProviderLeagueId,
                    row.ProviderSportKey,
                    row.IsEnabled,
                    row.SupportsResultsImport,
                    row.SupportsOddsImport,
                    row.SupportsSeasonMetadata,
                    row.IsFixedSeason,
                    row.FixedSeason,
                    row.Priority,
                    row.Notes,
                    row.UpdatedAtUtc
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                includeDisabled = includeDisabled == true,
                competitionCount = rows.Length,
                competitions = rows
            });
        })
        .WithName("ProviderDomainConfigCompetitionsDebug")
        .WithSummary("Read-only provider competition configuration rows.");

        endpoints.MapGet("/api/debug/provider-domain-config/markets", async (
            bool? includeDisabled,
            IProviderDomainConfigService providerDomainConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await providerDomainConfigService.GetSnapshotAsync(cancellationToken);
            var rows = snapshot.ProviderMarkets
                .Where(row => includeDisabled == true || row.IsEnabled)
                .OrderBy(row => row.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CompetitionKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ProviderCategory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.MarketKey, StringComparer.OrdinalIgnoreCase)
                .Select(row => new
                {
                    row.ProviderKey,
                    row.Sport,
                    row.CompetitionKey,
                    row.ProviderCategory,
                    row.MarketKey,
                    row.DisplayName,
                    row.InternalPropType,
                    row.IsEnabled,
                    row.IsDefault,
                    row.SupportsOddsImport,
                    row.SupportsLineShopping,
                    row.SupportsTopPicks,
                    row.Priority,
                    row.Notes,
                    row.UpdatedAtUtc
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                includeDisabled = includeDisabled == true,
                marketCount = rows.Length,
                markets = rows
            });
        })
        .WithName("ProviderDomainConfigMarketsDebug")
        .WithSummary("Read-only provider market key configuration rows.");

        endpoints.MapGet("/api/debug/provider-domain-config/bookmakers", async (
            bool? includeDisabled,
            IProviderDomainConfigService providerDomainConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await providerDomainConfigService.GetSnapshotAsync(cancellationToken);
            var rows = snapshot.ProviderBookmakers
                .Where(row => includeDisabled == true || row.IsEnabled)
                .OrderBy(row => row.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ProviderCategory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Region, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.BookmakerKey, StringComparer.OrdinalIgnoreCase)
                .Select(row => new
                {
                    row.ProviderKey,
                    row.BookmakerKey,
                    row.DisplayName,
                    row.ProviderCategory,
                    row.Region,
                    row.IsEnabled,
                    row.IsDefault,
                    row.IsExpandedCoverage,
                    row.SupportsLineShopping,
                    row.SupportsEntryBuilder,
                    row.Priority,
                    row.Notes,
                    row.UpdatedAtUtc
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                includeDisabled = includeDisabled == true,
                bookmakerCount = rows.Length,
                bookmakers = rows
            });
        })
        .WithName("ProviderDomainConfigBookmakersDebug")
        .WithSummary("Read-only provider bookmaker configuration rows.");

        endpoints.MapGet("/api/debug/provider-domain-config/refresh-targets", async (
            bool? includeDisabled,
            IProviderDomainConfigService providerDomainConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await providerDomainConfigService.GetSnapshotAsync(cancellationToken);
            var rows = snapshot.ProviderRefreshTargets
                .Where(row => includeDisabled == true || row.IsEnabled)
                .OrderBy(row => row.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.TargetKey, StringComparer.OrdinalIgnoreCase)
                .Select(row => new
                {
                    row.ProviderKey,
                    row.TargetKey,
                    row.Sport,
                    row.CompetitionKey,
                    row.CompetitionDisplayName,
                    row.ProviderCategory,
                    row.BookmakerPreset,
                    row.ProviderSportKey,
                    row.Regions,
                    row.MaxEvents,
                    row.EstimatedApiCalls,
                    row.SupportsResults,
                    row.SupportsHistory,
                    row.IsEnabled,
                    row.DisabledReason,
                    row.Notes,
                    row.Priority,
                    row.UpdatedAtUtc
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                includeDisabled = includeDisabled == true,
                targetCount = rows.Length,
                targets = rows
            });
        })
        .WithName("ProviderDomainConfigRefreshTargetsDebug")
        .WithSummary("Read-only scheduled provider refresh target configuration rows.");

        return endpoints;
    }

    private static string[] BuildWarnings(ProviderDomainConfigSnapshot snapshot)
    {
        var warnings = new List<string>();
        if (!snapshot.LoadSucceeded)
        {
            warnings.Add("Provider domain config DB read failed; appsettings fallback is active.");
        }

        if (snapshot.ProviderCompetitions.Count == 0)
        {
            warnings.Add("No provider competition rows exist.");
        }

        if (snapshot.ProviderMarkets.Count == 0)
        {
            warnings.Add("No provider market rows exist.");
        }

        if (snapshot.ProviderBookmakers.Count == 0)
        {
            warnings.Add("No provider bookmaker rows exist.");
        }

        if (snapshot.ProviderRefreshTargets.Count == 0)
        {
            warnings.Add("No provider refresh target rows exist.");
        }

        return warnings.ToArray();
    }

    private static DateTimeOffset? ResolveLastSeededAt(ProviderDomainConfigSnapshot snapshot)
    {
        var timestamps = snapshot.ProviderCompetitions.Select(row => row.UpdatedAtUtc)
            .Concat(snapshot.ProviderMarkets.Select(row => row.UpdatedAtUtc))
            .Concat(snapshot.ProviderBookmakers.Select(row => row.UpdatedAtUtc))
            .Concat(snapshot.ProviderRefreshTargets.Select(row => row.UpdatedAtUtc))
            .ToArray();

        return timestamps.Length == 0 ? null : timestamps.Max();
    }
}
