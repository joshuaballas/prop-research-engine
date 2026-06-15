using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Ingestion.Players;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsApiMlbEventIngestionService(
    ApplicationDbContext dbContext,
    ITheOddsApiMlbPlayerPropsAdapter adapter,
    IPlayerResolutionService playerResolutionService,
    ILogger<TheOddsApiMlbEventIngestionService> logger) : ITheOddsApiMlbEventIngestionService
{
    private const string DataProvider = "The Odds API";
    private const string LeagueCode = "MLB";
    private const string SportCode = "BASEBALL";
    private const string SportName = "Baseball";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TheOddsApiMlbEventIngestionResult> IngestEventAsync(
        TheOddsApiMlbEventIngestionRequest request,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            return Failed(request.EventId, dryRun, "eventId is required.");
        }

        var fetched = await adapter.FetchSpikeAsync(
            request.EventId,
            request.Markets,
            request.Bookmakers,
            regions: null,
            oddsFormat: null,
            cancellationToken);

        var eventRecord = fetched.Events.FirstOrDefault();
        var warnings = new List<string>();
        var errors = new List<string>();
        var records = fetched.Records
            .Where(record => !string.IsNullOrWhiteSpace(record.BookmakerKey)
                && !string.IsNullOrWhiteSpace(record.PlayerName)
                && !string.IsNullOrWhiteSpace(record.MarketKey)
                && record.Line.HasValue)
            .ToList();
        var skippedRecords = fetched.Records.Count - records.Count;
        if (skippedRecords > 0)
        {
            warnings.Add($"Skipped {skippedRecords} records missing bookmaker, player, market, or line.");
        }

        var marketGroups = records
            .GroupBy(record => new MarketGroupKey(
                record.BookmakerKey!,
                record.BookmakerTitle,
                record.EventId ?? request.EventId,
                record.MarketKey!,
                record.PlayerName!,
                record.Line!.Value),
                MarketGroupKeyComparer.Instance)
            .Select(group => new MarketGroup(group.Key, group.ToList()))
            .ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var observedAt = DateTimeOffset.UtcNow;
        var sports = await dbContext.Sports.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var sport = GetOrCreateSport(sports);
        var leagues = await dbContext.Leagues.ToDictionaryAsync(x => $"{x.SportId:N}:{x.Code}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var league = GetOrCreateLeague(leagues, sport);
        var propTypes = await dbContext.PropTypes.ToDictionaryAsync(x => $"{x.SportId:N}:{x.StatKey}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var props = await dbContext.Props.ToDictionaryAsync(x => x.NormalizedKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var playerResolutionCache = await playerResolutionService.LoadCacheAsync(cancellationToken);
        var platformCache = await dbContext.Platforms.ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var rawPayloadCache = new Dictionary<Guid, RawIngestionPayload>();
        var platformProps = await dbContext.PlatformProps
            .Include(x => x.Platform)
            .Where(x => x.Platform != null)
            .ToDictionaryAsync(x => $"{x.Platform!.Slug}:{x.PlatformPropId}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var latestSnapshots = await dbContext.PropLineSnapshots
            .Where(x => x.PlatformProp != null)
            .GroupBy(x => x.PlatformPropId)
            .Select(group => group.OrderByDescending(x => x.ObservedAt).First())
            .ToDictionaryAsync(x => x.PlatformPropId, cancellationToken);

        var propsUpserted = 0;
        var platformPropsUpserted = 0;
        var snapshotsCreated = 0;
        var snapshotsUnchanged = 0;
        var playersResolved = 0;
        var playersCreated = 0;
        var propTypesResolved = 0;

        foreach (var group in marketGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sample = group.Records[0];
            var platform = GetOrCreatePlatform(platformCache, group.Key.BookmakerKey, group.Key.BookmakerTitle, observedAt);
            var rawPayload = GetOrCreateRawPayload(rawPayloadCache, platform, fetched, observedAt);
            var propType = GetOrCreatePropType(propTypes, sport, sample.NormalizedPropTypeCandidate);
            propTypesResolved++;

            var playerResult = playerResolutionService.ResolveOrCreate(
                playerResolutionCache,
                new PlayerResolutionRequest(
                    "theodds",
                    ProviderPlayerId: null,
                    group.Key.PlayerName,
                    FirstName: null,
                    LastName: null,
                    Position: null,
                    league,
                    LegacyExternalRefs: null,
                    observedAt));
            playersResolved++;
            playersCreated += playerResult.Created ? 1 : 0;

            var propResult = GetOrCreateProp(props, league, propType, playerResult.Player, group, eventRecord, observedAt);
            propsUpserted += propResult.Created || propResult.Updated ? 1 : 0;

            var platformPropResult = GetOrCreatePlatformProp(
                platformProps,
                platform,
                propResult.Prop,
                rawPayload,
                group,
                eventRecord,
                observedAt);
            platformPropsUpserted += platformPropResult.Created || platformPropResult.Updated ? 1 : 0;

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
            else
            {
                snapshotsUnchanged++;
            }
        }

        if (!dryRun)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
            warnings.Add("Dry run completed; no database changes were committed.");
        }

        logger.LogInformation(
            "The Odds API MLB event ingestion spike completed. EventId: {EventId}. DryRun: {DryRun}. Groups: {GroupCount}. SnapshotsCreated: {SnapshotsCreated}.",
            request.EventId,
            dryRun,
            marketGroups.Count,
            snapshotsCreated);

        return new TheOddsApiMlbEventIngestionResult(
            Success: errors.Count == 0,
            dryRun,
            request.EventId,
            eventRecord?.EventStartTime,
            eventRecord?.HomeTeam,
            eventRecord?.AwayTeam,
            BookmakersParsed: records.Select(record => record.BookmakerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            MarketsParsed: records.Select(record => record.MarketKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            NormalizedRecords: fetched.Records.Count,
            propsUpserted,
            platformPropsUpserted,
            snapshotsCreated,
            snapshotsUnchanged,
            playersResolved,
            playersCreated,
            propTypesResolved,
            warnings,
            errors,
            fetched.CreditUsage);
    }

    private static TheOddsApiMlbEventIngestionResult Failed(string eventId, bool dryRun, string error)
    {
        return new TheOddsApiMlbEventIngestionResult(
            false,
            dryRun,
            eventId,
            EventStartTime: null,
            HomeTeam: null,
            AwayTeam: null,
            BookmakersParsed: 0,
            MarketsParsed: 0,
            NormalizedRecords: 0,
            PropsUpserted: 0,
            PlatformPropsUpserted: 0,
            SnapshotsCreated: 0,
            SnapshotsUnchanged: 0,
            PlayersResolved: 0,
            PlayersCreated: 0,
            PropTypesResolved: 0,
            Warnings: [],
            Errors: [error],
            CreditUsage: new TheOddsApiCreditUsage(null, null, null));
    }

    private Platform GetOrCreatePlatform(
        Dictionary<string, Platform> platforms,
        string bookmakerKey,
        string? bookmakerTitle,
        DateTimeOffset observedAt)
    {
        var slug = $"theodds-{NormalizeCode(bookmakerKey).ToLowerInvariant()}";
        var name = string.IsNullOrWhiteSpace(bookmakerTitle)
            ? bookmakerKey
            : bookmakerTitle;

        if (platforms.TryGetValue(slug, out var platform))
        {
            platform.Name = name;
            platform.IsActive = true;
            platform.UpdatedAt = observedAt;
            return platform;
        }

        platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            IsActive = true,
            CreatedAt = observedAt,
            UpdatedAt = observedAt
        };

        dbContext.Platforms.Add(platform);
        platforms[slug] = platform;
        return platform;
    }

    private RawIngestionPayload GetOrCreateRawPayload(
        Dictionary<Guid, RawIngestionPayload> rawPayloads,
        Platform platform,
        TheOddsApiPlayerPropsSpikeResult fetched,
        DateTimeOffset observedAt)
    {
        if (rawPayloads.TryGetValue(platform.Id, out var payload))
        {
            return payload;
        }

        var rawJson = string.IsNullOrWhiteSpace(fetched.RawJson) ? "{}" : fetched.RawJson;
        var payloadHash = ComputeSha256(rawJson);
        var ingestionRun = new IngestionRun
        {
            Id = Guid.NewGuid(),
            PlatformId = platform.Id,
            Platform = platform,
            Status = "succeeded",
            StartedAt = observedAt,
            CompletedAt = observedAt,
            PropsSeen = fetched.Records.Count,
            PropsChanged = 0,
            PayloadSizeBytes = Encoding.UTF8.GetByteCount(rawJson),
            PayloadHash = payloadHash,
            SchemaVersion = "the-odds-api-mlb-event-odds-v1"
        };
        payload = new RawIngestionPayload
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRun.Id,
            IngestionRun = ingestionRun,
            PlatformId = platform.Id,
            Platform = platform,
            Endpoint = fetched.RequestUrl,
            PayloadHash = payloadHash,
            Payload = JsonDocument.Parse(rawJson),
            CapturedAt = observedAt
        };

        dbContext.IngestionRuns.Add(ingestionRun);
        dbContext.RawIngestionPayloads.Add(payload);
        rawPayloads[platform.Id] = payload;
        return payload;
    }

    private Sport GetOrCreateSport(Dictionary<string, Sport> sports)
    {
        if (sports.TryGetValue(SportCode, out var sport))
        {
            sport.Name = SportName;
            return sport;
        }

        sport = new Sport
        {
            Id = Guid.NewGuid(),
            Code = SportCode,
            Name = SportName
        };

        dbContext.Sports.Add(sport);
        sports[SportCode] = sport;
        return sport;
    }

    private League GetOrCreateLeague(Dictionary<string, League> leagues, Sport sport)
    {
        var leagueCode = CanonicalLeagueNormalizer.NormalizeCode(LeagueCode);
        var key = $"{sport.Id:N}:{leagueCode}";

        if (leagues.TryGetValue(key, out var league))
        {
            league.Name = LeagueCode;
            league.Code = leagueCode;
            return league;
        }

        league = new League
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Sport = sport,
            Name = LeagueCode,
            Code = leagueCode
        };

        dbContext.Leagues.Add(league);
        leagues[key] = league;
        return league;
    }

    private PropType GetOrCreatePropType(Dictionary<string, PropType> propTypes, Sport sport, string propTypeName)
    {
        var statKey = NormalizeCode(propTypeName);
        var key = $"{sport.Id:N}:{statKey}";

        if (propTypes.TryGetValue(key, out var propType))
        {
            propType.Name = propTypeName;
            return propType;
        }

        propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Sport = sport,
            Name = propTypeName,
            StatKey = statKey,
            ValueType = "numeric"
        };

        dbContext.PropTypes.Add(propType);
        propTypes[key] = propType;
        return propType;
    }

    private PropUpsertResult GetOrCreateProp(
        Dictionary<string, Prop> props,
        League league,
        PropType propType,
        Player player,
        MarketGroup group,
        TheOddsApiEventRecord? eventRecord,
        DateTimeOffset observedAt)
    {
        var normalizedKey = BuildPropNormalizedKey(league, propType, player, group);
        var startsAt = eventRecord?.EventStartTime?.ToUniversalTime() ?? observedAt;

        if (props.TryGetValue(normalizedKey, out var prop))
        {
            var updated = false;
            if (prop.Status != "active")
            {
                prop.Status = "active";
                updated = true;
            }

            if (prop.StartsAt != startsAt)
            {
                prop.StartsAt = startsAt;
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
            Player = player,
            PropTypeId = propType.Id,
            PropType = propType,
            LeagueId = league.Id,
            League = league,
            NormalizedKey = normalizedKey,
            StartsAt = startsAt,
            Status = "active",
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
        MarketGroup group,
        TheOddsApiEventRecord? eventRecord,
        DateTimeOffset observedAt)
    {
        var status = "active";
        var over = group.Records.FirstOrDefault(record => record.Side == "Over");
        var under = group.Records.FirstOrDefault(record => record.Side == "Under");
        var overAvailable = over is not null;
        var underAvailable = under is not null;
        var platformPropId = BuildPlatformPropIdCandidate(group.Key);
        var sourceHash = BuildSourceHash(group, status, overAvailable, underAvailable);
        var platformPropKey = $"{platform.Slug}:{platformPropId}";
        var line = group.Key.Line;

        if (platformProps.TryGetValue(platformPropKey, out var platformProp))
        {
            var previousLine = platformProp.CurrentLine;
            var previousStatus = platformProp.Status;
            var previousOverAvailable = platformProp.OverAvailable;
            var previousUnderAvailable = platformProp.UnderAvailable;

            var updated = previousLine != line
                || previousStatus != status
                || previousOverAvailable != overAvailable
                || previousUnderAvailable != underAvailable
                || platformProp.PropId != prop.Id;

            platformProp.PropId = prop.Id;
            platformProp.Prop = prop;
            platformProp.ExternalStableKey = group.Key.EventId;
            platformProp.Label = $"{group.Key.PlayerName} {group.Records[0].NormalizedPropTypeCandidate}";
            platformProp.CurrentLine = line;
            platformProp.OverAvailable = overAvailable;
            platformProp.UnderAvailable = underAvailable;
            platformProp.PayoutType = "standard";
            platformProp.Status = status;
            platformProp.ObservedAt = observedAt;
            platformProp.RawPayloadId = rawPayload.Id;
            platformProp.Metadata = CreatePlatformPropMetadata(group, eventRecord);

            return new PlatformPropUpsertResult(
                platformProp,
                Created: false,
                Updated: updated,
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
            PlatformId = platform.Id,
            Platform = platform,
            PropId = prop.Id,
            Prop = prop,
            PlatformPropId = platformPropId,
            ExternalStableKey = group.Key.EventId,
            Label = $"{group.Key.PlayerName} {group.Records[0].NormalizedPropTypeCandidate}",
            CurrentLine = line,
            OverAvailable = overAvailable,
            UnderAvailable = underAvailable,
            PayoutType = "standard",
            Status = status,
            ObservedAt = observedAt,
            RawPayloadId = rawPayload.Id,
            Metadata = CreatePlatformPropMetadata(group, eventRecord)
        };

        dbContext.PlatformProps.Add(platformProp);
        platformProps[platformPropKey] = platformProp;

        return new PlatformPropUpsertResult(
            platformProp,
            Created: true,
            Updated: true,
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
            || latestSnapshot.SourceHash != platformPropResult.SourceHash
            || platformPropResult.PreviousLine != platformPropResult.Line
            || platformPropResult.PreviousStatus != platformPropResult.Status
            || platformPropResult.PreviousOverAvailable != platformPropResult.OverAvailable
            || platformPropResult.PreviousUnderAvailable != platformPropResult.UnderAvailable;
    }

    private static JsonDocument CreatePlatformPropMetadata(MarketGroup group, TheOddsApiEventRecord? eventRecord)
    {
        var over = group.Records.FirstOrDefault(record => record.Side == "Over");
        var under = group.Records.FirstOrDefault(record => record.Side == "Under");

        return JsonSerializer.SerializeToDocument(new
        {
            dataProvider = DataProvider,
            marketClassification = "Standard",
            bookmaker = new
            {
                key = group.Key.BookmakerKey,
                title = group.Key.BookmakerTitle
            },
            eventInfo = new
            {
                eventId = group.Key.EventId,
                eventRecord?.EventStartTime,
                eventRecord?.HomeTeam,
                eventRecord?.AwayTeam
            },
            market = new
            {
                key = group.Key.MarketKey,
                lastUpdate = group.Records
                    .Select(record => record.MarketLastUpdate)
                    .Where(value => value.HasValue)
                    .Max()
            },
            options = new
            {
                higher = ToOptionMetadata(over),
                lower = ToOptionMetadata(under)
            },
            rawOutcomes = group.Records.Select(record => new
            {
                record.OutcomeName,
                record.PlayerName,
                record.Side,
                record.Line,
                record.PriceAmerican,
                record.PriceDecimal,
                record.MarketLastUpdate
            })
        }, JsonOptions);
    }

    private static object? ToOptionMetadata(TheOddsApiPlayerPropSpikeRecord? record)
    {
        return record is null
            ? null
            : new
            {
                choice = record.Side,
                status = "active",
                americanPrice = record.PriceAmerican,
                decimalPrice = record.PriceDecimal,
                point = record.Line,
                selectionHeader = record.PlayerName,
                selectionSubheader = record.NormalizedPropTypeCandidate,
                updatedAt = record.MarketLastUpdate
            };
    }

    private static string BuildPropNormalizedKey(
        League league,
        PropType propType,
        Player player,
        MarketGroup group)
    {
        return string.Join(
            ':',
            "theodds",
            league.Code,
            propType.StatKey,
            player.Id.ToString("N"),
            NormalizeCode(group.Key.EventId),
            NormalizeCode(group.Key.MarketKey),
            group.Key.Line.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private static string BuildPlatformPropIdCandidate(MarketGroupKey key)
    {
        var value = string.Join(
            ':',
            "theodds",
            NormalizeCode(key.BookmakerKey).ToLowerInvariant(),
            NormalizeCode(key.EventId).ToLowerInvariant(),
            NormalizeCode(key.MarketKey).ToLowerInvariant(),
            NormalizeCode(key.PlayerName).ToLowerInvariant(),
            key.Line.ToString("0.####", CultureInfo.InvariantCulture));

        if (value.Length <= 190)
        {
            return value;
        }

        var hash = ComputeSha256(value)[..16];
        return string.Concat(value.AsSpan(0, 173), ":", hash);
    }

    private static string BuildSourceHash(
        MarketGroup group,
        string status,
        bool overAvailable,
        bool underAvailable)
    {
        var priceParts = group.Records
            .OrderBy(record => record.Side)
            .Select(record => $"{record.Side}:{record.PriceAmerican}:{record.PriceDecimal}:{record.MarketLastUpdate:O}");
        var value = string.Join(
            '|',
            group.Key.BookmakerKey,
            group.Key.EventId,
            group.Key.MarketKey,
            group.Key.PlayerName,
            group.Key.Line,
            status,
            overAvailable,
            underAvailable,
            string.Join('|', priceParts));

        return ComputeSha256(value);
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

    private sealed record MarketGroup(MarketGroupKey Key, IReadOnlyList<TheOddsApiPlayerPropSpikeRecord> Records);

    private sealed record MarketGroupKey(
        string BookmakerKey,
        string? BookmakerTitle,
        string EventId,
        string MarketKey,
        string PlayerName,
        decimal Line);

    private sealed class MarketGroupKeyComparer : IEqualityComparer<MarketGroupKey>
    {
        public static readonly MarketGroupKeyComparer Instance = new();

        public bool Equals(MarketGroupKey? x, MarketGroupKey? y)
        {
            return x is not null
                && y is not null
                && string.Equals(x.BookmakerKey, y.BookmakerKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EventId, y.EventId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.MarketKey, y.MarketKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.PlayerName, y.PlayerName, StringComparison.OrdinalIgnoreCase)
                && x.Line == y.Line;
        }

        public int GetHashCode(MarketGroupKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BookmakerKey),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EventId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MarketKey),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PlayerName),
                obj.Line);
        }
    }

    private sealed record PropUpsertResult(Prop Prop, bool Created, bool Updated);

    private sealed record PlatformPropUpsertResult(
        PlatformProp PlatformProp,
        bool Created,
        bool Updated,
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
