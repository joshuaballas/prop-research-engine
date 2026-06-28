using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Models;
using System.Security.Cryptography;
using System.Text;

namespace SportsBetting.Api.Data;

public sealed record RecommendationLedgerCandidate(
    DateTimeOffset CapturedAtUtc,
    Guid? RefreshRunId,
    Guid? PredictionRunId,
    string Sport,
    DateOnly SlateDate,
    DateOnly ExpectedResultDate,
    string? ResultDateSource,
    string? EventId,
    DateTimeOffset? EventStartTimeUtc,
    Guid? CanonicalPlayerId,
    string PlayerName,
    Guid? PropTypeId,
    string PropType,
    string Side,
    decimal Line,
    string Provider,
    string? Bookmaker,
    string ProviderCategory,
    string EntryProvider,
    string EntryCompatibilityGroup,
    decimal? HitChance,
    decimal? ImpliedProbability,
    decimal? ProbabilityEdge,
    decimal? EstimatedEv,
    string TrustLevel,
    string? SampleWarning,
    string? Warning,
    int? HistoricalSampleSize,
    string StrategyClassification,
    string StrategyTier,
    string? ModelVersion,
    string? RecommendationContractVersion,
    string? ProbabilityModelVersion,
    string? ModelEra,
    decimal RankingScore,
    bool IsCandidate,
    bool IsTopPick,
    bool IsWatchlist,
    bool IsActionableForUser,
    string LifecycleStatus);

public sealed record RecommendationLedgerUpsertResult(
    int CandidateRowsFound,
    int RowsEligibleForLedger,
    int RowsAlreadyExist,
    int RowsCreated,
    int RowsUpdated,
    int SkippedMissingPlayerId,
    int SkippedMissingExpectedResultDate,
    int SkippedMissingPropType,
    int SkippedAmbiguousPlayer,
    IReadOnlyDictionary<DateOnly, RecommendationLedgerDateBreakdown> DateBreakdown);

public sealed record RecommendationLedgerDateBreakdown(
    DateOnly Date,
    int CandidateRowsFound,
    int RowsEligibleForLedger,
    int RowsAlreadyExist,
    int RowsCreated,
    int RowsUpdated,
    int Skipped);

