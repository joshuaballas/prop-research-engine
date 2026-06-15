using SportsBetting.Api.Models;

namespace SportsBetting.Api.Diagnostics;

public interface IApplicationErrorLogger
{
    Task<ApplicationError?> LogAsync(
        ApplicationErrorLogRequest request,
        CancellationToken cancellationToken = default);
}
