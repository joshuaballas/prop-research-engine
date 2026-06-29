using System.Text.Json;

namespace SportsBetting.Api.Models;

public sealed class Platform
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PlatformProp> PlatformProps { get; set; } = [];
    public ICollection<IngestionRun> IngestionRuns { get; set; } = [];
    public ICollection<RawIngestionPayload> RawPayloads { get; set; } = [];
}

public sealed class Sport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public ICollection<League> Leagues { get; set; } = [];
    public ICollection<PropType> PropTypes { get; set; } = [];
}

public sealed class League
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? SeasonType { get; set; }

    public Sport? Sport { get; set; }
    public ICollection<Team> Teams { get; set; } = [];
    public ICollection<Player> Players { get; set; } = [];
    public ICollection<Game> Games { get; set; } = [];
    public ICollection<Prop> Props { get; set; } = [];
}

public sealed class LeagueConfiguration
{
    public Guid Id { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public bool IsEnabledForAnalysis { get; set; }
    public bool IsEnabledForOptimizer { get; set; }
    public bool IsEnabledForDisplay { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Team
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Abbreviation { get; set; }
    public JsonDocument? ExternalRefs { get; set; }

    public League? League { get; set; }
    public ICollection<Player> Players { get; set; } = [];
}

public sealed class Player
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Guid? TeamId { get; set; }
    public Guid LeagueId { get; set; }
    public string? Position { get; set; }
    public JsonDocument? ExternalRefs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team? Team { get; set; }
    public League? League { get; set; }
    public ICollection<Prop> Props { get; set; } = [];
    public ICollection<PropResult> PropResults { get; set; } = [];
    public ICollection<PlayerExternalRef> ExternalProviderRefs { get; set; } = [];
}

public sealed class PlayerExternalRef
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderPlayerId { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Player? Player { get; set; }
}

public sealed class ProviderEntityLink
{
    public Guid Id { get; set; }
    public Guid? SportId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? CanonicalEntityId { get; set; }
    public string? CanonicalEntityKind { get; set; }
    public string? CanonicalEntityName { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderEntityId { get; set; } = string.Empty;
    public string? ProviderEntityName { get; set; }
    public string? ProviderTeamId { get; set; }
    public string? ProviderTeamName { get; set; }
    public string? ProviderCompetitionId { get; set; }
    public string? ProviderCompetitionName { get; set; }
    public string? ProviderEventId { get; set; }
    public DateTimeOffset? EventStartUtc { get; set; }
    public decimal? Confidence { get; set; }
    public string LinkStatus { get; set; } = "Candidate";
    public string LinkSource { get; set; } = "Import";
    public JsonDocument? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string? RejectedReason { get; set; }

    public Sport? Sport { get; set; }
}

public sealed class ProviderStatMapping
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderProduct { get; set; } = string.Empty;
    public string? CompetitionScope { get; set; }
    public int? Season { get; set; }
    public string ProviderMappingKey { get; set; } = string.Empty;
    public string? ProviderPropName { get; set; }
    public string? ProviderStatKey { get; set; }
    public JsonDocument? ProviderStatFieldsJson { get; set; } = JsonDocument.Parse("[]");
    public Guid? CanonicalPropTypeId { get; set; }
    public string CanonicalStatKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ValueTransform { get; set; } = string.Empty;
    public string RuleConfidence { get; set; } = string.Empty;
    public bool IsGradeable { get; set; }
    public bool PublicGradingEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public JsonDocument? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Sport? Sport { get; set; }
    public PropType? CanonicalPropType { get; set; }
}

public sealed class PlayerMergeAudit
{
    public Guid MergeId { get; set; }
    public Guid SurvivorPlayerId { get; set; }
    public Guid MergedPlayerId { get; set; }
    public DateTimeOffset MergedAt { get; set; } = DateTimeOffset.UtcNow;
    public int AffectedProps { get; set; }
    public int AffectedSnapshots { get; set; }
    public int AffectedResults { get; set; }
    public int AffectedMappings { get; set; }
}

public sealed class Game
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public Guid HomeTeamId { get; set; }
    public Guid AwayTeamId { get; set; }
    public DateTimeOffset ScheduledStartAt { get; set; }
    public string Status { get; set; } = "scheduled";
    public JsonDocument? ExternalRefs { get; set; }

