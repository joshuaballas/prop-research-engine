namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed record MlbResultCoverageAuditResponse(
    DateTimeOffset GeneratedAtUtc,
    bool CallsExternalApis,
    bool ReadOnly,
    DateOnly FromDate,
    DateOnly ToDate,
    int DatesAudited,
    int DatesComplete,
    int DatesPartial,
    int DatesMissing,
    int DatesPending,
    int DatesNoGames,
    int DatesFailed,
    IReadOnlyCollection<MlbResultCoverageDateAudit> Details,
    IReadOnlyCollection<object> RecommendedRepairActions);

public sealed record MlbResultCoverageDateAudit(
    DateOnly Date,
    int? CompletedGamesExpected,
    int? CompletedGamesImported,
    int ImportedResults,
    int DistinctPlayers,
    int DistinctPropTypes,
    string CoverageStatus,
    string CoverageReason,
    DateTimeOffset? LatestImportAttemptUtc,
    DateTimeOffset? LatestSuccessfulRepairUtc,
    string? LatestErrorMessage,
    bool NeedsRepair,
    string RecommendedRepairAction);
