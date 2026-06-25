namespace SportsBetting.Api.Integrations.Results.Mlb;

public interface IMlbResultCoverageService
{
    Task<MlbResultCoverageAuditResponse> AuditAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool includeDetails,
        bool persist,
        CancellationToken cancellationToken);

    Task<MlbResultCoverageDateAudit> AuditDateAsync(
        DateOnly eventDate,
        bool persist,
        CancellationToken cancellationToken);
}