    public League? League { get; set; }
    public Team? HomeTeam { get; set; }
    public Team? AwayTeam { get; set; }
    public ICollection<Prop> Props { get; set; } = [];
}

public sealed class PropType
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StatKey { get; set; } = string.Empty;
    public string ValueType { get; set; } = "numeric";

    public Sport? Sport { get; set; }
    public ICollection<Prop> Props { get; set; } = [];
    public ICollection<PropResult> PropResults { get; set; } = [];
}

public sealed class Prop
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid? GameId { get; set; }
    public Guid PropTypeId { get; set; }
    public Guid LeagueId { get; set; }
    public string NormalizedKey { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Player? Player { get; set; }
    public Game? Game { get; set; }
    public PropType? PropType { get; set; }
    public League? League { get; set; }
    public ICollection<PlatformProp> PlatformProps { get; set; } = [];
    public ICollection<Prediction> Predictions { get; set; } = [];
}

public sealed class PlatformProp
{
    public Guid Id { get; set; }
    public Guid PropId { get; set; }
    public Guid PlatformId { get; set; }
    public string PlatformPropId { get; set; } = string.Empty;
    public string? ExternalStableKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal CurrentLine { get; set; }
    public bool OverAvailable { get; set; }
    public bool UnderAvailable { get; set; }
    public string PayoutType { get; set; } = "standard";
    public string Status { get; set; } = "active";
    public DateTimeOffset ObservedAt { get; set; }
    public Guid? RawPayloadId { get; set; }
    public JsonDocument? Metadata { get; set; }

    public Prop? Prop { get; set; }
    public Platform? Platform { get; set; }
    public RawIngestionPayload? RawPayload { get; set; }
    public ICollection<PropLineSnapshot> LineSnapshots { get; set; } = [];
    public ICollection<PropAnalysis> Analyses { get; set; } = [];
    public ICollection<LineMovement> LineMovements { get; set; } = [];
}

