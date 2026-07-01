using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;

namespace SportsBetting.Api.Tests;

public sealed class ApplicationErrorLoggingTests
{
    [Fact]
    public async Task ApplicationErrorLogger_PersistsContextAndSanitizesMetadata()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        var runId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            var logged = await logger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "UnitTestSource",
                "UnitComponent",
                "UnitOperation",
                new InvalidOperationException("unit failure"),
                RequestPath: "/api/unit-test",
                RequestMethod: "GET",
                StatusCode: 500,
                CorrelationId: "unit-correlation",
                Metadata: new
                {
                    apiKey = "SHOULD_NOT_BE_STORED",
                    nested = new
                    {
                        authorization = "Bearer SHOULD_NOT_BE_STORED",
                        safeValue = "visible"
                    },
                    oversized = new string('x', 25000)
                },
                UserAgent: "UnitTestAgent/1.0",
                RemoteIpAddress: "203.0.113.10",
                RunId: runId,
                Sport: "MLB",
                Provider: "The Odds API"));

            Assert.NotNull(logged);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var error = await dbContext.ApplicationErrors.AsNoTracking().SingleAsync();
            Assert.Equal("Error", error.Severity);
            Assert.Equal("UnitComponent", error.Component);
            Assert.Equal("UnitTestSource", error.Source);
            Assert.Equal("UnitOperation", error.Operation);
            Assert.Equal("/api/unit-test", error.RequestPath);
            Assert.Equal("GET", error.RequestMethod);
            Assert.Equal("unit-correlation", error.CorrelationId);
            Assert.Equal(runId, error.RunId);
            Assert.Equal("MLB", error.Sport);
            Assert.Equal("The Odds API", error.Provider);
            Assert.NotNull(error.RemoteIpHash);
            Assert.NotEqual("203.0.113.10", error.RemoteIpHash);
            Assert.NotEqual(default, error.CreatedAtUtc);
            Assert.Contains("InvalidOperationException", error.StackTrace);

            var metadata = error.MetadataJson!.RootElement.GetRawText();
            Assert.DoesNotContain("SHOULD_NOT_BE_STORED", metadata);
            Assert.Contains("[redacted]", metadata);
            Assert.Contains("visible", metadata);
            Assert.True(metadata.Length <= 17000);
        }
    }

    [Fact]
    public async Task ApplicationErrorLogger_DoesNotThrowForNullOrOversizedMetadata()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();

        var first = await logger.LogAsync(new ApplicationErrorLogRequest(
            "Warning",
            "UnitTestSource",
            "UnitComponent",
            null,
            new Exception("metadata is null"),
            Metadata: null));
        var second = await logger.LogAsync(new ApplicationErrorLogRequest(
            "Error",
            "UnitTestSource",
            "UnitComponent",
            "OversizedMetadata",
            new Exception("metadata is oversized"),
            Metadata: new
            {
                values = Enumerable.Range(0, 500).Select(index => new
                {
                    index,
                    value = new string('z', 500)
                })
            }));

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ApplicationErrorDebugEndpoints_FilterAndReturnFullDetails()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        var runId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await logger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "FilterSource",
                "FilterComponent",
                "FilterOperation",
                new InvalidOperationException("matching error"),
                CorrelationId: "filter-correlation",
                RunId: runId,
                Sport: "SOCCER",
                Provider: "API-Sports"));
            await logger.LogAsync(new ApplicationErrorLogRequest(
                "Warning",
                "OtherSource",
                "OtherComponent",
                "OtherOperation",
                new InvalidOperationException("non-matching error"),
                CorrelationId: "other-correlation",
                Sport: "MLB",
                Provider: "The Odds API"));
        }

        using var client = factory.CreateClient();
        var listResponse = await client.GetAsync($"/api/debug/application-errors?severity=Error&component=FilterComponent&sport=SOCCER&provider=API-Sports&correlationId=filter-correlation&runId={runId}&take=10");
        listResponse.EnsureSuccessStatusCode();
        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(listJson.GetProperty("readOnly").GetBoolean());
        Assert.False(listJson.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, listJson.GetProperty("count").GetInt32());
        var row = listJson.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("FilterComponent", row.GetProperty("component").GetString());
        Assert.Equal("SOCCER", row.GetProperty("sport").GetString());
        var id = row.GetProperty("id").GetGuid();

        var detailResponse = await client.GetAsync($"/api/debug/application-errors/{id}");
        detailResponse.EnsureSuccessStatusCode();
        var detailJson = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detailJson.GetProperty("readOnly").GetBoolean());
        Assert.False(detailJson.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal("matching error", detailJson.GetProperty("message").GetString());
        Assert.Contains("InvalidOperationException", detailJson.GetProperty("stackTrace").GetString());

        var summaryResponse = await client.GetAsync("/api/debug/application-errors/summary?component=FilterComponent");
        summaryResponse.EnsureSuccessStatusCode();
        var summaryJson = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summaryJson.GetProperty("readOnly").GetBoolean());
        Assert.False(summaryJson.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, summaryJson.GetProperty("totalRecords").GetInt32());
    }

    [Fact]
    public async Task ApplicationErrorDebugEndpoints_DefaultListExcludesCanceledRequests()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await logger.LogAsync(new ApplicationErrorLogRequest(
                "Information",
                "CanceledSource",
                "GlobalExceptionMiddleware",
                "ClientRequestCanceled",
                new OperationCanceledException("The operation was canceled."),
                RequestPath: "/api/markets/top-picks",
                RequestMethod: "GET",
                StatusCode: 499,
                CorrelationId: "canceled-correlation"));
            await logger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "FailureSource",
                "FailureComponent",
                "UnhandledApiException",
                new InvalidOperationException("real backend failure"),
                CorrelationId: "failure-correlation"));
        }

        using var client = factory.CreateClient();
        var defaultResponse = await client.GetAsync("/api/debug/application-errors?take=25");
        defaultResponse.EnsureSuccessStatusCode();
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(defaultJson.GetProperty("readOnly").GetBoolean());
        Assert.False(defaultJson.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(defaultJson.GetProperty("filters").GetProperty("includeCanceled").GetBoolean());
        Assert.Equal(1, defaultJson.GetProperty("count").GetInt32());
        var defaultRow = defaultJson.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("FailureComponent", defaultRow.GetProperty("component").GetString());

        var includeCanceledResponse = await client.GetAsync("/api/debug/application-errors?take=25&includeCanceled=true");
        includeCanceledResponse.EnsureSuccessStatusCode();
        var includeCanceledJson = await includeCanceledResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(includeCanceledJson.GetProperty("filters").GetProperty("includeCanceled").GetBoolean());
        Assert.Equal(2, includeCanceledJson.GetProperty("count").GetInt32());
        Assert.Contains(
            includeCanceledJson.GetProperty("errors").EnumerateArray(),
            row => row.GetProperty("operation").GetString() == "ClientRequestCanceled");

        var aliasResponse = await client.GetAsync("/api/debug/errors/recent?take=25&includeCanceled=true");
        aliasResponse.EnsureSuccessStatusCode();
        var aliasJson = await aliasResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, aliasJson.GetProperty("count").GetInt32());

        var summaryResponse = await client.GetAsync("/api/debug/application-errors/summary?hours=24");
        summaryResponse.EnsureSuccessStatusCode();
        var summaryJson = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, summaryJson.GetProperty("totalRecords").GetInt32());
        Assert.Equal(1, summaryJson.GetProperty("errorRecords").GetInt32());
        Assert.Equal(1, summaryJson.GetProperty("canceledRequests").GetInt32());
    }

    [Fact]
    public async Task ApplicationErrorMiddleware_PersistsUnexpectedApiExceptions()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "middleware-correlation");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MiddlewareTest/1.0");

        var response = await client.GetAsync("/api/debug/test/throw-application-error");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("middleware-correlation", body.GetProperty("correlationId").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var error = await dbContext.ApplicationErrors.AsNoTracking().SingleAsync();
        Assert.Equal("Middleware", error.Component);
        Assert.Equal("/api/debug/test/throw-application-error", error.Source);
        Assert.Equal("/api/debug/test/throw-application-error", error.RequestPath);
        Assert.Equal("GET", error.RequestMethod);
        Assert.Equal("middleware-correlation", error.CorrelationId);
        Assert.Equal("MiddlewareTest/1.0", error.UserAgent);
        Assert.Equal(500, error.StatusCode);
        Assert.Contains("Testing application error middleware persistence", error.Message);
    }
}
