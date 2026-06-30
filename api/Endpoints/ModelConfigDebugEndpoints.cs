using SportsBetting.Api.Configuration;

namespace SportsBetting.Api.Endpoints;

public static class ModelConfigDebugEndpoints
{
    public static IEndpointRouteBuilder MapModelConfigDebugEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/debug/model-config")
            .WithTags("Model Config");

        group.MapGet("/status", async (
            IModelConfigService modelConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await modelConfigService.GetSnapshotAsync(cancellationToken);
            var activeTopPicks = snapshot.FindActive(ModelConfigService.TopPicksModelKey);
            var activeMlbPrediction = snapshot.FindActive(ModelConfigService.MlbPredictionGenerationModelKey, "MLB");

            return Results.Ok(new
            {
                generatedAtUtc = snapshot.GeneratedAtUtc,
                readOnly = true,
                callsExternalApis = false,
                writesRows = false,
                loadSucceeded = snapshot.LoadSucceeded,
                loadError = snapshot.LoadError,
                modelConfigSetCount = snapshot.ModelConfigSets.Count,
                activeModelConfigSetCount = snapshot.ModelConfigSets.Count(row => row.IsActive),
                parameterCount = snapshot.ModelConfigSets.Sum(row => row.Parameters.Count),
                activeTopPicksModelEra = activeTopPicks?.ModelEra,
                activeMlbPredictionConfigPresent = activeMlbPrediction is not null,
                appsettingsFallbackCount = snapshot.AppsettingsFallbackCount,
                warnings = BuildWarnings(snapshot).Take(10).ToArray()
            });
        });

        group.MapGet("", async (
            bool includeParameters,
            IModelConfigService modelConfigService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await modelConfigService.GetSnapshotAsync(cancellationToken);
            var rows = snapshot.ModelConfigSets
                .OrderBy(row => row.ModelKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ModelEra, StringComparer.OrdinalIgnoreCase)
                .Select(row => new
                {
                    row.Id,
                    row.ModelKey,
                    row.ModelEra,
                    row.Sport,
                    row.IsActive,
                    row.Description,
                    row.CreatedAtUtc,
                    row.UpdatedAtUtc,
                    parameterCount = row.Parameters.Count,
                    parameters = includeParameters
                        ? row.Parameters
                            .OrderBy(parameter => parameter.SortOrder)
                            .ThenBy(parameter => parameter.ParameterKey, StringComparer.OrdinalIgnoreCase)
                            .Select(parameter => new
                            {
                                parameter.ParameterKey,
                                parameter.ValueType,
                                parameter.Value,
                                parameter.DisplayName,
                                parameter.Description,
                                parameter.Unit,
                                parameter.SortOrder,
                                parameter.UpdatedAtUtc
                            })
                            .ToArray()
                        : null
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = snapshot.GeneratedAtUtc,
                readOnly = true,
                callsExternalApis = false,
                writesRows = false,
                count = rows.Length,
                rows
            });
        });

        return app;
    }

    private static IEnumerable<string> BuildWarnings(ModelConfigSnapshot snapshot)
    {
        if (!snapshot.LoadSucceeded)
        {
            yield return "Model config DB load failed; runtime is using options/code defaults.";
        }

        if (snapshot.FindActive(ModelConfigService.TopPicksModelKey) is null)
        {
            yield return "No active TopPicksModel config set was found.";
        }

        if (snapshot.FindActive(ModelConfigService.MlbPredictionGenerationModelKey, "MLB") is null)
        {
            yield return "No active MLB prediction generation config set was found.";
        }
    }
}