public static class RecommendationLedgerWriter
{
    public static async Task<RecommendationLedgerUpsertResult> UpsertAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<RecommendationLedgerCandidate> candidates,
        bool execute,
        CancellationToken cancellationToken)
    {
        var dateBreakdown = new Dictionary<DateOnly, MutableRecommendationLedgerDateBreakdown>();
        var eligible = new List<(RecommendationLedgerCandidate Candidate, string Key)>();
        var skippedMissingPlayerId = 0;
        var skippedMissingExpectedResultDate = 0;
        var skippedMissingPropType = 0;
        var skippedAmbiguousPlayer = 0;

        foreach (var candidate in candidates)
        {
            var breakdown = GetBreakdown(dateBreakdown, candidate.ExpectedResultDate);
            breakdown.CandidateRowsFound++;

            if (candidate.ExpectedResultDate == default)
            {
                skippedMissingExpectedResultDate++;
                breakdown.Skipped++;
                continue;
            }

            if (!candidate.CanonicalPlayerId.HasValue)
            {
                skippedMissingPlayerId++;
                breakdown.Skipped++;
                continue;
            }

            if (candidate.PropTypeId is null || string.IsNullOrWhiteSpace(candidate.PropType))
            {
                skippedMissingPropType++;
                breakdown.Skipped++;
                continue;
            }

            var key = BuildIdempotencyKey(candidate);
            eligible.Add((candidate, key));
            breakdown.RowsEligibleForLedger++;
        }

        var distinctEligible = eligible
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.Candidate.CapturedAtUtc).First())
            .ToArray();
        var keys = distinctEligible.Select(row => row.Key).ToArray();
        var existingByKey = keys.Length == 0
            ? new Dictionary<string, RecommendationSnapshot>(StringComparer.OrdinalIgnoreCase)
            : await dbContext.RecommendationSnapshots
                .Where(row => keys.Contains(row.IdempotencyKey))
                .ToDictionaryAsync(row => row.IdempotencyKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var rowsAlreadyExist = 0;
        var rowsCreated = 0;
        var rowsUpdated = 0;

        foreach (var (candidate, key) in distinctEligible)
        {
            var breakdown = GetBreakdown(dateBreakdown, candidate.ExpectedResultDate);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                rowsAlreadyExist++;
                breakdown.RowsAlreadyExist++;
                if (execute)
                {
                    UpdateExisting(existing, candidate);
                    rowsUpdated++;
                    breakdown.RowsUpdated++;
                }
                continue;
            }

            if (execute)
            {
                dbContext.RecommendationSnapshots.Add(CreateNew(candidate, key));
            }
            rowsCreated++;
            breakdown.RowsCreated++;
        }

        if (execute)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new RecommendationLedgerUpsertResult(
            CandidateRowsFound: candidates.Count,
            RowsEligibleForLedger: distinctEligible.Length,
            RowsAlreadyExist: rowsAlreadyExist,
            RowsCreated: rowsCreated,
            RowsUpdated: rowsUpdated,
            SkippedMissingPlayerId: skippedMissingPlayerId,
            SkippedMissingExpectedResultDate: skippedMissingExpectedResultDate,
            SkippedMissingPropType: skippedMissingPropType,
            SkippedAmbiguousPlayer: skippedAmbiguousPlayer,
            DateBreakdown: dateBreakdown
                .OrderBy(row => row.Key)
                .ToDictionary(
                    row => row.Key,
                    row => row.Value.ToImmutable()));
    }

    public static string BuildIdempotencyKey(RecommendationLedgerCandidate candidate)
    {
        var canonical = string.Join("|", new[]
        {
            candidate.Sport.Trim().ToUpperInvariant(),
            candidate.ExpectedResultDate.ToString("yyyy-MM-dd"),
            candidate.EventId?.Trim().ToLowerInvariant() ?? "no-event",
            candidate.CanonicalPlayerId?.ToString("D") ?? "no-player",
            NormalizeKey(candidate.PropType),
            NormalizeKey(candidate.Side),
            candidate.Line.ToString("0.####"),
            NormalizeKey(candidate.Provider),
            NormalizeKey(candidate.Bookmaker),
            NormalizeKey(candidate.StrategyClassification),
            NormalizeKey(candidate.ModelEra),
            candidate.IsCandidate ? "candidate" : "non-candidate"
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static RecommendationSnapshot CreateNew(RecommendationLedgerCandidate candidate, string key)
    {
        var now = candidate.CapturedAtUtc;
        var row = new RecommendationSnapshot
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = key,
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now,
            SeenCount = 1,
            GradeStatus = "Pending"
        };
        ApplyLatest(row, candidate);
        return row;
    }

    private static void UpdateExisting(RecommendationSnapshot row, RecommendationLedgerCandidate candidate)
    {
        row.LastSeenAtUtc = candidate.CapturedAtUtc > row.LastSeenAtUtc ? candidate.CapturedAtUtc : row.LastSeenAtUtc;
        row.SeenCount += 1;
        ApplyLatest(row, candidate);
    }

    private static void ApplyLatest(RecommendationSnapshot row, RecommendationLedgerCandidate candidate)
    {
        row.CapturedAtUtc = candidate.CapturedAtUtc;
        row.RefreshRunId = candidate.RefreshRunId;
        row.PredictionRunId = candidate.PredictionRunId;
        row.Sport = candidate.Sport;
        row.SlateDate = candidate.SlateDate;
        row.ExpectedResultDate = candidate.ExpectedResultDate;
        row.ResultDateSource = candidate.ResultDateSource;
        row.EventId = candidate.EventId;
        row.EventStartTimeUtc = candidate.EventStartTimeUtc;
        row.CanonicalPlayerId = candidate.CanonicalPlayerId;
        row.PlayerName = candidate.PlayerName;
        row.NormalizedPlayerName = NormalizeKey(candidate.PlayerName);
        row.PropTypeId = candidate.PropTypeId;
        row.PropType = candidate.PropType;
        row.NormalizedPropType = NormalizeKey(candidate.PropType);
        row.Side = candidate.Side;
        row.Line = candidate.Line;
        row.Provider = candidate.Provider;
        row.Bookmaker = candidate.Bookmaker;
        row.ProviderCategory = candidate.ProviderCategory;
        row.EntryProvider = candidate.EntryProvider;
        row.EntryCompatibilityGroup = candidate.EntryCompatibilityGroup;
        row.HitChance = candidate.HitChance;
        row.ImpliedProbability = candidate.ImpliedProbability;
        row.ProbabilityEdge = candidate.ProbabilityEdge;
        row.EstimatedEv = candidate.EstimatedEv;
        row.TrustLevel = candidate.TrustLevel;
        row.SampleWarning = candidate.SampleWarning;
        row.Warning = candidate.Warning;
        row.HistoricalSampleSize = candidate.HistoricalSampleSize;
        row.StrategyClassification = candidate.StrategyClassification;
        row.StrategyTier = candidate.StrategyTier;
        row.ModelVersion = candidate.ModelVersion;
        row.RecommendationContractVersion = candidate.RecommendationContractVersion;
        row.ProbabilityModelVersion = candidate.ProbabilityModelVersion;
        row.ModelEra = candidate.ModelEra;
        row.RankingScore = candidate.RankingScore;
        row.IsCandidate = candidate.IsCandidate;
        row.IsTopPick = candidate.IsTopPick;
        row.IsWatchlist = candidate.IsWatchlist;
        row.IsActionableForUser = candidate.IsActionableForUser;
        row.LifecycleStatus = candidate.LifecycleStatus;
    }

    private static MutableRecommendationLedgerDateBreakdown GetBreakdown(
        Dictionary<DateOnly, MutableRecommendationLedgerDateBreakdown> breakdown,
        DateOnly date)
    {
        if (!breakdown.TryGetValue(date, out var value))
        {
            value = new MutableRecommendationLedgerDateBreakdown(date);
            breakdown[date] = value;
        }

        return value;
    }

    private sealed class MutableRecommendationLedgerDateBreakdown(DateOnly date)
    {
        public int CandidateRowsFound { get; set; }
        public int RowsEligibleForLedger { get; set; }
        public int RowsAlreadyExist { get; set; }
        public int RowsCreated { get; set; }
        public int RowsUpdated { get; set; }
        public int Skipped { get; set; }

        public RecommendationLedgerDateBreakdown ToImmutable()
        {
            return new RecommendationLedgerDateBreakdown(
                date,
                CandidateRowsFound,
                RowsEligibleForLedger,
                RowsAlreadyExist,
                RowsCreated,
                RowsUpdated,
                Skipped);
        }
    }
}
