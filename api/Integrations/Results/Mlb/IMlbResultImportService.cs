namespace SportsBetting.Api.Integrations.Results.Mlb;

public interface IMlbResultImportService
{
    Task<MlbResultImportResult> ImportAsync(DateOnly eventDate, CancellationToken cancellationToken);
    Task<MlbResultImportStatus> GetStatusAsync(DateOnly eventDate, CancellationToken cancellationToken);
}
