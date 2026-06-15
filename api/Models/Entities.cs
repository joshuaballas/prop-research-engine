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