public sealed class PropLineSnapshot
{
    public Guid Id { get; set; }
    public Guid PlatformPropId { get; set; }
    public decimal Line { get; set; }
    public bool OverAvailable { get; set; }
    public bool UnderAvailable { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset ObservedAt { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public Guid? RawPayloadId { get; set; }

    public PlatformProp? PlatformProp { get; set; }
    public RawIngestionPayload? RawPayload { get; set; }
}

public sealed class PropAnalysis
{
    public Guid Id { get; set; }
    public Guid PlatformPropId { get; set; }
    public string Side { get; set; } = string.Empty;
    public decimal HitProbability { get; set; }
    public decimal FairLine { get; set; }
    public decimal ExpectedValue { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal HistoricalHitRate { get; set; }
    public decimal LineMovementScore { get; set; }
    public decimal PlatformEdgeScore { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public JsonDocument? Explanation { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }

    public PlatformProp? PlatformProp { get; set; }
}

public sealed class LineMovement
{
    public Guid Id { get; set; }
    public Guid PlatformPropId { get; set; }
    public decimal PreviousLine { get; set; }
    public decimal CurrentLine { get; set; }
    public decimal Delta { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal MovementPercent { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public decimal SignificanceScore { get; set; }

    public PlatformProp? PlatformProp { get; set; }
}

public sealed class PropResult
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid LeagueId { get; set; }
    public Guid PropTypeId { get; set; }
    public DateOnly EventDate { get; set; }
    public decimal ActualResultValue { get; set; }
    public string ExternalSource { get; set; } = string.Empty;
    public string ExternalSourceIdentifier { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Player? Player { get; set; }
    public League? League { get; set; }
    public PropType? PropType { get; set; }
}

public sealed class MlbResultImportState
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "mlb";
    public DateOnly? LastSuccessfulImportDate { get; set; }
    public DateTimeOffset? LastSuccessfulRunAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public string LastStatus { get; set; } = "never_run";
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MlbResultImportDateState
{
    public Guid Id { get; set; }
    public DateOnly EventDate { get; set; }
    public string Status { get; set; } = "pending";
    public int? CompletedGamesExpected { get; set; }
    public int? CompletedGamesImported { get; set; }
    public int ImportedResults { get; set; }
    public int DistinctPlayers { get; set; }
    public int DistinctPropTypes { get; set; }
    public string CoverageStatus { get; set; } = "NotStarted";
    public string? CoverageReason { get; set; }
    public DateTimeOffset? LatestImportAttemptUtc { get; set; }
    public DateTimeOffset? LatestSuccessfulRepairUtc { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? LastSuccessfulAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public int RecordsFound { get; set; }
    public int RecordsMapped { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsUpdated { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SoccerResultImportState
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "api-sports-football";
    public string Competition { get; set; } = "World Cup";
    public DateOnly? LastSuccessfulImportDate { get; set; }
    public DateTimeOffset? LastSuccessfulRunAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public string LastStatus { get; set; } = "never_run";
    public string? LastErrorMessage { get; set; }
    public string? LatestCompletedFixtureDetectedId { get; set; }
    public DateOnly? LatestCompletedFixtureDetectedDate { get; set; }
    public string? LatestCompletedFixtureImportedId { get; set; }
    public DateOnly? LatestCompletedFixtureImportedDate { get; set; }
    public int LastApiCallsUsed { get; set; }
    public int LastDiscoveredFixtureCount { get; set; }
    public int LastCompletedFixtureCount { get; set; }
    public int LastImportedFixtureCount { get; set; }
    public int LastFailedFixtureCount { get; set; }
    public int LastSkippedPendingFixtureCount { get; set; }
    public int LastResultRowsWritten { get; set; }
    public int LastResultRowsInserted { get; set; }
    public int LastResultRowsUpdated { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SoccerResultImportFixtureState
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "api-sports-football";
    public string Competition { get; set; } = "World Cup";
    public string FixtureId { get; set; } = string.Empty;
    public DateOnly? EventDate { get; set; }
    public DateTimeOffset? KickoffUtc { get; set; }
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }
    public string? StatusShort { get; set; }
    public string? StatusLong { get; set; }
    public string ImportStatus { get; set; } = "NotStarted";
    public string? SkipReason { get; set; }
    public int ResultRowsPreviewed { get; set; }
    public int ResultRowsWritten { get; set; }
    public int ResultRowsInserted { get; set; }
    public int ResultRowsUpdated { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? LastSuccessfulAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApiSportsSoccerSeasonMetadata
{
    public Guid Id { get; set; }
    public int LeagueId { get; set; }
    public string? LeagueName { get; set; }
    public string? Country { get; set; }
    public string? Type { get; set; }
    public int Season { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public DateTimeOffset LastRefreshedAtUtc { get; set; }
    public string? RawSeasonJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppConfig
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "String";
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsRuntimeEditable { get; set; } = true;
    public bool RequiresRestart { get; set; }
    public string? ValidationJson { get; set; }
    public string Environment { get; set; } = "All";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PredictionRun
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }

    public ICollection<Prediction> Predictions { get; set; } = [];
    public ICollection<PredictionRunSkipCount> SkipCounts { get; set; } = [];
}

public sealed class Prediction
{
    public Guid Id { get; set; }
    public Guid PredictionRunId { get; set; }
    public Guid PropId { get; set; }
    public decimal? PredictedProbability { get; set; }
    public decimal? PredictedValue { get; set; }
    public decimal? SportsbookLine { get; set; }
    public decimal? Edge { get; set; }
    public string Recommendation { get; set; } = "PASS";
    public decimal Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PredictionRun? PredictionRun { get; set; }
    public Prop? Prop { get; set; }
}

public sealed class PredictionRunSkipCount
{
    public Guid Id { get; set; }
    public Guid PredictionRunId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PredictionRun? PredictionRun { get; set; }
}

public sealed class LineShoppingStrategyBacktestSnapshot
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Sport { get; set; } = "MLB";
    public DateOnly? SlateDate { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalGroups { get; set; }
    public int GradedGroups { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Pushes { get; set; }
    public decimal HitRate { get; set; }
    public decimal SimulatedProfit { get; set; }
    public decimal SimulatedRoi { get; set; }
    public decimal? AverageBestEstimatedEv { get; set; }
    public decimal? AverageBestProbability { get; set; }
    public decimal? AverageBestImpliedProbability { get; set; }
    public decimal? AverageProbabilityEdge { get; set; }
    public decimal AverageHistoricalSampleSize { get; set; }
    public decimal? AverageBestPrice { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PredictionRun? PredictionRun { get; set; }
}

public sealed class TheOddsRefreshRun
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "The Odds API";
    public string Sport { get; set; } = "MLB";
    public DateOnly SlateDate { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public bool Succeeded { get; set; }
    public bool Failed { get; set; }
    public bool Blocked { get; set; }
    public bool DryRun { get; set; }
    public bool ForceLive { get; set; }
    public string BookmakerPreset { get; set; } = string.Empty;
    public string ProviderCategoriesIncluded { get; set; } = string.Empty;
    public string SportsbookBookmakersRequested { get; set; } = string.Empty;
    public string DfsBookmakersRequested { get; set; } = string.Empty;
    public string MarketsRequested { get; set; } = string.Empty;
    public int EstimatedApiCalls { get; set; }
    public int ActualApiCalls { get; set; }
    public int PlatformPropsCreated { get; set; }
    public int PlatformPropsUpdated { get; set; }
    public int PlatformPropsUpserted { get; set; }
    public int PredictionCount { get; set; }
    public int LineShoppingGroupCount { get; set; }
    public int ActiveCandidateCount { get; set; }
    public int ActiveWatchlistCount { get; set; }
    public int ActiveAvoidCount { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonDocument? DiagnosticsJson { get; set; }

    public ICollection<MarketTopPickSnapshot> TopPickSnapshots { get; set; } = [];
    public ICollection<MarketLineShoppingSnapshot> LineShoppingSnapshots { get; set; } = [];
}

public sealed class TheOddsRefreshPlannerDecision
{
    public Guid Id { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Sport { get; set; } = "MLB";
    public DateOnly SlateDate { get; set; }
    public string BookmakerPreset { get; set; } = string.Empty;
    public bool ShouldRefresh { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public bool ProviderAvailable { get; set; }
    public bool ProviderRejectedKeyDetected { get; set; }
    public int? RequestsUsed { get; set; }
    public int? RequestsRemaining { get; set; }
    public int EstimatedApiCalls { get; set; }
    public int DailyCreditsUsed { get; set; }
    public int MonthlyCreditsUsed { get; set; }
    public int DailyCreditLimit { get; set; }
    public int MonthlyCreditLimit { get; set; }
    public bool BudgetAllowed { get; set; }
    public string CadenceRule { get; set; } = string.Empty;
    public decimal? MinutesUntilNextEventStart { get; set; }
    public decimal? MinutesSinceLastSuccessfulRefresh { get; set; }
    public DateTimeOffset NextEligibleRefreshUtc { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public JsonDocument DetailsJson { get; set; } = JsonDocument.Parse("{}");
}

public sealed class MarketTopPickSnapshot
{
    public Guid Id { get; set; }
    public Guid TheOddsRefreshRunId { get; set; }
    public Guid? PredictionRunId { get; set; }
    public string Sport { get; set; } = "MLB";
    public DateOnly SlateDate { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string BookmakerPreset { get; set; } = string.Empty;
    public string ProviderCategory { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Bookmaker { get; set; }
    public string Player { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public string? EventName { get; set; }
    public DateTimeOffset? EventStartTime { get; set; }
    public string LifecycleStatus { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public bool IsExpired { get; set; }
    public string PropType { get; set; } = string.Empty;
    public decimal Line { get; set; }
    public string BestSide { get; set; } = string.Empty;
    public string? BestBookmaker { get; set; }
    public int? BestAmericanPrice { get; set; }
    public decimal? BestDecimalPrice { get; set; }
    public decimal? BestProbability { get; set; }
    public decimal? BestImpliedProbability { get; set; }
    public decimal? BestProbabilityEdge { get; set; }
    public decimal? BestEstimatedEv { get; set; }
    public string StrategyClassification { get; set; } = string.Empty;
    public string StrategyTier { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public string? RecommendationContractVersion { get; set; }
    public string? ProbabilityModelVersion { get; set; }
    public string? ModelEra { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsWatchlist { get; set; }
    public bool IsAvoid { get; set; }
    public bool IsResearchOnly { get; set; }
    public string EntryProvider { get; set; } = string.Empty;
    public string EntryCompatibilityGroup { get; set; } = string.Empty;
    public string EntryProviderCategory { get; set; } = string.Empty;
    public bool EntryActionableForUser { get; set; }
    public JsonDocument DetailsJson { get; set; } = JsonDocument.Parse("{}");

    public TheOddsRefreshRun? TheOddsRefreshRun { get; set; }
    public PredictionRun? PredictionRun { get; set; }
}

public sealed class MarketLineShoppingSnapshot
{
    public Guid Id { get; set; }
    public Guid TheOddsRefreshRunId { get; set; }
    public Guid? PredictionRunId { get; set; }
    public string Sport { get; set; } = "MLB";
    public DateOnly SlateDate { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string BookmakerPreset { get; set; } = string.Empty;
    public string ProviderCategory { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Bookmaker { get; set; }
    public string Player { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public string? EventName { get; set; }
    public DateTimeOffset? EventStartTime { get; set; }
    public string LifecycleStatus { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public bool IsExpired { get; set; }
    public string PropType { get; set; } = string.Empty;
    public decimal Line { get; set; }
    public string BestSide { get; set; } = string.Empty;
    public string? BestBookmaker { get; set; }
    public int? BestAmericanPrice { get; set; }
    public decimal? BestDecimalPrice { get; set; }
    public decimal? BestProbability { get; set; }
    public decimal? BestImpliedProbability { get; set; }
    public decimal? BestProbabilityEdge { get; set; }
    public decimal? BestEstimatedEv { get; set; }
    public string StrategyClassification { get; set; } = string.Empty;
    public string StrategyTier { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public string? RecommendationContractVersion { get; set; }
    public string? ProbabilityModelVersion { get; set; }
    public string? ModelEra { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsWatchlist { get; set; }
    public bool IsAvoid { get; set; }
    public bool IsResearchOnly { get; set; }
    public string EntryProvider { get; set; } = string.Empty;
    public string EntryCompatibilityGroup { get; set; } = string.Empty;
    public string EntryProviderCategory { get; set; } = string.Empty;
    public bool EntryActionableForUser { get; set; }
    public JsonDocument DetailsJson { get; set; } = JsonDocument.Parse("{}");

    public TheOddsRefreshRun? TheOddsRefreshRun { get; set; }
    public PredictionRun? PredictionRun { get; set; }
}

public sealed class RecommendationSnapshot
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public int SeenCount { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public Guid? RefreshRunId { get; set; }
    public Guid? PredictionRunId { get; set; }
    public string Sport { get; set; } = "MLB";
    public DateOnly SlateDate { get; set; }
    public DateOnly ExpectedResultDate { get; set; }
    public string? ResultDateSource { get; set; }
    public string? EventId { get; set; }
    public DateTimeOffset? EventStartTimeUtc { get; set; }
    public Guid? CanonicalPlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string NormalizedPlayerName { get; set; } = string.Empty;
    public Guid? PropTypeId { get; set; }
    public string PropType { get; set; } = string.Empty;
    public string NormalizedPropType { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Line { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Bookmaker { get; set; }
    public string ProviderCategory { get; set; } = string.Empty;
    public string EntryProvider { get; set; } = string.Empty;
    public string EntryCompatibilityGroup { get; set; } = string.Empty;
    public decimal? HitChance { get; set; }
    public decimal? ImpliedProbability { get; set; }
    public decimal? ProbabilityEdge { get; set; }
    public decimal? EstimatedEv { get; set; }
    public string TrustLevel { get; set; } = string.Empty;
    public string? SampleWarning { get; set; }
    public string? Warning { get; set; }
    public int? HistoricalSampleSize { get; set; }
    public string StrategyClassification { get; set; } = string.Empty;
    public string StrategyTier { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public string? RecommendationContractVersion { get; set; }
    public string? ProbabilityModelVersion { get; set; }
    public string? ModelEra { get; set; }
    public decimal RankingScore { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsTopPick { get; set; }
    public bool IsWatchlist { get; set; }
    public bool IsActionableForUser { get; set; }
    public string LifecycleStatus { get; set; } = string.Empty;
    public decimal? ResultValue { get; set; }
    public string GradeStatus { get; set; } = "Pending";
    public DateTimeOffset? GradedAtUtc { get; set; }
    public string? GradingFailureReason { get; set; }
    public bool IsExcludedFromPerformance { get; set; }
    public string? PerformanceExclusionReason { get; set; }
    public DateTimeOffset? PerformanceExcludedAtUtc { get; set; }
    public string? PerformanceExcludedBy { get; set; }

    public TheOddsRefreshRun? RefreshRun { get; set; }
    public PredictionRun? PredictionRun { get; set; }
    public Player? CanonicalPlayer { get; set; }
    public PropType? PropTypeRecord { get; set; }
}

public sealed class ProviderLineLifecycle
{
    public Guid Id { get; set; }
    public string Sport { get; set; } = "MLB";
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ProviderCategory { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public DateTimeOffset? EventStartTimeUtc { get; set; }
    public DateOnly? SlateDate { get; set; }
    public Guid? CanonicalPlayerId { get; set; }
    public string? ProviderPlayerId { get; set; }
    public string NormalizedPlayerName { get; set; } = string.Empty;
    public string MarketKey { get; set; } = string.Empty;
    public string PropType { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal? CurrentLine { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string CurrentStatus { get; set; } = ProviderLineLifecycleStatuses.Active;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public DateTimeOffset? LastChangedUtc { get; set; }
    public Guid? LastRefreshRunId { get; set; }
    public Guid? LastSnapshotId { get; set; }
    public int SeenCount { get; set; }
    public int ChangedCount { get; set; }
    public int MissingCount { get; set; }
    public DateTimeOffset? PulledAtUtc { get; set; }
    public DateTimeOffset? LockedAtUtc { get; set; }
    public DateTimeOffset? ExpiredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public TheOddsRefreshRun? LastRefreshRun { get; set; }
    public ICollection<ProviderLineLifecycleEvent> Events { get; set; } = [];
}

public sealed class ProviderLineLifecycleEvent
{
    public Guid Id { get; set; }
    public Guid ProviderLineLifecycleId { get; set; }
    public Guid? RefreshRunId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public decimal? PreviousLine { get; set; }
    public decimal? NewLine { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public JsonDocument? MetadataJson { get; set; }

    public ProviderLineLifecycle? ProviderLineLifecycle { get; set; }
    public TheOddsRefreshRun? RefreshRun { get; set; }
}

public static class ProviderLineLifecycleStatuses
{
    public const string Active = "Active";
    public const string Stale = "Stale";
    public const string Pulled = "Pulled";
    public const string Locked = "Locked";
    public const string Expired = "Expired";
    public const string ProviderUnavailable = "ProviderUnavailable";
}

public static class ProviderLineLifecycleEventTypes
{
    public const string FirstSeen = "FirstSeen";
    public const string SeenUnchanged = "SeenUnchanged";
    public const string LineChanged = "LineChanged";
    public const string PriceChanged = "PriceChanged";
    public const string LineAndPriceChanged = "LineAndPriceChanged";
    public const string MissingFromRefresh = "MissingFromRefresh";
    public const string ProviderPulled = "ProviderPulled";
    public const string EventLocked = "EventLocked";
    public const string MarkedStale = "MarkedStale";
    public const string MarkedExpired = "MarkedExpired";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string Reactivated = "Reactivated";
}

public sealed class IngestionRun
{
    public Guid Id { get; set; }
    public Guid PlatformId { get; set; }
    public string Status { get; set; } = "running";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int PropsSeen { get; set; }
    public int PropsChanged { get; set; }
    public string? ErrorMessage { get; set; }
    public long? PayloadSizeBytes { get; set; }
    public string? PayloadHash { get; set; }
    public string? SchemaVersion { get; set; }
    public int WarningCount { get; set; }

    public Platform? Platform { get; set; }
    public ICollection<RawIngestionPayload> RawPayloads { get; set; } = [];
}

public sealed class RawIngestionPayload
{
    public Guid Id { get; set; }
    public Guid IngestionRunId { get; set; }
    public Guid PlatformId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CapturedAt { get; set; }

    public IngestionRun? IngestionRun { get; set; }
    public Platform? Platform { get; set; }
}

public sealed class CorrelationPair
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? PropAId { get; set; }
    public Guid? PropBId { get; set; }
    public Guid? PropTypeAId { get; set; }
    public Guid? PropTypeBId { get; set; }
    public string RelationshipScope { get; set; } = string.Empty;
    public decimal CorrelationCoefficient { get; set; }
    public decimal ConfidenceScore { get; set; }
    public int SampleSize { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public DateTimeOffset CalculatedAt { get; set; }

    public Prop? PropA { get; set; }
    public Prop? PropB { get; set; }
    public PropType? PropTypeA { get; set; }
    public PropType? PropTypeB { get; set; }
}

public sealed class CorrelationRule
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public Guid PropTypeAId { get; set; }
    public Guid PropTypeBId { get; set; }
    public string Context { get; set; } = string.Empty;
    public string ExpectedDirection { get; set; } = string.Empty;
    public decimal DefaultWeight { get; set; }
    public string Description { get; set; } = string.Empty;

    public League? League { get; set; }
    public PropType? PropTypeA { get; set; }
    public PropType? PropTypeB { get; set; }
}

public sealed class OptimizedEntry
{
    public Guid Id { get; set; }
    public Guid? PlatformId { get; set; }
    public int EntrySize { get; set; }
    public decimal CombinedProbability { get; set; }
    public decimal ExpectedValue { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal CorrelationScore { get; set; }
    public decimal RiskScore { get; set; }
    public int Rank { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public Platform? Platform { get; set; }
    public ICollection<OptimizedEntryLeg> Legs { get; set; } = [];
}

public sealed class OptimizedEntryLeg
{
    public Guid Id { get; set; }
    public Guid OptimizedEntryId { get; set; }
    public Guid PlatformPropId { get; set; }
    public string Side { get; set; } = string.Empty;
    public decimal LegProbability { get; set; }
    public decimal LegExpectedValue { get; set; }
    public int LegOrder { get; set; }

    public OptimizedEntry? OptimizedEntry { get; set; }
    public PlatformProp? PlatformProp { get; set; }
}

public sealed class ApplicationError
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Severity { get; set; } = "Error";
    public string Source { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? RequestPath { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? InnerException { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public JsonDocument? MetadataJson { get; set; }
}
