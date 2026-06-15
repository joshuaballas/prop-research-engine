using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Ingestion.Players;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed class PrizePicksIngestionService(
    ApplicationDbContext dbContext,
    IPrizePicksAdapter adapter,
    IPlayerResolutionService playerResolutionService,
    ILogger<PrizePicksIngestionService> logger) : IPrizePicksIngestionService
{
    private static readonly HashSet<string> EnabledLeagueConfigurations = new(StringComparer.OrdinalIgnoreCase)
    {
        "NFL",
        "NBA",
        "WNBA",
        "NHL",
        "MLB",
        "SOCCER",
        "UFC"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PrizePicksIngestionResult> IngestAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var fetched = await adapter.FetchActiveProjectionsAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;
        var payloadHash = ComputeSha256(fetched.RawJson);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var platform = await GetOrCreatePlatformAsync(observedAt, cancellationToken);

        var ingestionRun = new IngestionRun
        {
            Id = Guid.NewGuid(),
            PlatformId = platform.Id,
            Status = "running",
            StartedAt = observedAt,
            PropsSeen = fetched.Projections.Count,
            PayloadSizeBytes = Encoding.UTF8.GetByteCount(fetched.RawJson),
            PayloadHash = payloadHash,
            SchemaVersion = "prizepicks-projections-v1"
        };

        dbContext.IngestionRuns.Add(ingestionRun);

        var rawPayload = new RawIngestionPayload
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRun.Id,
            PlatformId = platform.Id,
            Endpoint = fetched.Endpoint,
            PayloadHash = payloadHash,
            Payload = JsonDocument.Parse(fetched.RawJson),
            CapturedAt = observedAt
        };

        dbContext.RawIngestionPayloads.Add(rawPayload);

        var sports = await dbContext.Sports.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var leagues = await dbContext.Leagues.ToDictionaryAsync(x => $"{x.SportId:N}:{x.Code}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var leagueConfigurations = await dbContext.LeagueConfigurations.ToDictionaryAsync(x => x.LeagueName, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var propTypes = await dbContext.PropTypes.ToDictionaryAsync(x => $"{x.SportId:N}:{x.StatKey}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var playerResolutionCache = await playerResolutionService.LoadCacheAsync(cancellationToken);
        var props = await dbContext.Props.ToDictionaryAsync(x => x.NormalizedKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var platformProps = await dbContext.PlatformProps
            .Where(x => x.PlatformId == platform.Id)
            .ToDictionaryAsync(x => x.PlatformPropId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var latestSnapshots = await dbContext.PropLineSnapshots
            .GroupBy(x => x.PlatformPropId)
            .Select(group => group.OrderByDescending(x => x.ObservedAt).First())
            .ToDictionaryAsync(x => x.PlatformPropId, cancellationToken);

        var playersCreated = 0;
        var playersUpdated = 0;
        var propsCreated = 0;
        var propsUpdated = 0;
        var snapshotsCreated = 0;

        foreach (var record in fetched.Projections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sportInfo = InferSport(record.League);
            var sport = GetOrCreateSport(sports, sportInfo);
            var league = GetOrCreateLeague(leagues, sport, record);
            EnsureLeagueConfiguration(leagueConfigurations, record.League);

            var propType = GetOrCreatePropType(propTypes, sport, record);
            var playerResult = GetOrCreatePlayer(playerResolutionCache, league, record, observedAt);
            playersCreated += playerResult.Created ? 1 : 0;
            playersUpdated += playerResult.Updated ? 1 : 0;

            var propResult = GetOrCreateProp(props, league, propType, playerResult.Player, record, observedAt);
            propsCreated += propResult.Created ? 1 : 0;
            propsUpdated += propResult.Updated ? 1 : 0;

            var platformPropResult = GetOrCreatePlatformProp(platformProps, platform, propResult.Prop, rawPayload, record, observedAt);

            if (ShouldCreateSnapshot(platformPropResult, latestSnapshots))
            {
                var snapshot = new PropLineSnapshot
                {
                    Id = Guid.NewGuid(),
                    PlatformPropId = platformPropResult.PlatformProp.Id,
                    Line = platformPropResult.Line,
                    OverAvailable = platformPropResult.OverAvailable,
                    UnderAvailable = platformPropResult.UnderAvailable,
                    Status = platformPropResult.Status,
                    ObservedAt = observedAt,
                    SourceHash = platformPropResult.SourceHash,
                    RawPayloadId = rawPayload.Id
                };

                dbContext.PropLineSnapshots.Add(snapshot);
                latestSnapshots[platformPropResult.PlatformProp.Id] = snapshot;
                snapshotsCreated++;
            }
        }

        ingestionRun.PropsChanged = propsCreated + propsUpdated;
        ingestionRun.Status = "succeeded";
        ingestionRun.CompletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        stopwatch.Stop();

        logger.LogInformation(
            "Persisted {ProjectionCount} PrizePicks projections in {ElapsedMilliseconds}ms. Players +{PlayersCreated}/~{PlayersUpdated}, props +{PropsCreated}/~{PropsUpdated}, snapshots +{SnapshotsCreated}.",
            fetched.Projections.Count,
            stopwatch.ElapsedMilliseconds,
            playersCreated,
            playersUpdated,
            propsCreated,
            propsUpdated,
            snapshotsCreated);

        return new PrizePicksIngestionResult(
            fetched.Projections.Count,
            playersCreated,
            playersUpdated,
            propsCreated,
            propsUpdated,
            snapshotsCreated,
            stopwatch.Elapsed);
    }

    private async Task<Platform> GetOrCreatePlatformAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var platform = await dbContext.Platforms.SingleOrDefaultAsync(x => x.Slug == "prizepicks", cancellationToken);
        if (platform is not null)
        {
            platform.Name = "PrizePicks";
            platform.IsActive = true;
            platform.UpdatedAt = observedAt;
            return platform;
        }

        platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "PrizePicks",
            Slug = "prizepicks",
            IsActive = true,
            CreatedAt = observedAt,
            UpdatedAt = observedAt
        };

        dbContext.Platforms.Add(platform);
        return platform;
    }

    private Sport GetOrCreateSport(Dictionary<string, Sport> sports, SportInfo sportInfo)
    {
        if (sports.TryGetValue(sportInfo.Code, out var sport))
        {
            if (sport.Name != sportInfo.Name)
            {
                sport.Name = sportInfo.Name;
            }

            return sport;
        }

        sport = new Sport
        {
            Id = Guid.NewGuid(),
            Name = sportInfo.Name,
            Code = sportInfo.Code
        };

        dbContext.Sports.Add(sport);
        sports[sport.Code] = sport;
        return sport;
    }

    private League GetOrCreateLeague(Dictionary<string, League> leagues, Sport sport, PrizePicksProjectionRecord record)
    {
        var leagueCode = CanonicalLeagueNormalizer.NormalizeCode(record.League ?? record.LeagueId ?? "UNKNOWN");
        var key = $"{sport.Id:N}:{leagueCode}";

        if (leagues.TryGetValue(key, out var league))
        {
            league.Name = record.League ?? leagueCode;
            league.Code = leagueCode;
            return league;
        }

        league = new League
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = record.League ?? leagueCode,
            Code = leagueCode
        };

        dbContext.Leagues.Add(league);
        leagues[key] = league;
        return league;
    }

    private void EnsureLeagueConfiguration(Dictionary<string, LeagueConfiguration> configurations, string? leagueName)
    {
        if (string.IsNullOrWhiteSpace(leagueName) || configurations.ContainsKey(leagueName))
        {
            return;
        }

        var isEnabled = EnabledLeagueConfigurations.Contains(leagueName);
        var configuration = new LeagueConfiguration
        {
            Id = Guid.NewGuid(),
            LeagueName = leagueName,
            IsEnabledForAnalysis = isEnabled,
            IsEnabledForOptimizer = isEnabled,
            IsEnabledForDisplay = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.LeagueConfigurations.Add(configuration);
        configurations[leagueName] = configuration;
    }

    private PropType GetOrCreatePropType(Dictionary<string, PropType> propTypes, Sport sport, PrizePicksProjectionRecord record)
    {
        var statKey = NormalizeCode(record.StatType);
        var key = $"{sport.Id:N}:{statKey}";

        if (propTypes.TryGetValue(key, out var propType))
        {
            propType.Name = record.StatDisplayName ?? record.StatType;
            return propType;
        }

        propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = record.StatDisplayName ?? record.StatType,
            StatKey = statKey,
            ValueType = "numeric"
        };

        dbContext.PropTypes.Add(propType);
        propTypes[key] = propType;
        return propType;
    }

    private PlayerUpsertResult GetOrCreatePlayer(PlayerResolutionCache cache, League league, PrizePicksProjectionRecord record, DateTimeOffset observedAt)
    {
        var result = playerResolutionService.ResolveOrCreate(
            cache,
            new PlayerResolutionRequest(
                "prizepicks",
                record.PlayerId,
                record.PlayerName,
                FirstName: null,
                LastName: null,
                record.Position,
                league,
                CreateExternalRefs(record.PlayerId),
                observedAt));

        return new PlayerUpsertResult(result.Player, result.Created, result.Updated);
    }

    private PropUpsertResult GetOrCreateProp(
        Dictionary<string, Prop> props,
        League league,
        PropType propType,
        Player player,
        PrizePicksProjectionRecord record,
        DateTimeOffset observedAt)
    {
        var normalizedKey = BuildPropNormalizedKey(record, league, propType, player);
        var startTime = record.StartTime?.ToUniversalTime();

        if (props.TryGetValue(normalizedKey, out var prop))
        {
            var updated = false;
            if (prop.Status != (record.Status ?? "active"))
            {
                prop.Status = record.Status ?? "active";
                updated = true;
            }

            if (startTime.HasValue && prop.StartsAt != startTime.Value)
            {
                prop.StartsAt = startTime.Value;
                updated = true;
            }

            if (updated)
            {
                prop.UpdatedAt = observedAt;
            }

            return new PropUpsertResult(prop, Created: false, Updated: updated);
        }

        prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            PropTypeId = propType.Id,
            LeagueId = league.Id,
            NormalizedKey = normalizedKey,
            StartsAt = startTime ?? observedAt,
            Status = record.Status ?? "active",
            CreatedAt = observedAt,
            UpdatedAt = observedAt
        };

        dbContext.Props.Add(prop);
        props[normalizedKey] = prop;
        return new PropUpsertResult(prop, Created: true, Updated: false);
    }

    private PlatformPropUpsertResult GetOrCreatePlatformProp(
        Dictionary<string, PlatformProp> platformProps,
        Platform platform,
        Prop prop,
        RawIngestionPayload rawPayload,
        PrizePicksProjectionRecord record,
        DateTimeOffset observedAt)
    {
        var line = record.Line ?? 0m;
        var status = record.Status ?? "active";
        const bool overAvailable = true;
        const bool underAvailable = true;
        var sourceHash = ComputeSha256($"{record.ProjectionId}|{line}|{status}|{overAvailable}|{underAvailable}|{record.OddsType}");

        if (platformProps.TryGetValue(record.ProjectionId, out var platformProp))
        {
            var previousLine = platformProp.CurrentLine;
            var previousStatus = platformProp.Status;
            var previousOverAvailable = platformProp.OverAvailable;
            var previousUnderAvailable = platformProp.UnderAvailable;

            platformProp.PropId = prop.Id;
            platformProp.ExternalStableKey = record.StableKey;
            platformProp.Label = $"{record.PlayerName} {record.StatType}";
            platformProp.CurrentLine = line;
            platformProp.OverAvailable = overAvailable;
            platformProp.UnderAvailable = underAvailable;
            platformProp.PayoutType = record.OddsType ?? "standard";
            platformProp.Status = status;
            platformProp.ObservedAt = observedAt;
            platformProp.RawPayloadId = rawPayload.Id;
            platformProp.Metadata = CreatePlatformPropMetadata(record);

            return new PlatformPropUpsertResult(
                platformProp,
                Created: false,
                PreviousLine: previousLine,
                PreviousStatus: previousStatus,
                PreviousOverAvailable: previousOverAvailable,
                PreviousUnderAvailable: previousUnderAvailable,
                Line: line,
                Status: status,
                OverAvailable: overAvailable,
                UnderAvailable: underAvailable,
                SourceHash: sourceHash);
        }

        platformProp = new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = record.ProjectionId,
            ExternalStableKey = record.StableKey,
            Label = $"{record.PlayerName} {record.StatType}",
            CurrentLine = line,
            OverAvailable = overAvailable,
            UnderAvailable = underAvailable,
            PayoutType = record.OddsType ?? "standard",
            Status = status,
            ObservedAt = observedAt,
            RawPayloadId = rawPayload.Id,
            Metadata = CreatePlatformPropMetadata(record)
        };

        dbContext.PlatformProps.Add(platformProp);
        platformProps[record.ProjectionId] = platformProp;

        return new PlatformPropUpsertResult(
            platformProp,
            Created: true,
            PreviousLine: null,
            PreviousStatus: null,
            PreviousOverAvailable: null,
            PreviousUnderAvailable: null,
            Line: line,
            Status: status,
            OverAvailable: overAvailable,
            UnderAvailable: underAvailable,
            SourceHash: sourceHash);
    }

    private static bool ShouldCreateSnapshot(
        PlatformPropUpsertResult platformPropResult,
        Dictionary<Guid, PropLineSnapshot> latestSnapshots)
    {
        if (platformPropResult.Created)
        {
            return true;
        }

        if (!latestSnapshots.TryGetValue(platformPropResult.PlatformProp.Id, out var latestSnapshot))
        {
            return true;
        }

        return latestSnapshot.Line != platformPropResult.Line
            || latestSnapshot.Status != platformPropResult.Status
            || latestSnapshot.OverAvailable != platformPropResult.OverAvailable
            || latestSnapshot.UnderAvailable != platformPropResult.UnderAvailable
            || platformPropResult.PreviousLine != platformPropResult.Line
            || platformPropResult.PreviousStatus != platformPropResult.Status
            || platformPropResult.PreviousOverAvailable != platformPropResult.OverAvailable
            || platformPropResult.PreviousUnderAvailable != platformPropResult.UnderAvailable;
    }

    private static string BuildPropNormalizedKey(PrizePicksProjectionRecord record, League league, PropType propType, Player player)
    {
        var stablePart = !string.IsNullOrWhiteSpace(record.StableKey)
            ? record.StableKey
            : $"{record.PlayerId}:{record.StatType}:{record.Description}:{record.StartTime:O}";

        return $"prizepicks:{league.Code}:{propType.StatKey}:{player.Id:N}:{NormalizeCode(stablePart)}";
    }

    private static JsonDocument? CreateExternalRefs(string? prizePicksPlayerId)
    {
        return string.IsNullOrWhiteSpace(prizePicksPlayerId)
            ? null
            : JsonSerializer.SerializeToDocument(new { prizepicks = new { player_id = prizePicksPlayerId } }, JsonOptions);
    }

    private static JsonDocument CreatePlatformPropMetadata(PrizePicksProjectionRecord record)
    {
        return JsonSerializer.SerializeToDocument(new
        {
            record.PromoLine,
            record.IsPromo,
            record.IsLive,
            record.Description,
            record.Team,
            record.TeamName,
            record.Position,
            record.LeagueId,
            record.StartTime,
            record.UpdatedAt
        }, JsonOptions);
    }

    private static SportInfo InferSport(string? league)
    {
        var value = league?.ToUpperInvariant() ?? "UNKNOWN";

        if (value.Contains("WORLD CUP", StringComparison.Ordinal) || value.Contains("SOCCER", StringComparison.Ordinal))
        {
            return new SportInfo("SOCCER", "Soccer");
        }

        if (value.StartsWith("NBA", StringComparison.Ordinal) || value.StartsWith("WNBA", StringComparison.Ordinal))
        {
            return new SportInfo("BASKETBALL", "Basketball");
        }

        if (value.Contains("MLB", StringComparison.Ordinal) || value is "KBO" or "NPB")
        {
            return new SportInfo("BASEBALL", "Baseball");
        }

        if (value.Contains("NFL", StringComparison.Ordinal) || value is "CFL")
        {
            return new SportInfo("FOOTBALL", "Football");
        }

        if (value.StartsWith("NHL", StringComparison.Ordinal))
        {
            return new SportInfo("HOCKEY", "Hockey");
        }

        if (value is "LOL" or "COD" or "CS2" or "VAL" or "R6")
        {
            return new SportInfo("ESPORTS", "Esports");
        }

        return value switch
        {
            "UFC" => new SportInfo("MMA", "MMA"),
            "TENNIS" => new SportInfo("TENNIS", "Tennis"),
            "AFL" => new SportInfo("AFL", "AFL"),
            "F1" or "NASCAR" => new SportInfo("RACING", "Racing"),
            "BAD" => new SportInfo("BADMINTON", "Badminton"),
            "CRICKET" => new SportInfo("CRICKET", "Cricket"),
            "BOWLING" => new SportInfo("BOWLING", "Bowling"),
            _ => new SportInfo("UNKNOWN", "Unknown")
        };
    }

    private static string NormalizeCode(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record SportInfo(string Code, string Name);
    private sealed record PlayerUpsertResult(Player Player, bool Created, bool Updated);
    private sealed record PropUpsertResult(Prop Prop, bool Created, bool Updated);

    private sealed record PlatformPropUpsertResult(
        PlatformProp PlatformProp,
        bool Created,
        decimal? PreviousLine,
        string? PreviousStatus,
        bool? PreviousOverAvailable,
        bool? PreviousUnderAvailable,
        decimal Line,
        string Status,
        bool OverAvailable,
        bool UnderAvailable,
        string SourceHash);
}
