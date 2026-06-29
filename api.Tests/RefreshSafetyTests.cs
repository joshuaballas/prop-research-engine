using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SportsBetting.Api.Integrations.Results;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Integrations.Underdog;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Tests;

public sealed class RefreshSafetyTests
{
    [Fact]
    public async Task MlbSideSelectionAudit_FlagsSuspiciousSideProbabilityRows_AndMissingSide()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 24);

        await SeedSideSelectionAuditRowsAsync(factory, date, [
            CreateLineShoppingSnapshot("Under", "Jordan Walker", "Total Bases", 1.5m, 0.70m, 0.30m, 0.30m, 0.55m, 0.10m),
            CreateLineShoppingSnapshot("", "Paul Goldschmidt", "Hits", 0.5m, 0.62m, 0.38m, null, null, null)
        ]);

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=false&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("totalRowsEvaluated").GetInt32());
        Assert.Equal(1, json.GetProperty("underSelections").GetInt32());
        Assert.Equal(1, json.GetProperty("missingSideSelections").GetInt32());
        Assert.Equal(1, json.GetProperty("suspiciousUnderCount").GetInt32());
        Assert.Equal(1, json.GetProperty("possibleSideInversionCount").GetInt32());
        Assert.Equal(1, json.GetProperty("possibleEvProbabilityMismatchCount").GetInt32());
        Assert.Equal(1, json.GetProperty("missingSideDefaultedCount").GetInt32());
        Assert.Contains(
            json.GetProperty("samples").EnumerateArray(),
            sample => sample.GetProperty("reasonCodes").EnumerateArray().Any(reason => reason.GetString() == "PossibleSideInversion"));
    }

    [Fact]
    public async Task MlbSideSelectionAudit_CountsLineShoppingToLedgerSideChanges()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 24);
        var predictionRunId = Guid.NewGuid();

        await SeedSideSelectionAuditRowsAsync(
            factory,
            date,
            [CreateLineShoppingSnapshot("Over", "Jordan Walker", "Total Bases", 1.5m, 0.70m, 0.30m, 0.70m, 0.55m, 0.15m, predictionRunId)],
            [CreateRecommendationSnapshot("Under", "Jordan Walker", "Total Bases", 1.5m, predictionRunId, date)]);

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=false&includeSamples=false");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("sideLabelChangedCount").GetInt32());
        Assert.Contains("side label continuity", json.GetProperty("recommendedNextAction").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MlbSideSelectionAudit_ReturnsPredictionStageSamples_WhenNoSnapshotsExist()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPredictionStageAuditScenarioAsync(factory);

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("totalRowsEvaluated").GetInt32());
        Assert.Equal(3, json.GetProperty("predictionRowsEvaluated").GetInt32());
        Assert.Equal(0, json.GetProperty("predictionOverSelections").GetInt32());
        Assert.Equal(2, json.GetProperty("predictionUnderSelections").GetInt32());
        Assert.Equal(1, json.GetProperty("predictionPassSelections").GetInt32());
        Assert.NotEmpty(json.GetProperty("selectedUnderSamples").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("passSamples").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("highestOverEvSamples").EnumerateArray());
        var passSample = json.GetProperty("passSamples").EnumerateArray().First();
        Assert.Contains(
            passSample.GetProperty("passReasonCodes").EnumerateArray(),
            reason => reason.GetString() == "RecommendationSuppressedByThreshold");
        Assert.Contains(
            passSample.GetProperty("failedGates").EnumerateArray(),
            gate => gate.GetString() == "GenerationProjectionEdgeThreshold");
        Assert.Equal(0.75m, passSample.GetProperty("thresholdsApplied").GetProperty("overProjectionEdgeThreshold").GetDecimal());
        Assert.True(passSample.GetProperty("overCandidateBeforeGates").GetProperty("includedBeforeProductionGates").GetBoolean());
        Assert.False(passSample.GetProperty("overCandidateAfterGates").GetProperty("includedAfterProductionGates").GetBoolean());
    }

    [Fact]
    public async Task MlbSideSelectionAudit_FlagsPredictionStageProjectionAndEvMismatches()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPredictionStageAuditScenarioAsync(factory);

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("projectionAboveLineButSelectedUnderCount").GetInt32());
        Assert.Equal(1, json.GetProperty("overEvPositiveButPassedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("overEvPositiveButSelectedUnderCount").GetInt32());
        Assert.Equal(1, json.GetProperty("selectedUnderWithModelUnderBelowBreakEvenCount").GetInt32());
        Assert.True(json.GetProperty("overEvPositiveCount").GetInt32() >= 2);
        Assert.True(json.GetProperty("overEvGreaterThanUnderEvCount").GetInt32() >= 2);
        Assert.True(json.GetProperty("passGateSummary").GetProperty("passRowsWithPositiveOverEv").GetInt32() >= 1);
        Assert.True(json.GetProperty("passGateSummary").GetProperty("overPositivePassRowsRejectedByGenerationProjectionEdgeThreshold").GetInt32() >= 1);
        Assert.True(json.GetProperty("passReasonsByCount").TryGetProperty("RecommendationSuppressedByThreshold", out _));
        Assert.True(json.GetProperty("failedGatesByCount").TryGetProperty("GenerationProjectionEdgeThreshold", out _));
        Assert.Contains(
            json.GetProperty("projectionAboveLineButSelectedUnderSamples").EnumerateArray(),
            sample => sample.GetProperty("suspiciousReasons").EnumerateArray().Any(reason => reason.GetString() == "ProjectionAboveLineButSelectedUnder"));
        Assert.Contains(
            json.GetProperty("overEvPositiveButPassedSamples").EnumerateArray(),
            sample => sample.GetProperty("suspiciousReasons").EnumerateArray().Any(reason => reason.GetString() == "OverEvPositiveButPassed"));
        Assert.Contains(
            json.GetProperty("overPositivePassSamples").EnumerateArray(),
            sample => sample.GetProperty("passReasonCodes").EnumerateArray().Any(reason => reason.GetString() == "DiagnosticOverEvPositiveButProductionOverEdgeBelowThreshold"));
        Assert.All(
            json.GetProperty("overPositivePassSamples").EnumerateArray(),
            sample => Assert.Equal(
                sample.GetProperty("diagnosticOverEv").GetDecimal(),
                sample.GetProperty("productionOverEv").GetDecimal()));
    }

    [Fact]
    public async Task MlbSideSelectionAudit_ReportsHitProbabilityPolicy_AndSaneOverThreshold()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPredictionStageAuditScenarioAsync(factory);

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.55m, json.GetProperty("minimumPickHitProbability").GetDecimal());
        Assert.Equal(0.60m, json.GetProperty("strongPickHitProbability").GetDecimal());
        Assert.Equal(0.50m, json.GetProperty("researchOnlyBelowHitProbability").GetDecimal());

        var passSample = json.GetProperty("passSamples").EnumerateArray().First();
        var thresholds = passSample.GetProperty("thresholdsApplied");
        Assert.Equal(0.75m, thresholds.GetProperty("overProjectionEdgeThreshold").GetDecimal());
        Assert.Equal(0.75m, thresholds.GetProperty("underProjectionEdgeThreshold").GetDecimal());
        Assert.Equal(0.55m, thresholds.GetProperty("minimumPickHitProbability").GetDecimal());
        Assert.True(json.GetProperty("candidateSideDistributionAfterFix").TryGetProperty("Over", out _)
            || json.GetProperty("candidateSideDistributionAfterFix").TryGetProperty("Under", out _)
            || json.GetProperty("candidateSideDistributionAfterFix").TryGetProperty("Pass", out _));
    }

    [Fact]
    public async Task MlbPredictionGeneration_SelectsOver_WhenProjectionAndHitProbabilityClearGates()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbPredictionGenerationSideSelectionScenarioAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var generator = scope.ServiceProvider.GetRequiredService<IPredictionGenerationService>();
            await generator.GenerateMlbBaselinePredictionsAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var predictions = await dbContext.Predictions
                .AsNoTracking()
                .Include(prediction => prediction.Prop)
                .ThenInclude(prop => prop!.Player)
                .Include(prediction => prediction.Prop)
                .ThenInclude(prop => prop!.PropType)
                .ToListAsync();

            var aaron = predictions.Single(prediction => prediction.Prop!.Player!.DisplayName == "Aaron Nola Style");
            Assert.Equal("OVER", aaron.Recommendation);
            Assert.True(aaron.PredictedProbability >= 0.55m);

            var pete = predictions.Single(prediction => prediction.Prop!.Player!.DisplayName == "Pete Crow-Armstrong Style");
            Assert.Equal("OVER", pete.Recommendation);
            Assert.True(pete.PredictedProbability >= 0.55m);

            var under = predictions.Single(prediction => prediction.Prop!.Player!.DisplayName == "Clear Under Style");
            Assert.Equal("UNDER", under.Recommendation);
            Assert.True(under.PredictedProbability >= 0.55m);

            var pass = predictions.Single(prediction => prediction.Prop!.Player!.DisplayName == "Clear Pass Style");
            Assert.Equal("PASS", pass.Recommendation);
            Assert.Null(pass.PredictedProbability);
        }

        var response = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=20");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("predictionOverSelections").GetInt32() >= 2);
        Assert.Equal(0, json.GetProperty("overEligibleButPassedCount").GetInt32());
        Assert.Equal(0, json.GetProperty("passReasonMissingCount").GetInt32());
        Assert.NotEmpty(json.GetProperty("actualSelectedOverSamples").EnumerateArray());
        Assert.All(
            json.GetProperty("actualSelectedOverSamples").EnumerateArray(),
            sample => Assert.Equal("Over", sample.GetProperty("selectedSide").GetString()));
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbPredictionGeneration_DoesNotInvertOverProbability_WhenPlayerHistoryBeatsLineDespiteOppositePrior()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.SeedMlbPredictionGenerationPriorShrinkageInversionScenarioAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var generator = scope.ServiceProvider.GetRequiredService<IPredictionGenerationService>();
            await generator.GenerateMlbBaselinePredictionsAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var prediction = await dbContext.Predictions
                .AsNoTracking()
                .Include(row => row.Prop)
                .ThenInclude(prop => prop!.Player)
                .SingleAsync(row => row.Prop!.Player!.DisplayName == "Jacob Misiorowski Style");

            Assert.Equal("OVER", prediction.Recommendation);
            Assert.True(prediction.PredictedValue > prediction.SportsbookLine);
            Assert.True(prediction.PredictedProbability > 0.55m);
        }

        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSideSelectionAudit_ExcludesSamplePredictionRuns_ByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbPredictionGenerationSideSelectionScenarioAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var generator = scope.ServiceProvider.GetRequiredService<IPredictionGenerationService>();
            await generator.GenerateMlbBaselinePredictionsAsync(CancellationToken.None);
        }

        var sampleResponse = await client.PostAsync("/api/debug/predictions/sample", content: null);
        await EnsureSuccessWithBodyAsync(sampleResponse);

        var auditResponse = await client.GetAsync("/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=false");

        await EnsureSuccessWithBodyAsync(auditResponse);
        var json = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BaselineAverageModel", json.GetProperty("latestPredictionRunModelName").GetString());
        Assert.NotEqual("SampleModel", json.GetProperty("latestPredictionRunModelName").GetString());
        Assert.True(json.GetProperty("predictionRowsWithLine").GetInt32() > 0);
        Assert.True(json.GetProperty("predictionOverSelections").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("passReasonMissingCount").GetInt32());
        Assert.True(json.GetProperty("filters").GetProperty("excludeSampleRuns").GetBoolean());
    }

    [Fact]
    public async Task Planner_AllowsBudget_WhenLocalDailyAndMonthlyCapsAreConfiguredButEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxDailyCredits"] = "1",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "1"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("plannerEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("dailyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("monthlyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localDailyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localMonthlyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("perRunEstimateBlockingEnabled").GetBoolean());
        Assert.False(plan.GetProperty("providerCreditBlockingEnabled").GetBoolean());
        Assert.False(plan.GetProperty("dailyBudgetEnabled").GetBoolean());
        Assert.Equal(1, plan.GetProperty("maxDailyCredits").GetInt32());
        Assert.True(plan.GetProperty("monthlyBudgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetExceeded").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
        Assert.Equal(1, plan.GetProperty("configuredMonthlyCreditLimit").GetInt32());
        Assert.Equal(2360, plan.GetProperty("providerReportedRequestsUsed").GetInt32());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
    }

    [Fact]
    public async Task Planner_AllowsEstimatedCalls_WhenProviderTelemetryDoesNotBlockAndNoPerRunCap()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxDailyCredits"] = "1",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "1"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("dailyBudgetEnabled").GetBoolean());
        Assert.True(plan.GetProperty("monthlyBudgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetExceeded").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
    }

    [Fact]
    public async Task Planner_IgnoresStaleProviderCreditTelemetry_ForProviderCreditHardStop()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsRefreshPlanner:ProviderTelemetryStaleAfterMinutes"] = "60"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 180);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.True(plan.GetProperty("isProviderTelemetryStale").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
    }

    [Fact]
    public async Task Planner_WarnsButAllowsProviderCreditsExceeded_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
        Assert.False(plan.GetProperty("providerTelemetryUsedForBlocking").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
    }

    [Fact]
    public async Task Planner_BlocksWithProviderCreditsExceeded_WhenEnforcementAndProviderCreditBlockingAreEnabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:EnforcementEnabled"] = "true",
            ["TheOddsRefreshPlanner:BlockOnProviderCredits"] = "true",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("ProviderCreditsExceeded", plan.GetProperty("budgetBlockReason").GetString());
        Assert.True(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
        Assert.True(plan.GetProperty("providerCreditBlockingEnabled").GetBoolean());
        Assert.True(plan.GetProperty("providerTelemetryUsedForBlocking").GetBoolean());
    }

    [Fact]
    public async Task Planner_WarnsButAllowsPerRunEstimateExceeded_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsScheduledRefresh:MaxEstimatedCallsPerRun"] = "5"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("isBlockedByPerRunEstimate").GetBoolean());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
    }

    [Fact]
    public async Task RefreshPlan_UsesSimpleCompletionInterval_AndIgnoresWorkerWakeEstimate()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "true",
            ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "true",
            ["TheOddsScheduledRefresh:ExpectedRefreshDurationSeconds"] = "45",
            ["TheOddsScheduledRefresh:PostRefreshReadBufferSeconds"] = "30"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 14);
        var observedAt = DateTimeOffset.UtcNow;
        var workerWakeAt = observedAt.AddSeconds(90);
        factory.Services.GetRequiredService<ITheOddsScheduledRefreshState>().MarkWaiting(observedAt, workerWakeAt);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lastSuccessfulRefreshCompletedAtUtc = DateTimeOffset.Parse(
            json.GetProperty("lastSuccessfulRefreshCompletedAtUtc").GetString()!,
            CultureInfo.InvariantCulture);
        var nextRefreshAtUtc = DateTimeOffset.Parse(json.GetProperty("nextRefreshAtUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextRefreshDueUtc = DateTimeOffset.Parse(json.GetProperty("nextRefreshDueUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextRefreshStartEstimateUtc = DateTimeOffset.Parse(json.GetProperty("nextRefreshStartEstimateUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextHeaderSyncTargetUtc = DateTimeOffset.Parse(json.GetProperty("nextHeaderSyncTargetUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextDataAvailableEstimateUtc = DateTimeOffset.Parse(json.GetProperty("nextDataAvailableEstimateUtc").GetString()!, CultureInfo.InvariantCulture);
        var conservativeDataAvailableEstimateUtc = DateTimeOffset.Parse(json.GetProperty("conservativeDataAvailableEstimateUtc").GetString()!, CultureInfo.InvariantCulture);
        var workerNextWakeUtc = DateTimeOffset.Parse(json.GetProperty("workerNextWakeUtc").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(
            Math.Abs((nextRefreshAtUtc - lastSuccessfulRefreshCompletedAtUtc.AddMinutes(15)).TotalSeconds),
            0,
            1.5);
        Assert.NotEqual(workerWakeAt.ToUnixTimeSeconds(), nextRefreshAtUtc.ToUnixTimeSeconds());
        Assert.Equal(nextRefreshAtUtc, nextRefreshDueUtc);
        Assert.Equal(nextRefreshAtUtc, nextRefreshStartEstimateUtc);
        Assert.Equal(nextRefreshAtUtc.AddSeconds(30), nextHeaderSyncTargetUtc);
        Assert.Equal(nextHeaderSyncTargetUtc, nextDataAvailableEstimateUtc);
        Assert.Equal(nextHeaderSyncTargetUtc, conservativeDataAvailableEstimateUtc);
        Assert.Equal(nextRefreshAtUtc, workerNextWakeUtc);
        Assert.Equal("SimpleCompletionInterval", json.GetProperty("estimateBasis").GetString());
        Assert.Equal(45, json.GetProperty("expectedRefreshDurationSeconds").GetInt32());
        Assert.Equal(30, json.GetProperty("postRefreshReadBufferSeconds").GetInt32());
        Assert.False(json.GetProperty("isRefreshInProgress").GetBoolean());
        Assert.False(json.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(
            json.GetProperty("secondsUntilNextRefresh").GetInt32(),
            json.GetProperty("secondsUntilNextRefreshStart").GetInt32());
        Assert.Equal(
            json.GetProperty("secondsUntilNextDataAvailable").GetInt32(),
            json.GetProperty("secondsUntilConservativeDataAvailable").GetInt32());
        Assert.InRange(
            json.GetProperty("secondsUntilNextDataAvailable").GetInt32()
                - json.GetProperty("secondsUntilNextRefresh").GetInt32(),
            29,
            31);
    }

    [Fact]
    public async Task RefreshPlan_ReportsInProgressOnlyFromWorkerRunningState()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "true",
            ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "true",
            ["TheOddsScheduledRefresh:ExpectedRefreshDurationSeconds"] = "45",
            ["TheOddsScheduledRefresh:PostRefreshReadBufferSeconds"] = "30"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 15);
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-15);
        factory.Services.GetRequiredService<ITheOddsScheduledRefreshState>().MarkStarted(startedAt);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isRefreshInProgress").GetBoolean());
        Assert.False(json.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("SimpleCompletionInterval", json.GetProperty("estimateBasis").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextRefreshAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextRefreshDueUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextHeaderSyncTargetUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextDataAvailableEstimateUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("conservativeDataAvailableEstimateUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("workerNextWakeUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("secondsUntilNextRefresh").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("secondsUntilNextDataAvailable").ValueKind);
        Assert.Equal(startedAt.ToUnixTimeSeconds(), DateTimeOffset.Parse(json.GetProperty("currentRunStartedAtUtc").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeSeconds());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("currentRunScheduledDueUtc").ValueKind);
        Assert.True(json.GetProperty("currentRunElapsedSeconds").GetDecimal() >= 0m);
        Assert.Equal("RefreshInProgress", json.GetProperty("dataAvailableStatus").GetString());
    }

    [Fact]
    public void ScheduledRefreshWorker_NoCompletionAnchorRunsImmediately()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var plan = CreateScheduledRefreshPlan(
            generatedAtUtc: observedAt,
            nextEligibleRefreshUtc: observedAt,
            shouldRefresh: true,
            targetShouldRefresh: true,
            skipReason: null);

        var nextRefreshAt = TheOddsMlbScheduledRefreshWorker.ResolveNextRefreshAt(
            plan,
            CreateScheduledRefreshStateSnapshot(),
            observedAt,
            TimeSpan.FromMinutes(15));

        Assert.Equal(observedAt, nextRefreshAt);
    }

    [Fact]
    public void ScheduledRefreshWorker_WaitsUntilLastSuccessfulCompletionPlusInterval()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var completedAt = observedAt.AddMinutes(-5);
        var plan = CreateScheduledRefreshPlan(
            generatedAtUtc: observedAt,
            nextEligibleRefreshUtc: completedAt.AddMinutes(15),
            shouldRefresh: false,
            targetShouldRefresh: false,
            skipReason: null);

        var nextRefreshAt = TheOddsMlbScheduledRefreshWorker.ResolveNextRefreshAt(
            plan,
            CreateScheduledRefreshStateSnapshot(lastSuccessfulRefreshCompletedAtUtc: completedAt),
            observedAt,
            TimeSpan.FromMinutes(15));

        Assert.Equal(completedAt.AddMinutes(15), nextRefreshAt);
    }

    [Fact]
    public void ScheduledRefreshWorker_LastCompletedAttemptPreventsTightRetryLoop()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var lastSuccessAt = observedAt.AddMinutes(-30);
        var lastCompletedAttemptAt = observedAt.AddMinutes(-1);
        var plan = CreateScheduledRefreshPlan(
            generatedAtUtc: observedAt,
            nextEligibleRefreshUtc: observedAt,
            shouldRefresh: true,
            targetShouldRefresh: true,
            skipReason: null);

        var nextRefreshAt = TheOddsMlbScheduledRefreshWorker.ResolveNextRefreshAt(
            plan,
            CreateScheduledRefreshStateSnapshot(
                lastRunCompletedAtUtc: lastCompletedAttemptAt,
                lastSuccessfulRefreshCompletedAtUtc: lastSuccessAt),
            observedAt,
            TimeSpan.FromMinutes(15));

        Assert.Equal(lastCompletedAttemptAt.AddMinutes(15), nextRefreshAt);
    }

    [Fact]
    public void ScheduledRefreshWorker_TargetTimingDiagnosticsDetectParallelOverlap()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var timings = new[]
        {
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt, startedAt.AddSeconds(40)),
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt.AddSeconds(1), startedAt.AddSeconds(35)),
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt.AddSeconds(2), startedAt.AddSeconds(20))
        };

        var spread = TheOddsMlbScheduledRefreshWorker.CalculateTargetStartSpreadSeconds(timings);
        var overlapDetected = TheOddsMlbScheduledRefreshWorker.DetectTargetOverlap(timings);

        Assert.Equal(2m, spread);
        Assert.True(overlapDetected);
    }

    [Fact]
    public void ScheduledRefreshWorker_TargetTimingDiagnosticsDoNotReportSequentialOverlap()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var timings = new[]
        {
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt, startedAt.AddSeconds(10)),
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt.AddSeconds(10), startedAt.AddSeconds(20)),
            new TheOddsMlbScheduledRefreshWorker.ScheduledTargetTiming(startedAt.AddSeconds(20), startedAt.AddSeconds(30))
        };

        var spread = TheOddsMlbScheduledRefreshWorker.CalculateTargetStartSpreadSeconds(timings);
        var overlapDetected = TheOddsMlbScheduledRefreshWorker.DetectTargetOverlap(timings);

        Assert.Equal(20m, spread);
        Assert.False(overlapDetected);
    }

    [Fact]
    public async Task RecentRefreshRuns_DoesNotCountSameCycleSiblingOverlapAsForbidden()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var cycleId = Guid.NewGuid();
        var startedAt = DateTimeOffset.Parse("2026-06-28T21:00:00Z", CultureInfo.InvariantCulture);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.TheOddsRefreshRuns.AddRange(
                CreateTheOddsRun(
                    Guid.NewGuid(),
                    "MLB",
                    startedAt,
                    startedAt.AddSeconds(45),
                    "dfsOnly",
                    "DFS",
                    new { scheduledCycleId = cycleId, targetKey = "mlb-dfs", targetIngestMode = "Parallel" }),
                CreateTheOddsRun(
                    Guid.NewGuid(),
                    "MLB",
                    startedAt.AddSeconds(2),
                    startedAt.AddSeconds(35),
                    "default",
                    "Sportsbook",
                    new { scheduledCycleId = cycleId, targetKey = "mlb-sportsbook", targetIngestMode = "Parallel" }));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/debug/the-odds/refresh-runs/recent?take=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("rowsWithOverlap").GetInt32());
        Assert.Equal(0, json.GetProperty("rowsStartedWhileAnotherRunActive").GetInt32());
        Assert.Equal(1, json.GetProperty("parallelSiblingOverlapCount").GetInt32());
        Assert.Contains(
            json.GetProperty("runs").EnumerateArray(),
            row => row.GetProperty("parallelSiblingOverlapDetected").GetBoolean()
                && !row.GetProperty("forbiddenOverlapDetected").GetBoolean());
    }

    [Fact]
    public async Task RecentRefreshRuns_CountsDifferentCycleOverlapAsForbidden()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var startedAt = DateTimeOffset.Parse("2026-06-28T21:00:00Z", CultureInfo.InvariantCulture);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.TheOddsRefreshRuns.AddRange(
                CreateTheOddsRun(
                    Guid.NewGuid(),
                    "MLB",
                    startedAt,
                    startedAt.AddSeconds(45),
                    "dfsOnly",
                    "DFS",
                    new { scheduledCycleId = Guid.NewGuid(), targetKey = "mlb-dfs", targetIngestMode = "Parallel" }),
                CreateTheOddsRun(
                    Guid.NewGuid(),
                    "MLB",
                    startedAt.AddSeconds(2),
                    startedAt.AddSeconds(35),
                    "default",
                    "Sportsbook",
                    new { scheduledCycleId = Guid.NewGuid(), targetKey = "mlb-sportsbook", targetIngestMode = "Parallel" }));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/debug/the-odds/refresh-runs/recent?take=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("rowsWithOverlap").GetInt32());
        Assert.Equal(1, json.GetProperty("rowsStartedWhileAnotherRunActive").GetInt32());
        Assert.Equal(0, json.GetProperty("parallelSiblingOverlapCount").GetInt32());
    }

    [Fact]
    public async Task MarketFreshness_ClassifiesScheduledFinalizationSeparatelyFromProviderIngestion()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var providerRunId = Guid.NewGuid();
        var finalizationRunId = Guid.NewGuid();
        var startedAt = DateTimeOffset.Parse("2026-06-28T21:00:00Z", CultureInfo.InvariantCulture);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var providerRun = CreateTheOddsRun(
                providerRunId,
                "MLB",
                startedAt,
                startedAt.AddSeconds(20),
                "default",
                "Sportsbook",
                new
                {
                    ingestion = new[] { new { success = true } },
                    lineShoppingSummary = new { activeGroups = 1, staleOrExpiredGroups = 0 }
                });
            providerRun.ActualApiCalls = 12;
            var finalizationRun = CreateTheOddsRun(
                finalizationRunId,
                "MLB",
                startedAt.AddMinutes(1),
                startedAt.AddMinutes(1).AddSeconds(5),
                "scheduledCycleFinalization",
                "DFS,Sportsbook",
                new
                {
                    targetIngestMode = "ParallelFinalization",
                    publicBoardPublicationSkipped = true,
                    publicBoardPublicationSkipReason = "DeadSlateNoActionableFutureLinesPreservedPriorUsableBoard"
                });
            finalizationRun.ActualApiCalls = 0;
            dbContext.TheOddsRefreshRuns.AddRange(providerRun, finalizationRun);
            dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = providerRunId,
                Sport = "MLB",
                SlateDate = new DateOnly(2026, 6, 28),
                GeneratedAtUtc = startedAt.AddSeconds(21),
                BookmakerPreset = "default",
                ProviderCategory = "Sportsbook",
                Player = "Test Player",
                EventStartTime = startedAt.AddHours(2),
                LifecycleStatus = "Active",
                IsStale = false,
                IsExpired = false,
                PropType = "Hits",
                Line = 0.5m,
                BestSide = "Over",
                StrategyClassification = "Candidate",
                StrategyTier = "MlbSportsbookRecommendation",
                EntryProvider = "Sportsbook",
                EntryCompatibilityGroup = "Sportsbook",
                EntryProviderCategory = "Sportsbook",
                DetailsJson = JsonSerializer.SerializeToDocument(new { player = "Test Player" })
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/markets/freshness?sport=MLB&freshnessMinutes=9999");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(providerRunId, json.GetProperty("latestSuccessfulRefresh").GetProperty("id").GetGuid());
        Assert.Equal(providerRunId, json.GetProperty("latestProviderIngestionRun").GetProperty("id").GetGuid());
        Assert.Equal(finalizationRunId, json.GetProperty("latestFinalizationRun").GetProperty("id").GetGuid());
        Assert.Equal(providerRunId, json.GetProperty("latestUsablePublicSnapshotRun").GetProperty("id").GetGuid());
        Assert.Equal(1, json.GetProperty("providerIngestionSuccessCount").GetInt32());
        Assert.Equal(providerRunId, json.GetProperty("providerStatus").GetProperty("latestRefreshRunId").GetGuid());
        Assert.Equal(12, json.GetProperty("providerStatus").GetProperty("actualApiCalls").GetInt32());
    }

    [Fact]
    public async Task PublicSnapshots_ExcludePreservedRowsAfterEventStart_AndFreshnessReportsNoCurrentBoard()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var refreshRunId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var completedAt = startedAt.AddSeconds(30);
        var eventStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        var capturedAt = eventStart.AddMinutes(-14);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var refreshRun = CreateTheOddsRun(
                refreshRunId,
                "MLB",
                startedAt,
                completedAt,
                "default",
                "Sportsbook",
                new
                {
                    ingestion = new[] { new { success = true } },
                    lineShoppingSummary = new { activeGroups = 1, staleOrExpiredGroups = 0 }
                });
            dbContext.TheOddsRefreshRuns.Add(refreshRun);
            var details = JsonSerializer.SerializeToDocument(new
            {
                sport = "MLB",
                competition = "MLB",
                eventId = "expired-event-1",
                eventStartTime = eventStart,
                eventStartTimeUtc = eventStart,
                lastObservedAt = capturedAt,
                player = "Expired Snapshot Player",
                playerName = "Expired Snapshot Player",
                normalizedPropType = "Hits",
                line = 0.5m,
                providerCategory = "Sportsbook",
                bestBookmaker = "FanDuel",
                bookmaker = "FanDuel",
                bestSide = "Over",
                selectedSide = "Over",
                bestAmericanPrice = -110,
                bestDecimalPrice = 1.91m,
                bestImpliedProbability = 0.52356m,
                selectedSideProbability = 0.62m,
                bestProbability = 0.62m,
                bestEstimatedEv = 0.1842m,
                displayExpectedValue = 0.1842m,
                rankingScore = 0.7m,
                entryActionableForUser = false,
                strategyClassification = "Candidate",
                strategyTier = "MlbSportsbookRecommendation",
                isCandidate = true,
                isRecommendation = true,
                recommendationActionableForUser = true,
                recommendationType = "Sportsbook",
                lifecycleStatus = "Active",
                isFresh = true,
                isStale = false,
                isExpired = false,
                minutesUntilStart = 13.1m,
                lifecycleReason = "Event starts in 13.1 minutes",
                lifecycleReasons = new[] { "Event starts in 13.1 minutes" }
            });
            var lineSnapshot = new MarketLineShoppingSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRunId,
                Sport = "MLB",
                SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
                GeneratedAtUtc = capturedAt,
                BookmakerPreset = "default",
                ProviderCategory = "Sportsbook",
                Provider = "The Odds API",
                Bookmaker = "FanDuel",
                Player = "Expired Snapshot Player",
                EventId = "expired-event-1",
                EventName = "Away @ Home",
                EventStartTime = eventStart,
                LifecycleStatus = "Active",
                IsStale = false,
                IsExpired = false,
                PropType = "Hits",
                Line = 0.5m,
                BestSide = "Over",
                BestBookmaker = "FanDuel",
                BestAmericanPrice = -110,
                BestDecimalPrice = 1.91m,
                BestProbability = 0.62m,
                BestImpliedProbability = 0.52356m,
                BestProbabilityEdge = 0.09644m,
                BestEstimatedEv = 0.1842m,
                StrategyClassification = "Candidate",
                StrategyTier = "MlbSportsbookRecommendation",
                IsCandidate = true,
                EntryProvider = "FanDuel",
                EntryCompatibilityGroup = "Sportsbook",
                EntryProviderCategory = "Sportsbook",
                EntryActionableForUser = false,
                DetailsJson = details
            };
            dbContext.MarketLineShoppingSnapshots.Add(lineSnapshot);
            dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRunId,
                Sport = lineSnapshot.Sport,
                SlateDate = lineSnapshot.SlateDate,
                GeneratedAtUtc = lineSnapshot.GeneratedAtUtc,
                BookmakerPreset = lineSnapshot.BookmakerPreset,
                ProviderCategory = lineSnapshot.ProviderCategory,
                Provider = lineSnapshot.Provider,
                Bookmaker = lineSnapshot.Bookmaker,
                Player = lineSnapshot.Player,
                EventId = lineSnapshot.EventId,
                EventName = lineSnapshot.EventName,
                EventStartTime = lineSnapshot.EventStartTime,
                LifecycleStatus = lineSnapshot.LifecycleStatus,
                IsStale = lineSnapshot.IsStale,
                IsExpired = lineSnapshot.IsExpired,
                PropType = lineSnapshot.PropType,
                Line = lineSnapshot.Line,
                BestSide = lineSnapshot.BestSide,
                BestBookmaker = lineSnapshot.BestBookmaker,
                BestAmericanPrice = lineSnapshot.BestAmericanPrice,
                BestDecimalPrice = lineSnapshot.BestDecimalPrice,
                BestProbability = lineSnapshot.BestProbability,
                BestImpliedProbability = lineSnapshot.BestImpliedProbability,
                BestProbabilityEdge = lineSnapshot.BestProbabilityEdge,
                BestEstimatedEv = lineSnapshot.BestEstimatedEv,
                StrategyClassification = lineSnapshot.StrategyClassification,
                StrategyTier = lineSnapshot.StrategyTier,
                IsCandidate = lineSnapshot.IsCandidate,
                EntryProvider = lineSnapshot.EntryProvider,
                EntryCompatibilityGroup = lineSnapshot.EntryCompatibilityGroup,
                EntryProviderCategory = lineSnapshot.EntryProviderCategory,
                EntryActionableForUser = lineSnapshot.EntryActionableForUser,
                DetailsJson = details
            });
            await dbContext.SaveChangesAsync();
        }

        var topResponse = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25&freshnessMinutes=9999");
        var lineResponse = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=Sportsbook&take=25&freshnessMinutes=9999");
        var freshnessResponse = await client.GetAsync("/api/markets/freshness?sport=MLB&freshnessMinutes=9999");
        var auditResponse = await client.GetAsync("/api/debug/the-odds/latest-market-lifecycle-audit?sport=MLB&providerCategory=Sportsbook&freshnessMinutes=9999&take=20");

        await EnsureSuccessWithBodyAsync(topResponse);
        await EnsureSuccessWithBodyAsync(lineResponse);
        await EnsureSuccessWithBodyAsync(freshnessResponse);
        await EnsureSuccessWithBodyAsync(auditResponse);
        var top = await topResponse.Content.ReadFromJsonAsync<JsonElement>();
        var line = await lineResponse.Content.ReadFromJsonAsync<JsonElement>();
        var freshness = await freshnessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var audit = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(top.GetProperty("topPicks").EnumerateArray());
        Assert.Contains("no currently actionable", top.GetProperty("fallbackReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(line.GetProperty("comparisonGroups").EnumerateArray());
        Assert.Contains("no currently actionable", line.GetProperty("fallbackReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(freshness.GetProperty("hasUsableMarketData").GetBoolean());
        Assert.Equal("NoCurrentlyActionableRows", freshness.GetProperty("currentPublicBoardStatus").GetString());
        Assert.Equal(0, freshness.GetProperty("currentActionableSnapshotRowCount").GetInt32());
        Assert.True(freshness.GetProperty("preservedSnapshotExpiredAtReadTime").GetBoolean());
        Assert.Equal(0, audit.GetProperty("recomputedActiveGroups").GetInt32());
        Assert.True(audit.GetProperty("countEventStarted").GetInt32() > 0);
    }

    [Fact]
    public async Task ScheduledRefreshStatus_ExposesSchedulerPrecisionFields()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "true",
            ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "true",
            ["TheOddsScheduledRefresh:PostRefreshReadBufferSeconds"] = "30"
        });
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-21);
        var completedAt = startedAt.AddSeconds(11);
        var nextWakeAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var state = factory.Services.GetRequiredService<ITheOddsScheduledRefreshState>();
        state.MarkStarted(startedAt);
        state.MarkCompleted(completedAt);
        state.MarkWaiting(DateTimeOffset.UtcNow, nextWakeAt);

        var response = await client.GetAsync("/api/debug/the-odds/scheduled-refresh/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.TryGetProperty("nowUtc", out _));
        var nextRefreshAtUtc = DateTimeOffset.Parse(json.GetProperty("nextRefreshAtUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextRefreshDueUtc = DateTimeOffset.Parse(json.GetProperty("nextRefreshDueUtc").GetString()!, CultureInfo.InvariantCulture);
        var nextDataAvailableEstimateUtc = DateTimeOffset.Parse(json.GetProperty("nextDataAvailableEstimateUtc").GetString()!, CultureInfo.InvariantCulture);
        var workerNextWakeUtc = DateTimeOffset.Parse(json.GetProperty("workerNextWakeUtc").GetString()!, CultureInfo.InvariantCulture);
        var lastSuccessfulRefreshCompletedAtUtc = DateTimeOffset.Parse(
            json.GetProperty("lastSuccessfulRefreshCompletedAtUtc").GetString()!,
            CultureInfo.InvariantCulture);
        Assert.InRange(
            Math.Abs((nextRefreshAtUtc - completedAt.AddMinutes(15)).TotalSeconds),
            0,
            1.5);
        Assert.Equal(completedAt.ToUnixTimeSeconds(), lastSuccessfulRefreshCompletedAtUtc.ToUnixTimeSeconds());
        Assert.Equal(nextRefreshAtUtc, nextRefreshDueUtc);
        Assert.Equal(nextRefreshAtUtc.AddSeconds(30), nextDataAvailableEstimateUtc);
        Assert.Equal(nextRefreshAtUtc, workerNextWakeUtc);
        Assert.NotEqual(nextWakeAt.ToUnixTimeSeconds(), nextRefreshAtUtc.ToUnixTimeSeconds());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastRunScheduledDueUtc").ValueKind);
        Assert.Equal(startedAt.ToUnixTimeSeconds(), DateTimeOffset.Parse(json.GetProperty("lastRunStartedAtUtc").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeSeconds());
        Assert.Equal(completedAt.ToUnixTimeSeconds(), DateTimeOffset.Parse(json.GetProperty("lastRunCompletedAtUtc").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeSeconds());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastRunStartedDelaySeconds").ValueKind);
        Assert.Equal(11m, json.GetProperty("lastRunDurationSeconds").GetDecimal());
        Assert.False(json.GetProperty("isRefreshInProgress").GetBoolean());
        Assert.True(json.TryGetProperty("secondsUntilNextRefreshDue", out _));
        Assert.True(json.TryGetProperty("secondsUntilNextDataAvailable", out _));
        Assert.True(json.TryGetProperty("currentRunScheduledDueUtc", out _));
        Assert.True(json.TryGetProperty("currentRunElapsedSeconds", out _));
        Assert.True(json.TryGetProperty("currentRunEstimatedRemainingSeconds", out _));
        Assert.True(json.TryGetProperty("dataAvailableStatus", out _));
        Assert.True(json.TryGetProperty("lastRunPhaseTimings", out _));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("currentRunStartedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastError").ValueKind);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task Planner_BlocksWithPerRunEstimateExceeded_WhenEnforcementAndPerRunBlockingAreEnabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:EnforcementEnabled"] = "true",
            ["TheOddsRefreshPlanner:BlockOnPerRunEstimate"] = "true",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsScheduledRefresh:MaxEstimatedCallsPerRun"] = "5"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("PerRunEstimateExceeded", plan.GetProperty("budgetBlockReason").GetString());
        Assert.True(plan.GetProperty("isBlockedByPerRunEstimate").GetBoolean());
        Assert.True(plan.GetProperty("perRunEstimateBlockingEnabled").GetBoolean());
    }

    [Fact]
    public async Task RefreshPlan_WiresSoccerWorldCupSportsbookTarget_ButKeepsUnverifiedSoccerLeaguesSkipped()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "true",
            ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "true",
            ["TheOddsScheduledRefresh:Targets:2:Enabled"] = "true",
            ["TheOddsScheduledRefresh:Targets:2:DisabledReason"] = null,
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_shots"
        });
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var targets = json.GetProperty("targets").EnumerateArray().ToArray();
        var worldCup = targets.Single(row => row.GetProperty("key").GetString() == "soccer-world-cup-sportsbook");
        var epl = targets.Single(row => row.GetProperty("key").GetString() == "soccer-epl-sportsbook");

        Assert.True(worldCup.GetProperty("enabled").GetBoolean());
        Assert.True(worldCup.GetProperty("executionWired").GetBoolean());
        Assert.True(worldCup.GetProperty("providerSportKeyVerified").GetBoolean());
        Assert.Contains(worldCup.GetProperty("effectiveBookmakers").EnumerateArray(), row => row.GetString() == "bovada");
        Assert.True(worldCup.GetProperty("shouldRefresh").GetBoolean());
        Assert.Equal(JsonValueKind.Null, worldCup.GetProperty("skipReason").ValueKind);
        Assert.False(epl.GetProperty("enabled").GetBoolean());
        Assert.False(epl.GetProperty("executionWired").GetBoolean());
        Assert.False(epl.GetProperty("providerSportKeyVerified").GetBoolean());
        Assert.Contains("The Odds EPL sport key needs verification", epl.GetProperty("skipReason").GetString());
        Assert.Contains("MissingProviderSportKey", epl.GetProperty("skipReason").GetString());
        Assert.Contains("ScheduledExecutionNotYetWiredForTarget", epl.GetProperty("skipReason").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task RefreshPlan_AttributesLatestRunsToExactTargetOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "true",
            ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "false"
        });
        using var client = factory.CreateClient();
        var mlbDfsRunId = Guid.NewGuid();
        var mlbSportsbookRunId = Guid.NewGuid();
        var worldCupRunId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.TheOddsRefreshRuns.AddRange(
                new TheOddsRefreshRun
                {
                    Id = mlbDfsRunId,
                    Provider = "The Odds API",
                    Sport = "MLB",
                    SlateDate = new DateOnly(2026, 6, 26),
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-29),
                    Succeeded = true,
                    BookmakerPreset = "dfsOnly",
                    ProviderCategoriesIncluded = "DFS",
                    ActualApiCalls = 8,
                    PlatformPropsCreated = 1,
                    PlatformPropsUpdated = 2,
                    PlatformPropsUpserted = 3
                },
                new TheOddsRefreshRun
                {
                    Id = mlbSportsbookRunId,
                    Provider = "The Odds API",
                    Sport = "MLB",
                    SlateDate = new DateOnly(2026, 6, 26),
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-19),
                    Succeeded = true,
                    BookmakerPreset = "default",
                    ProviderCategoriesIncluded = "Sportsbook",
                    ActualApiCalls = 9,
                    PlatformPropsCreated = 4,
                    PlatformPropsUpdated = 5,
                    PlatformPropsUpserted = 6
                },
                new TheOddsRefreshRun
                {
                    Id = worldCupRunId,
                    Provider = "The Odds API",
                    Sport = "SOCCER",
                    SlateDate = new DateOnly(2026, 6, 26),
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
                    Succeeded = true,
                    BookmakerPreset = "soccerSportsbookContext",
                    ProviderCategoriesIncluded = "Sportsbook",
                    ActualApiCalls = 10,
                    PlatformPropsCreated = 7,
                    PlatformPropsUpdated = 8,
                    PlatformPropsUpserted = 9,
                    DiagnosticsJson = JsonSerializer.SerializeToDocument(new
                    {
                        sport = "SOCCER",
                        competition = "World Cup"
                    })
                });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var targets = json.GetProperty("targets").EnumerateArray().ToArray();
        var mlbDfs = targets.Single(row => row.GetProperty("key").GetString() == "mlb-dfs");
        var mlbSportsbook = targets.Single(row => row.GetProperty("key").GetString() == "mlb-sportsbook");
        var worldCup = targets.Single(row => row.GetProperty("key").GetString() == "soccer-world-cup-sportsbook");
        var epl = targets.Single(row => row.GetProperty("key").GetString() == "soccer-epl-sportsbook");

        Assert.Equal(mlbDfsRunId, mlbDfs.GetProperty("latestRefreshRunId").GetGuid());
        Assert.Equal(8, mlbDfs.GetProperty("actualApiCalls").GetInt32());
        Assert.Contains(mlbDfs.GetProperty("effectiveBookmakers").EnumerateArray(), row => row.GetString() == "prizepicks");
        Assert.Contains(mlbDfs.GetProperty("effectiveBookmakers").EnumerateArray(), row => row.GetString() == "betr_us_dfs");
        Assert.Equal(mlbSportsbookRunId, mlbSportsbook.GetProperty("latestRefreshRunId").GetGuid());
        Assert.Equal(9, mlbSportsbook.GetProperty("actualApiCalls").GetInt32());
        Assert.Contains(mlbSportsbook.GetProperty("effectiveBookmakers").EnumerateArray(), row => row.GetString() == "bovada");
        Assert.Contains(mlbSportsbook.GetProperty("effectiveBookmakers").EnumerateArray(), row => row.GetString() == "mybookieag");
        Assert.Equal(worldCupRunId, worldCup.GetProperty("latestRefreshRunId").GetGuid());
        Assert.Equal(10, worldCup.GetProperty("actualApiCalls").GetInt32());
        Assert.Equal(JsonValueKind.Null, epl.GetProperty("latestRefreshRunId").ValueKind);
        Assert.Equal(JsonValueKind.Null, epl.GetProperty("latestRunSucceeded").ValueKind);
        Assert.Equal(JsonValueKind.Null, epl.GetProperty("actualApiCalls").ValueKind);
        Assert.Equal(JsonValueKind.Null, epl.GetProperty("rowsCreated").ValueKind);

        var latestEnabledTargetSuccessUtc = targets
            .Where(row => row.GetProperty("enabled").GetBoolean()
                && row.GetProperty("latestSuccessfulRefreshCompletedAtUtc").ValueKind == JsonValueKind.String)
            .Select(row => DateTimeOffset.Parse(
                row.GetProperty("latestSuccessfulRefreshCompletedAtUtc").GetString()!,
                CultureInfo.InvariantCulture))
            .Max();
        var nextRefreshAtUtc = DateTimeOffset.Parse(
            json.GetProperty("nextRefreshAtUtc").GetString()!,
            CultureInfo.InvariantCulture);
        var nextHeaderSyncTargetUtc = DateTimeOffset.Parse(
            json.GetProperty("nextHeaderSyncTargetUtc").GetString()!,
            CultureInfo.InvariantCulture);
        var nextDataAvailableEstimateUtc = DateTimeOffset.Parse(
            json.GetProperty("nextDataAvailableEstimateUtc").GetString()!,
            CultureInfo.InvariantCulture);
        var workerNextWakeUtc = DateTimeOffset.Parse(
            json.GetProperty("workerNextWakeUtc").GetString()!,
            CultureInfo.InvariantCulture);
        var nextRefreshDueUtc = DateTimeOffset.Parse(
            json.GetProperty("nextRefreshDueUtc").GetString()!,
            CultureInfo.InvariantCulture);
        Assert.InRange(
            Math.Abs((nextRefreshAtUtc - latestEnabledTargetSuccessUtc.AddMinutes(15)).TotalSeconds),
            0,
            1.5);
        Assert.Equal(nextRefreshAtUtc, nextRefreshDueUtc);
        Assert.Equal(nextRefreshAtUtc.AddSeconds(30), nextHeaderSyncTargetUtc);
        Assert.Equal(nextHeaderSyncTargetUtc, nextDataAvailableEstimateUtc);
        Assert.Equal(nextRefreshAtUtc, workerNextWakeUtc);
    }

    [Fact]
    public async Task DryRunRefreshCurrent_DoesNotCallProvider_AndDoesNotWriteRefreshArtifacts()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsync(
            "/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true&bookmakerPreset=dfsOnly",
            content: null);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostics = json.GetProperty("diagnostics");
        Assert.False(diagnostics.GetProperty("wouldCallTheOddsApi").GetBoolean());
        Assert.False(diagnostics.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DryRun_DoesNotCallProvider_AndReportsMissingConfig()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = string.Empty,
            ["TheOddsApi:SoccerWorldCupMarkets"] = string.Empty
        });
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = true,
            forceLive = false
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldCallExternalApis").GetBoolean());
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("World Cup", json.GetProperty("competition").GetString());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "TheOddsApi:SoccerWorldCupSportKey");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_WithoutForceLive_DoesNotCallProvider()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = false,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("wouldCallExternalApis").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_ForceLiveWithMissingConfig_DoesNotCallProvider()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = string.Empty,
            ["TheOddsApi:SoccerWorldCupMarkets"] = string.Empty
        });
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = false,
            forceLive = true
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingSoccerWorldCupProviderConfig", json.GetProperty("blockReason").GetString());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "TheOddsApi:SoccerWorldCupSportKey");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_ForceLive_PersistsProviderEventContext()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(2, factory.TheOddsAdapter.CallCount);
        Assert.Equal(1, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(2, json.GetProperty("soccerMarketsDiscovered").GetInt32());
        Assert.False(json.GetProperty("diagnostics").GetProperty("providerPlayerIdAvailable").GetBoolean());
        Assert.False(json.GetProperty("diagnostics").GetProperty("playerTeamAvailable").GetBoolean());
        Assert.Equal(1, json.GetProperty("diagnostics").GetProperty("normalizedPropsCount").GetInt32());
        Assert.False(json.GetProperty("diagnostics").GetProperty("noProviderMarketsReturned").GetBoolean());
        Assert.False(json.GetProperty("diagnostics").GetProperty("providerMarketsReturnedButNoRowsMapped").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("mappedMarketsReturned").EnumerateArray(), row => row.GetString() == "player_shots");

        var contextResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true&maxSamples=10");
        await EnsureSuccessWithBodyAsync(contextResponse);
        var context = await contextResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasEventNameCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasStartTimeCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasProviderEventIdCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasLeagueOrCompetitionCount").GetInt32() > 0);
        Assert.Equal(0, context.GetProperty("fieldAvailability").GetProperty("hasProviderPlayerIdCount").GetInt32());
        Assert.NotEqual("PreContextOrMissingContext", context.GetProperty("contextEnrichment").GetProperty("status").GetString());
        var sample = Assert.Single(context.GetProperty("samples").EnumerateArray());
        Assert.Equal("Away FC @ Home FC", sample.GetProperty("eventName").GetString());
        Assert.Equal("soccer-event-1", sample.GetProperty("providerEventId").GetString());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_Provider422_ReturnsStructuredProviderRejection()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "bad_market"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupProviderRejection();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadGateway);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("providerRejectedRequest").GetBoolean());
        Assert.Equal("ProviderRejectedRequest", json.GetProperty("blockReason").GetString());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        var providerError = json.GetProperty("providerError");
        Assert.Equal(422, providerError.GetProperty("statusCode").GetInt32());
        Assert.Equal("FetchEventOdds", providerError.GetProperty("operation").GetString());
        Assert.Contains("bad_market", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.DoesNotContain("TEST_THE_ODDS_API_KEY", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.Contains("Invalid market", providerError.GetProperty("rawProviderBody").GetString());
        var rejectedInputs = json.GetProperty("diagnostics").GetProperty("likelyRejectedInputs");
        Assert.Equal("soccer_fifa_world_cup", rejectedInputs.GetProperty("sportKey").GetString());
        Assert.Equal("bad_market", rejectedInputs.GetProperty("markets").GetString());
        Assert.Equal("soccer-event-1", rejectedInputs.GetProperty("eventId").GetString());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(2, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DefaultsToSportsbookContext_PersistsResearchOnlyRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_assists,player_shots,player_shots_on_target,player_tackles",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us",
            ["TheOddsApi:SoccerWorldCupDfsRegion"] = "us_dfs"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.True(json.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(json.GetProperty("sportsbookContextNotDfsPlayable").GetBoolean());
        Assert.Equal(1, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(1, json.GetProperty("sportsbookContextRowsCreated").GetInt32());
        Assert.Equal(0, json.GetProperty("dfsRowsCreated").GetInt32());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("configuredMarketsReturned").EnumerateArray(), row => row.GetString() == "player_goals");
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("configuredMarketsMissing").EnumerateArray(), row => row.GetString() == "player_tackles");

        var metadata = await GetSingleSoccerPlatformPropMetadataAsync(factory);
        Assert.Equal("Sportsbook", metadata.GetProperty("providerCategory").GetString());
        Assert.Equal("us", metadata.GetProperty("region").GetString());
        Assert.False(metadata.GetProperty("isActionableForUser").GetBoolean());
        Assert.False(metadata.GetProperty("isIndicativeOdds").GetBoolean());
        var entry = metadata.GetProperty("entry");
        Assert.Equal("Sportsbook", entry.GetProperty("providerCategory").GetString());
        Assert.False(entry.GetProperty("actionableForUser").GetBoolean());
        Assert.Contains("research-only", entry.GetProperty("ruleWarning").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("soccer_fifa_world_cup", metadata.GetProperty("sport").GetProperty("providerSportKey").GetString());
        Assert.Equal("World Cup", metadata.GetProperty("sport").GetProperty("competition").GetString());
        Assert.Equal("betmgm", metadata.GetProperty("bookmaker").GetProperty("key").GetString());
        Assert.Equal("player_goals", metadata.GetProperty("market").GetProperty("key").GetString());
        var eventInfo = metadata.GetProperty("eventInfo");
        Assert.Equal("soccer-event-1", eventInfo.GetProperty("providerEventId").GetString());
        Assert.Equal("Home FC", eventInfo.GetProperty("homeTeam").GetString());
        Assert.Equal("Away FC", eventInfo.GetProperty("awayTeam").GetString());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DryRunForceLive_PlansEligibleWindowEvents_AndWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_shots",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupMultiEventSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            fromDate = "2026-06-21",
            toDate = "2026-06-24",
            dryRun = true,
            forceLive = true,
            maxEvents = 2,
            includeSportsbook = true,
            bookmakers = new[] { "fanduel", "betmgm", "betrivers" },
            markets = new[] { "player_goals", "player_shots" }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.Equal(3, json.GetProperty("eventsDiscovered").GetInt32());
        Assert.Equal(3, json.GetProperty("eventsEligible").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsSkipped").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsToFetch").GetInt32());
        Assert.Equal(3, json.GetProperty("estimatedApiCalls").GetInt32());
        Assert.Equal(1, json.GetProperty("skipReasons").GetProperty("maxEventsLimit").GetInt32());
        Assert.Equal(2, json.GetProperty("perEventExpectedRequests").GetArrayLength());
        Assert.Equal(1, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_RealMode_PersistsMultipleProviderEvents_RespectingMaxEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_shots",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupMultiEventSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            fromDate = "2026-06-21",
            toDate = "2026-06-24",
            dryRun = false,
            forceLive = true,
            maxEvents = 2,
            includeSportsbook = true,
            bookmakers = new[] { "fanduel", "betmgm", "betrivers" },
            markets = new[] { "player_goals", "player_shots" }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.True(json.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(json.GetProperty("sportsbookContextNotDfsPlayable").GetBoolean());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(3, json.GetProperty("eventsDiscovered").GetInt32());
        Assert.Equal(3, json.GetProperty("eventsEligible").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsWithRows").GetInt32());
        Assert.Equal(0, json.GetProperty("eventsWithoutRows").GetInt32());
        Assert.Equal(2, json.GetProperty("providerEventsPersisted").GetInt32());
        Assert.Equal(2, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(2, json.GetProperty("sportsbookContextRowsCreated").GetInt32());
        Assert.Equal(0, json.GetProperty("dfsRowsCreated").GetInt32());
        Assert.Equal(3, factory.TheOddsAdapter.CallCount);

        var rowsByProviderEvent = json.GetProperty("rowsByProviderEvent");
        Assert.Equal(1, rowsByProviderEvent.GetProperty("soccer-event-1").GetInt32());
        Assert.Equal(1, rowsByProviderEvent.GetProperty("soccer-event-2").GetInt32());
        Assert.False(rowsByProviderEvent.TryGetProperty("soccer-event-3", out _));
        Assert.Equal(2, await CountSoccerPlatformPropsAsync(factory));

        var metadataRows = await GetSoccerPlatformPropMetadataAsync(factory);
        Assert.Equal(2, metadataRows.Length);
        Assert.All(metadataRows, metadata =>
        {
            Assert.Equal("the-odds-provider-context-v2", metadata.GetProperty("contextSchemaVersion").GetString());
            Assert.Equal("Sportsbook", metadata.GetProperty("providerCategory").GetString());
            Assert.False(metadata.GetProperty("isActionableForUser").GetBoolean());
            Assert.Equal("soccer_fifa_world_cup", metadata.GetProperty("sport").GetProperty("providerSportKey").GetString());
            Assert.Equal("World Cup", metadata.GetProperty("sport").GetProperty("competition").GetString());
            var providerEventId = metadata.GetProperty("eventInfo").GetProperty("providerEventId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(providerEventId));
            Assert.DoesNotContain("|balanced", providerEventId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("|alternate", providerEventId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SCR_game", providerEventId, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("zeroBookmakers", 0, 0, 0, "NoBookmakersReturned")]
    [InlineData("zeroMarkets", 1, 0, 0, "NoMarketsReturned")]
    [InlineData("unsupportedMarket", 1, 1, 2, "UnsupportedMarketsReturned")]
    [InlineData("configuredMarketZeroOutcomes", 1, 1, 0, "ConfiguredMarketsReturnedWithNoOutcomes")]
    public async Task SportNeutralSoccerRefresh_ZeroRowDiagnostics_ExplainProviderShape(
        string payloadMode,
        int expectedBookmakerCount,
        int expectedMarketCount,
        int expectedOutcomeCount,
        string expectedSkipReason)
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupContextPayload(payloadMode);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
        var diagnostics = json.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(1, diagnostics.GetProperty("events").GetArrayLength());
        var eventDiagnostics = diagnostics.GetProperty("events")[0];
        Assert.Equal("soccer-event-1", eventDiagnostics.GetProperty("providerEventId").GetString());
        Assert.Equal(expectedBookmakerCount, eventDiagnostics.GetProperty("bookmakerCount").GetInt32());
        Assert.Equal(expectedMarketCount, eventDiagnostics.GetProperty("marketCount").GetInt32());
        Assert.Equal(expectedOutcomeCount, eventDiagnostics.GetProperty("outcomeCount").GetInt32());
        Assert.Equal(0, eventDiagnostics.GetProperty("normalizedPropsCount").GetInt32());
        Assert.Contains(
            eventDiagnostics.GetProperty("skipReasons").EnumerateArray(),
            row => row.GetString() == expectedSkipReason);

        if (payloadMode == "unsupportedMarket")
        {
            Assert.Contains(json.GetProperty("soccerMarketsUnsupported").EnumerateArray(), row => row.GetString() == "player_passes");
            Assert.True(diagnostics.GetProperty("providerMarketsReturnedButNoRowsMapped").GetBoolean());
        }

        if (payloadMode is "zeroBookmakers" or "zeroMarkets")
        {
            Assert.True(diagnostics.GetProperty("noProviderMarketsReturned").GetBoolean());
        }
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_WithoutForceLive_DoesNotCallProviderOrWrite()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21");

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_ComparesRegions_WithoutWritingRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupAvailabilityByRegion();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs,us&markets=player_shots&maxEvents=1&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(3, json.GetProperty("estimatedLiveCalls").GetInt32());
        Assert.Equal(3, json.GetProperty("actualLiveCalls").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsMatchedToSlate").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsWithBookmakers").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsWithMarkets").GetInt32());
        Assert.True(json.GetProperty("hasAnyProviderLines").GetBoolean());
        var events = json.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(2, events.Length);
        var dfsEvent = Assert.Single(events, row => row.GetProperty("requestedRegions").GetString() == "us_dfs");
        Assert.Equal(0, dfsEvent.GetProperty("bookmakerCount").GetInt32());
        var usEvent = Assert.Single(events, row => row.GetProperty("requestedRegions").GetString() == "us");
        Assert.Equal(1, usEvent.GetProperty("bookmakerCount").GetInt32());
        Assert.Contains(usEvent.GetProperty("bookmakerKeysReturned").EnumerateArray(), row => row.GetString() == "prizepicks");
        Assert.Contains(usEvent.GetProperty("configuredMarketsReturned").EnumerateArray(), row => row.GetString() == "player_shots");
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_Provider422_IsStructuredAndDoesNotWriteRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "bad_market"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupProviderRejection();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs&markets=bad_market&maxEvents=1&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(1, json.GetProperty("providerRejectedCount").GetInt32());
        var eventRow = Assert.Single(json.GetProperty("events").EnumerateArray());
        Assert.True(eventRow.GetProperty("providerRejectedRequest").GetBoolean());
        Assert.False(eventRow.GetProperty("eventOddsHttpSucceeded").GetBoolean());
        var providerError = eventRow.GetProperty("providerError");
        Assert.Equal(422, providerError.GetProperty("statusCode").GetInt32());
        Assert.Equal("FetchEventOdds", providerError.GetProperty("operation").GetString());
        Assert.Contains("bad_market", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.DoesNotContain("TEST_THE_ODDS_API_KEY", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.Contains("Invalid market", providerError.GetProperty("rawProviderBody").GetString());
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task LiveShapedRefreshCurrent_WithoutForceLive_Returns409_AndDoesNotCallProviderOrWrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsync(
            "/api/debug/the-odds/mlb/recommendations/refresh-current?bookmakerPreset=dfsOnly&includeCandidates=true&includeLineShopping=true&includeFullDiagnostics=true&freshnessMinutes=30",
            content: null);

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Theory]
    [InlineData("/api/markets/freshness")]
    [InlineData("/api/markets/provider-status")]
    [InlineData("/api/markets/refresh-plan")]
    [InlineData("/api/markets/refresh-plan/history?take=20")]
    [InlineData("/api/markets/top-picks?take=10")]
    [InlineData("/api/markets/line-shopping?take=10&includeBookmakerPrices=false")]
    [InlineData("/api/markets/performance-summary?candidateOnly=true")]
    [InlineData("/api/dfs/entry-rules")]
    public async Task PublicMarketEndpoints_AreReadOnly_AndDoNotCallProviders(string path)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync(path);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, factory.UnderdogAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task EntryRules_SeparateBuildableDfsProvidersFromContextOnlySportsbooks()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dfs/entry-rules");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var buildableProviders = json.GetProperty("entryBuildableProviders").EnumerateArray().ToArray();
        var contextOnlyProviders = json.GetProperty("contextOnlyProviders").EnumerateArray().ToArray();
        var rules = json.GetProperty("rules").EnumerateArray().ToArray();

        Assert.Contains(buildableProviders, row => row.GetProperty("provider").GetString() == "PrizePicks");
        Assert.Contains(buildableProviders, row => row.GetProperty("provider").GetString() == "Underdog");
        Assert.Contains(contextOnlyProviders, row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.DoesNotContain(rules, row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.All(rules, row => Assert.True(row.TryGetProperty("payoutMultipliers", out _)));
        var prizePicks = rules.Single(row => row.GetProperty("provider").GetString() == "PrizePicks");
        var prizePicksMultipliers = prizePicks.GetProperty("payoutMultipliers").EnumerateArray().ToArray();
        Assert.Equal(9, prizePicksMultipliers.Length);
        AssertMultiplier(prizePicksMultipliers, "Power", 2, 3m);
        AssertMultiplier(prizePicksMultipliers, "Power", 3, 6m);
        AssertMultiplier(prizePicksMultipliers, "Power", 4, 10m);
        AssertMultiplier(prizePicksMultipliers, "Power", 5, 20m);
        AssertMultiplier(prizePicksMultipliers, "Power", 6, 37.5m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 3, 3m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 4, 6m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 5, 10m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 6, 25m);
        Assert.Equal("FixedMultiplierTableWithPartialHitRows", prizePicks.GetProperty("payoutModelType").GetString());
        Assert.True(prizePicks.GetProperty("hasPartialHitPayouts").GetBoolean());
        Assert.False(prizePicks.GetProperty("partialHitPayoutsModeled").GetBoolean());

        foreach (var provider in new[] { "Underdog", "DraftKings Pick6" })
        {
            var rule = rules.Single(row => row.GetProperty("provider").GetString() == provider);
            Assert.Equal("UnknownOrUnavailable", rule.GetProperty("payoutModelType").GetString());
            Assert.False(rule.GetProperty("hasPartialHitPayouts").GetBoolean());
            Assert.False(rule.GetProperty("partialHitPayoutsModeled").GetBoolean());
            Assert.Empty(rule.GetProperty("payoutMultipliers").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("payoutRulesNotes").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("sourceUrl").GetString()));
        }

        var betrPicks = rules.Single(row => row.GetProperty("provider").GetString() == "Betr Picks");
        Assert.Equal("VariableMultiplier", betrPicks.GetProperty("payoutModelType").GetString());
        Assert.True(betrPicks.GetProperty("hasPartialHitPayouts").GetBoolean());
        Assert.False(betrPicks.GetProperty("partialHitPayoutsModeled").GetBoolean());
        Assert.Empty(betrPicks.GetProperty("payoutMultipliers").EnumerateArray());
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 }, betrPicks.GetProperty("allowedLegCounts").EnumerateArray().Select(row => row.GetInt32()).ToArray());
        Assert.Contains(betrPicks.GetProperty("entryTypes").EnumerateArray(), row => row.GetString() == "Perfect Play");
        Assert.Contains(betrPicks.GetProperty("entryTypes").EnumerateArray(), row => row.GetString() == "Flex Play");
    }

    [Fact]
    public async Task ScheduledWorkerStatus_DefaultsDisabled_AndShowsPlannerSkip()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/debug/the-odds/mlb/scheduled-refresh/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("enabled").GetBoolean());
        Assert.False(json.GetProperty("allowScheduledLiveRefresh").GetBoolean());
        Assert.Equal("disabled", json.GetProperty("status").GetString());
        var currentPlan = json.GetProperty("currentPlan");
        Assert.False(currentPlan.GetProperty("shouldRefresh").GetBoolean());
        Assert.False(currentPlan.GetProperty("providerAvailable").GetBoolean());
        Assert.Equal("Unavailable", currentPlan.GetProperty("providerStatus").GetString());
    }

    [Fact]
    public async Task Planner_BlocksWhenProviderUnavailableOrCreditsBelowHardStop()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("shouldRefresh").GetBoolean());
        Assert.Equal("ProviderRejectedKey", plan.GetProperty("skipReason").GetString());
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("providerAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
    }

    [Fact]
    public async Task Planner_BlocksOnRecentProviderQuotaFailureBackoff_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedProviderQuotaFailureAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("shouldRefresh").GetBoolean());
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("RecentProviderFailureBackoff", plan.GetProperty("skipReason").GetString());
    }

    [Fact]
    public async Task ProviderStatus_DetectsRejectedKeyFromPersistedDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/markets/provider-status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var provider = json.GetProperty("providers")[0];
        Assert.Equal("Unavailable", provider.GetProperty("status").GetString());
        Assert.True(provider.GetProperty("providerRejectedKeyDetected").GetBoolean());
        Assert.Equal(498, provider.GetProperty("requestsUsed").GetInt32());
        Assert.Equal(2, provider.GetProperty("requestsRemaining").GetInt32());
    }

    [Fact]
    public async Task ProviderStatus_ClarifiesRequestTelemetryIsTheOddsOnly()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/markets/provider-status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal("TheOddsApiMarketRefreshOnly", json.GetProperty("requestTelemetryScope").GetString());
        var telemetry = json.GetProperty("providerRequestTelemetry");
        Assert.False(telemetry.GetProperty("includesApiSportsFootballResultCalls").GetBoolean());
        Assert.False(telemetry.GetProperty("apiSportsFootballRequestTelemetryPersisted").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ScheduledRefreshStatus_ExposesSoccerResultImportWorkerStateWithoutExternalCalls()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.GetAsync("/api/debug/the-odds/scheduled-refresh/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var soccerImport = json
            .GetProperty("resultImportWorkflows")
            .GetProperty("soccerWorldCup");
        Assert.Equal("API-Sports Football", soccerImport.GetProperty("provider").GetString());
        Assert.Equal("Disabled", soccerImport.GetProperty("status").GetString());
        Assert.True(soccerImport.GetProperty("implemented").GetBoolean());
        Assert.True(soccerImport.GetProperty("workerExists").GetBoolean());
        Assert.False(soccerImport.GetProperty("workerEnabled").GetBoolean());
        Assert.False(soccerImport.GetProperty("scheduledImportEnabled").GetBoolean());
        Assert.False(soccerImport.GetProperty("hourlyWorkerPullsSoccerOutcomes").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ScheduledRefreshStatus_ReportsSoccerResultImportNotConfiguredWhenEnabledWithoutApiSports()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["SoccerResultImportWorker:Enabled"] = "true"
        });
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.GetAsync("/api/debug/the-odds/scheduled-refresh/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var soccerImport = json
            .GetProperty("resultImportWorkflows")
            .GetProperty("soccerWorldCup");
        Assert.Equal("NotConfigured", soccerImport.GetProperty("status").GetString());
        Assert.True(soccerImport.GetProperty("implemented").GetBoolean());
        Assert.True(soccerImport.GetProperty("workerExists").GetBoolean());
        Assert.True(soccerImport.GetProperty("workerEnabled").GetBoolean());
        Assert.False(soccerImport.GetProperty("scheduledImportEnabled").GetBoolean());
        Assert.False(soccerImport.GetProperty("hourlyWorkerPullsSoccerOutcomes").GetBoolean());
        Assert.Contains("ApiSports:Enabled is false", soccerImport.GetProperty("latestErrorSummary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SnapshotReads_ReturnSnapshotBackedFalse_WhenNoUsableRowsMatch()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync(includeStaleSnapshot: true);

        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?take=10");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?take=10&includeBookmakerPrices=false");
        await EnsureSuccessWithBodyAsync(topPicksResponse);
        await EnsureSuccessWithBodyAsync(lineShoppingResponse);
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(topPicks.GetProperty("snapshotBacked").GetBoolean());
        Assert.False(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task PublicMarketSnapshots_ExposeExpectedValueDisplaySemantics_ByProviderCategory()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();

        var dfsResponse = await client.GetAsync("/api/markets/line-shopping?providerCategory=DFS&take=10&includeBookmakerPrices=false");
        var sportsbookResponse = await client.GetAsync("/api/markets/line-shopping?providerCategory=Sportsbook&topOnly=false&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(dfsResponse);
        await EnsureSuccessWithBodyAsync(sportsbookResponse);
        AssertNoCaseInsensitiveDuplicatePropertiesInComparisonGroups(await dfsResponse.Content.ReadAsStringAsync());
        AssertNoCaseInsensitiveDuplicatePropertiesInComparisonGroups(await sportsbookResponse.Content.ReadAsStringAsync());
        var dfsJson = await dfsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sportsbookJson = await sportsbookResponse.Content.ReadFromJsonAsync<JsonElement>();

        var dfsRow = dfsJson.GetProperty("comparisonGroups")[0];
        Assert.Equal("DFS", dfsRow.GetProperty("providerCategory").GetString());
        Assert.True(dfsRow.GetProperty("isDfs").GetBoolean());
        Assert.False(dfsRow.GetProperty("isSportsbook").GetBoolean());
        Assert.False(dfsRow.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, dfsRow.GetProperty("displayExpectedValue").ValueKind);
        Assert.Equal(
            "EV is hidden for DFS props because sportsbook odds/break-even pricing is not available.",
            dfsRow.GetProperty("expectedValueUnavailableReason").GetString());

        var sportsbookRows = sportsbookJson.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        var pricedSportsbookRow = sportsbookRows.Single(row => row.GetProperty("player").GetString() == "Priced Sportsbook Player");
        Assert.Equal("Sportsbook", pricedSportsbookRow.GetProperty("providerCategory").GetString());
        Assert.False(pricedSportsbookRow.GetProperty("isDfs").GetBoolean());
        Assert.True(pricedSportsbookRow.GetProperty("isSportsbook").GetBoolean());
        Assert.True(pricedSportsbookRow.GetProperty("showExpectedValue").GetBoolean());
        Assert.True(pricedSportsbookRow.GetProperty("rawExpectedValue").GetDecimal() > pricedSportsbookRow.GetProperty("displayExpectedValue").GetDecimal());
        Assert.Equal(pricedSportsbookRow.GetProperty("calibratedSelectedSideProbability").GetDecimal(), pricedSportsbookRow.GetProperty("selectedSideProbability").GetDecimal());
        Assert.Equal(pricedSportsbookRow.GetProperty("calibratedExpectedValue").GetDecimal(), pricedSportsbookRow.GetProperty("displayExpectedValue").GetDecimal());
        Assert.Equal(JsonValueKind.Null, pricedSportsbookRow.GetProperty("expectedValueUnavailableReason").ValueKind);

        var missingPricingRow = sportsbookRows.Single(row => row.GetProperty("player").GetString() == "Unpriced Sportsbook Player");
        Assert.False(missingPricingRow.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, missingPricingRow.GetProperty("displayExpectedValue").ValueKind);
        Assert.Equal("Sportsbook EV requires model probability and real odds.", missingPricingRow.GetProperty("expectedValueUnavailableReason").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Theory]
    [InlineData(165, 2.65)]
    [InlineData(-123, 1.813008)]
    [InlineData(-175, 1.571429)]
    [InlineData(-220, 1.454545)]
    [InlineData(-400, 1.25)]
    public void SportsbookExpectedValue_ConvertsAmericanOddsToDecimal(int americanPrice, decimal expectedDecimalPrice)
    {
        var decimalPrice = TheOddsApiDebugEndpoints.ConvertAmericanToDecimalForTesting(americanPrice);

        Assert.Equal(expectedDecimalPrice, decimalPrice);
    }

    [Theory]
    [InlineData(0.55, 165, 0.4575)]
    [InlineData(0.6866, -123, 0.244811)]
    [InlineData(0.65, -175, 0.021429)]
    [InlineData(0.60, -220, -0.127273)]
    [InlineData(0.80, -400, 0.0)]
    public void SportsbookExpectedValue_UsesSelectedProbabilityTimesDecimalOddsMinusOne(
        decimal selectedProbability,
        int americanPrice,
        decimal expectedValue)
    {
        var decimalPrice = TheOddsApiDebugEndpoints.ConvertAmericanToDecimalForTesting(americanPrice);
        var actual = TheOddsApiDebugEndpoints.CalculateExpectedValueForTesting(selectedProbability, decimalPrice);

        Assert.Equal(expectedValue, actual);
    }

    [Fact]
    public async Task PublicLineShopping_ProviderCategoryAllCombinesDfsAndSportsbookRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();
        await factory.SeedNewerDfsOnlyExpectedValueSnapshotAsync();

        var allResponse = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=All&topOnly=false&take=10&includeBookmakerPrices=false");
        var dfsResponse = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=DFS&topOnly=false&take=10&includeBookmakerPrices=false");
        var sportsbookResponse = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=Sportsbook&topOnly=false&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(allResponse);
        await EnsureSuccessWithBodyAsync(dfsResponse);
        await EnsureSuccessWithBodyAsync(sportsbookResponse);

        var allJson = await allResponse.Content.ReadFromJsonAsync<JsonElement>();
        var dfsJson = await dfsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sportsbookJson = await sportsbookResponse.Content.ReadFromJsonAsync<JsonElement>();
        var allGroups = allJson.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        var dfsGroups = dfsJson.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        var sportsbookGroups = sportsbookJson.GetProperty("comparisonGroups").EnumerateArray().ToArray();

        Assert.NotEmpty(allGroups);
        Assert.Contains(allGroups, row => row.GetProperty("providerCategory").GetString() == "DFS");
        Assert.Contains(allGroups, row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.Equal(dfsGroups.Length + sportsbookGroups.Length, allGroups.Length);
        Assert.Equal(allGroups.Length, allJson.GetProperty("summary").GetProperty("returnedComparisonGroups").GetInt32());
        Assert.Contains(allJson.GetProperty("summary").GetProperty("providerCategoryBreakdown").EnumerateArray(), row => row.GetProperty("providerCategory").GetString() == "DFS");
        Assert.Contains(allJson.GetProperty("summary").GetProperty("providerCategoryBreakdown").EnumerateArray(), row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookTopPicks_AreRecommendationsButNotEntryBuilderLegs()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();
        await factory.SeedMlbLikelyHitSportsbookTopPickSnapshotsForPublicReadAsync(includeSuppressedEvPick: false);

        var response = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=10");

        await EnsureSuccessWithBodyAsync(response);
        AssertNoCaseInsensitiveDuplicatePropertiesInTopPicks(await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var picks = json.GetProperty("topPicks").EnumerateArray().ToArray();
        var pick = picks.Single(row => row.GetProperty("player").GetString() == "Priced Sportsbook Player");
        Assert.False(pick.TryGetProperty("IsDfs", out _));
        Assert.False(pick.TryGetProperty("IsSportsbook", out _));
        Assert.False(pick.TryGetProperty("ExpectedValueUnavailableReason", out _));

        Assert.Equal("Sportsbook", pick.GetProperty("providerCategory").GetString());
        Assert.True(pick.GetProperty("isRecommendation").GetBoolean());
        Assert.True(pick.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.Equal("Sportsbook", pick.GetProperty("recommendationType").GetString());
        Assert.False(pick.GetProperty("entryActionableForUser").GetBoolean());
        Assert.Equal("Sportsbook", pick.GetProperty("entryProviderCategory").GetString());
        Assert.Equal("MLB sportsbook recommendation. Entry Builder is available only for DFS entries.", pick.GetProperty("entryRuleWarning").GetString());
        Assert.True(pick.GetProperty("selectedSideProbability").GetDecimal() >= 0.65m);
        Assert.Equal(0.65m, pick.GetProperty("publicTopPickMinimumHitChance").GetDecimal());
        Assert.True(pick.GetProperty("hitChanceGatePassed").GetBoolean());
        Assert.True(pick.GetProperty("publicTopPickHitChanceGatePassed").GetBoolean());
        Assert.True(pick.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.Equal("EmpiricalMeanFromProbabilityPlayerHistory", pick.GetProperty("recommendationProjectionSource").GetString());
        Assert.True(pick.GetProperty("isProjectionRecommendationAligned").GetBoolean());
        Assert.False(pick.GetProperty("projectionProbabilityConflict").GetBoolean());
        Assert.False(pick.GetProperty("recentWeightedProjectionProbabilityConflict").GetBoolean());
        Assert.True(pick.GetProperty("showExpectedValue").GetBoolean());
        Assert.True(pick.GetProperty("displayExpectedValue").GetDecimal() > 0);
        Assert.True(pick.GetProperty("rawExpectedValue").GetDecimal() > pick.GetProperty("displayExpectedValue").GetDecimal());
        Assert.True(pick.GetProperty("calibratedExpectedValue").GetDecimal() > 0);
        Assert.Equal(JsonValueKind.Null, pick.GetProperty("evSuppressedReason").ValueKind);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookTopPicks_AreLikelyHitFirstAndSuppressUnsafeEvDisplay()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();
        await factory.SeedMlbLikelyHitSportsbookTopPickSnapshotsForPublicReadAsync(includeSuppressedEvPick: true);

        var response = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var picks = json.GetProperty("topPicks").EnumerateArray().ToArray();

        Assert.DoesNotContain(picks, row => row.GetProperty("player").GetString() == "Borderline Sportsbook Inventory Player");
        Assert.DoesNotContain(picks, row => row.GetProperty("player").GetString() == "Negative EV Sportsbook Player");
        var unsafeEvPick = picks.Single(row => row.GetProperty("player").GetString() == "Low Sample Huge EV Sportsbook Player");
        Assert.Equal("Low Sample Huge EV Sportsbook Player", unsafeEvPick.GetProperty("playerName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(unsafeEvPick.GetProperty("provider").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(unsafeEvPick.GetProperty("bookmaker").GetString()));
        Assert.True(unsafeEvPick.GetProperty("isRecommendation").GetBoolean());
        Assert.True(unsafeEvPick.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.True(unsafeEvPick.GetProperty("selectedSideProbability").GetDecimal() >= 0.65m);
        Assert.True(unsafeEvPick.GetProperty("publicTopPickHitChanceGatePassed").GetBoolean());
        Assert.False(unsafeEvPick.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unsafeEvPick.GetProperty("displayExpectedValue").ValueKind);
        Assert.Contains("model-vs-market", unsafeEvPick.GetProperty("evSuppressedReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.All(picks, row =>
        {
            Assert.True(row.GetProperty("isRecommendation").GetBoolean());
            Assert.True(row.GetProperty("recommendationActionableForUser").GetBoolean());
            if (string.Equals(row.GetProperty("providerCategory").GetString(), "Sportsbook", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(row.GetProperty("selectedSideProbability").GetDecimal() >= 0.65m);
                Assert.True(row.GetProperty("publicTopPickHitChanceGatePassed").GetBoolean());
                Assert.False(row.GetProperty("usesAggregateProbability").GetBoolean());
                Assert.True(row.GetProperty("selectedSideHistoryConsistencyGuardApplied").GetBoolean());
                Assert.True(row.GetProperty("selectedSideHistoryConsistencyGuardPassed").GetBoolean());
                Assert.Empty(row.GetProperty("fatalSelectedSideHistoryConsistencyCodes").EnumerateArray());
            }

            if (row.GetProperty("showExpectedValue").GetBoolean())
            {
                Assert.True(row.GetProperty("displayExpectedValue").GetDecimal() > 0m);
                Assert.Equal(JsonValueKind.Null, row.GetProperty("evSuppressedReason").ValueKind);
            }
        });
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task LikelyHitTopPickConsistencyAudit_IsReadOnlyAndReportsSelectedSideHistory()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/debug/model/likely-hit/top-pick-consistency-audit?providerCategory=Sportsbook&take=50");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(TopPicksModelOptions.DefaultLikelyHitV1ModelEra, json.GetProperty("currentModelEra").GetString());
        Assert.True(json.GetProperty("totalRowsEvaluated").GetInt32() > 0);
        Assert.True(json.GetProperty("totalPublicTopPicks").GetInt32() >= 1);

        var examples = json.GetProperty("examples").EnumerateArray().ToArray();
        Assert.NotEmpty(examples);
        Assert.Contains(examples, row => row.TryGetProperty("selectedSideHistoryConsistencyGuardApplied", out _));
        Assert.Contains(examples, row => row.TryGetProperty("chartHitRate", out _));
        Assert.Contains(examples, row => row.TryGetProperty("recent10Average", out _));
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookLineShopping_SuppressesHugeRawEvAndNegativeEvPromotion()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=Sportsbook&topOnly=false&take=25&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var groups = json.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        var hugeEv = groups.Single(row => row.GetProperty("player").GetString() == "Low Sample Huge EV Sportsbook Player");
        var negativeEv = groups.Single(row => row.GetProperty("player").GetString() == "Negative EV Sportsbook Player");
        var borderline = groups.Single(row => row.GetProperty("player").GetString() == "Borderline Sportsbook Inventory Player");

        Assert.True(borderline.GetProperty("modelProbabilityAvailable").GetBoolean());

        Assert.True(hugeEv.GetProperty("rawExpectedValue").GetDecimal() > 0.30m);
        Assert.True(hugeEv.GetProperty("calibratedExpectedValue").GetDecimal() > 0m);
        Assert.Equal(JsonValueKind.Null, hugeEv.GetProperty("displayExpectedValue").ValueKind);
        Assert.False(hugeEv.GetProperty("showExpectedValue").GetBoolean());
        Assert.False(hugeEv.GetProperty("isCandidate").GetBoolean());
        Assert.False(hugeEv.GetProperty("isRecommendation").GetBoolean());
        Assert.Contains("model-vs-market", hugeEv.GetProperty("evSuppressedReason").GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.True(negativeEv.GetProperty("rawExpectedValue").GetDecimal() <= 0m);
        Assert.True(negativeEv.GetProperty("calibratedExpectedValue").GetDecimal() <= 0m);
        Assert.Equal(JsonValueKind.Null, negativeEv.GetProperty("displayExpectedValue").ValueKind);
        Assert.False(negativeEv.GetProperty("showExpectedValue").GetBoolean());
        Assert.False(negativeEv.GetProperty("isCandidate").GetBoolean());
        Assert.False(negativeEv.GetProperty("isRecommendation").GetBoolean());
        Assert.Equal("Calibrated EV is not positive.", negativeEv.GetProperty("evSuppressedReason").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task LikelyHitOpportunityAudit_ReportsSportsbookPublicTopPickHitChanceGate()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/debug/model/likely-hit/opportunity-audit?sport=MLB&providerCategory=Sportsbook&includeRejected=true&take=50");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("summary").GetProperty("sportsbookRowsBlockedByPublicHitChance").GetInt32() > 0);
        var borderline = json.GetProperty("rows")
            .EnumerateArray()
            .Single(row => row.GetProperty("player").GetString() == "Borderline Sportsbook Inventory Player");

        Assert.Equal(0.65m, borderline.GetProperty("publicTopPickMinimumHitChance").GetDecimal());
        Assert.InRange(borderline.GetProperty("hitChance").GetDecimal(), 0.60m, 0.649999m);
        Assert.False(borderline.GetProperty("publicTopPickHitChanceGatePassed").GetBoolean());
        Assert.False(borderline.GetProperty("topPickEligible").GetBoolean());
        Assert.Contains(
            borderline.GetProperty("fatalConflictCodes").EnumerateArray(),
            reason => reason.GetString() == "BelowSportsbookPublicTopPickHitChance");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbDfsTopPicks_DoNotEmitDuplicateExpectedValueCasing()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();

        var response = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=DFS&take=10");

        await EnsureSuccessWithBodyAsync(response);
        AssertNoCaseInsensitiveDuplicatePropertiesInTopPicks(await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pick = Assert.Single(json.GetProperty("topPicks").EnumerateArray());
        Assert.False(pick.TryGetProperty("IsDfs", out _));
        Assert.False(pick.TryGetProperty("IsSportsbook", out _));
        Assert.False(pick.TryGetProperty("ExpectedValueUnavailableReason", out _));
        Assert.True(pick.GetProperty("isDfs").GetBoolean());
        Assert.False(pick.GetProperty("isSportsbook").GetBoolean());
        Assert.False(pick.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookTopPicks_NormalizeUnsafeOldSnapshotEvBeforeReturning()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();

        var response = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var picks = json.GetProperty("topPicks").EnumerateArray().ToArray();

        Assert.DoesNotContain(picks, row => row.GetProperty("player").GetString() == "Unsafe Old Sportsbook Snapshot Player");
        var pick = Assert.Single(picks, row => row.GetProperty("player").GetString() == "Priced Sportsbook Player");
        Assert.True(pick.GetProperty("rawExpectedValue").GetDecimal() > pick.GetProperty("calibratedExpectedValue").GetDecimal());
        Assert.Equal(pick.GetProperty("calibratedSelectedSideProbability").GetDecimal(), pick.GetProperty("selectedSideProbability").GetDecimal());
        Assert.True(pick.GetProperty("displayExpectedValue").GetDecimal() is > 0m and <= 0.30m);
        Assert.Equal(JsonValueKind.Null, pick.GetProperty("evSuppressedReason").ValueKind);
        Assert.All(picks, row =>
        {
            Assert.True(row.GetProperty("displayExpectedValue").GetDecimal() is > 0m and <= 0.30m);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("evSuppressedReason").ValueKind);
            Assert.True(row.GetProperty("isRecommendation").GetBoolean());
        });
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task TopPickLedgerCaptureAudit_ReportsMissingCurrentEraSportsbookLedgerRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshots = await dbContext.MarketTopPickSnapshots.ToArrayAsync();
            foreach (var snapshot in snapshots)
            {
                snapshot.ModelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra;
            }

            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/debug/performance/top-pick-ledger-capture-audit");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(TopPicksModelOptions.DefaultLikelyHitV1ModelEra, json.GetProperty("currentModelEra").GetString());
        Assert.Equal("persistedTopPickSnapshots", json.GetProperty("publicTopPickSource").GetString());
        var publicTopPickCount = json.GetProperty("publicTopPickCount").GetInt32();
        Assert.True(publicTopPickCount > 0);
        Assert.Equal(0, json.GetProperty("recommendationLedgerCountForCurrentSlate").GetInt32());
        Assert.Equal(publicTopPickCount, json.GetProperty("missingLedgerCount").GetInt32());
        Assert.Contains(
            json.GetProperty("missingLedgerByProviderCategory").EnumerateArray(),
            row => row.GetProperty("providerCategory").GetString() == "Sportsbook" && row.GetProperty("count").GetInt32() > 0);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task LikelyHitReplay_IsReadOnlyAndReportsCalibrationBuckets()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();
        var before = await factory.CountArtifactsAsync();
        var propResultCountBefore = await factory.CountPropResultsAsync();

        var response = await client.GetAsync("/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&includeSamples=true&sampleTake=5&maxRows=100");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal("LikelyHitV1", json.GetProperty("modelVersion").GetString());
        Assert.True(json.GetProperty("replayRowsEvaluated").GetInt32() > 0);
        Assert.True(json.GetProperty("replayableRows").GetInt32() >= 0);
        Assert.NotEmpty(json.GetProperty("hitChanceBuckets").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("propShapeBuckets").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("oddsBuckets").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("sideBuckets").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("effectiveSampleBuckets").EnumerateArray());
        var borderline = json.GetProperty("borderlineHitChanceAnalysis");
        Assert.Equal("60-65", borderline.GetProperty("bucket").GetString());
        Assert.Equal("Sportsbook", borderline.GetProperty("providerCategory").GetString());
        Assert.True(borderline.GetProperty("diagnosticOnly").GetBoolean());
        var borderlineSegments = borderline.GetProperty("segments");
        Assert.True(borderlineSegments.TryGetProperty("oddsBucket", out _));
        Assert.True(borderlineSegments.TryGetProperty("breakEvenMarginBucket", out _));
        Assert.True(borderlineSegments.TryGetProperty("propShape", out _));
        Assert.True(borderlineSegments.TryGetProperty("effectiveSampleSizeBucket", out _));
        Assert.True(borderlineSegments.TryGetProperty("recentAgreementBucket", out _));
        Assert.True(borderlineSegments.TryGetProperty("marketGapBucket", out _));
        Assert.True(borderlineSegments.TryGetProperty("bookmaker", out _));
        Assert.True(borderlineSegments.TryGetProperty("side", out _));
        Assert.True(borderline.GetProperty("total").GetInt32() > 0);
        Assert.True(json.TryGetProperty("borderlineProfileRecommendations", out _));
        Assert.True(json.TryGetProperty("currentBoardBorderlineSummary", out _));
        Assert.False(json.GetProperty("simulation").GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("borderlineSamples").TryGetProperty("best", out _));
        var publicPerformance = json.GetProperty("publicTopPickPerformanceAnalysis");
        Assert.True(publicPerformance.GetProperty("diagnosticOnly").GetBoolean());
        Assert.True(publicPerformance.GetProperty("total").GetInt32() >= 0);
        var publicSegments = publicPerformance.GetProperty("segments");
        Assert.True(publicSegments.TryGetProperty("hitChanceBucket", out var publicHitChanceBuckets));
        Assert.True(publicSegments.TryGetProperty("propShape", out var publicPropShapeBuckets));
        Assert.True(publicSegments.TryGetProperty("oddsBucket", out _));
        Assert.True(publicSegments.TryGetProperty("breakEvenMarginBucket", out _));
        Assert.True(publicSegments.TryGetProperty("effectiveSampleSizeBucket", out _));
        Assert.True(publicSegments.TryGetProperty("side", out _));
        Assert.True(publicSegments.TryGetProperty("bookmaker", out _));
        Assert.True(publicSegments.TryGetProperty("providerCategory", out _));
        Assert.True(publicSegments.TryGetProperty("sportCompetition", out _));
        Assert.True(publicSegments.TryGetProperty("balancedUtilityBucket", out _));
        Assert.True(publicSegments.TryGetProperty("payoutQualityBucket", out _));
        Assert.True(publicSegments.TryGetProperty("sweetSpotScoreBucket", out _));
        Assert.NotEmpty(publicHitChanceBuckets.EnumerateArray());
        Assert.NotEmpty(publicPropShapeBuckets.EnumerateArray());
        Assert.All(publicHitChanceBuckets.EnumerateArray(), bucket =>
            Assert.NotEqual("<65", bucket.GetProperty("key").GetString()));
        var concentration = json.GetProperty("publicTopPickConcentrationAnalysis");
        Assert.True(concentration.GetProperty("diagnosticOnly").GetBoolean());
        Assert.Equal(json.GetProperty("topPickRows").GetInt32(), concentration.GetProperty("currentTopPickCount").GetInt32());
        Assert.True(concentration.GetProperty("topPropShapes").EnumerateArray().Any());
        Assert.True(concentration.GetProperty("maxShapeShare").GetDecimal() > 0.25m);
        Assert.True(concentration.GetProperty("overConcentratedShapes").EnumerateArray().Any());
        var profileRecommendations = json.GetProperty("publicTopPickProfileRecommendations").EnumerateArray().ToArray();
        Assert.NotEmpty(profileRecommendations);
        Assert.Contains(profileRecommendations, profile => profile.GetProperty("recommendation").GetString() == "NeedsMoreData");
        Assert.All(profileRecommendations, profile =>
        {
            Assert.True(profile.TryGetProperty("profileKey", out _));
            Assert.True(profile.TryGetProperty("hitChanceBucket", out _));
            Assert.True(profile.TryGetProperty("recommendation", out _));
        });
        var publicSimulation = json.GetProperty("publicTopPickSimulation");
        Assert.False(publicSimulation.GetProperty("enabled").GetBoolean());
        Assert.True(publicSimulation.GetProperty("diagnosticOnly").GetBoolean());
        Assert.Equal(publicPerformance.GetProperty("total").GetInt32(), publicSimulation.GetProperty("baselineTopPickRows").GetInt32());
        Assert.Equal("NoHistoricalGradedRows", publicSimulation.GetProperty("simulationUnavailableReason").GetString());
        var currentSummary = json.GetProperty("currentPublicTopPickSummary");
        Assert.Equal(json.GetProperty("topPickRows").GetInt32(), currentSummary.GetProperty("currentTopPickCount").GetInt32());
        Assert.True(currentSummary.TryGetProperty("byPropShape", out _));
        Assert.True(currentSummary.TryGetProperty("byOddsBucket", out _));
        Assert.True(currentSummary.TryGetProperty("byHitChanceBucket", out _));
        Assert.True(currentSummary.TryGetProperty("byBreakEvenMargin", out _));
        Assert.True(currentSummary.TryGetProperty("byEffectiveSampleBucket", out _));
        Assert.True(currentSummary.TryGetProperty("byBookmaker", out _));
        Assert.True(currentSummary.TryGetProperty("bySide", out _));
        var publicSamples = json.GetProperty("publicTopPickSamples");
        Assert.True(publicSamples.TryGetProperty("best", out _));
        Assert.True(publicSamples.TryGetProperty("worst", out _));
        Assert.True(publicSamples.TryGetProperty("largestConcentration", out _));
        Assert.True(publicSamples.TryGetProperty("ungradedCurrent", out _));
        Assert.False(json.GetProperty("recommendations").GetProperty("below60TopPickLeakDetected").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(propResultCountBefore, await factory.CountPropResultsAsync());
    }

    [Fact]
    public async Task LikelyHitReplay_PublicTopPickPerformanceUsesHistoricalReplayRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbLikelyHitHistoricalReplaySnapshotsAsync();
        var before = await factory.CountArtifactsAsync();
        var propResultCountBefore = await factory.CountPropResultsAsync();

        var response = await client.GetAsync("/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&includeSamples=true&sampleTake=5&maxRows=100");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var publicPerformance = json.GetProperty("publicTopPickPerformanceAnalysis");
        Assert.Equal(3, publicPerformance.GetProperty("total").GetInt32());
        Assert.Equal(3, publicPerformance.GetProperty("replayable").GetInt32());
        Assert.Equal(3, publicPerformance.GetProperty("graded").GetInt32());
        Assert.Equal(2, publicPerformance.GetProperty("wins").GetInt32());
        Assert.Equal(1, publicPerformance.GetProperty("losses").GetInt32());
        Assert.Equal(0, publicPerformance.GetProperty("pushes").GetInt32());
        Assert.Equal(0.666667m, publicPerformance.GetProperty("hitRate").GetDecimal());
        Assert.True(publicPerformance.GetProperty("roi").GetDecimal() > 0m);
        Assert.True(publicPerformance.GetProperty("averageHitChance").GetDecimal() >= 0.65m);
        Assert.NotEmpty(publicPerformance.GetProperty("segments").GetProperty("propShape").EnumerateArray());

        var currentSummary = json.GetProperty("currentPublicTopPickSummary");
        Assert.Equal(json.GetProperty("topPickRows").GetInt32(), currentSummary.GetProperty("currentTopPickCount").GetInt32());
        var concentration = json.GetProperty("publicTopPickConcentrationAnalysis");
        var topShape = concentration.GetProperty("topPropShapes").EnumerateArray().First();
        Assert.Equal(3, topShape.GetProperty("currentCount").GetInt32());
        Assert.Equal(3, topShape.GetProperty("historicalReplayTotal").GetInt32());
        Assert.Equal(3, topShape.GetProperty("historicalReplayable").GetInt32());
        Assert.Equal(3, topShape.GetProperty("historicalGraded").GetInt32());
        Assert.Equal(0.666667m, topShape.GetProperty("historicalHitRate").GetDecimal());

        var profile = json.GetProperty("publicTopPickProfileRecommendations").EnumerateArray().Single();
        Assert.Equal(3, profile.GetProperty("total").GetInt32());
        Assert.Equal(3, profile.GetProperty("replayable").GetInt32());
        Assert.Equal(3, profile.GetProperty("graded").GetInt32());
        Assert.Equal("NeedsMoreData", profile.GetProperty("recommendation").GetString());
        Assert.Contains(profile.GetProperty("reasonCodes").EnumerateArray(), reason => reason.GetString() == "GradedSampleBelow30");

        var samples = json.GetProperty("publicTopPickSamples");
        Assert.NotEmpty(samples.GetProperty("best").EnumerateArray());
        Assert.NotEmpty(samples.GetProperty("worst").EnumerateArray());
        Assert.All(samples.GetProperty("best").EnumerateArray(), sample =>
            Assert.True(sample.GetProperty("isReplayTopPick").GetBoolean()));

        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(propResultCountBefore, await factory.CountPropResultsAsync());
    }

    [Fact]
    public async Task LikelyHitReplay_BorderlineSimulationReportsWhatIfWithoutChangingPublicTopPicks()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var beforePublicResponse = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25");
        await EnsureSuccessWithBodyAsync(beforePublicResponse);
        var beforePublic = await beforePublicResponse.Content.ReadFromJsonAsync<JsonElement>();
        var beforeTopPickCount = beforePublic.GetProperty("topPicks").EnumerateArray().Count();

        var simulationResponse = await client.GetAsync("/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&simulateBorderlineProfileGate=true&minBorderlineBreakEvenMargin=0.05&minBorderlineEffectiveSampleSize=30&borderlineWorstAllowedFavoriteAmericanOdds=-125&borderlineBestAllowedUnderdogAmericanOdds=150&requireBorderlineProfileAllow=true&includeSamples=true&sampleTake=5&maxRows=100");

        await EnsureSuccessWithBodyAsync(simulationResponse);
        var simulationJson = await simulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var simulation = simulationJson.GetProperty("simulation");
        Assert.True(simulation.GetProperty("enabled").GetBoolean());
        Assert.True(simulation.GetProperty("diagnosticOnly").GetBoolean());
        Assert.True(simulation.GetProperty("baselineTopPickRows").GetInt32() >= simulation.GetProperty("simulatedTopPickRows").GetInt32());
        Assert.True(simulation.TryGetProperty("removedBucketBreakdown", out _));
        Assert.True(simulation.TryGetProperty("keptBucketBreakdown", out _));

        var afterPublicResponse = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25");
        await EnsureSuccessWithBodyAsync(afterPublicResponse);
        var afterPublic = await afterPublicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(beforeTopPickCount, afterPublic.GetProperty("topPicks").EnumerateArray().Count());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task LikelyHitReplay_PublicTopPickSimulationCapsPropShapesWithoutChangingPublicTopPicks()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbLikelyHitHistoricalReplaySnapshotsAsync();

        var beforePublicResponse = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25");
        await EnsureSuccessWithBodyAsync(beforePublicResponse);
        var beforePublic = await beforePublicResponse.Content.ReadFromJsonAsync<JsonElement>();
        var beforeTopPickCount = beforePublic.GetProperty("topPicks").EnumerateArray().Count();

        var simulationResponse = await client.GetAsync("/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&simulatePublicTopPickProfileGate=true&maxRowsPerPropShape=1&includeSamples=true&sampleTake=5&maxRows=100");

        await EnsureSuccessWithBodyAsync(simulationResponse);
        var simulationJson = await simulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var simulation = simulationJson.GetProperty("publicTopPickSimulation");
        Assert.True(simulation.GetProperty("enabled").GetBoolean());
        Assert.True(simulation.GetProperty("diagnosticOnly").GetBoolean());
        Assert.True(simulation.GetProperty("baselineTopPickRows").GetInt32() >= simulation.GetProperty("simulatedTopPickRows").GetInt32());
        Assert.True(simulation.GetProperty("removedRows").GetInt32() > 0, simulation.GetRawText());
        Assert.NotEqual(JsonValueKind.Null, simulation.GetProperty("baselineHitRate").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, simulation.GetProperty("simulatedHitRate").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, simulation.GetProperty("baselineRoi").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, simulation.GetProperty("simulatedRoi").ValueKind);
        Assert.Equal(JsonValueKind.Null, simulation.GetProperty("simulationUnavailableReason").ValueKind);
        Assert.True(simulation.TryGetProperty("removedBucketBreakdown", out _));
        Assert.True(simulation.TryGetProperty("keptBucketBreakdown", out _));
        Assert.Equal(1, simulation.GetProperty("criteria").GetProperty("maxRowsPerPropShape").GetInt32());

        var afterPublicResponse = await client.GetAsync("/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=25");
        await EnsureSuccessWithBodyAsync(afterPublicResponse);
        var afterPublic = await afterPublicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(beforeTopPickCount, afterPublic.GetProperty("topPicks").EnumerateArray().Count());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task LikelyHitReplay_IgnoresOldPersistedCandidateFlags()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedExpectedValueDisplaySnapshotsAsync();

        var response = await client.GetAsync("/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&includeSamples=true&sampleTake=10&maxRows=100");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("replayRowsEvaluated").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("topPickRows").GetInt32());
        Assert.Equal(0, json.GetProperty("publicTopPickPerformanceAnalysis").GetProperty("total").GetInt32());
        Assert.Equal(0, json.GetProperty("currentPublicTopPickSummary").GetProperty("currentTopPickCount").GetInt32());
        Assert.False(json.GetProperty("recommendations").GetProperty("below60TopPickLeakDetected").GetBoolean());
        Assert.All(json.GetProperty("samples").GetProperty("rejected").EnumerateArray(), sample =>
        {
            if (string.Equals(sample.GetProperty("providerCategory").GetString(), "Sportsbook", StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(sample.GetProperty("isTopPick").GetBoolean());
                Assert.Contains(
                    sample.GetProperty("fatalConflictCodes").EnumerateArray(),
                    reason => reason.GetString() is "BelowBalancedMinimumHitChance" or "BelowSportsbookPublicTopPickHitChance" or "MissingApprovedPlayerMappingOrHistory");
            }
        });
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }


    [Fact]
    public async Task MlbPublicRowEligibility_ReportsSportsbookRecommendationAndDfsEntryCounts()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/debug/mlb/public-row-eligibility");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");

        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(summary.GetProperty("sportsbookRows").GetInt32() >= 1);
        Assert.True(summary.GetProperty("dfsRows").GetInt32() >= 1);
        Assert.True(summary.GetProperty("sportsbookRecommendationEligible").GetInt32() >= 1);
        Assert.True(summary.GetProperty("entryActionableCandidateCount").GetInt32() >= 1);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbDfsLineShopping_UsesHitProbabilityBeforePromotingCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=DFS&topOnly=false&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var groups = json.GetProperty("comparisonGroups").EnumerateArray().ToArray();

        var overCandidate = groups.Single(row => row.GetProperty("player").GetString() == "High Probability Over");
        Assert.Equal("Over", overCandidate.GetProperty("bestSide").GetString());
        Assert.True(overCandidate.GetProperty("bestProbability").GetDecimal() >= 0.55m);
        Assert.Equal(overCandidate.GetProperty("bestProbability").GetDecimal(), overCandidate.GetProperty("selectedSideProbability").GetDecimal());
        Assert.True(overCandidate.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.Equal("EmpiricalMeanFromProbabilityPlayerHistory", overCandidate.GetProperty("recommendationProjectionSource").GetString());
        Assert.True(overCandidate.GetProperty("isProjectionRecommendationAligned").GetBoolean());
        Assert.False(overCandidate.GetProperty("projectionProbabilityConflict").GetBoolean());
        Assert.False(overCandidate.GetProperty("recentWeightedProjectionProbabilityConflict").GetBoolean());
        Assert.True(overCandidate.GetProperty("isCandidate").GetBoolean());
        Assert.False(overCandidate.GetProperty("showExpectedValue").GetBoolean());

        var lowProbability = groups.Single(row => row.GetProperty("player").GetString() == "Low Probability Longshot");
        Assert.Equal("None", lowProbability.GetProperty("bestSide").GetString());
        Assert.False(lowProbability.GetProperty("isCandidate").GetBoolean());

        var lean = groups.Single(row => row.GetProperty("player").GetString() == "Research Lean Over");
        Assert.Equal("Over", lean.GetProperty("bestSide").GetString());
        Assert.InRange(lean.GetProperty("bestProbability").GetDecimal(), 0.50m, 0.55m);
        Assert.False(lean.GetProperty("isCandidate").GetBoolean());
        Assert.True(lean.GetProperty("isWatchlist").GetBoolean() || lean.GetProperty("isResearchOnly").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookLineShopping_SuppressesCandidate_WhenProjectionAndProbabilityConflict()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/markets/line-shopping?sport=MLB&providerCategory=Sportsbook&topOnly=false&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var groups = json.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        var conflicted = groups.Single(row => row.GetProperty("player").GetString() == "Valid Skew Conflict");

        Assert.Equal("None", conflicted.GetProperty("bestSide").GetString());
        Assert.False(conflicted.GetProperty("isCandidate").GetBoolean());
        Assert.False(conflicted.GetProperty("isRecommendation").GetBoolean());
        Assert.False(conflicted.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, conflicted.GetProperty("selectedSideProbability").ValueKind);
        Assert.Equal(JsonValueKind.Null, conflicted.GetProperty("bestEstimatedEv").ValueKind);
        Assert.Equal(JsonValueKind.Null, conflicted.GetProperty("displayExpectedValue").ValueKind);
        Assert.False(conflicted.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal("Over", conflicted.GetProperty("projectionDirection").GetString());
        Assert.Equal("Under", conflicted.GetProperty("probabilityDirection").GetString());
        Assert.True(conflicted.GetProperty("projectionProbabilityConflict").GetBoolean());
        Assert.Equal(0.30m, conflicted.GetProperty("rawPlayerEmpiricalOverRate").GetDecimal());
        Assert.Equal(0.70m, conflicted.GetProperty("rawPlayerEmpiricalUnderRate").GetDecimal());
        Assert.Equal(30, conflicted.GetProperty("playerSampleSize").GetInt32());

        var recentTrendMismatch = groups.Single(row => row.GetProperty("player").GetString() == "Projection Conflict Under");
        Assert.False(recentTrendMismatch.GetProperty("projectionProbabilityConflict").GetBoolean());
        Assert.True(recentTrendMismatch.GetProperty("isProjectionRecommendationAligned").GetBoolean());
        Assert.Equal("Under", recentTrendMismatch.GetProperty("projectionDirection").GetString());
        Assert.Equal("Under", recentTrendMismatch.GetProperty("probabilityDirection").GetString());
        Assert.True(recentTrendMismatch.GetProperty("recentWeightedProjectionProbabilityConflict").GetBoolean());
        Assert.Equal("None", recentTrendMismatch.GetProperty("bestSide").GetString());
        Assert.False(recentTrendMismatch.GetProperty("isCandidate").GetBoolean());
        Assert.False(recentTrendMismatch.GetProperty("isRecommendation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, recentTrendMismatch.GetProperty("displayExpectedValue").ValueKind);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbProjectionProbabilityConflictAudit_ReturnsPropTypeBreakdownAndHistorySamples()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/debug/the-odds/mlb/projection-probability-conflicts?propType=Pitcher%20Strikeouts&includeSamples=true&sampleTake=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("totalGroupsEvaluated").GetInt32() >= 1);
        Assert.True(json.GetProperty("conflictGroups").GetInt32() >= 1);
        Assert.True(json.GetProperty("conflictRate").GetDecimal() > 0m);
        Assert.NotEmpty(json.GetProperty("conflictBuckets").EnumerateArray());
        Assert.True(json.GetProperty("recentWeightedProjectionProbabilityConflicts").GetInt32() >= 1);
        Assert.True(summary.GetProperty("conflictGroups").GetInt32() >= 1);
        Assert.Contains(
            summary.GetProperty("conflictBuckets").EnumerateArray(),
            row => row.GetProperty("bucket").GetString() == "ValidSkew");
        Assert.True(summary.GetProperty("recentWeightedProjectionProbabilityConflicts").GetInt32() >= 1);

        var propTypeBreakdown = json.GetProperty("conflictsByPropType").EnumerateArray().ToArray();
        var pitcherStrikeouts = propTypeBreakdown.Single(row => row.GetProperty("normalizedPropType").GetString() == "Pitcher Strikeouts");
        Assert.True(pitcherStrikeouts.GetProperty("conflictGroups").GetInt32() >= 1);
        Assert.True(pitcherStrikeouts.GetProperty("averagePlayerSampleSize").GetDecimal() >= 10m);

        var samples = json.GetProperty("samples").EnumerateArray().ToArray();
        var conflicted = samples.Single(row => row.GetProperty("player").GetString() == "Valid Skew Conflict");
        Assert.Equal("Over", conflicted.GetProperty("projectionDirection").GetString());
        Assert.Equal("Under", conflicted.GetProperty("probabilityDirection").GetString());
        Assert.True(conflicted.GetProperty("projectionProbabilityConflict").GetBoolean());
        Assert.Equal("ValidSkew", conflicted.GetProperty("conflictBucket").GetString());
        Assert.Equal(30, conflicted.GetProperty("probabilityHistory").GetProperty("sampleSize").GetInt32());
        Assert.Equal(9, conflicted.GetProperty("probabilityHistory").GetProperty("overCount").GetInt32());
        Assert.Equal(21, conflicted.GetProperty("probabilityHistory").GetProperty("underCount").GetInt32());
        Assert.Equal("EmpiricalMeanFromProbabilityPlayerHistory", conflicted.GetProperty("recommendationProjectionSource").GetString());
        Assert.True(conflicted.GetProperty("isProjectionRecommendationAligned").GetBoolean());
        Assert.Equal("WeightedSeasonLast10Last5", conflicted.GetProperty("projectionFeatureSummary").GetProperty("projectionSource").GetString());

        var skew = samples.Single(row => row.GetProperty("player").GetString() == "Valid Skew Conflict");
        Assert.Equal("ValidSkew", skew.GetProperty("conflictBucket").GetString());
        Assert.True(skew.GetProperty("probabilityHistory").GetProperty("mean").GetDecimal() > skew.GetProperty("line").GetDecimal());
        Assert.True(skew.GetProperty("probabilityHistory").GetProperty("underRate").GetDecimal() > 0.5m);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task MlbSportsbookEvAudit_ReturnsBucketsAndSuppressionSamples()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedMlbDfsHitProbabilitySelectionScenarioAsync();

        var response = await client.GetAsync("/api/debug/the-odds/mlb/sportsbook-ev-audit?takeSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.True(summary.GetProperty("sportsbookGroupsEvaluated").GetInt32() >= 4);
        Assert.True(summary.GetProperty("suppressedForUnrealisticEv").GetInt32() >= 2);
        Assert.Equal(0, summary.GetProperty("negativeOrZeroDisplayEvRecommendations").GetInt32());
        Assert.Equal(0, summary.GetProperty("overThirtyPercentDisplayEvRecommendations").GetInt32());

        var displayBuckets = json.GetProperty("evBuckets").GetProperty("displayExpectedValue").EnumerateArray().ToArray();
        Assert.Contains(displayBuckets, row => row.GetProperty("bucket").GetString() == "Unavailable");
        var suppressedSamples = json.GetProperty("suppressedSamples").EnumerateArray().ToArray();
        Assert.Contains(suppressedSamples, row => row.GetProperty("player").GetString() == "Low Sample Huge EV Sportsbook Player");
        Assert.Contains(suppressedSamples, row => row.GetProperty("player").GetString() == "Negative EV Sportsbook Player");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task SoccerWorldCupMarketReads_ReturnPersistedSnapshots_WithoutCallingProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();
        var before = await factory.CountArtifactsAsync();

        var freshnessResponse = await client.GetAsync("/api/markets/freshness?sport=SOCCER&competition=World%20Cup");
        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10");
        var topPicksDfsResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&providerCategory=DFS&take=10");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&topOnly=false&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(freshnessResponse);
        await EnsureSuccessWithBodyAsync(topPicksResponse);
        await EnsureSuccessWithBodyAsync(topPicksDfsResponse);
        await EnsureSuccessWithBodyAsync(lineShoppingResponse);
        var freshness = await freshnessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var topPicksDfs = await topPicksDfsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();
        AssertNoCaseInsensitiveDuplicatePropertiesInTopPicks(await topPicksResponse.Content.ReadAsStringAsync());
        AssertNoCaseInsensitiveDuplicatePropertiesInComparisonGroups(await lineShoppingResponse.Content.ReadAsStringAsync());

        Assert.Equal("SOCCER", freshness.GetProperty("effectiveSport").GetString());
        Assert.Equal("World Cup", freshness.GetProperty("competition").GetString());
        Assert.False(freshness.GetProperty("resultGradingSupported").GetBoolean());
        Assert.True(topPicks.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", topPicks.GetProperty("topPicks")[0].GetProperty("sport").GetString());
        Assert.Equal("World Cup", topPicks.GetProperty("topPicks")[0].GetProperty("competition").GetString());
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(topPicks.GetProperty("topPicks")[0].GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.Equal("Sportsbook", topPicks.GetProperty("topPicks")[0].GetProperty("entryProviderCategory").GetString());
        Assert.Equal("Sportsbook", topPicks.GetProperty("topPicks")[0].GetProperty("providerCategory").GetString());
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal("Soccer grading and model hit chance are not available yet.", topPicks.GetProperty("topPicks")[0].GetProperty("resultGradingUnavailableReason").GetString());
        Assert.Equal("Soccer World Cup sportsbook context is research-only and not Entry Builder playable.", topPicks.GetProperty("topPicks")[0].GetProperty("entryUnavailableReason").GetString());
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("isRecommendation").GetBoolean());
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, topPicks.GetProperty("topPicks")[0].GetProperty("selectedSideProbability").ValueKind);
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, topPicks.GetProperty("topPicks")[0].GetProperty("displayExpectedValue").ValueKind);
        Assert.Equal(0, topPicks.GetProperty("summary").GetProperty("actionableCandidateCount").GetInt32());
        Assert.Empty(topPicksDfs.GetProperty("topPicks").EnumerateArray());
        Assert.True(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("sport").GetString());
        Assert.Equal("World Cup", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("competition").GetString());
        Assert.Equal("Sportsbook", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("providerCategory").GetString());
        Assert.False(lineShopping.GetProperty("comparisonGroups")[0].GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(lineShopping.GetProperty("comparisonGroups")[0].GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.Equal("Sportsbook", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("entryProviderCategory").GetString());
        Assert.False(lineShopping.GetProperty("comparisonGroups")[0].GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal("Soccer grading and model hit chance are not available yet.", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("hitChanceUnavailableReason").GetString());
        Assert.Equal(0, lineShopping.GetProperty("summary").GetProperty("actionableComparisonGroups").GetInt32());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SoccerWorldCupLineShopping_ReturnsPersistedProviderContext_WhenSnapshotsDoNotExist()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupProviderContextRowsWithoutSnapshotsAsync();
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "soccer-history-fixture-1",
            new DateOnly(2026, 1, 1),
            "SHOTS",
            "Shots",
            providerPlayerId: "70001",
            playerDisplayName: "Test Forward");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "soccer-history-fixture-2",
            new DateOnly(2026, 1, 2),
            "GOALS",
            "Goals",
            providerPlayerId: "70001",
            playerDisplayName: "Test Forward");
        Guid testForwardPlayerId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            testForwardPlayerId = await dbContext.Players
                .Where(player => player.DisplayName == "Test Forward" && player.League != null && player.League.Code == "WC2026")
                .Select(player => player.Id)
                .SingleAsync();
        }
        await AddApiSportsProviderEntityLinkAsync(factory, testForwardPlayerId, "70001", "Test Forward", "TestPublicHistoryEnrichment");
        var before = await factory.CountArtifactsAsync();

        var freshnessResponse = await client.GetAsync("/api/markets/freshness?sport=SOCCER&competition=World%20Cup");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");
        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&providerCategory=DFS&take=10");

        await EnsureSuccessWithBodyAsync(freshnessResponse);
        await EnsureSuccessWithBodyAsync(lineShoppingResponse);
        await EnsureSuccessWithBodyAsync(topPicksResponse);
        AssertNoCaseInsensitiveDuplicatePropertiesInComparisonGroups(await lineShoppingResponse.Content.ReadAsStringAsync());
        var freshness = await freshnessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(freshness.GetProperty("hasUsableMarketData").GetBoolean());
        Assert.Equal(0, freshness.GetProperty("lineShoppingSnapshotCount").GetInt32());
        Assert.Equal(2, freshness.GetProperty("publicReadableContextRowCount").GetInt32());
        Assert.Equal(1, freshness.GetProperty("publicReadableContextProviderEventCount").GetInt32());
        Assert.Equal(2, freshness.GetProperty("diagnostics").GetProperty("soccerProviderContextRows").GetInt32());
        Assert.False(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", lineShopping.GetProperty("effectiveSport").GetString());
        Assert.Equal("World Cup", lineShopping.GetProperty("competition").GetString());
        Assert.False(lineShopping.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(2, lineShopping.GetProperty("summary").GetProperty("providerContextRowCount").GetInt32());
        Assert.Equal(1, lineShopping.GetProperty("diagnostics").GetProperty("rowsWithPlayerHistory").GetInt32());
        Assert.Equal(1, lineShopping.GetProperty("diagnostics").GetProperty("rowsWithoutPlayerHistory").GetInt32());
        Assert.Equal(1, lineShopping.GetProperty("diagnostics").GetProperty("uniqueLinkedProviderPlayers").GetInt32());
        Assert.Contains(lineShopping.GetProperty("diagnostics").GetProperty("historyStatsIncluded").EnumerateArray(), stat => stat.GetString() == "Goals");
        Assert.Contains(lineShopping.GetProperty("diagnostics").GetProperty("historyStatsIncluded").EnumerateArray(), stat => stat.GetString() == "Shots");
        Assert.NotEmpty(lineShopping.GetProperty("comparisonGroups").EnumerateArray());

        var group = lineShopping.GetProperty("comparisonGroups").EnumerateArray().First(row => row.GetProperty("player").GetString() == "Test Forward");
        Assert.Equal("SOCCER", group.GetProperty("sport").GetString());
        Assert.Equal("World Cup", group.GetProperty("competition").GetString());
        Assert.Equal("Sportsbook", group.GetProperty("providerCategory").GetString());
        Assert.False(group.GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(group.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.False(group.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(JsonValueKind.Null, group.GetProperty("selectedSideProbability").ValueKind);
        Assert.False(group.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.False(group.GetProperty("isRecommendation").GetBoolean());
        Assert.False(group.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, group.GetProperty("recommendationType").ValueKind);
        Assert.Equal(JsonValueKind.Null, group.GetProperty("displayExpectedValue").ValueKind);
        Assert.Equal("Soccer grading and model hit chance are not available yet.", group.GetProperty("hitChanceUnavailableReason").GetString());
        Assert.False(group.GetProperty("showExpectedValue").GetBoolean());
        Assert.Equal("ResearchOnly", group.GetProperty("strategyClassification").GetString());
        var historySummary = group.GetProperty("playerHistorySummary");
        Assert.True(historySummary.GetProperty("available").GetBoolean());
        Assert.Equal("api-sports-football", historySummary.GetProperty("provider").GetString());
        Assert.Equal("70001", historySummary.GetProperty("providerPlayerId").GetString());
        Assert.Equal(2, historySummary.GetProperty("sampleSize").GetInt32());
        Assert.True(historySummary.GetProperty("stats").TryGetProperty("Shots", out _));
        Assert.True(historySummary.GetProperty("stats").TryGetProperty("Goals", out _));
        Assert.NotEmpty(historySummary.GetProperty("recentGameLog").EnumerateArray());
        var unlinkedGroup = lineShopping.GetProperty("comparisonGroups").EnumerateArray().First(row => row.GetProperty("player").GetString() == "Second Forward");
        var unavailableSummary = unlinkedGroup.GetProperty("playerHistorySummary");
        Assert.False(unavailableSummary.GetProperty("available").GetBoolean());
        Assert.Equal("No approved API-Sports player link found for this Soccer platform player.", unavailableSummary.GetProperty("unavailableReason").GetString());
        Assert.Empty(topPicks.GetProperty("topPicks").EnumerateArray());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SoccerPerformanceSummary_RemainsUnavailable_WithoutCallingProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/markets/performance-summary?sport=SOCCER&fromDate=2026-06-21&toDate=2026-06-24");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal("Soccer grading and model hit chance are not available yet.", json.GetProperty("unavailableReason").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SoccerPropHistory_ReturnsUnavailableState()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();

        var response = await client.GetAsync("/api/markets/prop-history?sport=SOCCER&playerName=Test%20Forward&propType=Shots&side=Over&line=1.5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("Soccer grading and model hit chance are not available yet.", json.GetProperty("emptyReason").GetString());
        Assert.Empty(json.GetProperty("games").EnumerateArray());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task ApiSportsConfigDiagnostics_ReportMissingConfig_WithoutCallingExternalApis()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/config");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal("API-Sports", json.GetProperty("provider").GetString());
        Assert.False(json.GetProperty("configured").GetBoolean());
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:ApiKey");
        Assert.DoesNotContain(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:WorldCupLeagueId");
        Assert.DoesNotContain(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:Season");
        Assert.Equal(1, json.GetProperty("config").GetProperty("football").GetProperty("worldCup").GetProperty("leagueId").GetInt32());
        Assert.Equal(2026, json.GetProperty("config").GetProperty("football").GetProperty("worldCup").GetProperty("season").GetInt32());
    }

    [Theory]
    [InlineData("/api/debug/soccer/api-sports/fixtures?date=2026-06-21&forceLive=false")]
    [InlineData("/api/debug/soccer/api-sports/fixture-player-stats?fixtureId=123&forceLive=false")]
    [InlineData("/api/debug/soccer/api-sports/fixture-prop-coverage?fixtureId=123&forceLive=false")]
    public async Task ApiSportsForceLiveEndpoints_BlockWithoutForceLive(string path)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsForceLiveFixtures_ReturnsMissingConfig_WhenKeyIsAbsent()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-21&forceLive=true");

        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.Equal("MissingApiSportsConfig", json.GetProperty("blockReason").GetString());
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:ApiKey");
        Assert.DoesNotContain(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:WorldCupLeagueId");
        Assert.DoesNotContain(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:Season");
    }

    [Fact]
    public async Task ApiSportsFixtures_FixtureId_BuildsIdOnlyRequest()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?fixtureId=12345&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal("12345", json.GetProperty("request").GetProperty("used").GetProperty("fixtureId").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("date").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("league").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("season").ValueKind);
        Assert.EndsWith("/fixtures?id=12345", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_DateOnly_CanOmitConfiguredLeagueAndSeason()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig(includeWorldCupDefaults: false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&useConfiguredLeague=false&useConfiguredSeason=false&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-06-20", json.GetProperty("request").GetProperty("used").GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("league").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("season").ValueKind);
        Assert.EndsWith("/fixtures?date=2026-06-20", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_ExplicitLeagueAndSeason_OverrideConfig()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&league=39&season=2024&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(39, json.GetProperty("request").GetProperty("used").GetProperty("league").GetInt32());
        Assert.Equal(2024, json.GetProperty("request").GetProperty("used").GetProperty("season").GetInt32());
        Assert.EndsWith("/fixtures?date=2026-06-20&league=39&season=2024", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_ConfiguredDefaults_PreserveCurrentBehavior()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("request").GetProperty("used").GetProperty("league").GetInt32());
        Assert.Equal(2026, json.GetProperty("request").GetProperty("used").GetProperty("season").GetInt32());
        Assert.EndsWith("/fixtures?date=2026-06-20&league=1&season=2026", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_OldApiFootballRoute_IsNotPromoted()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-football/fixtures?date=2026-06-20&forceLive=false");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_DryRun_WritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.True(json.GetProperty("resultRowsPreviewed").GetInt32() > 0);
        Assert.Equal(0, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(0, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
    }

    [Fact]
    public async Task ProviderStatMappingsDebug_ReturnsSeededApiSportsSoccerMappings()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/provider-stat-mappings?sport=SOCCER&providerKey=api-sports-football&competitionScope=World%20Cup&season=2026&includeSamples=true&maxSamples=100");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(20, json.GetProperty("totalMappings").GetInt32());
        Assert.Equal(20, json.GetProperty("activeMappings").GetInt32());
        var publicGradingGroup = Assert.Single(json.GetProperty("counts").GetProperty("byPublicGradingEnabled").EnumerateArray());
        Assert.False(publicGradingGroup.GetProperty("publicGradingEnabled").GetBoolean());
        Assert.Equal(20, publicGradingGroup.GetProperty("count").GetInt32());
        Assert.Contains(json.GetProperty("samples").EnumerateArray(), row =>
            row.GetProperty("displayName").GetString() == "Shots On Target"
            && row.GetProperty("providerStatFields").EnumerateArray().Single().GetString() == "shots.on");
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_UsesDbProviderStatMappings_WhenConfigured()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var mappingDiagnostics = json.GetProperty("mappingDiagnostics");
        Assert.Equal("Database", mappingDiagnostics.GetProperty("mappingSource").GetString());
        Assert.Equal(20, mappingDiagnostics.GetProperty("providerMappingsConfigured").GetInt32());
        Assert.Equal(20, mappingDiagnostics.GetProperty("dbMappingsUsed").GetInt32());
        Assert.Equal(0, mappingDiagnostics.GetProperty("fallbackMappingsUsed").GetInt32());
        Assert.True(json.GetProperty("resultRowsPreviewed").GetInt32() > 0);
        Assert.Contains(json.GetProperty("previewRowsByPlayer").EnumerateArray()
                .SelectMany(group => group.GetProperty("rows").EnumerateArray()),
            row => row.GetProperty("mappingSource").GetString() == "Database"
                && row.GetProperty("publicGradingEnabled").GetBoolean() == false);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_ReportsMissingMapping_WhenDbMappingIsAbsent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync(excludeCanonicalStatKey: "SHOTS");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("mappingDiagnostics").GetProperty("missingMappingCount").GetInt32() > 0);
        var missingMapping = Assert.Single(json.GetProperty("unsupportedSkippedSummary").EnumerateArray(), row => row.GetProperty("reason").GetString() == "MissingMapping");
        Assert.Contains(missingMapping.GetProperty("props").EnumerateArray(), prop => prop.GetString() == "shots.total");
        Assert.DoesNotContain(json.GetProperty("previewRowsByPlayer").EnumerateArray()
                .SelectMany(group => group.GetProperty("rows").EnumerateArray()),
            row => row.GetProperty("propType").GetString() == "Shots");
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_WritesIdempotently()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inserted = first.GetProperty("resultRowsInserted").GetInt32();
        var providerLinksAfterFirst = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inserted > 0);
        Assert.Equal(2, providerLinksAfterFirst);
        Assert.Equal(providerLinksAfterFirst, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
        Assert.Equal(inserted, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(0, second.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsUpdated").GetInt32());
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_AllowsDuplicateDisplayNamesWithDifferentStatKeys()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedSoccerPropTypesWithDuplicateNamesAndDifferentStatKeysAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("resultRowsInserted").GetInt32() > 0);
        Assert.True(await factory.HasSoccerPropTypeAsync("GOALIE_SAVES_COMBO"));
        Assert.True(await factory.HasSoccerPropTypeAsync("GOALIE_SAVES"));
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_ReturnsStructuredPropTypeConflict_ForDuplicateStatKey()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedConflictingSoccerPropTypesWithSameStatKeyAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PropTypeResolutionFailed", json.GetProperty("blockReason").GetString());
        Assert.Equal("PropTypeResolution", json.GetProperty("stage").GetString());
        var conflict = Assert.Single(json.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("GOALIE_SAVES", conflict.GetProperty("statKey").GetString());
        Assert.Equal("ExistingPropTypes", conflict.GetProperty("stage").GetString());
        Assert.Equal(2, conflict.GetProperty("conflictingMetadata").EnumerateArray().Count());
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_FlagsConservativeAndSkipsUnsupportedRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("unsupportedSkipped").GetInt32() > 0);
        Assert.True(json.GetProperty("ruleConfidenceCounts").TryGetProperty("NeedsConfirmation", out var needsConfirmation));
        Assert.True(needsConfirmation.GetInt32() > 0);

        var playerGroups = json.GetProperty("previewRowsByPlayer").EnumerateArray().ToArray();
        var forward = Assert.Single(playerGroups, group => group.GetProperty("playerName").GetString() == "Test Forward");
        Assert.DoesNotContain(forward.GetProperty("rows").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goalie Saves");
        Assert.DoesNotContain(playerGroups, group => group.GetProperty("playerName").GetString() == "Unused Substitute");
        var keeper = Assert.Single(playerGroups, group => group.GetProperty("playerName").GetString() == "Test Keeper");
        Assert.Contains(keeper.GetProperty("rows").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goalie Saves");
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixtureStatus_ReturnsLocalCounts()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var statusResponse = await client.GetAsync("/api/debug/soccer/results/api-sports/import-fixture/status?fixtureId=1489393");

        await EnsureSuccessWithBodyAsync(statusResponse);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(status.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(status.GetProperty("rowCount").GetInt32() > 0);
        Assert.Contains(status.GetProperty("countsByPropType").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goals");
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-13",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_DryRunDiscoversCompletedAndSkipsOpenFixturesWithoutWriting()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-13",
            dryRun = true,
            forceLive = true,
            includeFixtures = true,
            includeCoverageByDate = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(3, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(2, json.GetProperty("skippedFixtureCount").GetInt32());
        Assert.Equal(4, json.GetProperty("apiCallsUsed").GetInt32());
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(0, await CountApiSportsSoccerResultsAsync(factory));
        var skippedReasons = json.GetProperty("skippedFixtures").EnumerateArray().Select(row => row.GetProperty("reason").GetString()).ToArray();
        Assert.Contains("Scheduled", skippedReasons);
        Assert.Contains("InProgress", skippedReasons);
        var coverageByDate = json.GetProperty("coverageByDate").EnumerateArray().ToArray();
        Assert.Equal(3, coverageByDate.Length);
        var firstDate = coverageByDate[0];
        Assert.Equal("2026-06-11", firstDate.GetProperty("date").GetString());
        Assert.Equal(1, firstDate.GetProperty("fixtureCount").GetInt32());
        Assert.Equal(1, firstDate.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, firstDate.GetProperty("importedFixtureCount").GetInt32());
        Assert.Equal(0, firstDate.GetProperty("importedResultRows").GetInt32());
        Assert.True(firstDate.GetProperty("previewedResultRows").GetInt32() > 0);
        Assert.Equal(0, firstDate.GetProperty("missingCompletedFixtureCount").GetInt32());
        Assert.Equal(2, firstDate.GetProperty("apiCallsUsed").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_WritesCompletedFixturesIdempotently()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inserted = first.GetProperty("resultRowsInserted").GetInt32();

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inserted > 0);
        Assert.Equal(inserted, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(0, second.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsUpdated").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_ReportsPerFixtureFailureAndContinues()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-14",
            dryRun = false,
            forceLive = true,
            includeFixtures = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("failedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("resultRowsInserted").GetInt32() > 0);
        var failure = Assert.Single(json.GetProperty("failures").EnumerateArray());
        Assert.Equal("99999", failure.GetProperty("fixtureId").GetString());
    }

    [Fact]
    public async Task ApiSportsWorldCupImportStatus_ReturnsPersistedOnlyByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("discoveryAvailable").GetBoolean());
        Assert.Equal(0, json.GetProperty("apiCallsUsed").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("discoveredFixtureCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("completedFixtureCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missingCompletedFixtureCount").ValueKind);
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("totalRows").GetInt32() > 0);
        Assert.False(json.TryGetProperty("rowsByFixture", out _));
        Assert.Equal("1489393", json.GetProperty("latestImportedFixtureId").GetString());
        Assert.Equal("2026-06-11", json.GetProperty("latestImportedEventDate").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("latestImportedAtUtc").ValueKind);
        Assert.False(json.TryGetProperty("coverageByDate", out _));
    }

    [Fact]
    public async Task ApiSportsWorldCupScheduledImportStatus_ReportsWorkerWorkflowWithoutExternalCalls()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var fixtureCallsBefore = factory.ApiSportsFootballClient.FixturesCallCount;
        var statsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/scheduled-import-status?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        var resultImport = json.GetProperty("soccerResultImport");
        Assert.Equal("Disabled", resultImport.GetProperty("status").GetString());
        Assert.True(resultImport.GetProperty("implemented").GetBoolean());
        Assert.True(resultImport.GetProperty("workerExists").GetBoolean());
        Assert.False(resultImport.GetProperty("workerEnabled").GetBoolean());
        Assert.False(resultImport.GetProperty("scheduledImportEnabled").GetBoolean());
        Assert.False(resultImport.GetProperty("hourlyWorkerPullsSoccerOutcomes").GetBoolean());
        Assert.Equal("1489393", resultImport.GetProperty("lastImportedFixtureId").GetString());
        Assert.Equal(1, json.GetProperty("persistedResultCoverage").GetProperty("importedFixtureCount").GetInt32());
        Assert.Contains("SoccerResultImportWorker", json.GetProperty("workflowSeparation").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixtureCallsBefore, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(statsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupScheduledImportStatus_ReportsIdleWhenWorkerEnabledAndConfigured()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        await using var factory = new SafetyTestApplicationFactory(config);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/scheduled-import-status?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var resultImport = json.GetProperty("soccerResultImport");
        Assert.Equal("Idle", resultImport.GetProperty("status").GetString());
        Assert.True(resultImport.GetProperty("implemented").GetBoolean());
        Assert.True(resultImport.GetProperty("workerExists").GetBoolean());
        Assert.True(resultImport.GetProperty("workerEnabled").GetBoolean());
        Assert.True(resultImport.GetProperty("scheduledImportEnabled").GetBoolean());
        Assert.True(resultImport.GetProperty("hourlyWorkerPullsSoccerOutcomes").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SoccerResultImportWorker_ImportsCompletedFixturesAndPersistsPendingWithoutFailures()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:Competition"] = "World Cup";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        config["SoccerResultImportWorker:MaxFixturesPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun"] = "1";
        await using var factory = new SafetyTestApplicationFactory(config);
        await factory.EnsureDatabaseAsync();

        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await dbContext.SoccerResultImportStates.SingleAsync();
        var fixtureStates = await dbContext.SoccerResultImportFixtureStates
            .OrderBy(row => row.FixtureId)
            .ToArrayAsync();

        Assert.Equal("completed_with_pending_fixtures", state.LastStatus);
        Assert.Equal(0, state.LastFailedFixtureCount);
        Assert.Equal(3, state.LastSkippedPendingFixtureCount);
        Assert.Equal(1, state.LastImportedFixtureCount);
        Assert.True(state.LastResultRowsWritten > 0);
        var imported = Assert.Single(fixtureStates, row => row.FixtureId == "1489393");
        Assert.Equal("Imported", imported.ImportStatus);
        Assert.Null(imported.LastErrorMessage);
        Assert.True(imported.ResultRowsWritten > 0);

        var notStarted = Assert.Single(fixtureStates, row => row.FixtureId == "22222");
        Assert.Equal("Pending", notStarted.ImportStatus);
        Assert.Equal("Scheduled", notStarted.SkipReason);
        Assert.Null(notStarted.LastErrorMessage);

        var live = Assert.Single(fixtureStates, row => row.FixtureId == "33333");
        Assert.Equal("Pending", live.ImportStatus);
        Assert.Equal("InProgress", live.SkipReason);
        Assert.Null(live.LastErrorMessage);

        var maxLimited = Assert.Single(fixtureStates, row => row.FixtureId == "99999");
        Assert.Equal("Pending", maxLimited.ImportStatus);
        Assert.Equal("MaxFixturesPerRunLimit", maxLimited.SkipReason);
        Assert.Null(maxLimited.LastErrorMessage);
        Assert.True(factory.ApiSportsFootballClient.FixturesCallCount > 0);
        Assert.Equal(1, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SoccerResultImportWorker_RetriesMaxLimitDeferredCompletedFixturesOnLaterRun()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:Competition"] = "World Cup";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        config["SoccerResultImportWorker:MaxFixturesPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun"] = "1";
        await using var factory = new SafetyTestApplicationFactory(config);
        await factory.EnsureDatabaseAsync();

        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);
        await worker.RunImportCycleAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await dbContext.SoccerResultImportStates.SingleAsync();
        var imported = await dbContext.SoccerResultImportFixtureStates.SingleAsync(row => row.FixtureId == "1489393");
        var retried = await dbContext.SoccerResultImportFixtureStates.SingleAsync(row => row.FixtureId == "99999");

        Assert.Equal("partial_failure", state.LastStatus);
        Assert.Equal(1, state.LastFailedFixtureCount);
        Assert.Equal(0, state.LastImportedFixtureCount);
        Assert.Equal("Imported", imported.ImportStatus);
        Assert.True(imported.ResultRowsWritten > 0);
        Assert.Equal("Failed", retried.ImportStatus);
        Assert.Null(retried.SkipReason);
        Assert.Contains("Synthetic API-Sports player-stat failure", retried.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, retried.Attempts);
        Assert.Equal(2, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SoccerResultImportWorker_DoesNotLetAlreadyImportedFixturesConsumeMaxPerRun()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:Competition"] = "World Cup";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        config["SoccerResultImportWorker:MaxFixturesPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun"] = "1";
        await using var factory = new SafetyTestApplicationFactory(config);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var callsAfterManualImport = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;
        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await dbContext.SoccerResultImportStates.SingleAsync();
        var retried = await dbContext.SoccerResultImportFixtureStates.SingleAsync(row => row.FixtureId == "99999");

        Assert.Equal("partial_failure", state.LastStatus);
        Assert.Equal(0, state.LastImportedFixtureCount);
        Assert.Equal(1, state.LastFailedFixtureCount);
        Assert.Equal("Failed", retried.ImportStatus);
        Assert.Equal(callsAfterManualImport + 1, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SoccerResultImportWorker_TreatsPersistedSkippedMaxLimitFixtureAsEligible()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:Competition"] = "World Cup";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        config["SoccerResultImportWorker:MaxFixturesPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun"] = "1";
        await using var factory = new SafetyTestApplicationFactory(config);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SoccerResultImportFixtureStates.Add(new SoccerResultImportFixtureState
            {
                Id = Guid.NewGuid(),
                Source = SoccerResultImportWorker.Source,
                Competition = SoccerResultImportWorker.Competition,
                FixtureId = "99999",
                EventDate = new DateOnly(2026, 6, 14),
                KickoffUtc = new DateTimeOffset(2026, 6, 14, 19, 0, 0, TimeSpan.Zero),
                HomeTeamName = "Home FC",
                AwayTeamName = "Away FC",
                StatusShort = "FT",
                StatusLong = "Match Finished",
                ImportStatus = "Skipped",
                SkipReason = "MaxFixturesPerRunLimit",
                ResultRowsWritten = 0,
                Attempts = 0
            });
            await dbContext.SaveChangesAsync();
        }

        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recovered = await verifyDbContext.SoccerResultImportFixtureStates.SingleAsync(row => row.FixtureId == "99999");
        Assert.Equal("Failed", recovered.ImportStatus);
        Assert.Null(recovered.SkipReason);
        Assert.Equal(1, recovered.Attempts);
        Assert.Contains("Synthetic API-Sports player-stat failure", recovered.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SoccerResultImportWorker_ImportsMultipleEnabledCompetitionsInOneCycle()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:Competition"] = "AllSupported";
        config["SoccerResultImportWorker:CompetitionKeys:0"] = "world-cup-2026";
        config["SoccerResultImportWorker:CompetitionKeys:1"] = "MLS";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        config["SoccerResultImportWorker:MaxFixturesPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun"] = "1";
        config["SoccerResultImportWorker:MaxFixturesTotalPerRun"] = "2";
        await using var factory = new SafetyTestApplicationFactory(config);
        await factory.EnsureDatabaseAsync();

        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var states = await dbContext.SoccerResultImportStates
            .OrderBy(row => row.Competition)
            .ToArrayAsync();
        var worldCupState = Assert.Single(states, row => row.Competition == "World Cup");
        var mlsState = Assert.Single(states, row => row.Competition == "MLS");
        Assert.Equal("completed_with_pending_fixtures", worldCupState.LastStatus);
        Assert.Equal("completed_with_pending_fixtures", mlsState.LastStatus);
        Assert.Equal(1, worldCupState.LastImportedFixtureCount);
        Assert.Equal(1, mlsState.LastImportedFixtureCount);
        Assert.Equal(0, worldCupState.LastFailedFixtureCount);
        Assert.Equal(0, mlsState.LastFailedFixtureCount);
        Assert.True(worldCupState.LastResultRowsWritten > 0);
        Assert.True(mlsState.LastResultRowsWritten > 0);

        var worldCupImported = await dbContext.SoccerResultImportFixtureStates.SingleAsync(row =>
            row.Competition == "World Cup" && row.FixtureId == "1489393");
        var mlsImported = await dbContext.SoccerResultImportFixtureStates.SingleAsync(row =>
            row.Competition == "MLS" && row.FixtureId == "2530611");
        Assert.Equal("Imported", worldCupImported.ImportStatus);
        Assert.Equal("Imported", mlsImported.ImportStatus);
        Assert.Equal(2, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerBackfill_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_UsesRequestedCompetitionLeagueSeasonAndWindow()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2025-08-15",
            toDate = "2025-08-15",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("selectedCompletedFixtureCount").GetInt32());
        Assert.Equal(1, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
        var request = Assert.Single(factory.ApiSportsFootballClient.FixtureRequests);
        Assert.Equal(39, request.League);
        Assert.Equal(2025, request.Season);
        Assert.Equal(new DateOnly(2025, 8, 15), request.Date);
        Assert.Equal(1, factory.ApiSportsFootballClient.LeaguesCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_AutoRollsEplToNextProviderSeason()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2026-08-15",
            toDate = "2026-08-15",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("discoveredFixtureCount").GetInt32());
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.Equal(2026, competition.GetProperty("season").GetInt32());
        Assert.Equal(39, Assert.Single(factory.ApiSportsFootballClient.FixtureRequests).League);
        Assert.Equal(2026, Assert.Single(factory.ApiSportsFootballClient.FixtureRequests).Season);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_AutoRollsMlsByCalendarSeason()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
        {
            competitionKeys = new[] { "MLS" },
            fromDate = "2027-02-15",
            toDate = "2027-02-15",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.Equal(2027, competition.GetProperty("season").GetInt32());
        Assert.Equal(253, Assert.Single(factory.ApiSportsFootballClient.FixtureRequests).League);
        Assert.Equal(2027, Assert.Single(factory.ApiSportsFootballClient.FixtureRequests).Season);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_UsesCachedSeasonMetadataOnSecondCall()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        for (var index = 0; index < 2; index++)
        {
            var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
            {
                competitionKeys = new[] { "EPL" },
                fromDate = "2025-08-15",
                toDate = "2025-08-15",
                forceLive = true,
                summaryOnly = true
            });
            await EnsureSuccessWithBodyAsync(response);
        }

        Assert.Equal(1, factory.ApiSportsFootballClient.LeaguesCallCount);
        Assert.Equal(2, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_SkipsOutsideProviderSeasonWithoutFixtureCalls()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2026-06-15",
            toDate = "2026-06-15",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.Equal("OutsideProviderSeasonWindow", competition.GetProperty("skipReason").GetString());
        Assert.Equal(0, competition.GetProperty("fixtureDiscoveryApiCallsUsed").GetInt32());
        Assert.Equal(1, competition.GetProperty("seasonMetadataApiCallsUsed").GetInt32());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerDiscoverFixtures_ReportsSeasonMetadataUnavailableWithoutFixtureCalls()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        factory.ApiSportsFootballClient.SuppressedLeagueMetadataIds.Add(39);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/discover-fixtures", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2025-08-15",
            toDate = "2025-08-15",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.Equal("SeasonMetadataUnavailable", competition.GetProperty("skipReason").GetString());
        Assert.Equal(1, competition.GetProperty("seasonMetadataApiCallsUsed").GetInt32());
        Assert.Equal(0, competition.GetProperty("fixtureDiscoveryApiCallsUsed").GetInt32());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_RemainsFixedSeasonAndDoesNotCallSeasonMetadata()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", new
        {
            competitionKeys = new[] { "world-cup-2026" },
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.Equal(2026, competition.GetProperty("season").GetInt32());
        Assert.Equal(0, factory.ApiSportsFootballClient.LeaguesCallCount);
        Assert.Equal(1, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerBackfill_AllSupportedSkipsOutOfSeasonCompetitionsWithoutCalls()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", new
        {
            includeAllSupportedCompetitions = true,
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            forceLive = true,
            summaryOnly = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competitions = json.GetProperty("competitions").EnumerateArray().ToArray();
        var epl = competitions.Single(row => row.GetProperty("key").GetString() == "EPL");
        var mls = competitions.Single(row => row.GetProperty("key").GetString() == "MLS");
        Assert.Equal("OutsideProviderSeasonWindow", epl.GetProperty("skipReason").GetString());
        Assert.Equal(0, epl.GetProperty("apiCallsUsed").GetInt32());
        Assert.Equal(1, mls.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.DoesNotContain(factory.ApiSportsFootballClient.FixtureRequests, request => request.League == 39);
        Assert.Contains(factory.ApiSportsFootballClient.FixtureRequests, request => request.League == 253 && request.Season == 2026);
    }

    [Fact]
    public async Task ApiSportsSoccerBackfill_DefaultsToDryRunAndDoesNotWrite()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await CountApiSportsSoccerResultsAsync(factory);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2025-08-15",
            toDate = "2025-08-15",
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("previewedFixtureCount").GetInt32());
        Assert.Equal(0, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("resultRowsPreviewed").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.False(json.TryGetProperty("imports", out _));
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.False(competition.TryGetProperty("imports", out _));
        Assert.False(competition.TryGetProperty("coverageByDate", out _));
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsSoccerBackfill_DetailFlagsIncludeFixtureDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", new
        {
            competitionKeys = new[] { "EPL" },
            fromDate = "2025-08-15",
            toDate = "2025-08-15",
            forceLive = true,
            summaryOnly = false,
            includeFixtures = true,
            includeCoverageByDate = true,
            includeUnsupportedSkipped = true,
            includeMappingDiagnostics = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var competition = Assert.Single(json.GetProperty("competitions").EnumerateArray());
        Assert.True(competition.TryGetProperty("imports", out var imports));
        Assert.True(competition.TryGetProperty("coverageByDate", out _));
        var import = Assert.Single(imports.EnumerateArray());
        Assert.True(import.TryGetProperty("unsupportedSkipped", out _));
        Assert.True(import.TryGetProperty("mappingDiagnostics", out _));
    }

    [Fact]
    public async Task SoccerResultImportWorker_SkipsOutOfSeasonCompetitionWithoutApiCalls()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        config["SoccerResultImportWorker:CompetitionKeys:0"] = "EPL";
        config["SoccerResultImportWorker:RecentWindowDays"] = "30";
        await using var factory = new SafetyTestApplicationFactory(config);
        await factory.EnsureDatabaseAsync();

        var worker = ActivatorUtilities.CreateInstance<SoccerResultImportWorker>(factory.Services);

        await worker.RunImportCycleAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await dbContext.SoccerResultImportStates.SingleAsync();
        Assert.Equal("EPL", state.Competition);
        Assert.Equal("skipped", state.LastStatus);
        Assert.Contains("OutsideProviderSeasonWindow", state.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, state.LastApiCallsUsed);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerBackfill_LiveWritesIdempotentlyAcrossCompetitions()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var request = new
        {
            competitionKeys = new[] { "world-cup-2026", "MLS" },
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true,
            maxFixturesPerCompetition = 1,
            maxFixturesTotal = 2
        };

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", request);
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(first.GetProperty("readOnly").GetBoolean());
        Assert.True(first.GetProperty("writesRows").GetBoolean());
        Assert.Equal(2, first.GetProperty("selectedCompletedFixtureCount").GetInt32());
        Assert.Equal(2, first.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(first.GetProperty("resultRowsInserted").GetInt32() > 0);

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/backfill-completed", request);
        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, second.GetProperty("alreadyImportedFixtureCount").GetInt32());
        Assert.Equal(0, second.GetProperty("selectedCompletedFixtureCount").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsUpdated").GetInt32());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.SoccerResultImportFixtureStates.CountAsync(row => row.ImportStatus == "Imported"));
    }

    [Fact]
    public async Task ApiSportsSoccerScheduledImportStatus_IsReadOnlyAcrossCompetitions()
    {
        var config = ApiSportsLiveConfig();
        config["SoccerResultImportWorker:Enabled"] = "true";
        await using var factory = new SafetyTestApplicationFactory(config);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var fixtureCallsBefore = factory.ApiSportsFootballClient.FixturesCallCount;
        var statsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/scheduled-import-status?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("workerEnabled").GetBoolean());
        Assert.True(json.GetProperty("scheduledImportEnabled").GetBoolean());
        Assert.Equal(8, json.GetProperty("aggregateTotals").GetProperty("enabledCompetitionCount").GetInt32());
        Assert.Contains(json.GetProperty("competitions").EnumerateArray(), row => row.GetProperty("key").GetString() == "world-cup-2026");
        Assert.Contains(json.GetProperty("competitions").EnumerateArray(), row => row.GetProperty("key").GetString() == "EPL");
        Assert.Equal(fixtureCallsBefore, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(statsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupImportStatus_WithForceLiveReportsDiscoveryCounts()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-14&forceLive=true&includeFixtures=true&includeCoverageByDate=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("discoveryAvailable").GetBoolean());
        Assert.Equal(4, json.GetProperty("apiCallsUsed").GetInt32());
        Assert.Equal(4, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(2, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("missingCompletedFixtureCount").GetInt32());
        var missing = Assert.Single(json.GetProperty("missingCompletedFixtures").EnumerateArray());
        Assert.Equal("99999", missing.GetProperty("fixtureId").GetString());
        var coverageByDate = json.GetProperty("coverageByDate").EnumerateArray().ToArray();
        Assert.Equal(4, coverageByDate.Length);
        var june14 = coverageByDate.Single(row => row.GetProperty("date").GetString() == "2026-06-14");
        Assert.Equal(1, june14.GetProperty("fixtureCount").GetInt32());
        Assert.Equal(1, june14.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(0, june14.GetProperty("importedFixtureCount").GetInt32());
        Assert.Equal(1, june14.GetProperty("missingCompletedFixtureCount").GetInt32());
        Assert.Equal(1, june14.GetProperty("apiCallsUsed").GetInt32());
    }

    [Fact]
    public async Task ApiSportsHistoricalCoverageStatus_ReturnsConfigAndPersistedCoverage_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "100001", new DateOnly(2026, 1, 15), "GOALS", "Goals");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "100001", new DateOnly(2026, 1, 15), "SHOTS", "Shots");
        var before = await CountApiSportsSoccerResultsAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/historical/coverage-status?fromDate=2026-01-01&toDate=2026-01-31");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("guardrails").GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.False(json.GetProperty("guardrails").GetProperty("soccerPredictionsEnabled").GetBoolean());
        Assert.False(json.GetProperty("guardrails").GetProperty("soccerEntryBuilderEnabled").GetBoolean());
        Assert.Equal(7, json.GetProperty("configuredCompetitionCount").GetInt32());
        Assert.Equal(7, json.GetProperty("enabledCompetitionCount").GetInt32());
        var statKeys = json.GetProperty("statsOfInterest").EnumerateArray().Select(row => row.GetProperty("statKey").GetString()!).ToArray();
        Assert.Equal(["GOALS", "ASSISTS", "SHOTS", "SHOTS_ON_TARGET"], statKeys);
        var epl = json.GetProperty("competitions").EnumerateArray().Single(row => row.GetProperty("key").GetString() == "EPL");
        Assert.Equal("EPL", epl.GetProperty("displayName").GetString());
        Assert.Equal(39, epl.GetProperty("leagueId").GetInt32());
        Assert.Equal(JsonValueKind.Null, epl.GetProperty("season").ValueKind);
        Assert.True(epl.GetProperty("enabled").GetBoolean());
        Assert.True(epl.GetProperty("localLeagueExists").GetBoolean());
        Assert.Contains(epl.GetProperty("localLeagueCodes").EnumerateArray(), row => row.GetString() == "API_SPORTS_EPL_2025");
        Assert.Contains(epl.GetProperty("importedSeasons").EnumerateArray(), row => row.GetInt32() == 2025);
        Assert.Equal(2, epl.GetProperty("importedResultRows").GetInt32());
        Assert.Equal(1, epl.GetProperty("importedFixtureCount").GetInt32());
        Assert.Equal(1, epl.GetProperty("distinctPlayers").GetInt32());
        var eplRowsByStat = epl.GetProperty("rowsByStat").EnumerateArray().ToArray();
        Assert.Contains(eplRowsByStat, row => row.GetProperty("statKey").GetString() == "GOALS" && row.GetProperty("resultRows").GetInt32() == 1);
        Assert.Contains(eplRowsByStat, row => row.GetProperty("statKey").GetString() == "SHOTS" && row.GetProperty("resultRows").GetInt32() == 1);
        var championsLeague = json.GetProperty("competitions").EnumerateArray().Single(row => row.GetProperty("key").GetString() == "CHAMPIONS_LEAGUE");
        Assert.False(championsLeague.GetProperty("localLeagueExists").GetBoolean());
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsHistoricalBackfill_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/historical/backfill-completed", new
        {
            competitionKey = "EPL",
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsHistoricalBackfill_RejectsRangeOverMaxDays()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/historical/backfill-completed", new
        {
            competitionKey = "EPL",
            fromDate = "2026-06-11",
            toDate = "2026-06-20",
            dryRun = true,
            forceLive = true
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DateRangeTooLarge", json.GetProperty("blockReason").GetString());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(7, json.GetProperty("maxRangeDays").GetInt32());
        Assert.Equal(10, json.GetProperty("requestedRangeDays").GetInt32());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsHistoricalBackfill_DryRunPreviewsCompletedFixtureStatsWithoutWriting()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await CountApiSportsSoccerResultsAsync(factory);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/historical/backfill-completed", new
        {
            competitionKey = "EPL",
            fromDate = "2025-08-15",
            toDate = "2025-08-17",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("guardrails").GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.True(json.GetProperty("guardrails").GetProperty("dryRunDefault").GetBoolean());
        Assert.False(json.GetProperty("guardrails").GetProperty("importsRows").GetBoolean());
        Assert.Equal("EPL", json.GetProperty("scope").GetProperty("key").GetString());
        Assert.Equal(39, json.GetProperty("scope").GetProperty("leagueId").GetInt32());
        Assert.Equal(2025, json.GetProperty("scope").GetProperty("season").GetInt32());
        Assert.Equal(3, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(2, json.GetProperty("skippedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("previewedFixtureCount").GetInt32());
        Assert.Equal(0, json.GetProperty("failedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("previewResultRows").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(0, json.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, json.GetProperty("resultRowsUpdated").GetInt32());
        Assert.Equal(0, json.GetProperty("resultRowsUnchanged").GetInt32());
        Assert.True(json.GetProperty("distinctPlayers").GetInt32() > 0);
        Assert.Equal(4, json.GetProperty("apiCallsUsed").GetInt32());
        var preview = Assert.Single(json.GetProperty("previews").EnumerateArray());
        Assert.Equal(0, preview.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(0, preview.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, preview.GetProperty("resultRowsUpdated").GetInt32());
        Assert.Equal(0, preview.GetProperty("resultRowsUnchanged").GetInt32());
        var rowsByStat = preview.GetProperty("rowsByStat").EnumerateArray().ToArray();
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "GOALS" && row.GetProperty("resultRows").GetInt32() > 0);
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "ASSISTS" && row.GetProperty("resultRows").GetInt32() > 0);
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "SHOTS" && row.GetProperty("resultRows").GetInt32() > 0);
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "SHOTS_ON_TARGET" && row.GetProperty("resultRows").GetInt32() > 0);
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsHistoricalBackfill_WritesIdempotently_WhenDryRunFalse()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await CountApiSportsSoccerResultsAsync(factory);

        var request = new
        {
            competitionKey = "EPL",
            fromDate = "2025-08-15",
            toDate = "2025-08-15",
            dryRun = false,
            forceLive = true
        };

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/historical/backfill-completed", request);
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(first.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(first.GetProperty("readOnly").GetBoolean());
        Assert.True(first.GetProperty("writesRows").GetBoolean());
        Assert.True(first.GetProperty("guardrails").GetProperty("importsRows").GetBoolean());
        var inserted = first.GetProperty("resultRowsInserted").GetInt32();
        Assert.True(inserted > 0);
        Assert.Equal(inserted, first.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(0, first.GetProperty("resultRowsUpdated").GetInt32());
        Assert.Equal(0, first.GetProperty("resultRowsUnchanged").GetInt32());
        Assert.Equal(before + inserted, await CountApiSportsSoccerResultsAsync(factory));

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/historical/backfill-completed", request);
        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, second.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsUpdated").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(inserted, second.GetProperty("resultRowsUnchanged").GetInt32());
        Assert.Equal(before + inserted, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerHistory_ReturnsPersistedHistoryReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "GOALS", "Goals");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "SHOTS", "Shots");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-2", new DateOnly(2026, 1, 2), "SHOTS_ON_TARGET", "Shots On Target");
        var fixtureCallsBefore = factory.ApiSportsFootballClient.FixturesCallCount;
        var statsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-history?providerPlayerId=50001&competitionKey=EPL&fromDate=2026-01-01&toDate=2026-01-31&limit=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        var guardrails = json.GetProperty("guardrails");
        Assert.False(guardrails.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.False(guardrails.GetProperty("soccerPredictionsEnabled").GetBoolean());
        Assert.False(guardrails.GetProperty("soccerEntryBuilderEnabled").GetBoolean());
        Assert.True(guardrails.GetProperty("researchOnly").GetBoolean());
        Assert.Equal(1, json.GetProperty("matchedPlayerCount").GetInt32());
        Assert.Equal(3, json.GetProperty("sampleSize").GetProperty("resultRows").GetInt32());
        Assert.True(json.GetProperty("linkedApiSportsRefs").GetArrayLength() > 0);
        var rowsByStat = json.GetProperty("rowsByStat").EnumerateArray().ToArray();
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "GOALS" && row.GetProperty("sampleSize").GetInt32() == 1);
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "SHOTS" && row.GetProperty("sampleSize").GetInt32() == 1);
        Assert.Contains(rowsByStat, row => row.GetProperty("statKey").GetString() == "SHOTS_ON_TARGET" && row.GetProperty("sampleSize").GetInt32() == 1);
        var recentLog = json.GetProperty("recentGameLog").EnumerateArray().ToArray();
        Assert.Equal(2, recentLog.Length);
        Assert.Equal(fixtureCallsBefore, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(statsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerHistoryReadiness_ReturnsIdentityCoverageReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "GOALS", "Goals");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "LA_LIGA", "La Liga", 2025, "hist-2", new DateOnly(2026, 1, 2), "ASSISTS", "Assists");
        var fixtureCallsBefore = factory.ApiSportsFootballClient.FixturesCallCount;
        var statsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-history/readiness");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("guardrails").GetProperty("researchOnly").GetBoolean());
        Assert.Equal(2, json.GetProperty("historical").GetProperty("resultRows").GetInt32());
        Assert.Equal(1, json.GetProperty("historical").GetProperty("distinctLocalPlayers").GetInt32());
        Assert.Equal(1, json.GetProperty("historical").GetProperty("distinctApiSportsProviderPlayers").GetInt32());
        Assert.Equal(1, json.GetProperty("identityLinks").GetProperty("historicalProviderPlayersWithExternalRef").GetInt32());
        Assert.Equal(fixtureCallsBefore, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(statsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task SoccerApiSportsBridgePreview_ReturnsSafeExactNameCandidateReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.AddSoccerPlatformIdentityCandidateAsync("Historical Test Forward", "Home FC");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "GOALS", "Goals");
        var linksBefore = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/bridge-preview?playerName=Historical%20Test%20Forward&minConfidence=0.98&limit=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("guardrails").GetProperty("researchOnly").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("safeBridgeCandidates").GetInt32());
        var candidate = Assert.Single(json.GetProperty("candidates").EnumerateArray());
        Assert.Equal("SafeExactName", candidate.GetProperty("category").GetString());
        Assert.Equal("50001", candidate.GetProperty("providerPlayerId").GetString());
        Assert.Equal(linksBefore, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
    }

    [Fact]
    public async Task SoccerApiSportsBridgeApply_DryRunThenWritesSafeLinkIdempotently()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var platformPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Historical Test Forward", "Home FC");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "GOALS", "Goals");
        var before = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/bridge-apply", new
        {
            playerName = "Historical Test Forward",
            dryRun = true,
            forceWrite = false
        });
        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.False(dryRunJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, dryRunJson.GetProperty("summary").GetProperty("safeBridgeCandidates").GetInt32());
        Assert.Equal(before, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/bridge-apply", new
        {
            playerName = "Historical Test Forward",
            dryRun = false,
            forceWrite = true
        });
        await EnsureSuccessWithBodyAsync(writeResponse);
        var writeJson = await writeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(writeJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, writeJson.GetProperty("summary").GetProperty("writtenCount").GetInt32());
        Assert.Equal(before + 1, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var link = await dbContext.ProviderEntityLinks.SingleAsync(row =>
                row.ProviderKey == "api-sports-football"
                && row.ProviderEntityId == "50001"
                && row.LinkStatus == "Approved");
            Assert.Equal(platformPlayerId, link.CanonicalEntityId);
            Assert.Equal("BridgeSafeExactName", link.LinkSource);
        }

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/bridge-apply", new
        {
            playerName = "Historical Test Forward",
            dryRun = false,
            forceWrite = true
        });
        await EnsureSuccessWithBodyAsync(secondResponse);
        Assert.Equal(before + 1, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
    }

    [Fact]
    public async Task SoccerApiSportsBridgePreviewAndApply_SupportPagingAndExcludeAlreadyLinked()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.AddSoccerPlatformIdentityCandidateAsync("Bridge Alpha", "Home FC");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Bridge Beta", "Home FC");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Bridge Gamma", "Home FC");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "fixture-alpha",
            new DateOnly(2026, 1, 1),
            "GOALS",
            "Goals",
            providerPlayerId: "60001",
            playerDisplayName: "Bridge Alpha");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "fixture-beta",
            new DateOnly(2026, 1, 2),
            "GOALS",
            "Goals",
            providerPlayerId: "60002",
            playerDisplayName: "Bridge Beta");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "fixture-gamma",
            new DateOnly(2026, 1, 3),
            "GOALS",
            "Goals",
            providerPlayerId: "60003",
            playerDisplayName: "Bridge Gamma");

        var pageResponse = await client.GetAsync("/api/debug/soccer/player-identity/bridge-preview?category=SafeExactName&excludeAlreadyLinked=true&offset=1&limit=1");

        await EnsureSuccessWithBodyAsync(pageResponse);
        var pageJson = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, pageJson.GetProperty("summary").GetProperty("safeBridgeCandidates").GetInt32());
        Assert.Equal(3, pageJson.GetProperty("pagination").GetProperty("filteredCandidateCount").GetInt32());
        Assert.Equal(1, pageJson.GetProperty("pagination").GetProperty("returned").GetInt32());
        Assert.True(pageJson.GetProperty("pagination").GetProperty("hasMore").GetBoolean());
        var pagedCandidate = Assert.Single(pageJson.GetProperty("candidates").EnumerateArray());
        Assert.Equal("Bridge Beta", pagedCandidate.GetProperty("platformPlayer").GetProperty("playerName").GetString());

        var firstApplyResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/bridge-apply", new
        {
            category = "SafeExactName",
            excludeAlreadyLinked = true,
            limit = 2,
            dryRun = false,
            forceWrite = true
        });
        await EnsureSuccessWithBodyAsync(firstApplyResponse);
        var firstApplyJson = await firstApplyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, firstApplyJson.GetProperty("summary").GetProperty("writtenCount").GetInt32());

        var secondApplyResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/bridge-apply", new
        {
            category = "SafeExactName",
            excludeAlreadyLinked = true,
            limit = 2,
            dryRun = false,
            forceWrite = true
        });
        await EnsureSuccessWithBodyAsync(secondApplyResponse);
        var secondApplyJson = await secondApplyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, secondApplyJson.GetProperty("summary").GetProperty("writtenCount").GetInt32());
        Assert.Equal(3, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
    }

    [Fact]
    public async Task SoccerApiSportsBridgePreview_BlocksDuplicatePlatformNameFromSafeLinking()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.AddSoccerPlatformIdentityCandidateAsync("Historical Test Forward", "Home FC");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Historical Test Forward", "Away FC", providerName: "Underdog");
        await AddApiSportsHistoricalSoccerResultAsync(factory, "EPL", "EPL", 2025, "hist-1", new DateOnly(2026, 1, 1), "GOALS", "Goals");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/bridge-preview?playerName=Historical%20Test%20Forward&minConfidence=0.98&limit=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("safeBridgeCandidates").GetInt32());
        Assert.True(json.GetProperty("summary").GetProperty("needsReview").GetInt32() >= 1);
        Assert.All(json.GetProperty("candidates").EnumerateArray(), candidate =>
            Assert.Contains(candidate.GetProperty("blockingConflicts").EnumerateArray(), blocker => blocker.GetString() == "DuplicatePlatformPlayersWithSameNormalizedName"));
    }

    [Fact]
    public async Task SoccerDuplicateBridgePreviewAndApply_LinksResolvableDuplicatePlatformPlayers()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var prizePicksPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Duplicate Test Forward", "Home FC", providerName: "PrizePicks");
        var underdogPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Duplicate Test Forward", "Home FC", providerName: "Underdog");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "fixture-duplicate",
            new DateOnly(2026, 1, 1),
            "GOALS",
            "Goals",
            providerPlayerId: "61001",
            playerDisplayName: "Duplicate Test Forward");
        var before = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);

        var previewResponse = await client.GetAsync("/api/debug/soccer/player-identity/duplicate-bridge-preview?playerName=Duplicate%20Test%20Forward&onlyResolvable=true&limit=10");

        await EnsureSuccessWithBodyAsync(previewResponse);
        var previewJson = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(previewJson.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(previewJson.GetProperty("readOnly").GetBoolean());
        Assert.False(previewJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, previewJson.GetProperty("summary").GetProperty("resolvableDuplicateGroupCount").GetInt32());
        Assert.Equal(2, previewJson.GetProperty("summary").GetProperty("duplicatePlayersLinkableCount").GetInt32());
        var group = Assert.Single(previewJson.GetProperty("groups").EnumerateArray());
        Assert.Equal("ResolvableDuplicateSameProvider", group.GetProperty("category").GetString());
        Assert.Equal("61001", group.GetProperty("selectedProviderPlayerId").GetString());

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/duplicate-bridge-apply", new
        {
            playerName = "Duplicate Test Forward",
            dryRun = true,
            forceWrite = false,
            limit = 10
        });
        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.False(dryRunJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(before, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/duplicate-bridge-apply", new
        {
            playerName = "Duplicate Test Forward",
            dryRun = false,
            forceWrite = true,
            limit = 10
        });
        await EnsureSuccessWithBodyAsync(writeResponse);
        var writeJson = await writeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(writeJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(2, writeJson.GetProperty("summary").GetProperty("writtenCount").GetInt32());
        Assert.Equal(before + 2, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var linkedPlayerIds = await dbContext.ProviderEntityLinks
                .Where(link => link.ProviderKey == "api-sports-football"
                    && link.ProviderEntityId == "61001"
                    && link.LinkStatus == "Approved")
                .Select(link => link.CanonicalEntityId)
                .ToArrayAsync();
            Assert.Contains(prizePicksPlayerId, linkedPlayerIds);
            Assert.Contains(underdogPlayerId, linkedPlayerIds);
        }
    }

    [Fact]
    public async Task SoccerDuplicateBridgePreview_BlocksExistingMixedLinks()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var platformPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Mixed Link Forward", "Home FC", providerName: "PrizePicks");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Mixed Link Forward", "Home FC", providerName: "Underdog");
        await AddApiSportsHistoricalSoccerResultAsync(
            factory,
            "EPL",
            "EPL",
            2025,
            "fixture-mixed",
            new DateOnly(2026, 1, 1),
            "GOALS",
            "Goals",
            providerPlayerId: "62001",
            playerDisplayName: "Mixed Link Forward");
        await AddApiSportsProviderEntityLinkAsync(factory, platformPlayerId, "different-provider-player", "Mixed Link Forward", "TestMixedLink");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/duplicate-bridge-preview?playerName=Mixed%20Link%20Forward&onlyResolvable=false&limit=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("resolvableDuplicateGroupCount").GetInt32());
        var group = Assert.Single(json.GetProperty("groups").EnumerateArray());
        Assert.Equal("ExistingMixedLinks", group.GetProperty("category").GetString());
        Assert.Contains(group.GetProperty("blockingConflicts").EnumerateArray(), blocker => blocker.GetString() == "DuplicatePlatformPlayerAlreadyLinkedToDifferentApiSportsProviderPlayer");
    }

    [Fact]
    public async Task ApiSportsWorldCupCoverage_ReturnsPersistedResultCountsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var before = await CountApiSportsSoccerResultsAsync(factory);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(before, json.GetProperty("importedResultSummary").GetProperty("totalRows").GetInt32());
        Assert.Equal(1, json.GetProperty("importedResultSummary").GetProperty("distinctFixtures").GetInt32());
        Assert.NotEmpty(json.GetProperty("importedResultSummary").GetProperty("rowsByPropType").EnumerateArray());
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsWorldCupCoverage_ReportsNoMarketDataGracefully_WithSamples()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var join = json.GetProperty("marketJoinCoverage");
        Assert.True(join.GetProperty("noMarketDataAvailable").GetBoolean());
        Assert.Equal(0, join.GetProperty("matchedAny").GetInt32());
        Assert.True(join.GetProperty("unmatchedCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mismatchBreakdown").GetProperty("marketMissing").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("marketMissing").EnumerateArray());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupMarketJoinPreview_DetectsEventPlayerPropOverlap_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync();
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());

        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("importedResultRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("candidatePlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("eventMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("playerNameMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("propTypeMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("playerAndPropMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("unmatchedResultRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("unmatchedPlatformRows").GetInt32());

        var matched = Assert.Single(json.GetProperty("samples").GetProperty("matched").EnumerateArray());
        Assert.Equal("1489393", matched.GetProperty("apiSportsFixtureId").GetString());
        Assert.Equal("Test Forward", matched.GetProperty("apiSportsPlayerName").GetString());
        Assert.Equal("GOALS", matched.GetProperty("apiSportsStatKey").GetString());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", matched.GetProperty("providerEventId").GetString());
        Assert.Equal("player_goals", matched.GetProperty("providerMarketKey").GetString());
        Assert.Equal("betmgm", matched.GetProperty("bookmaker").GetString());

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupMarketJoinPreview_ReportsMissingProviderContextBlockers()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeProviderEventId: false,
            includeResultTeam: false,
            includeMarketKey: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var blockers = json.GetProperty("blockers");
        Assert.Equal(1, blockers.GetProperty("missingProviderEventId").GetInt32());
        Assert.Equal(1, blockers.GetProperty("missingApiSportsFixtureIdOnMarketRows").GetInt32());
        Assert.True(blockers.GetProperty("missingPlayerTeam").GetInt32() >= 2);
        Assert.Equal(1, blockers.GetProperty("missingProviderPlayerId").GetInt32());
        Assert.True(blockers.GetProperty("propTypeMappingMissing").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("unmatchedResults").EnumerateArray());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_IdentifiesMissingTheOddsMarketMapping_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_crosses",
            platformPropTypeName: "Crosses",
            platformStatKey: "CROSSES",
            apiSportsPropTypeName: "Crosses",
            apiSportsStatKey: "CROSSES");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Contains(
            json.GetProperty("theOddsMarketGaps").EnumerateArray(),
            row => row.GetProperty("providerMarketKey").GetString() == "player_crosses"
                && row.GetProperty("category").GetString() == "TheOddsMarketKeyMissingCanonicalMapping");
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_IdentifiesMissingApiSportsStatMapping()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Mystery Stat",
            apiSportsStatKey: "MYSTERY_STAT");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "MYSTERYSTAT"
                && row.GetProperty("category").GetString() == "ApiSportsStatKeyMissingProviderMapping");
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_SeparatesImportedStatNoise()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Passes Attempted",
            apiSportsStatKey: "PASSES_ATTEMPTED");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "PASSES_ATTEMPTED"
                && row.GetProperty("category").GetString() == "ImportedStatNotRequestedByTheOdds"
                && row.GetProperty("recommendation").GetString() == "IgnoreImportedStatNoise");
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_DetectsMappingExistsButNotGradeable()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Goal + Assist",
            apiSportsStatKey: "GOAL_ASSIST");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "GOAL_ASSIST"
                && row.GetProperty("category").GetString() == "MappingExistsButNotGradeable"
                && row.GetProperty("isGradeable").GetBoolean() == false);
    }

    [Fact]
    public async Task ApiSportsWorldCupStatMappingReview_IdentifiesNonGradeableMappings_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Goal + Assist",
            apiSportsStatKey: "GOAL_ASSIST");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();
        var before = await factory.CountArtifactsAsync();
        var apiSportsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.False(json.GetProperty("summary").GetProperty("publicGradingEligible").GetBoolean());
        Assert.Contains(
            json.GetProperty("mappings").EnumerateArray(),
            row => row.GetProperty("canonicalStatKey").GetString() == "GOAL_ASSIST"
                && row.GetProperty("ruleConfidence").GetString() == "NeedsConfirmation"
                && row.GetProperty("isGradeable").GetBoolean() == false
                && row.GetProperty("rowCountAffected").GetInt32() == 1
                && row.GetProperty("recommendation").GetString() == "NeedsMoreProviderEvidence"
                && row.GetProperty("publicGradingEligible").GetBoolean() == false);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(apiSportsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupStatMappingReview_SeparatesImportedStatNoise()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Passes Attempted",
            apiSportsStatKey: "PASSES_ATTEMPTED");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("summary").GetProperty("importedStatNoiseRows").GetInt32() >= 1);
        Assert.Contains(
            json.GetProperty("importedStatNoise").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "PASSES_ATTEMPTED"
                && row.GetProperty("recommendation").GetString() == "IgnoreAsImportedNoise");
        Assert.DoesNotContain(
            json.GetProperty("mappings").EnumerateArray(),
            row => row.GetProperty("canonicalStatKey").GetString() == "PASSES_ATTEMPTED"
                && row.GetProperty("recommendation").GetString() == "ManualApprovalCandidate");
    }

    [Fact]
    public async Task SoccerWorldCupSpikeStatus_IsResearchOnlyReadOnlyAndNoExternalApis()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();
        await AddSoccerTheOddsRefreshRunAsync(factory);
        var before = await factory.CountArtifactsAsync();
        var apiSportsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;
        var theOddsCallsBefore = factory.TheOddsAdapter.CallCount;

        var response = await client.GetAsync("/api/debug/soccer/world-cup/spike-status?fromDate=2026-06-21&toDate=2026-06-24&includeDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("World Cup", json.GetProperty("competition").GetString());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.False(json.GetProperty("entryBuilderEnabled").GetBoolean());

        var readiness = json.GetProperty("productionReadiness");
        Assert.Equal("ResearchOnly", readiness.GetProperty("status").GetString());
        Assert.False(readiness.GetProperty("canShowPublicGradedPerformance").GetBoolean());
        Assert.False(readiness.GetProperty("canUseForEntryBuilder").GetBoolean());

        var guardrails = json.GetProperty("guardrails");
        Assert.True(guardrails.GetProperty("publicEndpointsReadOnly").GetBoolean());
        Assert.True(guardrails.GetProperty("publicSoccerGradingDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("soccerEntryBuilderDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(guardrails.GetProperty("autoAliasApprovalDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("autoStatMappingApprovalDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("needsConfirmationMappingsNotPublicGradeable").GetBoolean());

        var summary = json.GetProperty("summary");
        Assert.True(summary.GetProperty("totalCandidatePlatformRows").GetInt32() >= 1);
        Assert.True(summary.GetProperty("aliasPlatformPlayerNamesEvaluated").GetInt32() >= 1);
        Assert.True(summary.GetProperty("aliasCandidateCount").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasLinksApproved").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasSafeCandidatesApproved").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasManualApprovalsApproved").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasNeedsReviewRemaining").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasNoCandidateRemaining").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasAmbiguousRemaining").GetInt32() >= 0);
        Assert.True(summary.GetProperty("aliasNewSafeCandidatesRemaining").GetInt32() >= 0);
        Assert.False(summary.TryGetProperty("unresolvedAliasCandidates", out _));
        Assert.NotEmpty(json.GetProperty("blockers").EnumerateArray());
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(apiSportsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
        Assert.Equal(theOddsCallsBefore, factory.TheOddsAdapter.CallCount);
    }

    [Theory]
    [InlineData(1, 0.5, true, false, "OverWin", 1, 0, 0, 0)]
    [InlineData(0, 0.5, false, true, "UnderWin", 0, 1, 0, 0)]
    [InlineData(0.5, 0.5, true, false, "Push", 0, 0, 1, 0)]
    [InlineData(1, 0.5, false, false, "NoLine", 0, 0, 0, 1)]
    public async Task ApiSportsWorldCupGradingPreview_GradesJoinableRows(
        decimal actualValue,
        decimal line,
        bool overAvailable,
        bool underAvailable,
        string expectedGrade,
        int expectedOverWins,
        int expectedUnderWins,
        int expectedPushes,
        int expectedNoLineRows)
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            actualValue: actualValue,
            currentLine: line,
            overAvailable: overAvailable,
            underAvailable: underAvailable);
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());

        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("candidatePlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("importedResultRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(expectedNoLineRows == 0 ? 1 : 0, summary.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(expectedOverWins, summary.GetProperty("overWins").GetInt32());
        Assert.Equal(expectedUnderWins, summary.GetProperty("underWins").GetInt32());
        Assert.Equal(expectedPushes, summary.GetProperty("pushes").GetInt32());
        Assert.Equal(expectedNoLineRows, summary.GetProperty("noLineRows").GetInt32());

        var sample = Assert.Single(json.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.Equal(expectedGrade, sample.GetProperty("gradeResult").GetString());
        Assert.Equal("Sportsbook", sample.GetProperty("providerCategory").GetString());
        Assert.True(sample.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.False(sample.GetProperty("entryActionableForUser").GetBoolean());
        Assert.Equal(actualValue, sample.GetProperty("actualValue").GetDecimal());
        Assert.Equal(line, sample.GetProperty("line").GetDecimal());

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingPreview_DoesNotGradeUnjoinableRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(platformPlayerName: "Different Forward");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("ungradedRows").GetInt32());
        Assert.Equal(2, json.GetProperty("blockers").GetProperty("unjoinableRows").GetInt32());
        Assert.Empty(json.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("unjoinableResults").EnumerateArray());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReturnsSafeZeroSummaryForEmptyData()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalCandidatePlatformRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalImportedResultRows").GetInt32());
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReportsResultsWithoutPlatformRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(includePlatformRow: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("resultOnlyFixtures").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var resultOnly = Assert.Single(json.GetProperty("resultOnlyFixtures").EnumerateArray());
        Assert.Equal("1489393", resultOnly.GetProperty("fixtureId").GetString());
        Assert.Equal(1, resultOnly.GetProperty("importedResultRows").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReportsPlatformRowsWithoutResults()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(includeResultRow: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("unresolvedProviderEvents").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var unresolved = Assert.Single(json.GetProperty("unresolvedProviderEvents").EnumerateArray());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", unresolved.GetProperty("providerEventId").GetString());
        Assert.Equal(1, unresolved.GetProperty("candidatePlatformRows").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_BucketsSyntheticProviderEventsSeparately()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            useTheOddsContext: false,
            providerEventIdOverride: "320878-50-SCR_game_8upFm4DZHyMeLIkeLhuBs52o-11-false");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedProviderEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("platformOnlyEvents").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var synthetic = Assert.Single(json.GetProperty("platformOnlyEvents").EnumerateArray());
        Assert.Equal("NotTheOddsSoccerWorldCupProviderEvent", synthetic.GetProperty("reason").GetString());
        Assert.Equal("320878-50-SCR_game_8upFm4DZHyMeLIkeLhuBs52o-11-false", synthetic.GetProperty("providerEventId").GetString());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_UsesApprovedAliasLinks()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398");
        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });
        await EnsureSuccessWithBodyAsync(writeResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true&includeSamples=true&maxSamplesPerFixture=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("knownWorkingPairDetected").GetBoolean());
        var detail = Assert.Single(json.GetProperty("fixtureDetails").EnumerateArray());
        Assert.Equal(0, detail.GetProperty("fullyJoinableRowsBeforeAlias").GetInt32());
        Assert.Equal(1, detail.GetProperty("fullyJoinableRowsAfterAlias").GetInt32());
        Assert.Equal(1, detail.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(1, detail.GetProperty("aliasLinkedPlayers").GetInt32());
        Assert.NotEmpty(detail.GetProperty("sampleAliasLinkedRows").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_AggregatesBlockersAndIsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(platformPlayerName: "Different Forward");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, json.GetProperty("topBlockersAggregated").GetProperty("playerNameMismatch").GetInt32());
        Assert.True(json.GetProperty("topBlockersAggregated").GetProperty("unjoinableRows").GetInt32() > 0);
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ResolvesKnownWorkingProviderEvent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.True(json.GetProperty("knownWorkingPairDetected").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedPlatformRows").GetInt32());
        var resolved = Assert.Single(json.GetProperty("resolvedEvents").EnumerateArray());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", resolved.GetProperty("providerEventId").GetString());
        Assert.Equal("1489398", resolved.GetProperty("resolvedFixtureId").GetString());
        Assert.Equal("Resolved", resolved.GetProperty("resolutionStatus").GetString());
        Assert.True(resolved.GetProperty("confidence").GetDecimal() >= 0.80m);
        Assert.Contains(resolved.GetProperty("matchReasons").EnumerateArray(), row => row.GetString() == "ExactEventDate");
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ExcludesSyntheticProviderEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            providerEventIdOverride: "852a3abe6d46820720febc0fedebff9a|balanced");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedEvents").GetInt32());
        var excluded = Assert.Single(json.GetProperty("excludedEvents").EnumerateArray());
        Assert.Equal("Excluded", excluded.GetProperty("resolutionStatus").GetString());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a|balanced", excluded.GetProperty("providerEventId").GetString());
        Assert.Contains(excluded.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "NotTheOddsSoccerWorldCupProviderEvent");
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ReportsNoCandidateEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489400", new DateOnly(2026, 6, 25), "Mexico");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-25&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("noCandidateEvents").GetInt32());
        var unresolved = Assert.Single(json.GetProperty("unresolvedEvents").EnumerateArray());
        Assert.Equal("NoCandidate", unresolved.GetProperty("resolutionStatus").GetString());
        Assert.Contains(unresolved.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "NoFixtureCandidates");
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ReportsAmbiguousEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489399", new DateOnly(2026, 6, 21), "Uruguay");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("ambiguousEvents").GetInt32());
        var unresolved = Assert.Single(json.GetProperty("unresolvedEvents").EnumerateArray());
        Assert.Equal("Ambiguous", unresolved.GetProperty("resolutionStatus").GetString());
        Assert.Contains(unresolved.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "MultipleHighConfidenceFixtures");
        Assert.True(unresolved.GetProperty("candidateApiSportsFixtures").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_UsesWorldCupTeamAliases()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            fixtureId: "1489398",
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde",
            resultPlayerTeamName: "Cape Verde Islands");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var resolved = Assert.Single(json.GetProperty("resolvedEvents").EnumerateArray());
        Assert.Equal("Resolved", resolved.GetProperty("resolutionStatus").GetString());
        Assert.Equal("1489398", resolved.GetProperty("resolvedFixtureId").GetString());
        Assert.Contains(resolved.GetProperty("matchReasons").EnumerateArray(), row => row.GetString() == "OneTeamMatch");
    }

    [Fact]
    public async Task TheOddsSoccerWorldCupIngestionCoverage_CountsRealLegacyAndApiSportsGaps_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        await AddLegacySoccerPlatformPropAsync(factory, "320878-50-SCR_game_test");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489400", new DateOnly(2026, 6, 22), "Mexico");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-21&toDate=2026-06-22&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("realTheOddsProviderEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("realTheOddsPlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("syntheticOrLegacySoccerRows").GetInt32());
        Assert.Equal(2, summary.GetProperty("apiSportsFixturesWithResults").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedProviderEvents").GetInt32());
        Assert.Equal(1, json.GetProperty("realProviderEvents").GetArrayLength());
        Assert.Equal(1, json.GetProperty("syntheticOrLegacySoccerRows").GetProperty("samples").GetArrayLength());
        Assert.Equal(1, json.GetProperty("gapAnalysis").GetProperty("apiSportsFixturesWithResultsButNoRealTheOddsEvent").GetInt32());
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsSoccerWorldCupIngestionCoverage_SurfacesLatestSoccerRefreshDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        var refreshRunId = await AddSoccerTheOddsRefreshRunAsync(factory);

        var response = await client.GetAsync("/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(refreshRunId, summary.GetProperty("latestSoccerRefreshRunId").GetGuid());
        Assert.True(summary.GetProperty("latestSoccerRefreshSucceeded").GetBoolean());
        Assert.Equal(34, summary.GetProperty("latestSoccerRefreshEventCount").GetInt32());
        Assert.Equal(119, summary.GetProperty("latestSoccerRefreshPlatformRowsUpserted").GetInt32());
        var refresh = json.GetProperty("latestSoccerRefreshRun");
        Assert.Equal("SOCCER", refresh.GetProperty("sport").GetString());
        Assert.Equal("Sportsbook", refresh.GetProperty("providerCategoriesIncluded").GetString());
        Assert.Equal("player_goals,player_shots", refresh.GetProperty("marketsRequested").GetString());
        Assert.Equal(34, refresh.GetProperty("diagnostics").GetProperty("eventsFetched").GetInt32());
    }

    [Theory]
    [InlineData("Test Forward", "Test Forward", "SafeAliasCandidate", true)]
    [InlineData("Mathías Olivera", "Mathias Olivera", "SafeAliasCandidate", true)]
    [InlineData("Maximiliano Araújo", "Maximiliano Javier Araujo Vilches", "SafeAliasCandidate", false)]
    [InlineData("Ryan Mendes", "Ryan Mendes da Graca", "SafeAliasCandidate", false)]
    public async Task TheOddsApiSportsAliasCandidates_ReturnsConservativeNameAliases(
        string apiSportsName,
        string theOddsName,
        string expectedAction,
        bool expectedExactNormalized)
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync([apiSportsName], [theOddsName]);
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("safeCandidateCount").GetInt32());
        Assert.Equal(expectedExactNormalized ? 0 : 1, json.GetProperty("summary").GetProperty("totalUnjoinedPlatformPlayers").GetInt32());

        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal(theOddsName, sample.GetProperty("platformPlayerName").GetString());
        Assert.Equal(expectedAction, sample.GetProperty("suggestedAction").GetString());
        Assert.Equal(expectedExactNormalized, sample.GetProperty("exactNormalizedMatch").GetBoolean());
        var candidate = Assert.Single(sample.GetProperty("candidates").EnumerateArray());
        Assert.Equal(apiSportsName, candidate.GetProperty("apiSportsPlayerName").GetString());
        Assert.True(candidate.GetProperty("confidence").GetDecimal() >= 0.92m);

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidates_BlocksAmbiguousLongNameCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca"]);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("ambiguousCount").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("safeCandidateCount").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal("Ambiguous", sample.GetProperty("suggestedAction").GetString());
        Assert.Equal(2, sample.GetProperty("candidates").GetArrayLength());
        Assert.Contains(sample.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AmbiguousCandidate");
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidates_ReturnsNoCandidateForWeakOverlap()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Kevin Lenini"], ["Completely Different"]);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("noCandidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockers").GetProperty("insufficientTokenOverlap").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("noCandidate").EnumerateArray());
        Assert.Equal("NoCandidate", sample.GetProperty("suggestedAction").GetString());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_DryRunProposesSafeLinksAndWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = true,
            minConfidence = 0.94m,
            maxLinks = 50
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("safeCandidates").GetInt32());
        Assert.Single(json.GetProperty("proposedLinks").EnumerateArray());
        Assert.Empty(json.GetProperty("writtenLinks").EnumerateArray());
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_WritesSafelyAndImprovesGradingPreviewJoin()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches");

        var beforePreview = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(beforePreview);
        var beforeJson = await beforePreview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, beforeJson.GetProperty("summary").GetProperty("fullyJoinableRows").GetInt32());

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489393",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 50
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));

        var afterPreview = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(afterPreview);
        var afterJson = await afterPreview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, afterJson.GetProperty("summary").GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(1, afterJson.GetProperty("summary").GetProperty("gradedPreviewRows").GetInt32());
        var sample = Assert.Single(afterJson.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.Contains("approvedTheOddsAliasLink", sample.GetProperty("joinMethod").GetString());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_IsIdempotentOnSecondWrite()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);

        var first = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });
        await EnsureSuccessWithBodyAsync(first);

        var second = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });

        await EnsureSuccessWithBodyAsync(second);
        var json = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksAlreadyExisting").GetInt32());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_RejectsAmbiguousAndNoCandidateRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca", "Completely Different"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedAmbiguous").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedNoCandidate").GetInt32());
        Assert.Empty(json.GetProperty("writtenLinks").EnumerateArray());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_RespectsMinimumConfidence()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false,
            minConfidence = 0.95m
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedBelowConfidence").GetInt32());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_DryRunWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);
        var candidate = await GetSingleTheOddsAliasCandidateAsync(client);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = true,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal("NeedsReview", json.GetProperty("summary").GetProperty("suggestedAction").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("Carlos Casemiro", json.GetProperty("proposedLink").GetProperty("platformPlayerName").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_WritesNeedsReviewLinkAndIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);
        var candidate = await GetSingleTheOddsAliasCandidateAsync(client);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("Carlos Casemiro", json.GetProperty("writtenLink").GetProperty("platformPlayerName").GetString());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));

        var second = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(second);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, secondJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("AlreadyLinked", secondJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_RejectsAmbiguousAndNoCandidateRows()
    {
        await using var ambiguousFactory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var ambiguousClient = ambiguousFactory.CreateClient();
        await ambiguousFactory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca"]);

        var ambiguousResponse = await ambiguousClient.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Ryan Mendes da Graca",
            apiSportsPlayerId = "1000",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(ambiguousResponse);
        var ambiguousJson = await ambiguousResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(ambiguousJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal("Ambiguous", ambiguousJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(ambiguousFactory));

        await using var noCandidateFactory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var noCandidateClient = noCandidateFactory.CreateClient();
        await noCandidateFactory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Kevin Lenini"], ["Completely Different"]);

        var noCandidateResponse = await noCandidateClient.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Completely Different",
            apiSportsPlayerId = "1000",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(noCandidateResponse);
        var noCandidateJson = await noCandidateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(noCandidateJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal("NoCandidate", noCandidateJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(noCandidateFactory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_RejectsMismatchedCandidateIds()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = "wrong-player-id",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal("CandidateIdMismatch", json.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidatesBatch_ReusesSingleFixtureBehaviorAcrossResolvedPairs()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await SeedTwoResolvedSoccerAliasPairsAsync(factory);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch?fromDate=2026-06-21&toDate=2026-06-22&includeSamples=true&maxFixtures=50&maxSamples=20&minConfidence=0.94");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(2, summary.GetProperty("platformPlayerNamesEvaluated").GetInt32());
        Assert.Equal(2, summary.GetProperty("safeCandidateCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("ambiguousCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("alreadyLinkedCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("newSafeCandidateCount").GetInt32());
        Assert.Equal(2, json.GetProperty("candidateGroups").GetArrayLength());
        Assert.True(json.GetProperty("deduplicatedCandidates").GetArrayLength() >= 2);
        var impact = json.GetProperty("estimatedCoverageImpact");
        Assert.Equal(0, impact.GetProperty("currentFullyJoinableRows").GetInt32());
        Assert.True(impact.GetProperty("projectedAdditionalJoinableRows").GetInt32() >= 2);
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLinkBatch_DryRunWritesNothing_RealWriteIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await SeedTwoResolvedSoccerAliasPairsAsync(factory);

        var dryRun = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = true,
            minConfidence = 0.94m,
            maxLinks = 200,
            includeExactNormalizedMatches = true,
            includeTokenSubsetMatches = true
        });

        await EnsureSuccessWithBodyAsync(dryRun);
        var dryRunJson = await dryRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.False(dryRunJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(2, dryRunJson.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, dryRunJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));

        var write = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200,
            includeExactNormalizedMatches = true,
            includeTokenSubsetMatches = true
        });

        await EnsureSuccessWithBodyAsync(write);
        var writeJson = await write.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(writeJson.GetProperty("readOnly").GetBoolean());
        Assert.True(writeJson.GetProperty("writesRows").GetBoolean());
        Assert.False(writeJson.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(2, writeJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(2, writeJson.GetProperty("writtenLinks").GetArrayLength());
        Assert.Equal(2, await CountTheOddsSoccerAliasLinksAsync(factory));

        var secondWrite = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200
        });

        await EnsureSuccessWithBodyAsync(secondWrite);
        var secondJson = await secondWrite.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, secondJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(2, secondJson.GetProperty("summary").GetProperty("linksAlreadyExisting").GetInt32());
        Assert.Equal(2, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLinkBatch_SkipsExistingConflictingLinks()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398",
            providerEventIdOverride: "event-conflict");
        await AddConflictingTheOddsSoccerAliasLinkAsync(
            factory,
            "event-conflict",
            "MAXIMILIANOJAVIERARAUJOVILCHES");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-21",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedConflictingExisting").GetInt32());
        Assert.Single(json.GetProperty("skipped").EnumerateArray());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCoverage_IsReadOnlyAndReportsNoCandidatesGracefully()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var before = await CountApiSportsSoccerResultsAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("importedPlayers").GetProperty("distinctApiSportsPlayers").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("existingSoccerIdentity").GetProperty("soccerPlatformProps").GetInt32());
        Assert.True(json.GetProperty("matchSummary").GetProperty("unmatchedCount").GetInt32() > 0);
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCandidates_ReturnsNormalizedNameTeamCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Tést-Forward", "Home FC", addApiSportsRef: true);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/candidates?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var candidate = Assert.Single(json.GetProperty("candidates").EnumerateArray());
        Assert.Equal("1001", candidate.GetProperty("apiSportsPlayerId").GetString());
        Assert.Equal("Tést-Forward", candidate.GetProperty("candidateCanonicalPlayerName").GetString());
        Assert.True(candidate.GetProperty("confidence").GetDecimal() >= 0.95m);
        Assert.Equal("SafeAliasLink", candidate.GetProperty("suggestedAction").GetString());
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCoverage_ReportsAmbiguousDuplicateNames()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", providerName: "Underdog");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("matchSummary").GetProperty("ambiguousMatchCount").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("ambiguous").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerPlatformContext_ReturnsReadOnlyEmptyContextGracefully()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await CountApiSportsSoccerResultsAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, json.GetProperty("soccerPlatformProps").GetInt32());
        Assert.Equal(0, json.GetProperty("fieldAvailability").GetProperty("hasTeamNameCount").GetInt32());
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlatformContext_ReportsPlatformTeamAndProviderFields()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("soccerPlatformProps").GetInt32());
        Assert.Equal(1, json.GetProperty("soccerPlatformPlayers").GetInt32());
        Assert.Equal(1, json.GetProperty("fieldAvailability").GetProperty("hasTeamNameCount").GetInt32());
        Assert.Equal(1, json.GetProperty("fieldAvailability").GetProperty("hasStartTimeCount").GetInt32());
        Assert.Equal(0, json.GetProperty("fieldAvailability").GetProperty("hasProviderPlayerIdCount").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").EnumerateArray());
        Assert.Equal("Test Forward", sample.GetProperty("playerName").GetString());
        Assert.Equal("Home FC", sample.GetProperty("teamName").GetString());
    }

    [Fact]
    public async Task ProviderEntityLinksDiagnostic_IsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var before = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/identity/provider-links?sport=SOCCER&entityType=Player&providerKey=api-sports-football&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(before, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(before, json.GetProperty("candidateCount").GetInt32());
        Assert.NotEmpty(json.GetProperty("samples").EnumerateArray());
        Assert.Equal(before, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityReviewQueue_IsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var before = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        var row = Assert.Single(json.GetProperty("rows").EnumerateArray(), row => row.GetProperty("apiSportsPlayerId").GetString() == "1001");
        Assert.Equal(before.Id, row.GetProperty("providerEntityLinkId").GetGuid());
        Assert.Equal("ApproveCandidate", row.GetProperty("suggestedAction").GetString());
        var after = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal(before.LinkStatus, after.LinkStatus);
        Assert.Null(after.CanonicalEntityId);
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityReviewQueue_FiltersCandidatesAndSortsActionableFirst()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);

        var filteredResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&suggestedAction=ApproveCandidate&maxResults=25");

        await EnsureSuccessWithBodyAsync(filteredResponse);
        var filteredJson = await filteredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filteredRows = filteredJson.GetProperty("rows").EnumerateArray().ToArray();
        Assert.NotEmpty(filteredRows);
        Assert.All(filteredRows, row =>
        {
            Assert.True(row.GetProperty("hasCandidate").GetBoolean());
            Assert.Equal("ApproveCandidate", row.GetProperty("suggestedAction").GetString());
            Assert.True(row.GetProperty("confidence").GetDecimal() >= 0.80m);
        });

        var sortedResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&includeNoCandidate=true&maxResults=25");

        await EnsureSuccessWithBodyAsync(sortedResponse);
        var sortedJson = await sortedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sortedRows = sortedJson.GetProperty("rows").EnumerateArray().ToArray();
        Assert.NotEmpty(sortedRows);
        Assert.Equal("ApproveCandidate", sortedRows[0].GetProperty("suggestedAction").GetString());
        Assert.True(sortedRows[0].GetProperty("hasCandidate").GetBoolean());
        Assert.Contains(sortedRows, row => row.GetProperty("suggestedAction").GetString() == "NoCandidate");
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_DryRunWritesNothing_AndWriteApproves()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = true,
            reason = "manual test review"
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.True(dryRunJson.GetProperty("changed").GetBoolean());
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.CanonicalEntityId);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });

        await EnsureSuccessWithBodyAsync(writeResponse);
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Approved", approved.LinkStatus);
        Assert.Equal("Manual", approved.LinkSource);
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);
        Assert.Equal("debug/manual", approved.ApprovedBy);
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueExport_ReturnsCompactApprovalRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&suggestedAction=ApproveCandidate&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        var row = Assert.Single(json.GetProperty("safeCandidates").EnumerateArray());
        Assert.Equal("Test Forward", row.GetProperty("apiSportsPlayerName").GetString());
        Assert.Equal("Test Forward", row.GetProperty("candidateCanonicalPlayerName").GetString());
        Assert.True(row.GetProperty("suggestedApprovalJson").TryGetProperty("providerEntityLinkId", out _));
        Assert.True(row.GetProperty("suggestedApprovalJson").TryGetProperty("canonicalPlayerId", out _));
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueSafeOnly_ExcludesMissingTeamContextCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");

        var needsReviewResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&suggestedAction=NeedsReview&maxResults=10");
        await EnsureSuccessWithBodyAsync(needsReviewResponse);
        var needsReviewJson = await needsReviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var needsReviewRow = Assert.Single(needsReviewJson.GetProperty("rows").EnumerateArray());
        Assert.Contains(needsReviewRow.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "MissingTeamContext");

        var safeOnlyResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&safeOnly=true&maxResults=10");
        await EnsureSuccessWithBodyAsync(safeOnlyResponse);
        var safeOnlyJson = await safeOnlyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(safeOnlyJson.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueExport_SeparatesNeedsReviewFromSafeCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Keeper");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(json.GetProperty("safeCandidates").EnumerateArray());
        var needsReviewRow = Assert.Single(json.GetProperty("needsReviewCandidates").EnumerateArray());
        Assert.Equal("Test Keeper", needsReviewRow.GetProperty("apiSportsPlayerName").GetString());
        Assert.Contains(needsReviewRow.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "MissingTeamContext");
        Assert.True(json.TryGetProperty("noCandidate", out _));
        Assert.True(json.TryGetProperty("ambiguousConflict", out _));
    }

    [Fact]
    public async Task ApiSportsSoccerLinkPreview_ReturnsOnlyNoBlockerSafeAliasRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/link-preview?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("candidateCount").GetInt32());
        Assert.Empty(json.GetProperty("candidates").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_DryRunWritesNothing_AndIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = true,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.Equal(1, dryRunJson.GetProperty("validCount").GetInt32());
        Assert.Equal(0, dryRunJson.GetProperty("changedCount").GetInt32());
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.CanonicalEntityId);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(writeResponse);
        var writeJson = await writeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, writeJson.GetProperty("changedCount").GetInt32());
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Approved", approved.LinkStatus);
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);

        var idempotentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(idempotentResponse);
        var idempotentJson = await idempotentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, idempotentJson.GetProperty("validCount").GetInt32());
        Assert.Equal(0, idempotentJson.GetProperty("changedCount").GetInt32());
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllOrNothingBlocksWrites_WhenPartialNotAllowed()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var badCanonicalPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var keeperLink = await GetApiSportsProviderLinkStateAsync(factory, "2001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new object[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" },
                new { providerEntityLinkId = keeperLink.Id, canonicalPlayerId = badCanonicalPlayerId, reason = "bad sport" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("validCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CanonicalPlayerSportMismatch");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
        Assert.Null((await GetApiSportsProviderLinkStateAsync(factory, "1001")).CanonicalEntityId);
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "2001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllowsPartialWritesValidRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var badCanonicalPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var keeperLink = await GetApiSportsProviderLinkStateAsync(factory, "2001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = true,
            approvals = new object[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" },
                new { providerEntityLinkId = keeperLink.Id, canonicalPlayerId = badCanonicalPlayerId, reason = "bad sport" }
            }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("allowPartial").GetBoolean());
        Assert.Equal(1, json.GetProperty("validCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("changedCount").GetInt32());
        Assert.Equal("Approved", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "2001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_BlocksNeedsReviewCandidatesByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CandidateReviewBlocker:MissingTeamContext");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllowsNeedsReviewOnlyWhenExplicit()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            allowNeedsReview = true,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("allowNeedsReview").GetBoolean());
        Assert.Equal(1, json.GetProperty("changedCount").GetInt32());
        var result = Assert.Single(json.GetProperty("results").EnumerateArray());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "ApprovingNeedsReviewCandidate:MissingTeamContext");
        Assert.Equal("Approved", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_BlocksAlreadyApprovedDifferentCanonical()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var otherCanonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Other Forward", "Home FC");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var differentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId = otherCanonicalPlayerId, reason = "different player" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, differentResponse.StatusCode);
        var json = await differentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AlreadyApprovedToDifferentCanonicalPlayer");
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_WarnsWhenExplicitManualApprovalHasReviewBlockers()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = true,
            reason = "manual explicit review"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(json.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "ManualApprovalNeedsReviewCandidate:MissingTeamContext");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_ValidatesCanonicalSport_AndBlocksDifferentApproval()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var otherSoccerPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Other Forward", "Home FC");
        var mlbPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var badSportResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId = mlbPlayerId,
            dryRun = false,
            reason = "bad sport"
        });
        Assert.Equal(HttpStatusCode.Conflict, badSportResponse.StatusCode);
        var badSportJson = await badSportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(badSportJson.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CanonicalPlayerSportMismatch");

        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var differentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId = otherSoccerPlayerId,
            dryRun = false,
            reason = "different player"
        });
        Assert.Equal(HttpStatusCode.Conflict, differentResponse.StatusCode);
        var differentJson = await differentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(differentJson.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AlreadyApprovedToDifferentCanonicalPlayer");
    }

    [Fact]
    public async Task ApiSportsSoccerRejectLink_DryRunWritesNothing_AndWriteRejects()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/reject-link", new
        {
            providerEntityLinkId = link.Id,
            dryRun = true,
            reason = "wrong player"
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.RejectedAtUtc);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/reject-link", new
        {
            providerEntityLinkId = link.Id,
            dryRun = false,
            reason = "wrong player"
        });

        await EnsureSuccessWithBodyAsync(writeResponse);
        var rejected = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Rejected", rejected.LinkStatus);
        Assert.Equal("Manual", rejected.LinkSource);
        Assert.Equal("wrong player", rejected.RejectedReason);
        Assert.NotNull(rejected.RejectedAtUtc);
    }

    [Fact]
    public async Task ApiSportsSoccerCoverage_ReportsApprovedPlayerLinkCoverage()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("playerLinkCoverage").GetProperty("approvedProviderLinksUsed").GetInt32());
        Assert.True(json.GetProperty("playerLinkCoverage").GetProperty("resultRowsWithApprovedPlayerLink").GetInt32() > 0);
        Assert.True(json.GetProperty("marketJoinCoverage").GetProperty("matchedToPlatformProps").GetInt32() > 0);
    }

    [Fact]
    public async Task MlbRegressionSmoke_IsReadOnlyAndDoesNotCallProviders()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddMlbPropResultAsync(factory, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/mlb/regression-smoke");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.TryGetProperty("players", out _));
        Assert.True(json.TryGetProperty("mlbPlayerExternalRefs", out _));
        Assert.True(json.TryGetProperty("latestMlbResultImportDate", out _));
        Assert.Equal("SportCode", json.GetProperty("resolvedMlb").GetProperty("lookupMethod").GetString());
        Assert.Equal("MLB", json.GetProperty("resolvedMlb").GetProperty("sportCode").GetString());
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("recentCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("allTimeCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mlbPropResults").TryGetProperty("latestResultDate", out _));
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("recentWindow").TryGetProperty("fromDate", out _));
        Assert.DoesNotContain(json.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "No MLB Sport or League identity was found.");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task MlbCoverageAudit_ClassifiesPartialAndCompleteDates()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var partialDate = new DateOnly(2026, 6, 23);
        var completeDate = new DateOnly(2026, 6, 22);
        await SeedMlbCoverageRowsAsync(factory, partialDate, playerCount: 20, propTypeCount: 11, rowsPerPlayerProp: 1);
        await SeedMlbCoverageRowsAsync(factory, completeDate, playerCount: 110, propTypeCount: 12, rowsPerPlayerProp: 1, resetDatabase: false);

        var response = await client.GetAsync($"/api/debug/results/import/mlb/coverage-audit?fromDate={completeDate:yyyy-MM-dd}&toDate={partialDate:yyyy-MM-dd}&includeDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("datesComplete").GetInt32());
        Assert.Equal(1, json.GetProperty("datesPartial").GetInt32());
        var details = json.GetProperty("details").EnumerateArray().ToArray();
        Assert.Contains(details, row =>
            row.GetProperty("date").GetString() == partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && row.GetProperty("coverageStatus").GetString() == "Partial");
        Assert.Contains(details, row =>
            row.GetProperty("date").GetString() == completeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && row.GetProperty("coverageStatus").GetString() == "Complete");
    }

    [Fact]
    public void MlbResultImportWorkerOutcomeClassifier_TreatsCoverageGapsAsNonFatal()
    {
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.Complete,
            MlbResultImportWorker.ClassifyDateOutcome("Complete", "succeeded", recordsFound: 1200, exceptionOccurred: false));
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.Partial,
            MlbResultImportWorker.ClassifyDateOutcome("Partial", "partial", recordsFound: 500, exceptionOccurred: false));
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.PendingGames,
            MlbResultImportWorker.ClassifyDateOutcome("PendingGames", "pending", recordsFound: 0, exceptionOccurred: false));
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.NoGames,
            MlbResultImportWorker.ClassifyDateOutcome("NoGames", "skipped", recordsFound: 0, exceptionOccurred: false));
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.NoGames,
            MlbResultImportWorker.ClassifyDateOutcome("NotStarted", "pending", recordsFound: 0, exceptionOccurred: false));
        Assert.Equal(
            MlbResultImportWorkerOutcomeCategory.Failed,
            MlbResultImportWorker.ClassifyDateOutcome("Partial", "partial", recordsFound: 500, exceptionOccurred: true));
    }

    [Fact]
    public void MlbResultImportWorkerSelection_DefaultPolicySkipsOldHistoricalNonCompleteDates()
    {
        var latestCompletedDate = new DateOnly(2026, 6, 24);
        var options = new MlbResultImportWorkerOptions
        {
            RecentWindowDays = 14,
            HistoricalBackfillEnabled = false,
            HistoricalNonCompleteAttemptCap = 3
        };
        var rows = MlbResultImportWorker.BuildSelectionRows(
            new[]
            {
                new MlbResultImportDateState
                {
                    EventDate = new DateOnly(2024, 8, 1),
                    CoverageStatus = "Partial",
                    Status = "partial",
                    Attempts = 0
                },
                new MlbResultImportDateState
                {
                    EventDate = new DateOnly(2026, 6, 23),
                    CoverageStatus = "NotStarted",
                    Status = "pending",
                    Attempts = 0
                }
            },
            latestCompletedDate,
            options);

        var oldRow = Assert.Single(rows, row => row.EventDate == new DateOnly(2024, 8, 1));
        Assert.False(oldRow.IsEligible);
        Assert.Equal("HistoricalBackfillDisabled", oldRow.SkippedReason);
        Assert.StartsWith("HistoricalBackfill:", oldRow.SelectedReason, StringComparison.Ordinal);

        var recentRow = Assert.Single(rows, row => row.EventDate == new DateOnly(2026, 6, 23));
        Assert.True(recentRow.IsEligible);
        Assert.Null(recentRow.SkippedReason);
        Assert.Equal("RecentWindow:CoverageNotStarted", recentRow.SelectedReason);
    }

    [Fact]
    public void MlbResultImportWorkerSelection_HistoricalBackfillUsesAttemptCap()
    {
        var latestCompletedDate = new DateOnly(2026, 6, 24);
        var options = new MlbResultImportWorkerOptions
        {
            RecentWindowDays = 14,
            HistoricalBackfillEnabled = true,
            HistoricalNonCompleteAttemptCap = 3
        };
        var rows = MlbResultImportWorker.BuildSelectionRows(
            new[]
            {
                new MlbResultImportDateState
                {
                    EventDate = new DateOnly(2024, 8, 1),
                    CoverageStatus = "Partial",
                    Status = "partial",
                    Attempts = 3
                },
                new MlbResultImportDateState
                {
                    EventDate = new DateOnly(2024, 8, 2),
                    CoverageStatus = "Partial",
                    Status = "partial",
                    Attempts = 2
                }
            },
            latestCompletedDate,
            options);

        var cappedRow = Assert.Single(rows, row => row.EventDate == new DateOnly(2024, 8, 1));
        Assert.False(cappedRow.IsEligible);
        Assert.Equal("HistoricalAttemptCapReached", cappedRow.SkippedReason);

        var retryRow = Assert.Single(rows, row => row.EventDate == new DateOnly(2024, 8, 2));
        Assert.True(retryRow.IsEligible);
        Assert.Equal("HistoricalBackfill:CoveragePartial", retryRow.SelectedReason);
    }

    [Fact]
    public async Task MlbImportStatus_IncludesLatestWorkerCycleDateOutcomes()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        var completeDate = new DateOnly(2026, 6, 20);
        var partialDate = new DateOnly(2026, 6, 21);
        var noGamesDate = new DateOnly(2026, 6, 22);
        var failedDate = new DateOnly(2026, 6, 23);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.MlbResultImportStates.Add(new MlbResultImportState
            {
                Id = Guid.NewGuid(),
                Source = "mlb",
                LastStartedAt = startedAt,
                LastCompletedAt = completedAt,
                LastStatus = "completed_with_coverage_gaps",
                CreatedAt = startedAt,
                UpdatedAt = completedAt
            });
            dbContext.MlbResultImportDateStates.AddRange(
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = completeDate,
                    Status = "succeeded",
                    CoverageStatus = "Complete",
                    RecordsFound = 1200,
                    RecordsMapped = 1200,
                    ImportedResults = 1200,
                    DistinctPlayers = 120,
                    DistinctPropTypes = 12,
                    LastAttemptedAt = startedAt.AddSeconds(10),
                    LastSuccessfulAt = startedAt.AddSeconds(12)
                },
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = partialDate,
                    Status = "partial",
                    CoverageStatus = "Partial",
                    RecordsFound = 500,
                    RecordsMapped = 500,
                    ImportedResults = 500,
                    DistinctPlayers = 80,
                    DistinctPropTypes = 10,
                    LastAttemptedAt = startedAt.AddSeconds(20),
                    LastSuccessfulAt = startedAt.AddSeconds(22)
                },
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = noGamesDate,
                    Status = "skipped",
                    CoverageStatus = "NoGames",
                    RecordsFound = 0,
                    RecordsMapped = 0,
                    ImportedResults = 0,
                    DistinctPlayers = 0,
                    DistinctPropTypes = 0,
                    LastAttemptedAt = startedAt.AddSeconds(30),
                    LastSuccessfulAt = startedAt.AddSeconds(31)
                },
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = failedDate,
                    Status = "failed",
                    CoverageStatus = "Failed",
                    RecordsFound = 0,
                    RecordsMapped = 0,
                    ImportedResults = 0,
                    DistinctPlayers = 0,
                    DistinctPropTypes = 0,
                    LastAttemptedAt = startedAt.AddSeconds(40),
                    LastErrorMessage = "Provider rejected request."
                });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/debug/results/import/mlb/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var latestCycle = json.GetProperty("latestWorkerCycle");
        Assert.Equal(4, latestCycle.GetProperty("processedDates").GetInt32());
        Assert.Equal(1, latestCycle.GetProperty("complete").GetInt32());
        Assert.Equal(1, latestCycle.GetProperty("partial").GetInt32());
        Assert.Equal(1, latestCycle.GetProperty("noGames").GetInt32());
        Assert.Equal(1, latestCycle.GetProperty("failed").GetInt32());
        var outcomes = latestCycle.GetProperty("dateOutcomes").EnumerateArray().ToArray();
        Assert.Contains(outcomes, row => row.GetProperty("eventDate").GetString() == partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && row.GetProperty("outcomeCategory").GetString() == "Partial");
        Assert.Contains(outcomes, row => row.GetProperty("eventDate").GetString() == noGamesDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && row.GetProperty("outcomeCategory").GetString() == "NoGames");
    }

    [Fact]
    public async Task MlbImportRangeStatus_ReportsSelectionPolicyAndSkipsOldHistoricalDates()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var latestCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var recentDate = latestCompletedDate;
        var oldDate = latestCompletedDate.AddDays(-30);
        var observedAt = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.MlbResultImportDateStates.AddRange(
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = oldDate,
                    Status = "partial",
                    CoverageStatus = "Partial",
                    Attempts = 0,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                },
                new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = recentDate,
                    Status = "pending",
                    CoverageStatus = "NotStarted",
                    Attempts = 0,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/debug/results/import/mlb-range/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var policy = json.GetProperty("workerSelectionPolicy");
        Assert.False(policy.GetProperty("historicalBackfillEnabled").GetBoolean());
        Assert.Equal(14, policy.GetProperty("recentWindowDays").GetInt32());
        Assert.Equal(1, policy.GetProperty("skippedOldHistoricalBackfillDisabledCount").GetInt32());
        Assert.Equal(1, json.GetProperty("pendingBackfillDateCount").GetInt32());
        var selected = Assert.Single(json.GetProperty("pendingBackfillDates").EnumerateArray());
        Assert.Equal(recentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), selected.GetProperty("eventDate").GetString());
        Assert.Equal("RecentWindow:CoverageNotStarted", selected.GetProperty("selectedReason").GetString());
    }

    [Fact]
    public async Task MlbRepairMissing_DryRunDoesNotWriteAndRealRepairIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 23);
        await SeedMlbCoverageRowsAsync(factory, date, playerCount: 20, propTypeCount: 11, rowsPerPlayerProp: 1);
        var repairRecords = await BuildMlbImportRecordsAsync(factory, date, playerCount: 110, propTypeCount: 12);
        factory.MlbResultsProvider.SetRecords(date, repairRecords);

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/results/import/mlb/repair-missing", new
        {
            fromDate = date,
            toDate = date,
            dryRun = true,
            repairPartial = true,
            repairMissing = true
        });
        await EnsureSuccessWithBodyAsync(dryRunResponse);
        Assert.Equal(0, factory.MlbResultsProvider.CallCount);

        var realResponse = await client.PostAsJsonAsync("/api/debug/results/import/mlb/repair-missing", new
        {
            fromDate = date,
            toDate = date,
            dryRun = false,
            repairPartial = true,
            repairMissing = true
        });
        await EnsureSuccessWithBodyAsync(realResponse);
        var realJson = await realResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, realJson.GetProperty("datesSelectedForRepair").GetInt32());

        var secondResponse = await client.PostAsJsonAsync("/api/debug/results/import/mlb/repair-missing", new
        {
            fromDate = date,
            toDate = date,
            dryRun = false,
            repairPartial = true,
            repairMissing = true
        });
        await EnsureSuccessWithBodyAsync(secondResponse);
        var secondJson = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, secondJson.GetProperty("datesSelectedForRepair").GetInt32());

        var statusResponse = await client.GetAsync($"/api/debug/results/import/mlb/status?date={date:yyyy-MM-dd}");
        await EnsureSuccessWithBodyAsync(statusResponse);
        var statusJson = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Complete", statusJson.GetProperty("dateStatus").GetProperty("coverageStatus").GetString());
    }

    [Fact]
    public async Task RefreshPlanHistory_ReadOnly_DoesNotCallProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan/history?take=20");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected success status code but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private static async Task AssertStatusWithBodyAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        if (response.StatusCode != expectedStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected status code {(int)expectedStatusCode} {expectedStatusCode} but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private static void AssertMultiplier(JsonElement[] multipliers, string entryType, int legCount, decimal expectedMultiplier)
    {
        var row = Assert.Single(multipliers, multiplier =>
            multiplier.GetProperty("entryType").GetString() == entryType
            && multiplier.GetProperty("legCount").GetInt32() == legCount);

        Assert.Equal(expectedMultiplier, row.GetProperty("multiplier").GetDecimal());
        Assert.Equal("StandardOnly", row.GetProperty("appliesToProjectionType").GetString());
        Assert.Equal("Known", row.GetProperty("sourceConfidence").GetString());
        Assert.Equal(JsonValueKind.String, row.GetProperty("lastVerifiedAtUtc").ValueKind);
        Assert.NotEmpty(row.GetProperty("notes").EnumerateArray());
    }

    private static Dictionary<string, string?> ApiSportsLiveConfig(bool includeWorldCupDefaults = true)
    {
        _ = includeWorldCupDefaults;
        var config = new Dictionary<string, string?>
        {
            ["ApiSports:Enabled"] = "true",
            ["ApiSports:ApiKey"] = "TEST_API_SPORTS_KEY",
            ["ApiSports:Football:BaseUrl"] = "https://v3.football.api-sports.io"
        };

        return config;
    }

    private static async Task<int> CountApiSportsSoccerResultsAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PropResults.CountAsync(result => result.ExternalSource == "api-sports-football");
    }

    private static async Task<int> CountApiSportsSoccerProviderPlayerLinksAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.EntityType == "Player");
    }

    private static void AssertNoCaseInsensitiveDuplicatePropertiesInComparisonGroups(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("comparisonGroups", out var comparisonGroups));
        Assert.Equal(JsonValueKind.Array, comparisonGroups.ValueKind);
        var index = 0;
        foreach (var group in comparisonGroups.EnumerateArray())
        {
            AssertNoCaseInsensitiveDuplicateProperties(group, $"$.comparisonGroups[{index}]");
            index++;
        }
    }

    private static void AssertNoCaseInsensitiveDuplicatePropertiesInTopPicks(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("topPicks", out var topPicks));
        Assert.Equal(JsonValueKind.Array, topPicks.ValueKind);
        var index = 0;
        foreach (var pick in topPicks.EnumerateArray())
        {
            AssertNoCaseInsensitiveDuplicateProperties(pick, $"$.topPicks[{index}]");
            index++;
        }
    }

    private static void AssertNoCaseInsensitiveDuplicateProperties(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    Assert.True(names.Add(property.Name), $"Duplicate JSON property by case-insensitive name at {path}: {property.Name}");
                    AssertNoCaseInsensitiveDuplicateProperties(property.Value, $"{path}.{property.Name}");
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoCaseInsensitiveDuplicateProperties(item, $"{path}[{index}]");
                    index++;
                }
                break;
        }
    }

    private static async Task AddApiSportsProviderEntityLinkAsync(
        SafetyTestApplicationFactory factory,
        Guid platformPlayerId,
        string providerPlayerId,
        string providerPlayerName,
        string linkSource)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleAsync(row => row.Code == "SOCCER");
        var player = await dbContext.Players.SingleAsync(row => row.Id == platformPlayerId);
        dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            EntityType = "Player",
            CanonicalEntityId = player.Id,
            CanonicalEntityKind = "Player",
            CanonicalEntityName = player.DisplayName,
            ProviderKey = "api-sports-football",
            ProviderEntityId = providerPlayerId,
            ProviderEntityName = providerPlayerName,
            Confidence = 1m,
            LinkStatus = "Approved",
            LinkSource = linkSource,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovedBy = "test"
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<int> CountTheOddsSoccerAliasLinksAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "the-odds-soccer"
            && link.EntityType == "Player");
    }

    private static async Task<TheOddsAliasCandidateIds> GetSingleTheOddsAliasCandidateAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal("NeedsReview", sample.GetProperty("suggestedAction").GetString());
        var candidate = Assert.Single(sample.GetProperty("candidates").EnumerateArray());
        return new TheOddsAliasCandidateIds(
            candidate.GetProperty("apiSportsPlayerId").GetString()!,
            candidate.GetProperty("localPlayerId").GetGuid());
    }

    private sealed record TheOddsAliasCandidateIds(
        string ApiSportsPlayerId,
        Guid LocalPlayerId);

    private static async Task SeedTwoResolvedSoccerAliasPairsAsync(SafetyTestApplicationFactory factory)
    {
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398",
            providerEventIdOverride: "event-one",
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde",
            resultPlayerTeamName: "Uruguay",
            eventDate: new DateOnly(2026, 6, 21));
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Ryan Mendes",
            platformPlayerName: "Ryan Mendes da Graca",
            fixtureId: "1489399",
            providerEventIdOverride: "event-two",
            eventHomeTeamName: "United States",
            eventAwayTeamName: "Czechia",
            resultPlayerTeamName: "United States",
            eventDate: new DateOnly(2026, 6, 22),
            resetDatabase: false);
    }

    private static async Task AddConflictingTheOddsSoccerAliasLinkAsync(
        SafetyTestApplicationFactory factory,
        string providerEventId,
        string normalizedPlatformPlayerName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleAsync(row => row.Code == "SOCCER");
        var league = await dbContext.Leagues.FirstAsync(row => row.SportId == sport.Id);
        var wrongPlayer = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = "Wrong Soccer Player",
            Position = "F"
        };
        dbContext.Players.Add(wrongPlayer);
        dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            EntityType = "Player",
            CanonicalEntityId = wrongPlayer.Id,
            CanonicalEntityKind = "Player",
            CanonicalEntityName = wrongPlayer.DisplayName,
            ProviderKey = "the-odds-soccer",
            ProviderEntityId = $"soccer_fifa_world_cup:{providerEventId}:{normalizedPlatformPlayerName}",
            ProviderEntityName = normalizedPlatformPlayerName,
            ProviderCompetitionId = "soccer_fifa_world_cup",
            ProviderCompetitionName = "World Cup",
            ProviderEventId = providerEventId,
            Confidence = 1m,
            LinkStatus = "Approved",
            LinkSource = "TestConflict",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovedBy = "test"
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<int> CountSoccerPlatformPropsAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .CountAsync(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER");
    }

    private static async Task<JsonElement> GetSingleSoccerPlatformPropMetadataAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metadata = await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER")
            .Select(row => row.Metadata)
            .SingleAsync();
        return metadata!.RootElement.Clone();
    }

    private static async Task<JsonElement[]> GetSoccerPlatformPropMetadataAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metadataRows = await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER")
            .OrderBy(row => row.PlatformPropId)
            .Select(row => row.Metadata)
            .ToListAsync();
        return metadataRows
            .Select(metadata => metadata!.RootElement.Clone())
            .ToArray();
    }

    private static async Task AddApiSportsSoccerResultFixtureAsync(
        SafetyTestApplicationFactory factory,
        string fixtureId,
        DateOnly eventDate,
        string teamName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var team = await dbContext.Teams.SingleOrDefaultAsync(row => row.LeagueId == league.Id && row.Name == teamName);
        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = teamName
            };
            dbContext.Teams.Add(team);
        }

        var propType = await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == "GOALS");
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goals",
                StatKey = "GOALS",
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = $"{teamName} Fixture Player {fixtureId}",
            TeamId = team.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        dbContext.Players.Add(player);
        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = 1m,
            ExternalSource = "api-sports-football",
            ExternalSourceIdentifier = $"api-sports-football:{fixtureId}:{Guid.NewGuid():N}:GOALS",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddApiSportsHistoricalSoccerResultAsync(
        SafetyTestApplicationFactory factory,
        string competitionKey,
        string competitionName,
        int season,
        string fixtureId,
        DateOnly eventDate,
        string statKey,
        string propTypeName,
        string providerPlayerId = "50001",
        string playerDisplayName = "Historical Test Forward")
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var leagueCode = $"API_SPORTS_{competitionKey}_{season}";
        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == leagueCode);
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = competitionName,
                Code = leagueCode,
                SeasonType = season.ToString(CultureInfo.InvariantCulture)
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var team = await dbContext.Teams.SingleOrDefaultAsync(row => row.LeagueId == league.Id && row.Name == $"{competitionName} Test FC");
        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = $"{competitionName} Test FC"
            };
            dbContext.Teams.Add(team);
        }

        var propType = await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == statKey);
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = propTypeName,
                StatKey = statKey,
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }

        var player = await dbContext.PlayerExternalRefs
            .Include(reference => reference.Player)
            .Where(reference => reference.Provider == "api-sports-football"
                && reference.ProviderPlayerId == providerPlayerId)
            .Select(reference => reference.Player)
            .SingleOrDefaultAsync();
        if (player is null)
        {
            player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = playerDisplayName,
                TeamId = team.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            dbContext.Players.Add(player);
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Provider = "api-sports-football",
                ProviderPlayerId = providerPlayerId,
                ProviderDisplayName = player.DisplayName
            });
        }

        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = 1m,
            ExternalSource = "api-sports-football",
            ExternalSourceIdentifier = $"api-sports-football:{fixtureId}:{providerPlayerId}:{statKey}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddLegacySoccerPlatformPropAsync(
        SafetyTestApplicationFactory factory,
        string providerEventId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleAsync(row => row.Code == "SOCCER");
        var league = await dbContext.Leagues.FirstAsync(row => row.SportId == sport.Id);
        var homeTeam = await dbContext.Teams.FirstAsync(row => row.LeagueId == league.Id);
        var awayTeam = await dbContext.Teams.FirstAsync(row => row.LeagueId == league.Id && row.Id != homeTeam.Id);
        var game = await dbContext.Games.FirstAsync(row => row.LeagueId == league.Id);
        var propType = await dbContext.PropTypes.FirstAsync(row => row.SportId == sport.Id);
        var platform = await dbContext.Platforms.FirstOrDefaultAsync(row => row.Slug == "legacy-bookmaker");
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Bookmaker",
                Slug = "legacy-bookmaker"
            };
            dbContext.Platforms.Add(platform);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy Soccer Player",
            TeamId = homeTeam.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            GameId = game.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"legacy-soccer-{Guid.NewGuid():N}",
            StartsAt = game.ScheduledStartAt,
            Status = "active"
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"legacy-{Guid.NewGuid():N}",
            ExternalStableKey = providerEventId,
            Label = "Legacy Soccer Player Goals",
            CurrentLine = 0.5m,
            OverAvailable = true,
            UnderAvailable = false,
            PayoutType = "sportsbook",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow,
            Metadata = JsonSerializer.SerializeToDocument(new
            {
                dataProvider = "Internal",
                contextSchemaVersion = "legacy",
                providerCategory = "Sportsbook",
                sport = new
                {
                    competition = "World Cup",
                    providerSportKey = "soccer_fifa_world_cup"
                },
                eventInfo = new
                {
                    providerEventId,
                    eventId = providerEventId,
                    homeTeam = homeTeam.Name,
                    awayTeam = awayTeam.Name
                },
                market = new { key = "player_goals" },
                player = new
                {
                    providerPlayerId = (string?)null,
                    teamName = (string?)null
                }
            })
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> AddSoccerTheOddsRefreshRunAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "SOCCER",
            SlateDate = new DateOnly(2026, 6, 21),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbookContext",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "betmgm,fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "player_goals,player_shots",
            EstimatedApiCalls = 35,
            ActualApiCalls = 35,
            PlatformPropsCreated = 119,
            PlatformPropsUpdated = 0,
            PlatformPropsUpserted = 119,
            PredictionCount = 0,
            LineShoppingGroupCount = 34,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "SOCCER",
                competition = "World Cup",
                configuredSportKey = "soccer_fifa_world_cup",
                requestedRegions = "us",
                requestedMarkets = new[] { "player_goals", "player_shots" },
                eventsFetched = 34,
                eventOddsCalls = 1,
                platformPropsCreated = 119,
                platformPropsUpdated = 0,
                platformPropsUpserted = 119
            })
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
        return refreshRun.Id;
    }

    private static async Task<ApiSportsProviderLinkTestState> GetApiSportsProviderLinkStateAsync(
        SafetyTestApplicationFactory factory,
        string providerEntityId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var link = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .SingleAsync(row => row.ProviderKey == "api-sports-football"
                && row.EntityType == "Player"
                && row.ProviderEntityId == providerEntityId);
        return new ApiSportsProviderLinkTestState(
            link.Id,
            link.LinkStatus,
            link.LinkSource,
            link.CanonicalEntityId,
            link.ApprovedBy,
            link.RejectedAtUtc,
            link.RejectedReason);
    }

    private static async Task<Guid> AddMlbCanonicalPlayerAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "MLB");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "MLB" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "MLB");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Major League Baseball",
                Code = "MLB"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Wrong Sport Player",
            LeagueId = league.Id
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    private static async Task<Guid> AddSoccerCanonicalPlayerWithoutPlatformAsync(
        SafetyTestApplicationFactory factory,
        string playerName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = playerName,
            LeagueId = league.Id
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    private static async Task AddMlbPropResultAsync(SafetyTestApplicationFactory factory, DateOnly eventDate)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "MLB");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Major League Baseball" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "MLB");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Major League Baseball",
                Code = "MLB"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Smoke Test Batter",
            LeagueId = league.Id
        };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Hits",
            StatKey = "HITS",
            ValueType = "numeric"
        };
        dbContext.Players.Add(player);
        dbContext.PropTypes.Add(propType);
        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = 1,
            ExternalSource = "mlb",
            ExternalSourceIdentifier = $"mlb:{eventDate:yyyyMMdd}:{player.Id:N}:{propType.Id:N}"
        });
        dbContext.MlbResultImportDateStates.Add(new MlbResultImportDateState
        {
            Id = Guid.NewGuid(),
            EventDate = eventDate,
            Status = "succeeded",
            LastSuccessfulAt = DateTimeOffset.UtcNow,
            RecordsFound = 1,
            RecordsMapped = 1,
            RecordsInserted = 1
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedMlbCoverageRowsAsync(
        SafetyTestApplicationFactory factory,
        DateOnly eventDate,
        int playerCount,
        int propTypeCount,
        int rowsPerPlayerProp,
        bool resetDatabase = true)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (resetDatabase)
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var (sport, league) = await GetOrCreateMlbSportLeagueAsync(dbContext);
        var propTypes = await GetOrCreateMlbPropTypesAsync(dbContext, sport.Id, propTypeCount);
        var players = Enumerable.Range(1, playerCount)
            .Select(index => new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = $"MLB Coverage Player {eventDate:yyyyMMdd}-{index}",
                LeagueId = league.Id
            })
            .ToArray();
        dbContext.Players.AddRange(players);

        var rows = 0;
        foreach (var player in players)
        {
            foreach (var propType in propTypes)
            {
                for (var copy = 0; copy < rowsPerPlayerProp; copy++)
                {
                    rows++;
                    dbContext.PropResults.Add(new PropResult
                    {
                        Id = Guid.NewGuid(),
                        PlayerId = player.Id,
                        LeagueId = league.Id,
                        PropTypeId = propType.Id,
                        EventDate = eventDate,
                        ActualResultValue = rows % 7,
                        ExternalSource = "mlb",
                        ExternalSourceIdentifier = $"mlb:test:{eventDate:yyyyMMdd}:{player.Id:N}:{propType.Id:N}:{copy}"
                    });
                }
            }
        }

        dbContext.MlbResultImportDateStates.Add(new MlbResultImportDateState
        {
            Id = Guid.NewGuid(),
            EventDate = eventDate,
            Status = "succeeded",
            LastSuccessfulAt = DateTimeOffset.UtcNow,
            RecordsFound = rows,
            RecordsMapped = rows,
            ImportedResults = rows,
            DistinctPlayers = playerCount,
            DistinctPropTypes = propTypeCount,
            CoverageStatus = rows >= 1000 && playerCount >= 100 && propTypeCount >= 12 ? "Complete" : "Partial",
            CoverageReason = "Seeded test coverage state."
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task<IReadOnlyCollection<MlbResultRecord>> BuildMlbImportRecordsAsync(
        SafetyTestApplicationFactory factory,
        DateOnly eventDate,
        int playerCount,
        int propTypeCount)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (sport, league) = await GetOrCreateMlbSportLeagueAsync(dbContext);
        var propTypes = await GetOrCreateMlbPropTypesAsync(dbContext, sport.Id, propTypeCount);
        var existingPlayers = await dbContext.Players
            .Where(row => row.LeagueId == league.Id)
            .Take(playerCount)
            .ToArrayAsync();
        var players = existingPlayers.ToList();
        while (players.Count < playerCount)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = $"MLB Repair Player {players.Count + 1}",
                LeagueId = league.Id
            };
            players.Add(player);
            dbContext.Players.Add(player);
        }

        await dbContext.SaveChangesAsync();

        return players
            .Take(playerCount)
            .SelectMany(player => propTypes.Select(propType => new MlbResultRecord(
                new ResultRecord(
                    player.Id,
                    league.Id,
                    propType.Id,
                    eventDate,
                    1,
                    $"mlb:test-provider:{eventDate:yyyyMMdd}:{player.Id:N}:{propType.Id:N}"),
                player.DisplayName,
                player.Id.ToString("N"),
                $"game-{eventDate:yyyyMMdd}",
                propType.Name,
                1,
                IsMapped: true)))
            .ToArray();
    }

    private static async Task<(Sport Sport, League League)> GetOrCreateMlbSportLeagueAsync(ApplicationDbContext dbContext)
    {
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "MLB");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Major League Baseball" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "MLB");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Major League Baseball",
                Code = "MLB"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        return (sport, league);
    }

    private static async Task<PropType[]> GetOrCreateMlbPropTypesAsync(ApplicationDbContext dbContext, Guid sportId, int count)
    {
        var existing = await dbContext.PropTypes
            .Where(row => row.SportId == sportId)
            .OrderBy(row => row.Name)
            .ToListAsync();
        while (existing.Count < count)
        {
            var index = existing.Count + 1;
            var propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sportId,
                Name = $"Coverage Prop {index}",
                StatKey = $"COVERAGE_PROP_{index}",
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
            existing.Add(propType);
        }

        await dbContext.SaveChangesAsync();
        return existing.Take(count).ToArray();
    }

    private static async Task SeedSideSelectionAuditRowsAsync(
        SafetyTestApplicationFactory factory,
        DateOnly slateDate,
        IReadOnlyCollection<MarketLineShoppingSnapshot> lineShoppingRows,
        IReadOnlyCollection<RecommendationSnapshot>? recommendationRows = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = slateDate,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Succeeded = true,
            ForceLive = false,
            DryRun = false,
            BookmakerPreset = "test",
            ProviderCategoriesIncluded = "DFS",
            MarketsRequested = "test"
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);

        foreach (var predictionRunId in lineShoppingRows.Select(row => row.PredictionRunId)
            .Concat((recommendationRows ?? []).Select(row => row.PredictionRunId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct())
        {
            dbContext.PredictionRuns.Add(new PredictionRun
            {
                Id = predictionRunId,
                ModelName = "TestModel",
                ModelVersion = "test",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
        }

        foreach (var row in lineShoppingRows)
        {
            row.TheOddsRefreshRunId = refreshRun.Id;
            dbContext.MarketLineShoppingSnapshots.Add(row);
        }

        if (recommendationRows is not null)
        {
            foreach (var row in recommendationRows)
            {
                row.RefreshRunId = refreshRun.Id;
                dbContext.RecommendationSnapshots.Add(row);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPredictionStageAuditScenarioAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var (sport, league) = await GetOrCreateMlbSportLeagueAsync(dbContext);
        var run = new PredictionRun
        {
            Id = Guid.NewGuid(),
            ModelName = "BaselineAverageModel",
            ModelVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        dbContext.PredictionRuns.Add(run);

        await AddPredictionStageAuditPredictionAsync(
            dbContext,
            run,
            league,
            sport.Id,
            "Projection Above Under",
            "Side Audit Total Bases",
            new DateOnly(2026, 6, 24),
            line: 1.5m,
            predictedValue: 2.5m,
            recommendation: "UNDER",
            historicalValues: [2m, 2m, 3m, 4m]);
        await AddPredictionStageAuditPredictionAsync(
            dbContext,
            run,
            league,
            sport.Id,
            "Positive Over Pass",
            "Side Audit Hits",
            new DateOnly(2026, 6, 24),
            line: 0.5m,
            predictedValue: 0.6m,
            recommendation: "PASS",
            historicalValues: [1m, 1m, 2m, 1m]);
        await AddPredictionStageAuditPredictionAsync(
            dbContext,
            run,
            league,
            sport.Id,
            "Normal Under",
            "Side Audit Strikeouts",
            new DateOnly(2026, 6, 24),
            line: 6.5m,
            predictedValue: 4.5m,
            recommendation: "UNDER",
            historicalValues: [2m, 3m, 4m, 5m]);

        await dbContext.SaveChangesAsync();
    }

    private static async Task AddPredictionStageAuditPredictionAsync(
        ApplicationDbContext dbContext,
        PredictionRun run,
        League league,
        Guid sportId,
        string playerName,
        string propTypeName,
        DateOnly eventDate,
        decimal line,
        decimal predictedValue,
        string recommendation,
        decimal[] historicalValues)
    {
        var propType = await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sportId && row.Name == propTypeName);
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sportId,
                Name = propTypeName,
                StatKey = propTypeName.Replace(" ", string.Empty).ToUpperInvariant(),
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = playerName,
            LeagueId = league.Id
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            PropTypeId = propType.Id,
            LeagueId = league.Id,
            NormalizedKey = $"side-audit-{Guid.NewGuid():N}",
            StartsAt = new DateTimeOffset(eventDate.ToDateTime(new TimeOnly(19, 0)), TimeSpan.Zero),
            Status = "active"
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.Predictions.Add(new Prediction
        {
            Id = Guid.NewGuid(),
            PredictionRunId = run.Id,
            PropId = prop.Id,
            PredictedValue = predictedValue,
            SportsbookLine = line,
            Edge = Math.Round(predictedValue - line, 4),
            Recommendation = recommendation,
            Confidence = 0.75m,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        });

        for (var index = 0; index < historicalValues.Length; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = eventDate.AddDays(-(index + 1)),
                ActualResultValue = historicalValues[index],
                ExternalSource = "mlb",
                ExternalSourceIdentifier = $"side-audit:{player.Id:N}:{index}",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1)),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1))
            });
        }
    }

    private static TheOddsRefreshRun CreateTheOddsRun(
        Guid id,
        string sport,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string bookmakerPreset,
        string providerCategoriesIncluded,
        object? diagnostics)
    {
        return new TheOddsRefreshRun
        {
            Id = id,
            Provider = "The Odds API",
            Sport = sport,
            SlateDate = new DateOnly(2026, 6, 28),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = bookmakerPreset,
            ProviderCategoriesIncluded = providerCategoriesIncluded,
            EstimatedApiCalls = 0,
            ActualApiCalls = 0,
            DiagnosticsJson = diagnostics is null
                ? null
                : JsonSerializer.SerializeToDocument(diagnostics)
        };
    }

    private static MarketLineShoppingSnapshot CreateLineShoppingSnapshot(
        string bestSide,
        string player,
        string propType,
        decimal line,
        decimal modelProbabilityOver,
        decimal modelProbabilityUnder,
        decimal? bestProbability,
        decimal? bestImpliedProbability,
        decimal? bestProbabilityEdge,
        Guid? predictionRunId = null)
    {
        return new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = Guid.Empty,
            PredictionRunId = predictionRunId,
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 24),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "test",
            ProviderCategory = "DFS",
            Provider = "Underdog Fantasy",
            Bookmaker = "Underdog Fantasy",
            Player = player,
            EventId = "event-1",
            EventName = "Away @ Home",
            EventStartTime = new DateTimeOffset(2026, 6, 24, 23, 0, 0, TimeSpan.Zero),
            LifecycleStatus = "Active",
            PropType = propType,
            Line = line,
            BestSide = bestSide,
            BestBookmaker = "Underdog Fantasy",
            BestAmericanPrice = -110,
            BestDecimalPrice = 1.91m,
            BestProbability = bestProbability,
            BestImpliedProbability = bestImpliedProbability,
            BestProbabilityEdge = bestProbabilityEdge,
            BestEstimatedEv = 0.10m,
            StrategyClassification = "Candidate",
            StrategyTier = "Medium",
            IsCandidate = true,
            EntryProvider = "Underdog Fantasy",
            EntryCompatibilityGroup = "Underdog Fantasy",
            EntryProviderCategory = "DFS",
            EntryActionableForUser = true,
            DetailsJson = JsonDocument.Parse($$"""
            {
              "modelProbabilityOver": {{modelProbabilityOver}},
              "modelProbabilityUnder": {{modelProbabilityUnder}},
              "rankingScore": 0.75,
              "bestOverEstimatedEv": 0.05,
              "bestUnderEstimatedEv": 0.10,
              "bestImpliedProbability": {{(bestImpliedProbability ?? 0m)}},
              "bestProbabilityEdge": {{(bestProbabilityEdge ?? 0m)}},
              "bestEstimatedEv": 0.10
            }
            """)
        };
    }

    private static TheOddsScheduledRefreshStateSnapshot CreateScheduledRefreshStateSnapshot(
        DateTimeOffset? lastRunStartedAtUtc = null,
        DateTimeOffset? lastRunCompletedAtUtc = null,
        DateTimeOffset? lastSuccessfulRefreshCompletedAtUtc = null,
        bool? lastRunSucceeded = null,
        string status = "waiting")
    {
        return new TheOddsScheduledRefreshStateSnapshot(
            LastRunStartedAtUtc: lastRunStartedAtUtc,
            LastRunScheduledDueUtc: null,
            LastRunCompletedAtUtc: lastRunCompletedAtUtc,
            LastSuccessfulRefreshCompletedAtUtc: lastSuccessfulRefreshCompletedAtUtc,
            LastRunSucceeded: lastRunSucceeded,
            LastRunSkipped: false,
            LastRunBlocked: false,
            LastRunError: null,
            Status: status,
            WorkerRunning: true,
            LastWakeAtUtc: null,
            NextWakeEstimateUtc: null,
            LastPlanGeneratedAtUtc: null,
            LastPlanShouldRefresh: null,
            LastPlanSkipReason: null,
            LastPlanEstimatedApiCalls: null,
            LastPlanBudgetAllowed: null,
            LastPlanProviderAvailable: null,
            LastSkippedAtUtc: null,
            LastSkipReason: null);
    }

    private static TheOddsRefreshPlanResult CreateScheduledRefreshPlan(
        DateTimeOffset generatedAtUtc,
        DateTimeOffset nextEligibleRefreshUtc,
        bool shouldRefresh,
        bool targetShouldRefresh,
        string? skipReason)
    {
        var budget = new TheOddsRefreshBudgetSummary(
            PlannerEnforcementEnabled: false,
            ConfiguredDailyCreditBudget: 0,
            DailyBudgetEnabled: false,
            ConfiguredMonthlyCreditBudget: 0,
            LocalBudgetEnforcementEnabled: false,
            LocalDailyBudgetEnforcementEnabled: false,
            LocalMonthlyBudgetEnforcementEnabled: false,
            PerRunEstimateBlockingEnabled: false,
            ProviderCreditBlockingEnabled: false,
            ConfiguredReservedCreditBuffer: 0,
            ConfiguredMaxEstimatedCallsPerRun: null,
            DailyUsedCredits: 0,
            MonthlyUsedCredits: 0,
            PersistedMonthlyUsedCredits: 0,
            ProviderReportedRequestsUsed: null,
            ProviderReportedRequestsRemaining: null,
            ProviderCreditTelemetryObservedAtUtc: null,
            ProviderCreditTelemetryAgeMinutes: null,
            IsProviderTelemetryStale: false,
            ProviderTelemetryUsedForBlocking: false,
            EstimatedApiCalls: 1,
            DailyAfterEstimate: 1,
            MonthlyAfterEstimate: 1,
            DailyPercentAfterEstimate: 0m,
            MonthlyPercentAfterEstimate: 0m,
            DailyWarning: false,
            MonthlyWarning: false,
            DailyHardStop: false,
            MonthlyHardStop: false,
            ProviderRemainingHardStop: false,
            PerRunEstimateHardStop: false,
            IsBlockedByLocalBudget: false,
            IsBlockedByProviderCredits: false,
            IsBlockedByPerRunEstimate: false,
            PlannerWouldBlockIfEnforced: false,
            PlannerWarnings: [],
            BudgetBlockReason: null,
            BudgetExplanation: "Allowed in test.",
            BudgetAllowed: true);

        return new TheOddsRefreshPlanResult(
            GeneratedAtUtc: generatedAtUtc,
            ProviderStatus: "Available",
            ProviderRejectedKeyDetected: false,
            ProviderAvailable: true,
            RequestsUsed: null,
            RequestsRemaining: null,
            SlateDate: DateOnly.FromDateTime(generatedAtUtc.UtcDateTime),
            BookmakerPreset: "test",
            EstimatedSlateEvents: 1,
            EstimatedApiCalls: 1,
            EstimateSource: "Test",
            CadenceRule: new PlannerCadenceRule("Test", null, null, 15, "Test cadence."),
            MinutesUntilNextEventStart: null,
            LastSuccessfulRefreshAtUtc: nextEligibleRefreshUtc.AddMinutes(-15),
            MinutesSinceLastSuccessfulRefresh: null,
            LastAttemptAtUtc: null,
            LastFailedAttemptAtUtc: null,
            MinutesSinceLastAttempt: null,
            NextEligibleRefreshUtc: nextEligibleRefreshUtc,
            Budget: budget,
            HasUsableMarketData: true,
            ShouldRefresh: shouldRefresh,
            SkipReason: skipReason,
            RecommendedAction: shouldRefresh ? "Refresh is due." : "Cadence not due.")
        {
            Targets =
            [
                new TheOddsRefreshTargetPlan(
                    Key: "mlb-dfs",
                    Enabled: true,
                    Sport: "MLB",
                    Competition: "MLB",
                    ProviderCategory: "DFS",
                    BookmakerPreset: "test",
                    ProviderSportKey: "baseball_mlb",
                    MaxEvents: null,
                    Regions: null,
                    Bookmakers: null,
                    EffectiveBookmakers: [],
                    RequestedBookmakers: [],
                    SkippedBookmakers: [],
                    Markets: null,
                    SupportsResults: true,
                    SupportsHistory: true,
                    ExecutionWired: true,
                    ProviderSportKeyVerified: true,
                    EstimatedApiCalls: 1,
                    LatestRefreshRunId: null,
                    LatestRunStartedAtUtc: null,
                    LatestRunCompletedAtUtc: null,
                    LatestRunSucceeded: null,
                    LatestRunFailed: null,
                    LatestRunBlocked: null,
                    LatestSuccessfulRefreshRunId: null,
                    LatestSuccessfulRefreshCompletedAtUtc: nextEligibleRefreshUtc.AddMinutes(-15),
                    LatestAttemptAtUtc: null,
                    ActualApiCallsFromLatestRun: null,
                    PlatformPropsCreatedFromLatestRun: null,
                    PlatformPropsUpdatedFromLatestRun: null,
                    PlatformPropsUpsertedFromLatestRun: null,
                    NextEligibleRefreshUtc: nextEligibleRefreshUtc,
                    ProviderStatus: "Available",
                    ProviderAvailable: true,
                    BudgetAllowed: true,
                    ShouldRefresh: targetShouldRefresh,
                    SkipReason: targetShouldRefresh ? null : skipReason,
                    Notes: null)
            ]
        };
    }

    private static RecommendationSnapshot CreateRecommendationSnapshot(
        string side,
        string player,
        string propType,
        decimal line,
        Guid predictionRunId,
        DateOnly slateDate)
    {
        return new RecommendationSnapshot
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            FirstSeenAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            SeenCount = 1,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PredictionRunId = predictionRunId,
            Sport = "MLB",
            SlateDate = slateDate,
            ExpectedResultDate = slateDate,
            EventId = "event-1",
            EventStartTimeUtc = new DateTimeOffset(2026, 6, 24, 23, 0, 0, TimeSpan.Zero),
            PlayerName = player,
            NormalizedPlayerName = player.Replace(" ", string.Empty).ToUpperInvariant(),
            PropType = propType,
            NormalizedPropType = propType.Replace(" ", string.Empty).ToUpperInvariant(),
            Side = side,
            Line = line,
            Provider = "Underdog Fantasy",
            Bookmaker = "Underdog Fantasy",
            ProviderCategory = "DFS",
            EntryProvider = "Underdog Fantasy",
            EntryCompatibilityGroup = "Underdog Fantasy",
            HitChance = 0.30m,
            ImpliedProbability = 0.55m,
            ProbabilityEdge = -0.25m,
            EstimatedEv = -0.10m,
            TrustLevel = "Medium",
            StrategyClassification = "Candidate",
            StrategyTier = "Medium",
            ModelVersion = LikelyHitModelService.ModelVersion,
            RecommendationContractVersion = LikelyHitModelService.RecommendationContractVersion,
            ProbabilityModelVersion = LikelyHitModelService.ProbabilityModelVersion,
            ModelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra,
            RankingScore = 0.75m,
            IsCandidate = true,
            IsTopPick = true,
            IsActionableForUser = true,
            LifecycleStatus = "Active"
        };
    }

    private sealed record ApiSportsProviderLinkTestState(
        Guid Id,
        string LinkStatus,
        string LinkSource,
        Guid? CanonicalEntityId,
        string? ApprovedBy,
        DateTimeOffset? RejectedAtUtc,
        string? RejectedReason);
}

public sealed class SafetyTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection databaseConnection = new("Data Source=:memory:");
    private readonly IReadOnlyDictionary<string, string?> configOverrides;

    public CountingTheOddsAdapter TheOddsAdapter { get; } = new();
    public CountingUnderdogAdapter UnderdogAdapter { get; } = new();
    public StubApiSportsFootballClient ApiSportsFootballClient { get; } = new();
    public StubMlbResultsProvider MlbResultsProvider { get; } = new();

    public SafetyTestApplicationFactory(IReadOnlyDictionary<string, string?>? configOverrides = null)
    {
        this.configOverrides = configOverrides ?? new Dictionary<string, string?>();
        databaseConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:SportsBettingDb"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["DebugAdmin:Enabled"] = "true",
                ["DebugAdmin:RequireApiKey"] = "false",
                ["TheOddsApi:ApiKey"] = "TEST_THE_ODDS_API_KEY",
                ["TheOddsRefreshPlanner:MaxDailyCredits"] = "500",
                ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "500",
                ["TheOddsRefreshPlanner:ProviderRemainingHardStopCredits"] = "5",
                ["TheOddsScheduledRefresh:Enabled"] = "false",
                ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "false",
                ["UnderdogIngestion:Enabled"] = "false",
                ["MlbResultImportWorker:Enabled"] = "false",
                ["ApiSports:Enabled"] = "false",
                ["ApiSports:ApiKey"] = "API_SPORTS_KEY",
                ["ApiSports:Football:BaseUrl"] = "https://v3.football.api-sports.io"
            };

            foreach (var (key, value) in configOverrides)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            RemoveProviderSpecificServices(services, "Npgsql");
            services.RemoveAll<ITheOddsApiMlbPlayerPropsAdapter>();
            services.RemoveAll<IUnderdogAdapter>();
            services.RemoveAll<IApiSportsFootballClient>();
            services.RemoveAll<IMlbResultsProvider>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(databaseConnection));
            services.AddSingleton<ITheOddsApiMlbPlayerPropsAdapter>(TheOddsAdapter);
            services.AddSingleton<IUnderdogAdapter>(UnderdogAdapter);
            services.AddSingleton<IApiSportsFootballClient>(ApiSportsFootballClient);
            services.AddSingleton<IMlbResultsProvider>(MlbResultsProvider);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            databaseConnection.Dispose();
        }
    }

    private static void RemoveProviderSpecificServices(IServiceCollection services, string providerName)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (ContainsProviderName(descriptor.ServiceType)
                || ContainsProviderName(descriptor.ImplementationType)
                || ContainsProviderName(descriptor.ImplementationInstance?.GetType()))
            {
                services.RemoveAt(index);
            }
        }

        bool ContainsProviderName(Type? type)
        {
            return type?.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) == true
                || type?.Assembly.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public async Task SeedRejectedKeyRefreshAsync(bool includeStaleSnapshot = false)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 16,
            ActualApiCalls = 16,
            PredictionCount = 87,
            LineShoppingGroupCount = 87,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 15).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                creditUsage = new
                {
                    requestsUsed = 498,
                    requestsRemaining = 2,
                    requestsLast = 0
                },
                ingestion = Enumerable.Range(1, 15).Select(index => new
                {
                    eventId = $"event-{index}",
                    success = false,
                    errors = new[] { "The Odds API rejected the configured API key." }
                })
            })
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);

        if (includeStaleSnapshot)
        {
            dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRun.Id,
                PredictionRunId = null,
                Sport = "MLB",
                SlateDate = refreshRun.SlateDate,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                BookmakerPreset = "dfsOnly",
                ProviderCategory = "DFS",
                Provider = "The Odds API",
                Bookmaker = "PrizePicks",
                Player = "Test Player",
                EventId = "event-1",
                EventName = "Away @ Home",
                EventStartTime = DateTimeOffset.UtcNow.AddHours(-1),
                LifecycleStatus = "GameStarted",
                IsStale = true,
                IsExpired = true,
                PropType = "Total Bases",
                Line = 1.5m,
                BestSide = "Under",
                BestBookmaker = "PrizePicks",
                BestAmericanPrice = -120,
                BestDecimalPrice = 1.833333m,
                BestProbability = 0.6m,
                BestImpliedProbability = 0.545455m,
                BestProbabilityEdge = 0.054545m,
                BestEstimatedEv = 0.05m,
                StrategyClassification = "Candidate",
                StrategyTier = "ConservativeCandidate",
                IsCandidate = true,
                IsWatchlist = false,
                IsAvoid = false,
                IsResearchOnly = false,
                EntryProvider = "PrizePicks",
                EntryCompatibilityGroup = "PrizePicks",
                EntryProviderCategory = "DFS",
                EntryActionableForUser = true,
                DetailsJson = JsonDocument.Parse("""{"historicalSampleSize":100}""")
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task SeedExpectedValueDisplaySnapshotsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "mixed",
            ProviderCategoriesIncluded = "DFS,Sportsbook",
            SportsbookBookmakersRequested = "draftkings",
            DfsBookmakersRequested = "prizepicks",
            MarketsRequested = "pitcher_strikeouts",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = 3,
            LineShoppingGroupCount = 3,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.MarketLineShoppingSnapshots.AddRange(
            BuildExpectedValueDisplaySnapshot(
                refreshRun,
                "DFS Player",
                "DFS",
                "PrizePicks",
                bestEstimatedEv: 0.14m,
                bestImpliedProbability: 0.5m,
                bestDecimalPrice: 2.0m,
                bestAmericanPrice: 100,
                entryActionable: true),
            BuildExpectedValueDisplaySnapshot(
                refreshRun,
                "Priced Sportsbook Player",
                "Sportsbook",
                "DraftKings",
                bestEstimatedEv: 0.09m,
                bestImpliedProbability: 0.52381m,
                bestDecimalPrice: 1.91m,
                bestAmericanPrice: -110,
                entryActionable: false),
            BuildExpectedValueDisplaySnapshot(
                refreshRun,
                "Unpriced Sportsbook Player",
                "Sportsbook",
                "DraftKings",
                bestEstimatedEv: 0.07m,
                bestImpliedProbability: null,
                bestDecimalPrice: null,
                bestAmericanPrice: null,
                entryActionable: false));
        dbContext.MarketTopPickSnapshots.AddRange(
            BuildExpectedValueDisplayTopPickSnapshot(
                refreshRun,
                "DFS Player",
                "DFS",
                "PrizePicks",
                bestEstimatedEv: 0.14m,
                bestImpliedProbability: 0.5m,
                bestDecimalPrice: 2.0m,
                bestAmericanPrice: 100,
                entryActionable: true),
            BuildExpectedValueDisplayTopPickSnapshot(
                refreshRun,
                "Priced Sportsbook Player",
                "Sportsbook",
                "DraftKings",
                bestEstimatedEv: 0.09m,
                bestImpliedProbability: 0.52381m,
                bestDecimalPrice: 1.91m,
                bestAmericanPrice: -110,
                entryActionable: false),
            BuildExpectedValueDisplayTopPickSnapshot(
                refreshRun,
                "Unsafe Old Sportsbook Snapshot Player",
                "Sportsbook",
                "DraftKings",
                bestEstimatedEv: 0.72409m,
                bestImpliedProbability: 0.377358m,
                bestDecimalPrice: 2.65m,
                bestAmericanPrice: 165,
                entryActionable: false,
                bestProbability: 0.6506m,
                historicalSampleSize: 150,
                confidenceScore: 0.80m));

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedNewerDfsOnlyExpectedValueSnapshotAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks",
            MarketsRequested = "pitcher_strikeouts",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = 1,
            LineShoppingGroupCount = 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.MarketLineShoppingSnapshots.Add(BuildExpectedValueDisplaySnapshot(
            refreshRun,
            "Newer DFS Player",
            "DFS",
            "PrizePicks",
            bestEstimatedEv: 0.11m,
            bestImpliedProbability: 0.5m,
            bestDecimalPrice: 2.0m,
            bestAmericanPrice: 100,
            entryActionable: true));

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedMlbDfsHitProbabilitySelectionScenarioAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Baseball" };
        var league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Code = "MLB", Name = "Major League Baseball" };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Pitcher Strikeouts",
            StatKey = "PITCHER_STRIKEOUTS",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "PrizePicks",
            Slug = "prizepicks",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var run = new PredictionRun
        {
            Id = Guid.NewGuid(),
            ModelName = "BaselineAverageModel",
            ModelVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-9)
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.PropTypes.Add(propType);
        dbContext.Platforms.Add(platform);
        dbContext.PredictionRuns.Add(run);

        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "High Probability Over",
            line: 6.5m,
            projection: 8.0m,
            overResults: 48,
            pushResults: 0,
            underResults: 12);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Low Probability Longshot",
            line: 6.5m,
            projection: 8.0m,
            overResults: 16,
            pushResults: 0,
            underResults: 44);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Research Lean Over",
            line: 6.5m,
            projection: 8.0m,
            overResults: 32,
            pushResults: 0,
            underResults: 28);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Priced Sportsbook Player",
            line: 6.5m,
            projection: 4.0m,
            overResults: 18,
            pushResults: 0,
            underResults: 42,
            providerCategory: "Sportsbook",
            bookmakerKey: "draftkings",
            bookmakerTitle: "DraftKings",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -110,
            underAmericanPrice: -180,
            overDecimalPrice: 1.91m,
            underDecimalPrice: 1.555556m,
            historicalValues: Enumerable.Repeat(5.5m, 42).Concat(Enumerable.Repeat(7.5m, 18)).ToArray());
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Borderline Sportsbook Inventory Player",
            line: 6.5m,
            projection: 4.0m,
            overResults: 21,
            pushResults: 0,
            underResults: 39,
            providerCategory: "Sportsbook",
            bookmakerKey: "fanduel",
            bookmakerTitle: "FanDuel",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -110,
            underAmericanPrice: -110,
            overDecimalPrice: 1.91m,
            underDecimalPrice: 1.91m);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Low Sample Huge EV Sportsbook Player",
            line: 6.5m,
            projection: 4.0m,
            overResults: 1,
            pushResults: 0,
            underResults: 19,
            providerCategory: "Sportsbook",
            bookmakerKey: "betmgm",
            bookmakerTitle: "BetMGM",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -110,
            underAmericanPrice: 100,
            overDecimalPrice: 1.91m,
            underDecimalPrice: 2.0m);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Negative EV Sportsbook Player",
            line: 6.5m,
            projection: 4.0m,
            overResults: 25,
            pushResults: 0,
            underResults: 30,
            providerCategory: "Sportsbook",
            bookmakerKey: "betonlineag",
            bookmakerTitle: "BetOnline.ag",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -400,
            underAmericanPrice: -220,
            overDecimalPrice: 1.25m,
            underDecimalPrice: 1.454545m);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Projection Conflict Under",
            line: 7.5m,
            projection: 9.0m,
            overResults: 3,
            pushResults: 0,
            underResults: 27,
            providerCategory: "Sportsbook",
            bookmakerKey: "thescorebet",
            bookmakerTitle: "theScore Bet",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -110,
            underAmericanPrice: 150,
            overDecimalPrice: 1.91m,
            underDecimalPrice: 2.5m);
        await AddMlbDfsSelectionScenarioPlayerAsync(
            dbContext,
            league,
            propType,
            platform,
            run,
            "Valid Skew Conflict",
            line: 1.5m,
            projection: 2.0m,
            overResults: 0,
            pushResults: 0,
            underResults: 0,
            providerCategory: "Sportsbook",
            bookmakerKey: "fanduel",
            bookmakerTitle: "FanDuel",
            region: "us",
            entryActionable: false,
            overAmericanPrice: -110,
            underAmericanPrice: -110,
            overDecimalPrice: 1.91m,
            underDecimalPrice: 1.91m,
            historicalValues: Enumerable.Repeat(1m, 21).Concat(Enumerable.Repeat(5m, 9)).ToArray());

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedMlbLikelyHitSportsbookTopPickSnapshotsForPublicReadAsync(bool includeSuppressedEvPick)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slateDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = slateDate,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "draftkings,betmgm",
            MarketsRequested = "pitcher_strikeouts",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = includeSuppressedEvPick ? 2 : 1,
            LineShoppingGroupCount = includeSuppressedEvPick ? 2 : 1,
            ActiveCandidateCount = includeSuppressedEvPick ? 2 : 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { source = "test" })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.MarketTopPickSnapshots.Add(BuildLikelyHitSportsbookTopPickSnapshot(
            refreshRun,
            player: "Priced Sportsbook Player",
            bookmaker: "DraftKings",
            americanPrice: -180,
            decimalPrice: 1.555556m,
            impliedProbability: 0.642857m,
            rawSelectedSideProbability: 0.80m,
            historicalSampleSize: 60,
            confidenceScore: 0.80m,
            sampleWarning: "None"));

        if (includeSuppressedEvPick)
        {
            dbContext.MarketTopPickSnapshots.Add(BuildLikelyHitSportsbookTopPickSnapshot(
                refreshRun,
                player: "Low Sample Huge EV Sportsbook Player",
                bookmaker: "BetMGM",
                americanPrice: -150,
                decimalPrice: 1.666667m,
                impliedProbability: 0.60m,
                rawSelectedSideProbability: 0.95m,
                historicalSampleSize: 20,
                confidenceScore: 0.80m,
                sampleWarning: "LowSample"));
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedMlbLikelyHitHistoricalReplaySnapshotsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Baseball" };
        var league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Code = "MLB", Name = "Major League Baseball" };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Pitcher Strikeouts",
            StatKey = "PITCHER_STRIKEOUTS",
            ValueType = "numeric"
        };
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = eventDate,
            StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "draftkings",
            MarketsRequested = "pitcher_strikeouts",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = 3,
            LineShoppingGroupCount = 3,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.PropTypes.Add(propType);
        dbContext.TheOddsRefreshRuns.Add(refreshRun);

        AddHistoricalLikelyHitReplayPlayer(dbContext, league, propType, refreshRun, eventDate, "Replay Win One", actualResult: 4m);
        AddHistoricalLikelyHitReplayPlayer(dbContext, league, propType, refreshRun, eventDate, "Replay Win Two", actualResult: 5m);
        AddHistoricalLikelyHitReplayPlayer(dbContext, league, propType, refreshRun, eventDate, "Replay Loss One", actualResult: 8m);

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedMlbPredictionGenerationSideSelectionScenarioAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Baseball" };
        var league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Code = "MLB", Name = "Major League Baseball" };
        var pitcherStrikeouts = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Pitcher Strikeouts",
            StatKey = "PITCHER_STRIKEOUTS",
            ValueType = "numeric"
        };
        var totalBases = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Total Bases",
            StatKey = "TOTAL_BASES",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "PrizePicks",
            Slug = "prizepicks",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.PropTypes.AddRange(pitcherStrikeouts, totalBases);
        dbContext.Platforms.Add(platform);

        await AddMlbPredictionGenerationScenarioPlayerAsync(
            dbContext,
            league,
            pitcherStrikeouts,
            platform,
            "Aaron Nola Style",
            line: 3.5m,
            historicalValues: Enumerable.Repeat(5m, 60).ToArray());
        await AddMlbPredictionGenerationScenarioPlayerAsync(
            dbContext,
            league,
            totalBases,
            platform,
            "Pete Crow-Armstrong Style",
            line: 0.5m,
            historicalValues: Enumerable.Repeat(3m, 60).ToArray());
        await AddMlbPredictionGenerationScenarioPlayerAsync(
            dbContext,
            league,
            pitcherStrikeouts,
            platform,
            "Clear Under Style",
            line: 6.5m,
            historicalValues: Enumerable.Repeat(4m, 60).ToArray());
        await AddMlbPredictionGenerationScenarioPlayerAsync(
            dbContext,
            league,
            totalBases,
            platform,
            "Clear Pass Style",
            line: 2.5m,
            historicalValues: Enumerable.Repeat(3m, 60).ToArray());

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedMlbPredictionGenerationPriorShrinkageInversionScenarioAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Baseball" };
        var league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Code = "MLB", Name = "Major League Baseball" };
        var pitcherStrikeouts = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Pitcher Strikeouts",
            StatKey = "PITCHER_STRIKEOUTS",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "theScore Bet",
            Slug = "thescorebet",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.PropTypes.Add(pitcherStrikeouts);
        dbContext.Platforms.Add(platform);

        await AddMlbPredictionGenerationScenarioPlayerAsync(
            dbContext,
            league,
            pitcherStrikeouts,
            platform,
            "Jacob Misiorowski Style",
            line: 7.5m,
            historicalValues: [9m, 8m, 11m, 10m, 8m, 12m, 8m, 8m, 15m, 7m]);

        for (var playerIndex = 0; playerIndex < 20; playerIndex++)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                DisplayName = $"Prior Under Style {playerIndex}",
                Position = "P"
            };
            dbContext.Players.Add(player);

            for (var resultIndex = 0; resultIndex < 10; resultIndex++)
            {
                dbContext.PropResults.Add(new PropResult
                {
                    Id = Guid.NewGuid(),
                    PlayerId = player.Id,
                    LeagueId = league.Id,
                    PropTypeId = pitcherStrikeouts.Id,
                    EventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-(resultIndex + 1)),
                    ActualResultValue = 4m,
                    ExternalSource = "mlb",
                    ExternalSourceIdentifier = $"prior-shrinkage-inversion:{player.Id:N}:{resultIndex}",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-(resultIndex + 1)),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(resultIndex + 1))
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task AddMlbPredictionGenerationScenarioPlayerAsync(
        ApplicationDbContext dbContext,
        League league,
        PropType propType,
        Platform platform,
        string playerName,
        decimal line,
        decimal[] historicalValues)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = playerName,
            Position = propType.Name == "Pitcher Strikeouts" ? "P" : "OF"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"mlb-generation-side-selection-{Guid.NewGuid():N}",
            StartsAt = DateTimeOffset.UtcNow.AddHours(6),
            Status = "active"
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"generation-side-selection:{Guid.NewGuid():N}",
            ExternalStableKey = $"generation-side-selection:{playerName}",
            Label = $"{playerName} {propType.Name}",
            CurrentLine = line,
            OverAvailable = true,
            UnderAvailable = true,
            PayoutType = "standard",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Metadata = JsonSerializer.SerializeToDocument(new
            {
                dataProvider = "The Odds API",
                providerCategory = "DFS",
                region = "us_dfs",
                isActionableForUser = true,
                bookmaker = new { key = "prizepicks", title = "PrizePicks" },
                market = new { key = propType.Name == "Pitcher Strikeouts" ? "pitcher_strikeouts" : "batter_total_bases", name = propType.Name }
            })
        });

        for (var index = 0; index < historicalValues.Length; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-(index + 1)),
                ActualResultValue = historicalValues[index],
                ExternalSource = "mlb",
                ExternalSourceIdentifier = $"generation-side-selection:{player.Id:N}:{index}",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1)),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1))
            });
        }

        await Task.CompletedTask;
    }

    private static async Task AddMlbDfsSelectionScenarioPlayerAsync(
        ApplicationDbContext dbContext,
        League league,
        PropType propType,
        Platform platform,
        PredictionRun run,
        string playerName,
        decimal line,
        decimal projection,
        int overResults,
        int pushResults,
        int underResults,
        string providerCategory = "DFS",
        string bookmakerKey = "prizepicks",
        string bookmakerTitle = "PrizePicks",
        string region = "us_dfs",
        bool entryActionable = true,
        int overAmericanPrice = 100,
        int underAmericanPrice = 100,
        decimal overDecimalPrice = 2.0m,
        decimal underDecimalPrice = 2.0m,
        decimal[]? historicalValues = null)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = playerName,
            Position = "P"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"mlb-dfs-hit-probability-{Guid.NewGuid():N}",
            StartsAt = DateTimeOffset.UtcNow.AddHours(6),
            Status = "active"
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.Predictions.Add(new Prediction
        {
            Id = Guid.NewGuid(),
            PredictionRunId = run.Id,
            PropId = prop.Id,
            PredictedValue = projection,
            SportsbookLine = line,
            Edge = Math.Round(projection - line, 4),
            Recommendation = "PASS",
            Confidence = 0.75m,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-8)
        });

        var eventStart = DateTimeOffset.UtcNow.AddHours(6);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"theodds:prizepicks:test:{Guid.NewGuid():N}",
            ExternalStableKey = $"theodds:prizepicks:{playerName}",
            Label = $"{playerName} Pitcher Strikeouts",
            CurrentLine = line,
            OverAvailable = true,
            UnderAvailable = true,
            PayoutType = "standard",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Metadata = JsonSerializer.SerializeToDocument(new
            {
                dataProvider = "The Odds API",
                providerCategory,
                region,
                isActionableForUser = entryActionable,
                isIndicativeOdds = false,
                projectionType = "Standard",
                projectionTypeConfidence = "Known",
                isAlternateMarket = false,
                payoutMultiplierMayDiffer = false,
                bookmaker = new { key = bookmakerKey, title = bookmakerTitle },
                market = new { key = "pitcher_strikeouts", name = "Pitcher Strikeouts" },
                eventInfo = new
                {
                    eventId = "mlb-dfs-hit-probability-event",
                    eventStartTime = eventStart,
                    homeTeam = "Home",
                    awayTeam = "Away"
                },
                options = new
                {
                    higher = new { americanPrice = overAmericanPrice, decimalPrice = overDecimalPrice },
                    lower = new { americanPrice = underAmericanPrice, decimalPrice = underDecimalPrice }
                }
            })
        });

        var values = historicalValues ?? Enumerable.Repeat(line + 1m, overResults)
            .Concat(Enumerable.Repeat(line, pushResults))
            .Concat(Enumerable.Repeat(line - 1m, underResults))
            .ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-(index + 1)),
                ActualResultValue = values[index],
                ExternalSource = "mlb",
                ExternalSourceIdentifier = $"mlb-dfs-hit-probability:{player.Id:N}:{index}",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1)),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 1))
            });
        }

        await Task.CompletedTask;
    }

    private static void AddHistoricalLikelyHitReplayPlayer(
        ApplicationDbContext dbContext,
        League league,
        PropType propType,
        TheOddsRefreshRun refreshRun,
        DateOnly eventDate,
        string playerName,
        decimal actualResult)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = playerName,
            Position = "P"
        };
        dbContext.Players.Add(player);

        for (var index = 0; index < 40; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = eventDate.AddDays(-(index + 1)),
                ActualResultValue = 4m,
                ExternalSource = "mlb",
                ExternalSourceIdentifier = $"likely-hit-replay-history:{player.Id:N}:{index}",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 2)),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(index + 2))
            });
        }

        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = actualResult,
            ExternalSource = "mlb",
            ExternalSourceIdentifier = $"likely-hit-replay-result:{player.Id:N}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var eventStart = new DateTimeOffset(eventDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(23))), TimeSpan.Zero);
        var details = JsonSerializer.SerializeToDocument(new
        {
            sport = "MLB",
            competition = "MLB",
            eventId = $"historical-replay-event-{player.Id:N}",
            eventStartTime = eventStart,
            eventStartTimeUtc = eventStart,
            expectedResultDate = eventDate,
            eventDate,
            homeTeam = "Home",
            awayTeam = "Away",
            player = playerName,
            playerId = player.Id,
            canonicalPlayerId = player.Id,
            normalizedPropType = "Pitcher Strikeouts",
            line = 6.5m,
            providerCategory = "Sportsbook",
            entryProviderCategory = "Sportsbook",
            bestBookmaker = "DraftKings",
            bestSide = "Under",
            bestAmericanPrice = -150,
            bestDecimalPrice = 1.666667m,
            bestProbability = 0.70m,
            selectedSideProbability = 0.70m,
            rawSelectedSideProbability = 0.70m,
            bestImpliedProbability = 0.60m,
            rawPriorUnderRate = 0.50m,
            historicalSampleSize = 40,
            lifecycleStatus = "Active",
            isFresh = true,
            isStale = false,
            isExpired = false,
            entryProvider = "DraftKings",
            entryActionableForUser = false
        });

        dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = null,
            Sport = "MLB",
            SlateDate = eventDate,
            GeneratedAtUtc = eventStart.AddHours(-4),
            BookmakerPreset = "sportsbook",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = "DraftKings",
            Player = playerName,
            EventId = $"historical-replay-event-{player.Id:N}",
            EventName = "Away @ Home",
            EventStartTime = eventStart,
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Pitcher Strikeouts",
            Line = 6.5m,
            BestSide = "Under",
            BestBookmaker = "DraftKings",
            BestAmericanPrice = -150,
            BestDecimalPrice = 1.666667m,
            BestProbability = 0.70m,
            BestImpliedProbability = 0.60m,
            BestProbabilityEdge = 0.10m,
            BestEstimatedEv = 0.166667m,
            StrategyClassification = "Candidate",
            StrategyTier = "LikelyHitV1",
            IsCandidate = true,
            IsWatchlist = false,
            IsAvoid = false,
            IsResearchOnly = false,
            EntryProvider = "DraftKings",
            EntryCompatibilityGroup = "DraftKings",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = details
        });
    }

    private static MarketLineShoppingSnapshot BuildExpectedValueDisplaySnapshot(
        TheOddsRefreshRun refreshRun,
        string player,
        string providerCategory,
        string bookmaker,
        decimal? bestEstimatedEv,
        decimal? bestImpliedProbability,
        decimal? bestDecimalPrice,
        int? bestAmericanPrice,
        bool entryActionable)
        => BuildExpectedValueDisplaySnapshot(
            refreshRun,
            player,
            providerCategory,
            bookmaker,
            bestEstimatedEv,
            bestImpliedProbability,
            bestDecimalPrice,
            bestAmericanPrice,
            entryActionable,
            bestProbability: 0.58m,
            historicalSampleSize: null,
            confidenceScore: null);

    private static MarketLineShoppingSnapshot BuildExpectedValueDisplaySnapshot(
        TheOddsRefreshRun refreshRun,
        string player,
        string providerCategory,
        string bookmaker,
        decimal? bestEstimatedEv,
        decimal? bestImpliedProbability,
        decimal? bestDecimalPrice,
        int? bestAmericanPrice,
        bool entryActionable,
        decimal bestProbability,
        int? historicalSampleSize,
        decimal? confidenceScore)
    {
        var details = JsonSerializer.SerializeToDocument(new
        {
            sport = "MLB",
            eventId = $"event-{player.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}",
            eventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            eventStartTimeUtc = DateTimeOffset.UtcNow.AddHours(3),
            homeTeam = "Home",
            awayTeam = "Away",
            player,
            normalizedPropType = "Pitcher Strikeouts",
            line = 6.5m,
            providerCategory,
            entryProviderCategory = providerCategory,
            bestBookmaker = bookmaker,
            bestSide = "Under",
            bestAmericanPrice,
            bestDecimalPrice,
            bestProbability,
            bestImpliedProbability,
            bestProbabilityEdge = bestImpliedProbability.HasValue
                ? Math.Round(bestProbability - bestImpliedProbability.Value, 6)
                : (decimal?)null,
            bestEstimatedEv,
            historicalSampleSize,
            confidenceScore,
            entryProvider = bookmaker,
            entryActionableForUser = entryActionable,
            strategyClassification = "Candidate",
            strategyTier = "ConservativeCandidate",
            isCandidate = true,
            lifecycleStatus = "Active"
        });

        return new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = null,
            Sport = "MLB",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "mixed",
            ProviderCategory = providerCategory,
            Provider = "The Odds API",
            Bookmaker = bookmaker,
            Player = player,
            EventId = $"event-{player.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}",
            EventName = "Away @ Home",
            EventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Pitcher Strikeouts",
            Line = 6.5m,
            BestSide = "Under",
            BestBookmaker = bookmaker,
            BestAmericanPrice = bestAmericanPrice,
            BestDecimalPrice = bestDecimalPrice,
            BestProbability = bestProbability,
            BestImpliedProbability = bestImpliedProbability,
            BestProbabilityEdge = bestImpliedProbability.HasValue
                ? Math.Round(bestProbability - bestImpliedProbability.Value, 6)
                : null,
            BestEstimatedEv = bestEstimatedEv,
            StrategyClassification = "Candidate",
            StrategyTier = "ConservativeCandidate",
            IsCandidate = true,
            IsWatchlist = false,
            IsAvoid = false,
            IsResearchOnly = false,
            EntryProvider = bookmaker,
            EntryCompatibilityGroup = bookmaker,
            EntryProviderCategory = providerCategory,
            EntryActionableForUser = entryActionable,
            DetailsJson = details
        };
    }

    private static MarketTopPickSnapshot BuildExpectedValueDisplayTopPickSnapshot(
        TheOddsRefreshRun refreshRun,
        string player,
        string providerCategory,
        string bookmaker,
        decimal? bestEstimatedEv,
        decimal? bestImpliedProbability,
        decimal? bestDecimalPrice,
        int? bestAmericanPrice,
        bool entryActionable)
        => BuildExpectedValueDisplayTopPickSnapshot(
            refreshRun,
            player,
            providerCategory,
            bookmaker,
            bestEstimatedEv,
            bestImpliedProbability,
            bestDecimalPrice,
            bestAmericanPrice,
            entryActionable,
            bestProbability: 0.58m,
            historicalSampleSize: null,
            confidenceScore: null);

    private static MarketTopPickSnapshot BuildExpectedValueDisplayTopPickSnapshot(
        TheOddsRefreshRun refreshRun,
        string player,
        string providerCategory,
        string bookmaker,
        decimal? bestEstimatedEv,
        decimal? bestImpliedProbability,
        decimal? bestDecimalPrice,
        int? bestAmericanPrice,
        bool entryActionable,
        decimal bestProbability,
        int? historicalSampleSize,
        decimal? confidenceScore)
    {
        var lineShoppingSnapshot = BuildExpectedValueDisplaySnapshot(
            refreshRun,
            player,
            providerCategory,
            bookmaker,
            bestEstimatedEv,
            bestImpliedProbability,
            bestDecimalPrice,
            bestAmericanPrice,
            entryActionable,
            bestProbability,
            historicalSampleSize,
            confidenceScore);

        return new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = null,
            Sport = lineShoppingSnapshot.Sport,
            SlateDate = lineShoppingSnapshot.SlateDate,
            GeneratedAtUtc = lineShoppingSnapshot.GeneratedAtUtc,
            BookmakerPreset = lineShoppingSnapshot.BookmakerPreset,
            ProviderCategory = lineShoppingSnapshot.ProviderCategory,
            Provider = lineShoppingSnapshot.Provider,
            Bookmaker = lineShoppingSnapshot.Bookmaker,
            Player = lineShoppingSnapshot.Player,
            EventId = lineShoppingSnapshot.EventId,
            EventName = lineShoppingSnapshot.EventName,
            EventStartTime = lineShoppingSnapshot.EventStartTime,
            LifecycleStatus = lineShoppingSnapshot.LifecycleStatus,
            IsStale = lineShoppingSnapshot.IsStale,
            IsExpired = lineShoppingSnapshot.IsExpired,
            PropType = lineShoppingSnapshot.PropType,
            Line = lineShoppingSnapshot.Line,
            BestSide = lineShoppingSnapshot.BestSide,
            BestBookmaker = lineShoppingSnapshot.BestBookmaker,
            BestAmericanPrice = lineShoppingSnapshot.BestAmericanPrice,
            BestDecimalPrice = lineShoppingSnapshot.BestDecimalPrice,
            BestProbability = lineShoppingSnapshot.BestProbability,
            BestImpliedProbability = lineShoppingSnapshot.BestImpliedProbability,
            BestProbabilityEdge = lineShoppingSnapshot.BestProbabilityEdge,
            BestEstimatedEv = lineShoppingSnapshot.BestEstimatedEv,
            StrategyClassification = lineShoppingSnapshot.StrategyClassification,
            StrategyTier = lineShoppingSnapshot.StrategyTier,
            IsCandidate = lineShoppingSnapshot.IsCandidate,
            IsWatchlist = lineShoppingSnapshot.IsWatchlist,
            IsAvoid = lineShoppingSnapshot.IsAvoid,
            IsResearchOnly = lineShoppingSnapshot.IsResearchOnly,
            EntryProvider = lineShoppingSnapshot.EntryProvider,
            EntryCompatibilityGroup = lineShoppingSnapshot.EntryCompatibilityGroup,
            EntryProviderCategory = lineShoppingSnapshot.EntryProviderCategory,
            EntryActionableForUser = lineShoppingSnapshot.EntryActionableForUser,
            DetailsJson = lineShoppingSnapshot.DetailsJson
        };
    }

    private static MarketTopPickSnapshot BuildLikelyHitSportsbookTopPickSnapshot(
        TheOddsRefreshRun refreshRun,
        string player,
        string bookmaker,
        int americanPrice,
        decimal decimalPrice,
        decimal impliedProbability,
        decimal rawSelectedSideProbability,
        int historicalSampleSize,
        decimal confidenceScore,
        string sampleWarning)
    {
        var eventStart = DateTimeOffset.UtcNow.AddHours(3);
        var eventId = $"event-{player.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}";
        var details = JsonSerializer.SerializeToDocument(new
        {
            sport = "MLB",
            eventId,
            eventStartTime = eventStart,
            eventStartTimeUtc = eventStart,
            homeTeam = "Home",
            awayTeam = "Away",
            player,
            playerName = player,
            normalizedPropType = "Pitcher Strikeouts",
            line = 6.5m,
            provider = "The Odds API",
            providerCategory = "Sportsbook",
            entryProviderCategory = "Sportsbook",
            bestBookmaker = bookmaker,
            bookmaker,
            bestSide = "Under",
            selectedSide = "Under",
            bestAmericanPrice = americanPrice,
            bestDecimalPrice = decimalPrice,
            bestImpliedProbability = impliedProbability,
            selectedSideProbability = rawSelectedSideProbability,
            rawSelectedSideProbability,
            bestProbability = rawSelectedSideProbability,
            modelOverProbability = 1m - rawSelectedSideProbability,
            modelUnderProbability = rawSelectedSideProbability,
            probabilitySource = "LikelyHitV1",
            historicalSampleSize,
            playerSampleSize = historicalSampleSize,
            sampleWarning,
            confidenceScore,
            entryProvider = bookmaker,
            entryActionableForUser = false,
            entryRuleWarning = "MLB sportsbook recommendation. Entry Builder is available only for DFS entries.",
            strategyClassification = "Candidate",
            strategyTier = "MlbSportsbookRecommendation",
            isCandidate = true,
            isRecommendation = true,
            recommendationActionableForUser = true,
            recommendationType = "Sportsbook",
            lifecycleStatus = "Active",
            publicTopPickMinimumHitChance = 0.65m,
            hitChanceGatePassed = true,
            publicTopPickHitChanceGatePassed = true,
            usesAggregateProbability = false,
            recommendationProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            displayProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            isProjectionRecommendationAligned = true,
            projectionProbabilityConflict = false,
            recentWeightedProjectionProbabilityConflict = false,
            selectedSideHistoryConsistencyGuardApplied = true,
            selectedSideHistoryConsistencyGuardPassed = true,
            fatalSelectedSideHistoryConsistencyCodes = Array.Empty<string>()
        });

        return new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = null,
            Sport = "MLB",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
            BookmakerPreset = "sportsbook",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = bookmaker,
            Player = player,
            EventId = eventId,
            EventName = "Away @ Home",
            EventStartTime = eventStart,
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Pitcher Strikeouts",
            Line = 6.5m,
            BestSide = "Under",
            BestBookmaker = bookmaker,
            BestAmericanPrice = americanPrice,
            BestDecimalPrice = decimalPrice,
            BestProbability = rawSelectedSideProbability,
            BestImpliedProbability = impliedProbability,
            BestProbabilityEdge = Math.Round(rawSelectedSideProbability - impliedProbability, 6),
            BestEstimatedEv = Math.Round((rawSelectedSideProbability * decimalPrice) - 1m, 6),
            StrategyClassification = "Candidate",
            StrategyTier = "MlbSportsbookRecommendation",
            ModelVersion = "LikelyHitV1",
            RecommendationContractVersion = "LikelyHitV1",
            ProbabilityModelVersion = "LikelyHitV1",
            ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
            IsCandidate = true,
            IsWatchlist = false,
            IsAvoid = false,
            IsResearchOnly = false,
            EntryProvider = bookmaker,
            EntryCompatibilityGroup = "Sportsbook",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = details
        };
    }

    public async Task SeedApiSportsSoccerProviderStatMappingsAsync(string? excludeCanonicalStatKey = null)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var rows = new[]
        {
            ("Goals", "GOALS", new[] { "goals.total" }, "Direct", "Known", true, "goals.total"),
            ("Assists", "ASSISTS", new[] { "goals.assists" }, "Direct", "Known", true, "goals.assists"),
            ("Shots", "SHOTS", new[] { "shots.total" }, "Direct", "Known", true, "shots.total"),
            ("Shots On Target", "SHOTS_ON_TARGET", new[] { "shots.on" }, "Direct", "Known", true, "shots.on"),
            ("Passes Attempted", "PASSES_ATTEMPTED", new[] { "passes.total" }, "Direct", "Known", true, "passes.total"),
            ("Tackles", "TACKLES", new[] { "tackles.total" }, "Direct", "Known", true, "tackles.total"),
            ("Fouls", "FOULS", new[] { "fouls.committed" }, "Direct", "Known", true, "fouls.committed"),
            ("Fouls Drawn", "FOULS_DRAWN", new[] { "fouls.drawn" }, "Direct", "Known", true, "fouls.drawn"),
            ("Offsides", "OFFSIDES", new[] { "offsides" }, "Direct", "Known", true, "offsides"),
            ("Goalie Saves", "GOALIE_SAVES", new[] { "goals.saves" }, "GoalkeeperOnlyDirect", "Known", true, "goals.saves"),
            ("Attempted Dribbles", "ATTEMPTED_DRIBBLES", new[] { "dribbles.attempts" }, "Direct", "Known", true, "dribbles.attempts"),
            ("Goal + Assist", "GOAL_ASSIST", new[] { "goals.total", "goals.assists" }, "Sum", "NeedsConfirmation", false, null),
            ("Cards", "CARDS", new[] { "cards.yellow", "cards.red" }, "Sum", "NeedsConfirmation", false, null),
            ("Goals Allowed", "GOALS_ALLOWED", new[] { "goals.conceded" }, "GoalkeeperOnlyDirect", "NeedsConfirmation", false, "goals.conceded"),
            ("Shots Assisted", "SHOTS_ASSISTED", new[] { "passes.key" }, "Direct", "NeedsConfirmation", false, "passes.key"),
            ("Crosses", "CROSSES", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Clearances", "CLEARANCES", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Outfield Fantasy Score", "OUTFIELD_FANTASY_SCORE", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Goalie Fantasy Score", "GOALIE_FANTASY_SCORE", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Combo props", "COMBO_PROPS", Array.Empty<string>(), "Unsupported", "Unsupported", false, null)
        };

        foreach (var (displayName, statKey, fields, transform, confidence, isGradeable, providerStatKey) in rows)
        {
            if (string.Equals(statKey, excludeCanonicalStatKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dbContext.ProviderStatMappings.Add(new ProviderStatMapping
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                ProviderKey = "api-sports-football",
                ProviderProduct = "API-Sports Football",
                CompetitionScope = "World Cup",
                Season = 2026,
                ProviderMappingKey = $"api-sports-football:SOCCER:World Cup:2026:{statKey}",
                ProviderPropName = displayName,
                ProviderStatKey = providerStatKey,
                ProviderStatFieldsJson = JsonSerializer.SerializeToDocument(fields),
                CanonicalStatKey = statKey,
                DisplayName = displayName,
                ValueTransform = transform,
                RuleConfidence = confidence,
                IsGradeable = isGradeable,
                PublicGradingEnabled = false,
                IsActive = true,
                Notes = "Seeded by test fixture.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedApiSportsSoccerMarketJoinRowsAsync(
        bool includeProviderEventId = true,
        bool includeResultTeam = true,
        bool includeMarketKey = true,
        decimal actualValue = 1m,
        decimal currentLine = 0.5m,
        bool overAvailable = true,
        bool underAvailable = false,
        string resultPlayerName = "Test Forward",
        string platformPlayerName = "Test Forward",
        bool includeResultRow = true,
        bool includePlatformRow = true,
        string fixtureId = "1489393",
        bool useTheOddsContext = true,
        string? providerEventIdOverride = null,
        string eventHomeTeamName = "Uruguay",
        string eventAwayTeamName = "Cape Verde",
        string resultPlayerTeamName = "Uruguay",
        DateOnly? eventDate = null,
        string platformMarketKey = "player_goals",
        string platformPropTypeName = "Goals",
        string platformStatKey = "GOALS",
        string apiSportsPropTypeName = "Goals",
        string apiSportsStatKey = "GOALS",
        bool resetDatabase = true)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (resetDatabase)
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var effectiveEventDate = eventDate ?? new DateOnly(2026, 6, 21);
        var eventStart = new DateTimeOffset(effectiveEventDate.Year, effectiveEventDate.Month, effectiveEventDate.Day, 21, 0, 0, TimeSpan.Zero);
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var league = resetDatabase
            ? null
            : await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
        }
        var homeTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = eventHomeTeamName
        };
        var awayTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = eventAwayTeamName
        };
        var resultTeam = string.Equals(resultPlayerTeamName, eventHomeTeamName, StringComparison.OrdinalIgnoreCase)
            ? homeTeam
            : new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = resultPlayerTeamName
            };
        var game = new Game
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ScheduledStartAt = eventStart,
            Status = "scheduled"
        };
        var propType = resetDatabase
            ? null
            : await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == platformStatKey);
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = platformPropTypeName,
                StatKey = platformStatKey,
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }
        var resultPropType = string.Equals(platformStatKey, apiSportsStatKey, StringComparison.OrdinalIgnoreCase)
            ? propType
            : resetDatabase
                ? null
                : await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == apiSportsStatKey);
        if (resultPropType is null)
        {
            resultPropType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = apiSportsPropTypeName,
                StatKey = apiSportsStatKey,
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(resultPropType);
        }
        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = resultPlayerName,
            TeamId = includeResultTeam ? resultTeam.Id : null,
            LeagueId = league.Id,
            Position = "F"
        };
        var platformPlayer = string.Equals(platformPlayerName, resultPlayerName, StringComparison.Ordinal)
            ? player
            : new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = platformPlayerName,
                TeamId = includeResultTeam ? resultTeam.Id : null,
                LeagueId = league.Id,
                Position = "F"
            };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = platformPlayer.Id,
            GameId = game.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"soccer-market-join-{Guid.NewGuid():N}",
            StartsAt = game.ScheduledStartAt,
            Status = "active"
        };
        var platform = resetDatabase
            ? null
            : await dbContext.Platforms.SingleOrDefaultAsync(row => row.Slug == "betmgm");
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = "BetMGM",
                Slug = "betmgm"
            };
            dbContext.Platforms.Add(platform);
        }
        var providerEventId = includeProviderEventId ? providerEventIdOverride ?? "852a3abe6d46820720febc0fedebff9a" : null;
        var metadata = JsonSerializer.SerializeToDocument(new
        {
            dataProvider = useTheOddsContext ? "The Odds API" : "Internal",
            contextSchemaVersion = useTheOddsContext ? "the-odds-provider-context-v2" : "legacy",
            providerCategory = "Sportsbook",
            sport = new
            {
                competition = "World Cup",
                providerSportKey = useTheOddsContext ? "soccer_fifa_world_cup" : "legacy_soccer"
            },
            eventInfo = new
            {
                providerEventId,
                eventId = providerEventId,
                homeTeam = eventHomeTeamName,
                awayTeam = eventAwayTeamName
            },
            market = includeMarketKey
                ? new { key = platformMarketKey }
                : null,
            player = new
            {
                providerPlayerId = (string?)null,
                teamName = (string?)null
            }
        });

        dbContext.Teams.AddRange(homeTeam, awayTeam);
        dbContext.Games.Add(game);
        dbContext.Players.Add(player);
        if (resultTeam.Id != homeTeam.Id)
        {
            dbContext.Teams.Add(resultTeam);
        }

        if (platformPlayer.Id != player.Id)
        {
            dbContext.Players.Add(platformPlayer);
        }

        dbContext.Props.Add(prop);
        if (includePlatformRow)
        {
            dbContext.PlatformProps.Add(new PlatformProp
            {
                Id = Guid.NewGuid(),
                PropId = prop.Id,
                PlatformId = platform.Id,
                PlatformPropId = $"betmgm-{fixtureId}-{Guid.NewGuid():N}",
                ExternalStableKey = providerEventId,
                Label = $"{platformPlayerName} {platformPropTypeName}",
                CurrentLine = currentLine,
                OverAvailable = overAvailable,
                UnderAvailable = underAvailable,
                PayoutType = "sportsbook",
                Status = "active",
                ObservedAt = DateTimeOffset.UtcNow,
                Metadata = metadata
            });
        }

        if (includeResultRow)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = resultPropType.Id,
                EventDate = effectiveEventDate,
                ActualResultValue = actualValue,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:{fixtureId}:1001:{apiSportsStatKey}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedApiSportsSoccerAliasCandidateRowsAsync(
        IReadOnlyList<string> apiSportsPlayerNames,
        IReadOnlyList<string> platformPlayerNames)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var league = new League
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "World Cup",
            Code = "WC2026",
            SeasonType = "2026"
        };
        var homeTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = "Uruguay"
        };
        var awayTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = "Cape Verde"
        };
        var game = new Game
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ScheduledStartAt = new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero),
            Status = "scheduled"
        };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Goals",
            StatKey = "GOALS",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "BetMGM",
            Slug = "theodds-betmgm"
        };
        const string providerEventId = "852a3abe6d46820720febc0fedebff9a";

        dbContext.Leagues.Add(league);
        dbContext.Teams.AddRange(homeTeam, awayTeam);
        dbContext.Games.Add(game);
        dbContext.PropTypes.Add(propType);
        dbContext.Platforms.Add(platform);

        for (var index = 0; index < apiSportsPlayerNames.Count; index++)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = apiSportsPlayerNames[index],
                TeamId = homeTeam.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            dbContext.Players.Add(player);
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = new DateOnly(2026, 6, 21),
                ActualResultValue = 0,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:1489398:{1000 + index}:GOALS",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        foreach (var platformPlayerName in platformPlayerNames)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = platformPlayerName,
                TeamId = homeTeam.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            var prop = new Prop
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                GameId = game.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                NormalizedKey = $"soccer-alias-{Guid.NewGuid():N}",
                StartsAt = game.ScheduledStartAt,
                Status = "active"
            };
            dbContext.Players.Add(player);
            dbContext.Props.Add(prop);
            dbContext.PlatformProps.Add(new PlatformProp
            {
                Id = Guid.NewGuid(),
                PropId = prop.Id,
                PlatformId = platform.Id,
                PlatformPropId = $"betmgm-{Guid.NewGuid():N}",
                ExternalStableKey = providerEventId,
                Label = $"{platformPlayerName} Goals",
                CurrentLine = 0.5m,
                OverAvailable = true,
                UnderAvailable = false,
                PayoutType = "sportsbook",
                Status = "active",
                ObservedAt = DateTimeOffset.UtcNow,
                Metadata = JsonSerializer.SerializeToDocument(new
                {
                    providerCategory = "Sportsbook",
                    eventInfo = new
                    {
                        providerEventId,
                        eventId = providerEventId,
                        homeTeam = "Uruguay",
                        awayTeam = "Cape Verde"
                    },
                    market = new { key = "player_goals" },
                    player = new
                    {
                        providerPlayerId = (string?)null,
                        teamName = (string?)null
                    }
                })
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedSoccerPropTypesWithDuplicateNamesAndDifferentStatKeysAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        dbContext.PropTypes.AddRange(
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES_COMBO",
                ValueType = "numeric"
            },
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES",
                ValueType = "numeric"
            });
        await dbContext.SaveChangesAsync();
    }

    public async Task SeedConflictingSoccerPropTypesWithSameStatKeyAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        dbContext.PropTypes.AddRange(
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES",
                ValueType = "numeric"
            },
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalkeeper Saves",
                StatKey = "goalie_saves",
                ValueType = "numeric"
            });
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasSoccerPropTypeAsync(string statKey)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PropTypes.AnyAsync(propType =>
            propType.Sport != null
            && propType.Sport.Code == "SOCCER"
            && propType.StatKey == statKey);
    }

    private static async Task<Sport> GetOrCreateSoccerSportAsync(ApplicationDbContext dbContext)
    {
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is not null)
        {
            return sport;
        }

        sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
        dbContext.Sports.Add(sport);
        await dbContext.SaveChangesAsync();
        return sport;
    }

    public async Task SeedCreditTelemetryRefreshAsync(int requestsUsed, int requestsRemaining, int observedMinutesAgo)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-observedMinutesAgo);
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = observedAt.AddMinutes(-1),
            CompletedAtUtc = observedAt,
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 8,
            ActualApiCalls = 8,
            PredictionCount = 12,
            LineShoppingGroupCount = 12,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 7).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                creditUsage = new
                {
                    requestsUsed,
                    requestsRemaining,
                    requestsLast = 0
                }
            })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
    }

    public async Task SeedSoccerWorldCupSnapshotsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "SOCCER",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbookContext",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "player_shots",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = 1,
            LineShoppingGroupCount = 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "SOCCER",
                competition = "World Cup",
                soccerResultGradingSupported = false
            })
        };

        var details = JsonDocument.Parse("""
        {
          "player": "Test Forward",
          "propType": "Shots",
          "side": "Over",
          "line": 1.5,
          "provider": "FanDuel",
          "providerCategory": "Sportsbook",
          "entryProviderCategory": "Sportsbook",
          "entryProvider": "FanDuel",
          "entryActionableForUser": false,
          "sportsbookContextResearchOnly": true,
          "isResearchOnly": true,
          "resultGradingSupported": false,
          "eventName": "Country A @ Country B"
        }
        """);

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            Sport = "SOCCER",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "sportsbookContext",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            Player = "Test Forward",
            EventId = "world-cup-event-1",
            EventName = "Country A @ Country B",
            EventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Shots",
            Line = 1.5m,
            BestSide = "Over",
            BestBookmaker = "FanDuel",
            BestProbability = null,
            BestEstimatedEv = null,
            BestProbabilityEdge = null,
            StrategyClassification = "ResearchOnly",
            StrategyTier = "ResearchOnly",
            IsCandidate = false,
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "SportsbookContext",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = details
        });
        dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            Sport = "SOCCER",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "sportsbookContext",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            Player = "Test Forward",
            EventId = "world-cup-event-1",
            EventName = "Country A @ Country B",
            EventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Shots",
            Line = 1.5m,
            BestSide = "Over",
            BestBookmaker = "FanDuel",
            BestProbability = null,
            BestEstimatedEv = null,
            BestProbabilityEdge = null,
            StrategyClassification = "ResearchOnly",
            StrategyTier = "ResearchOnly",
            IsCandidate = false,
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "SportsbookContext",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = details
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedSoccerWorldCupProviderContextRowsWithoutSnapshotsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
        var league = new League
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "World Cup",
            Code = "WC2026",
            SeasonType = "2026"
        };
        var homeTeam = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, Name = "Uruguay" };
        var awayTeam = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, Name = "Cape Verde" };
        var eventStart = new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero);
        var game = new Game
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ScheduledStartAt = eventStart,
            Status = "scheduled"
        };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Shots",
            StatKey = "SHOTS",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "FanDuel",
            Slug = "theodds-fanduel"
        };
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "SOCCER",
            SlateDate = new DateOnly(2026, 6, 21),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbookContext",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "player_shots",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PlatformPropsCreated = 2,
            PlatformPropsUpserted = 2,
            PredictionCount = 0,
            LineShoppingGroupCount = 0,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "SOCCER",
                competition = "World Cup",
                platformPropsUpserted = 2,
                lineShoppingGroupCount = 0,
                soccerResultGradingSupported = false
            })
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.Teams.AddRange(homeTeam, awayTeam);
        dbContext.Games.Add(game);
        dbContext.PropTypes.Add(propType);
        dbContext.Platforms.Add(platform);
        dbContext.TheOddsRefreshRuns.Add(refreshRun);

        foreach (var playerName in new[] { "Test Forward", "Second Forward" })
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = playerName,
                TeamId = homeTeam.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            var prop = new Prop
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                GameId = game.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                NormalizedKey = $"soccer-public-context-{Guid.NewGuid():N}",
                StartsAt = eventStart,
                Status = "active"
            };

            dbContext.Players.Add(player);
            dbContext.Props.Add(prop);
            dbContext.PlatformProps.Add(new PlatformProp
            {
                Id = Guid.NewGuid(),
                PropId = prop.Id,
                PlatformId = platform.Id,
                PlatformPropId = $"fanduel-{Guid.NewGuid():N}",
                ExternalStableKey = "852a3abe6d46820720febc0fedebff9a",
                Label = $"{playerName} Shots",
                CurrentLine = 1.5m,
                OverAvailable = true,
                UnderAvailable = true,
                PayoutType = "sportsbook",
                Status = "active",
                ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
                Metadata = JsonSerializer.SerializeToDocument(new
                {
                    dataProvider = "The Odds API",
                    contextSchemaVersion = "the-odds-provider-context-v2",
                    providerCategory = "Sportsbook",
                    region = "us",
                    sport = new
                    {
                        code = "SOCCER",
                        name = "Soccer",
                        providerSportKey = "soccer_fifa_world_cup",
                        competition = "World Cup",
                        leagueCode = "WC2026",
                        leagueName = "World Cup"
                    },
                    isActionableForUser = false,
                    isIndicativeOdds = false,
                    bookmaker = new
                    {
                        key = "fanduel",
                        title = "FanDuel"
                    },
                    eventInfo = new
                    {
                        eventId = "852a3abe6d46820720febc0fedebff9a",
                        providerEventId = "852a3abe6d46820720febc0fedebff9a",
                        eventName = "Cape Verde @ Uruguay",
                        eventStartTime = eventStart,
                        homeTeam = "Uruguay",
                        awayTeam = "Cape Verde"
                    },
                    market = new
                    {
                        key = "player_shots"
                    },
                    options = new
                    {
                        higher = new { americanPrice = 120, decimalPrice = 2.20m },
                        lower = new { americanPrice = -140, decimalPrice = 1.71m }
                    }
                })
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task<Guid> AddSoccerPlatformIdentityCandidateAsync(
        string playerName,
        string teamName,
        string providerName = "PrizePicks",
        bool addApiSportsRef = false)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var team = await dbContext.Teams.FirstOrDefaultAsync(row => row.LeagueId == league.Id && row.Name == teamName);
        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = teamName
            };
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();
        }

        var propType = await dbContext.PropTypes.FirstOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == "SHOTS");
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Shots",
                StatKey = "SHOTS",
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
            await dbContext.SaveChangesAsync();
        }

        var platformSlug = providerName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        var platform = await dbContext.Platforms.FirstOrDefaultAsync(row => row.Slug == platformSlug);
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = providerName,
                Slug = platformSlug
            };
            dbContext.Platforms.Add(platform);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = playerName,
            TeamId = team.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"soccer-test-{Guid.NewGuid():N}",
            StartsAt = new DateTimeOffset(2026, 6, 11, 20, 0, 0, TimeSpan.Zero),
            Status = "active"
        };
        var platformProp = new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"{platformSlug}-{Guid.NewGuid():N}",
            ExternalStableKey = $"{platformSlug}:{playerName}:{teamName}",
            Label = $"{playerName} Shots",
            CurrentLine = 1.5m,
            OverAvailable = true,
            UnderAvailable = true,
            PayoutType = "standard",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(platformProp);

        if (addApiSportsRef)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Provider = "api-sports-football",
                ProviderPlayerId = player.Id.ToString("N"),
                ProviderDisplayName = "Test Forward"
            });
        }

        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    public async Task SeedProviderQuotaFailureAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-20),
            Succeeded = false,
            Failed = true,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 8,
            ActualApiCalls = 1,
            PredictionCount = 0,
            LineShoppingGroupCount = 0,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 7).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                error = "The Odds API quota or rate limit was exceeded.",
                creditUsage = new
                {
                    requestsUsed = 2360,
                    requestsRemaining = 2,
                    requestsLast = 1
                }
            })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
    }

    public async Task<ArtifactCounts> CountArtifactsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new ArtifactCounts(
            await dbContext.TheOddsRefreshRuns.CountAsync(),
            await dbContext.MarketTopPickSnapshots.CountAsync(),
            await dbContext.MarketLineShoppingSnapshots.CountAsync(),
            await dbContext.TheOddsRefreshPlannerDecisions.CountAsync());
    }

    public async Task<int> CountPropResultsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PropResults.CountAsync();
    }
}

public sealed class StubMlbResultsProvider : IMlbResultsProvider
{
    private readonly Dictionary<DateOnly, IReadOnlyCollection<MlbResultRecord>> recordsByDate = [];

    public string SourceName => "mlb-test";

    public int CallCount { get; private set; }

    public void SetRecords(DateOnly date, IReadOnlyCollection<MlbResultRecord> records)
    {
        recordsByDate[date] = records;
    }

    public Task<IReadOnlyCollection<MlbResultRecord>> GetMlbResultsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(recordsByDate.TryGetValue(date, out var records)
            ? records
            : Array.Empty<MlbResultRecord>());
    }

    public Task<IReadOnlyCollection<ResultRecord>> GetResultsAsync(DateOnly eventDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(recordsByDate.TryGetValue(eventDate, out var records)
            ? (IReadOnlyCollection<ResultRecord>)records.Select(row => row.ResultRecord).ToArray()
            : Array.Empty<ResultRecord>());
    }

    public Task<IReadOnlyCollection<MlbPlayerRecord>> GetPlayersAsync(int season, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<MlbPlayerRecord>>(Array.Empty<MlbPlayerRecord>());
    }
}

public sealed class CountingTheOddsAdapter : ITheOddsApiMlbPlayerPropsAdapter
{
    public int CallCount { get; private set; }
    private bool useSoccerWorldCupContextPayload;
    private bool rejectSoccerWorldCupEventOdds;
    private string soccerWorldCupPayloadMode = "valid";

    public void ConfigureSoccerWorldCupContextPayload(string payloadMode = "valid")
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = payloadMode;
    }

    public void ConfigureSoccerWorldCupProviderRejection()
    {
        useSoccerWorldCupContextPayload = true;
        rejectSoccerWorldCupEventOdds = true;
    }

    public void ConfigureSoccerWorldCupAvailabilityByRegion()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "availabilityByRegion";
    }

    public void ConfigureSoccerWorldCupSportsbookContextPayload()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "sportsbookUs";
    }

    public void ConfigureSoccerWorldCupMultiEventSportsbookContextPayload()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "multiSportsbookUs";
    }

    public Task<TheOddsApiPlayerPropsSpikeResult> FetchSpikeAsync(
        string? eventId,
        string? markets,
        string? bookmakers,
        string? regions,
        string? oddsFormat,
        CancellationToken cancellationToken,
        string? sportKey = null)
    {
        CallCount++;
        if (useSoccerWorldCupContextPayload
            && string.Equals(sportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                var events = soccerWorldCupPayloadMode == "multiSportsbookUs"
                    ? new[]
                    {
                        new TheOddsApiEventRecord(
                            "soccer-event-1",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                            "Home FC",
                            "Away FC"),
                        new TheOddsApiEventRecord(
                            "soccer-event-2",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero),
                            "Second Home",
                            "Second Away"),
                        new TheOddsApiEventRecord(
                            "soccer-event-3",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 23, 21, 0, 0, TimeSpan.Zero),
                            "Third Home",
                            "Third Away")
                    }
                    : [
                        new TheOddsApiEventRecord(
                            "soccer-event-1",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                            "Home FC",
                            "Away FC")
                    ];
                return Task.FromResult(new TheOddsApiPlayerPropsSpikeResult(
                    events,
                    "soccer-event-1",
                    [],
                    [],
                    new TheOddsApiCreditUsage(1, 99, 1),
                    "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events",
                    "{}"));
            }

            if (rejectSoccerWorldCupEventOdds)
            {
                throw new TheOddsApiProviderException(
                    "PROVIDER_NON_SUCCESS",
                    "The Odds API returned HTTP 422.",
                    "Review the sanitized provider response and request parameters before retrying.",
                    HttpStatusCode.UnprocessableEntity,
                    """{"message":"Invalid market: bad_market","error_code":"INVALID_MARKET"}""",
                    "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events/soccer-event-1/odds?apiKey=***&regions=us_dfs&markets=bad_market&oddsFormat=american",
                    new TheOddsApiCreditUsage(2, 98, 1),
                    "FetchEventOdds");
            }

            return Task.FromResult(BuildSoccerWorldCupEventOddsResult(eventId, regions));
        }

        throw new InvalidOperationException("The Odds API adapter should not be called by safety tests.");
    }

    private TheOddsApiPlayerPropsSpikeResult BuildSoccerWorldCupEventOddsResult(string? eventId, string? regions)
    {
        var eventStartTime = eventId switch
        {
            "soccer-event-2" => new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero),
            "soccer-event-3" => new DateTimeOffset(2026, 6, 23, 21, 0, 0, TimeSpan.Zero),
            _ => new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero)
        };
        var homeTeam = eventId switch
        {
            "soccer-event-2" => "Second Home",
            "soccer-event-3" => "Third Home",
            _ => "Home FC"
        };
        var awayTeam = eventId switch
        {
            "soccer-event-2" => "Second Away",
            "soccer-event-3" => "Third Away",
            _ => "Away FC"
        };
        var eventRecord = new TheOddsApiEventRecord(
            eventId,
            "soccer_fifa_world_cup",
            eventStartTime,
            homeTeam,
            awayTeam);
        var requestUrl = "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events/soccer-event-1/odds";

        var effectivePayloadMode = soccerWorldCupPayloadMode switch
        {
            "availabilityByRegion" => string.Equals(regions, "us", StringComparison.OrdinalIgnoreCase) ? "valid" : "zeroBookmakers",
            "sportsbookUs" or "multiSportsbookUs" => string.Equals(regions, "us", StringComparison.OrdinalIgnoreCase) ? "sportsbookUs" : "zeroBookmakers",
            _ => soccerWorldCupPayloadMode
        };

        return effectivePayloadMode switch
        {
            "zeroBookmakers" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, [], [], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[]}"""),
            "zeroMarkets" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], [], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[]}]}"""),
            "unsupportedMarket" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], ["player_passes"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[{"key":"player_passes"}]}]}"""),
            "configuredMarketZeroOutcomes" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], ["player_shots"], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[{"key":"player_shots","outcomes":[]}]}]}"""),
            "sportsbookUs" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "betmgm",
                        "BetMGM",
                        eventId,
                        eventStartTime,
                        homeTeam,
                        awayTeam,
                        "player_goals",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Over",
                        "Test Forward",
                        "Over",
                        0.5m,
                        120,
                        2.2m,
                        "Goals",
                        $"theodds:betmgm:{eventId}:player_goals:test-forward:0.5",
                        new { }),
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "betmgm",
                        "BetMGM",
                        eventId,
                        eventStartTime,
                        homeTeam,
                        awayTeam,
                        "player_goals",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Under",
                        "Test Forward",
                        "Under",
                        0.5m,
                        -150,
                        1.6667m,
                        "Goals",
                        $"theodds:betmgm:{eventId}:player_goals:test-forward:0.5",
                        new { })
                ],
                [BuildSoccerWorldCupDiagnostics(eventId, ["betmgm"], ["player_goals"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                "{}"),
            _ => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "prizepicks",
                        "PrizePicks",
                        eventId,
                        new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                        "Home FC",
                        "Away FC",
                        "player_shots",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Over",
                        "Test Forward",
                        "Over",
                        1.5m,
                        -110,
                        1.91m,
                        "Shots",
                        "theodds:prizepicks:soccer-event-1:player_shots:test-forward:1.5",
                        new { }),
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "prizepicks",
                        "PrizePicks",
                        eventId,
                        new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                        "Home FC",
                        "Away FC",
                        "player_shots",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Under",
                        "Test Forward",
                        "Under",
                        1.5m,
                        -110,
                        1.91m,
                        "Shots",
                        "theodds:prizepicks:soccer-event-1:player_shots:test-forward:1.5",
                        new { })
                ],
                [BuildSoccerWorldCupDiagnostics(eventId, ["prizepicks"], ["player_shots"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                "{}")
        };
    }

    private static TheOddsApiEventOddsDiagnostics BuildSoccerWorldCupDiagnostics(
        string? eventId,
        IReadOnlyList<string> bookmakerKeys,
        IReadOnlyList<string> marketKeys,
        int outcomeCount,
        int playerOutcomeCount)
    {
        return new TheOddsApiEventOddsDiagnostics(
            eventId,
            "Home FC",
            "Away FC",
            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
            EventOddsHttpSucceeded: true,
            bookmakerKeys.Count,
            bookmakerKeys,
            marketKeys.Count,
            marketKeys,
            outcomeCount,
            playerOutcomeCount);
    }
}

public sealed class CountingUnderdogAdapter : IUnderdogAdapter
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<UnderdogProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("Underdog adapter should not be called by safety tests.");
    }

    public Task<UnderdogFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("Underdog adapter should not be called by safety tests.");
    }
}

public sealed class StubApiSportsFootballClient : IApiSportsFootballClient
{
    public int FixturesCallCount { get; private set; }
    public int FixturePlayerStatsCallCount { get; private set; }
    public int LeaguesCallCount { get; private set; }
    public List<ApiSportsFootballFixturesRequest> FixtureRequests { get; } = [];
    public List<ApiSportsFootballLeaguesRequest> LeagueRequests { get; } = [];
    public HashSet<int> SuppressedLeagueMetadataIds { get; } = [];

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturesAsync(
        ApiSportsFootballFixturesRequest request,
        CancellationToken cancellationToken)
    {
        FixturesCallCount++;
        FixtureRequests.Add(request);
        var sanitizedUrl = BuildFixturesUrl(request);
        var fixtures = BuildFixtureResponseItems(request);
        var payload = JsonDocument.Parse(
            $$"""
            {
              "get": "fixtures",
              "parameters": {},
              "errors": [],
              "results": {{fixtures.Count}},
              "paging": { "current": 1, "total": 1 },
              "response": [{{string.Join(",", fixtures)}}]
            }
            """);

        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(payload, fixtures.Count, 1, 1, sanitizedUrl));
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetLeaguesAsync(
        ApiSportsFootballLeaguesRequest request,
        CancellationToken cancellationToken)
    {
        LeaguesCallCount++;
        LeagueRequests.Add(request);
        var seasons = request.League.HasValue && SuppressedLeagueMetadataIds.Contains(request.League.Value)
            ? []
            : BuildLeagueSeasonResponseItems(request.League);
        var payload = JsonDocument.Parse(
            $$"""
            {
              "get": "leagues",
              "parameters": {},
              "errors": [],
              "results": {{seasons.Count}},
              "paging": { "current": 1, "total": 1 },
              "response": [{{string.Join(",", seasons)}}]
            }
            """);

        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(
            payload,
            seasons.Count,
            1,
            1,
            $"https://v3.football.api-sports.io/leagues?id={request.League}"));
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturePlayerStatsAsync(
        string fixtureId,
        CancellationToken cancellationToken)
    {
        FixturePlayerStatsCallCount++;
        if (fixtureId == "99999")
        {
            throw new InvalidOperationException("Synthetic API-Sports player-stat failure.");
        }

        var payload = JsonDocument.Parse(
            """
            {
              "get": "fixtures/players",
              "errors": [],
              "results": 2,
              "paging": { "current": 1, "total": 1 },
              "response": [
                {
                  "team": { "id": 10, "name": "Home FC" },
                  "players": [
                    {
                      "player": { "id": 1001, "name": "Test Forward" },
                      "statistics": [
                        {
                          "games": { "position": "F", "minutes": 90, "substitute": false },
                          "goals": { "total": 1, "assists": 1, "saves": 2, "conceded": 0 },
                          "shots": { "total": 4, "on": 2 },
                          "passes": { "total": 35, "key": 3 },
                          "tackles": { "total": 1 },
                          "fouls": { "committed": 2, "drawn": 1 },
                          "offsides": 1,
                          "dribbles": { "attempts": 5 },
                          "cards": { "yellow": 1, "red": 0 }
                        }
                      ]
                    },
                    {
                      "player": { "id": 1002, "name": "Unused Substitute" },
                      "statistics": [
                        {
                          "games": { "position": "M", "minutes": null, "substitute": true },
                          "goals": { "total": null }
                        }
                      ]
                    }
                  ]
                },
                {
                  "team": { "id": 20, "name": "Away FC" },
                  "players": [
                    {
                      "player": { "id": 2001, "name": "Test Keeper" },
                      "statistics": [
                        {
                          "games": { "position": "G", "minutes": 90, "substitute": false },
                          "goals": { "total": 0, "assists": 0, "saves": 6, "conceded": 1 },
                          "shots": { "total": 0, "on": 0 },
                          "passes": { "total": 22, "key": 0 },
                          "tackles": { "total": 0 },
                          "fouls": { "committed": 0, "drawn": 0 },
                          "offsides": 0,
                          "dribbles": { "attempts": 0 },
                          "cards": { "yellow": 0, "red": 0 }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);
        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(
            payload,
            2,
            1,
            1,
            $"https://v3.football.api-sports.io/fixtures/players?fixture={Uri.EscapeDataString(fixtureId)}"));
    }

    private static List<string> BuildFixtureResponseItems(ApiSportsFootballFixturesRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FixtureId))
        {
            return request.FixtureId switch
            {
                "1489393" => [BuildFixtureJson("1489393", "2026-06-11T19:00:00+00:00", "FT", "Match Finished")],
                "99999" => [BuildFixtureJson("99999", "2026-06-14T19:00:00+00:00", "FT", "Match Finished")],
                _ => [BuildFixtureJson(request.FixtureId, "2026-06-20T19:00:00+00:00", "FT", "Match Finished")]
            };
        }

        if (request.League.HasValue && request.League.Value != 1)
        {
            return BuildHistoricalFixtureResponseItems(request);
        }

        return request.Date?.ToString("yyyy-MM-dd") switch
        {
            "2026-06-11" => [BuildFixtureJson("1489393", "2026-06-11T19:00:00+00:00", "FT", "Match Finished")],
            "2026-06-12" => [BuildFixtureJson("22222", "2026-06-12T19:00:00+00:00", "NS", "Not Started")],
            "2026-06-13" => [BuildFixtureJson("33333", "2026-06-13T19:00:00+00:00", "1H", "First Half")],
            "2026-06-14" => [BuildFixtureJson("99999", "2026-06-14T19:00:00+00:00", "FT", "Match Finished")],
            _ => []
        };
    }

    private static List<string> BuildHistoricalFixtureResponseItems(ApiSportsFootballFixturesRequest request)
    {
        var league = request.League!.Value;
        var season = request.Season ?? 2025;
        var leagueName = league switch
        {
            39 => "EPL",
            140 => "La Liga",
            78 => "Bundesliga",
            135 => "Serie A",
            61 => "Ligue 1",
            253 => "MLS",
            2 => "Champions League",
            _ => $"League {league}"
        };
        var prefix = league.ToString(CultureInfo.InvariantCulture);
        return request.Date?.ToString("yyyy-MM-dd") switch
        {
            "2025-08-15" => [BuildFixtureJson($"{prefix}0815", "2025-08-15T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            "2025-08-16" => [BuildFixtureJson($"{prefix}0816", "2025-08-16T19:00:00+00:00", "NS", "Not Started", league, leagueName, season)],
            "2025-08-17" => [BuildFixtureJson($"{prefix}0817", "2025-08-17T19:00:00+00:00", "1H", "First Half", league, leagueName, season)],
            "2025-08-18" => [BuildFixtureJson($"{prefix}0818", "2025-08-18T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            "2026-08-15" => [BuildFixtureJson($"{prefix}2615", "2026-08-15T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            "2027-02-15" => [BuildFixtureJson($"{prefix}2715", "2027-02-15T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            "2026-06-11" => [BuildFixtureJson($"{prefix}0611", "2026-06-11T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            "2026-06-12" => [BuildFixtureJson($"{prefix}0612", "2026-06-12T19:00:00+00:00", "NS", "Not Started", league, leagueName, season)],
            "2026-06-13" => [BuildFixtureJson($"{prefix}0613", "2026-06-13T19:00:00+00:00", "1H", "First Half", league, leagueName, season)],
            "2026-06-14" => [BuildFixtureJson($"{prefix}0614", "2026-06-14T19:00:00+00:00", "FT", "Match Finished", league, leagueName, season)],
            _ => []
        };
    }

    private static List<string> BuildLeagueSeasonResponseItems(int? leagueId)
    {
        if (!leagueId.HasValue)
        {
            return [];
        }

        var name = leagueId.Value switch
        {
            39 => "EPL",
            140 => "La Liga",
            78 => "Bundesliga",
            135 => "Serie A",
            61 => "Ligue 1",
            253 => "MLS",
            2 => "Champions League",
            _ => $"League {leagueId.Value}"
        };
        var country = leagueId.Value switch
        {
            39 => "England",
            140 => "Spain",
            78 => "Germany",
            135 => "Italy",
            61 => "France",
            253 => "USA",
            2 => "Europe",
            _ => "Unknown"
        };
        var seasons = leagueId.Value == 253
            ? $$"""
              { "year": 2026, "start": "2026-02-01", "end": "2026-12-31", "current": true },
              { "year": 2027, "start": "2027-02-01", "end": "2027-12-31", "current": false }
              """
            : leagueId.Value == 2
                ? $$"""
                  { "year": 2025, "start": "2025-07-01", "end": "2026-05-31", "current": false },
                  { "year": 2026, "start": "2026-07-01", "end": "2027-05-31", "current": true }
                  """
                : $$"""
                  { "year": 2025, "start": "2025-08-01", "end": "2026-05-31", "current": false },
                  { "year": 2026, "start": "2026-08-01", "end": "2027-05-31", "current": true }
                  """;

        return
        [
            $$"""
            {
              "league": { "id": {{leagueId.Value}}, "name": "{{name}}", "type": "League" },
              "country": { "name": "{{country}}" },
              "seasons": [{{seasons}}]
            }
            """
        ];
    }

    private static string BuildFixtureJson(
        string fixtureId,
        string kickoffUtc,
        string statusShort,
        string statusLong,
        int leagueId = 1,
        string leagueName = "World Cup",
        int season = 2026)
    {
        return $$"""
        {
          "fixture": { "id": {{fixtureId}}, "date": "{{kickoffUtc}}", "status": { "short": "{{statusShort}}", "long": "{{statusLong}}" } },
          "league": { "id": {{leagueId}}, "name": "{{leagueName}}", "season": {{season}}, "round": "Group Stage" },
          "teams": {
            "home": { "id": 10, "name": "Home FC" },
            "away": { "id": 20, "name": "Away FC" }
          }
        }
        """;
    }

    private static string BuildFixturesUrl(ApiSportsFootballFixturesRequest request)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.FixtureId))
        {
            query.Add($"id={Uri.EscapeDataString(request.FixtureId)}");
        }
        else
        {
            if (request.Date.HasValue)
            {
                query.Add($"date={request.Date.Value:yyyy-MM-dd}");
            }

            if (request.League.HasValue)
            {
                query.Add($"league={request.League.Value}");
            }

            if (request.Season.HasValue)
            {
                query.Add($"season={request.Season.Value}");
            }
        }

        return $"https://v3.football.api-sports.io/fixtures?{string.Join("&", query)}";
    }
}

public sealed record ArtifactCounts(
    int RefreshRuns,
    int TopPickSnapshots,
    int LineShoppingSnapshots,
    int PlannerDecisions);

