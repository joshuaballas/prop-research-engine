using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Diagnostics;

public sealed class ApplicationErrorLogger(
    ApplicationDbContext dbContext,
    ILogger<ApplicationErrorLogger> logger) : IApplicationErrorLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxSeverityLength = 50;
    private const int MaxSourceLength = 150;
    private const int MaxComponentLength = 200;
    private const int MaxOperationLength = 200;
    private const int MaxRequestPathLength = 1000;
    private const int MaxRequestMethodLength = 20;
    private const int MaxUserAgentLength = 500;
    private const int MaxProviderLength = 150;
    private const int MaxSportLength = 50;
    private const int MaxCorrelationIdLength = 100;
    private const int MaxMessageLength = 4000;
    private const int MaxExceptionTypeLength = 500;
    private const int MaxStackTraceLength = 30000;
    private const int MaxInnerExceptionLength = 20000;
    private const int MaxMetadataJsonLength = 16000;
    private const int MaxMetadataDepth = 8;
    private const int MaxMetadataArrayItems = 50;
    private const int MaxMetadataStringLength = 2000;

    public async Task<ApplicationError?> LogAsync(
        ApplicationErrorLogRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exception = request.Exception;
            var now = DateTimeOffset.UtcNow;
            var error = new ApplicationError
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = now,
                CreatedAtUtc = now,
                Severity = Cap(NullIfWhiteSpace(request.Severity) ?? "Error", MaxSeverityLength)!,
                Source = Cap(NullIfWhiteSpace(request.Source) ?? "unknown", MaxSourceLength)!,
                Component = Cap(NullIfWhiteSpace(request.Component) ?? "unknown", MaxComponentLength)!,
                Operation = Cap(NullIfWhiteSpace(request.Operation), MaxOperationLength),
                RequestPath = Cap(NullIfWhiteSpace(request.RequestPath), MaxRequestPathLength),
                RequestMethod = Cap(NullIfWhiteSpace(request.RequestMethod), MaxRequestMethodLength),
                UserAgent = Cap(NullIfWhiteSpace(request.UserAgent), MaxUserAgentLength),
                RemoteIpHash = HashRemoteIp(request.RemoteIpAddress),
                StatusCode = request.StatusCode,
                Message = Cap(NullIfWhiteSpace(exception.Message) ?? exception.GetType().Name, MaxMessageLength)!,
                ExceptionType = Cap(exception.GetType().FullName ?? exception.GetType().Name, MaxExceptionTypeLength)!,
                StackTrace = Cap(exception.ToString(), MaxStackTraceLength),
                InnerException = Cap(BuildInnerExceptionText(exception.InnerException), MaxInnerExceptionLength),
                CorrelationId = Cap(NullIfWhiteSpace(request.CorrelationId) ?? Guid.NewGuid().ToString("N"), MaxCorrelationIdLength)!,
                RunId = request.RunId,
                Sport = Cap(NullIfWhiteSpace(request.Sport), MaxSportLength),
                Provider = Cap(NullIfWhiteSpace(request.Provider), MaxProviderLength),
                MetadataJson = BuildMetadataDocument(request.Metadata)
            };

            dbContext.ApplicationErrors.Add(error);
            await dbContext.SaveChangesAsync(cancellationToken);

            return error;
        }
        catch (Exception loggingException)
        {
            logger.LogError(
                loggingException,
                "Failed to persist application error. OriginalExceptionType: {OriginalExceptionType}; OriginalMessage: {OriginalMessage}",
                request.Exception.GetType().FullName,
                request.Exception.Message);

            return null;
        }
    }

    private static string? BuildInnerExceptionText(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var innerExceptions = new List<string>();
        var current = exception;

        while (current is not null)
        {
            innerExceptions.Add($"{current.GetType().FullName}: {current.Message}{Environment.NewLine}{current.StackTrace}");
            current = current.InnerException;
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", innerExceptions);
    }

    private static JsonDocument? BuildMetadataDocument(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            using var document = JsonSerializer.SerializeToDocument(metadata, JsonOptions);
            var sanitizedNode = SanitizeElement(document.RootElement, depth: 0);
            var json = sanitizedNode?.ToJsonString(JsonOptions) ?? "{}";

            if (json.Length > MaxMetadataJsonLength)
            {
                json = JsonSerializer.Serialize(new
                {
                    truncated = true,
                    originalLength = json.Length,
                    preview = json[..MaxMetadataJsonLength]
                }, JsonOptions);
            }

            return JsonDocument.Parse(json);
        }
        catch (Exception exception)
        {
            var fallback = JsonSerializer.Serialize(new
            {
                metadataSerializationFailed = true,
                exceptionType = exception.GetType().FullName,
                message = Cap(exception.Message, MaxMessageLength)
            }, JsonOptions);
            return JsonDocument.Parse(fallback);
        }
    }

    private static JsonNode? SanitizeElement(JsonElement element, int depth)
    {
        if (depth >= MaxMetadataDepth)
        {
            return JsonValue.Create("[max-depth]");
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(element, depth),
            JsonValueKind.Array => SanitizeArray(element, depth),
            JsonValueKind.String => JsonValue.Create(Cap(element.GetString(), MaxMetadataStringLength)),
            JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => JsonValue.Create(Cap(element.GetRawText(), MaxMetadataStringLength))
        };
    }

    private static JsonObject SanitizeObject(JsonElement element, int depth)
    {
        var obj = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            obj[property.Name] = IsSensitiveMetadataKey(property.Name)
                ? "[redacted]"
                : SanitizeElement(property.Value, depth + 1);
        }

        return obj;
    }

    private static JsonArray SanitizeArray(JsonElement element, int depth)
    {
        var array = new JsonArray();
        var count = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (count >= MaxMetadataArrayItems)
            {
                array.Add(new JsonObject
                {
                    ["truncated"] = true,
                    ["omittedItems"] = element.GetArrayLength() - MaxMetadataArrayItems
                });
                break;
            }

            array.Add(SanitizeElement(item, depth + 1));
            count++;
        }

        return array;
    }

    private static bool IsSensitiveMetadataKey(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("connectionstring", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal);
    }

    private static string? HashRemoteIp(string? remoteIpAddress)
    {
        var value = NullIfWhiteSpace(remoteIpAddress);
        if (value is null)
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? Cap(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        const string suffix = "[truncated]";
        return value[..Math.Max(0, maxLength - suffix.Length)] + suffix;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
