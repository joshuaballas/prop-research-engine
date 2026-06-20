using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Utilities;

namespace SportsBetting.Api.Tests;

public sealed class PlayerSearchTests
{
    [Theory]
    [InlineData("Julio Rodríguez", "juliorodriguez")]
    [InlineData("Julio Rodriguez", "juliorodriguez")]
    [InlineData("Yandy Díaz", "yandydiaz")]
    [InlineData("Ranger Suárez", "rangersuarez")]
    public void PlayerNameNormalizer_RemovesDiacriticsPunctuationSpacingAndCase(string value, string expected)
    {
        Assert.Equal(expected, PlayerNameNormalizer.ToCompactKey(value));
    }

    [Fact]
    public async Task PlayerSearch_PartialName_ReturnsPersistedPlayers()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=paul&limit=5");

        await EnsureSuccessWithBodyAsync(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(rows.EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.PaulGoldschmidtId
            && row.GetProperty("displayName").GetString() == "Paul Goldschmidt");
        Assert.All(rows.EnumerateArray(), row => Assert.Contains("paul", row.GetProperty("displayName").GetString()!.ToLowerInvariant()));
    }

    [Fact]
    public async Task PlayerSearch_LimitIsRespected()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=paul&limit=1");

        await EnsureSuccessWithBodyAsync(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(rows.EnumerateArray());
    }

    [Fact]
    public async Task PlayerSearch_MultiTokenName_RanksAllTokenMatchBeforeFirstNameOnlyMatch()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=paul%20skenes&limit=5");

        await EnsureSuccessWithBodyAsync(response);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.Equal("Paul Skenes", rows[0].GetProperty("displayName").GetString());
        Assert.Equal(seeded.PaulSkenesLiveDuplicateId, rows[0].GetProperty("playerId").GetGuid());
        Assert.DoesNotContain(rows, row => row.GetProperty("displayName").GetString() == "Paul Goldschmidt");
    }

    [Fact]
    public async Task PlayerSearch_DedupesIdenticalDisplayNamesWhenPractical()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=paul%20skenes&limit=5");

        await EnsureSuccessWithBodyAsync(response);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(1, rows.Count(row => row.GetProperty("displayName").GetString() == "Paul Skenes"));
    }

    [Fact]
    public async Task PlayerSearch_DedupesAndRanksAccentVariantWithCurrentLines()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAccentVariantPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=julio%20rodriguez&limit=5");

        await EnsureSuccessWithBodyAsync(response);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.Equal(seeded.JulioProviderLinePlayerId, rows[0].GetProperty("playerId").GetGuid());
        Assert.Equal(1, rows.Count(row => PlayerNameNormalizer.ToCompactKey(row.GetProperty("displayName").GetString()) == "juliorodriguez"));
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_UsesAliasEquivalentPlayerId()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.PaulSkenesLiveDuplicateId, payload.GetProperty("effectivePlayerId").GetGuid());
        var lines = payload.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Single(lines);
        Assert.Equal("Paul Skenes", lines[0].GetProperty("player").GetString());
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_UsesExactNameLineOwnerWhenUnambiguous()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulAlias: false);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.PaulSkenesLiveDuplicateId, payload.GetProperty("effectivePlayerId").GetGuid());
        Assert.Equal("ExactNormalizedNameSingleLineOwner", payload.GetProperty("audit").GetProperty("resolutionMode").GetString());
        var lines = payload.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Single(lines);
        Assert.Equal("Paul Skenes", lines[0].GetProperty("player").GetString());
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_UsesActivePlatformPropsWhenSnapshotsAreMissing()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulSnapshot: false, includePaulPlatformProp: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.PaulSkenesId, payload.GetProperty("effectivePlayerId").GetGuid());
        var lines = payload.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Single(lines);
        Assert.Equal("Paul Skenes", lines[0].GetProperty("player").GetString());
        Assert.Equal("Underdog", lines[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_DoesNotReturnPlaceholderTeamTextWhenMatchupUnavailable()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulSnapshot: false, includePaulPlatformProp: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = payload.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, line.GetProperty("awayTeam").ValueKind);
        Assert.Equal(JsonValueKind.Null, line.GetProperty("homeTeam").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, line.GetProperty("eventStartTimeUtc").ValueKind);
        Assert.DoesNotContain("Current provider line", line.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_PreservesRealPlatformPropMatchupWhenAvailable()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulSnapshot: false,
            includePaulPlatformProp: true,
            includePaulPlatformPropGame: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var line = payload.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("Pittsburgh Pirates", line.GetProperty("awayTeam").GetString());
        Assert.Equal("New York Yankees", line.GetProperty("homeTeam").GetString());
        Assert.NotEqual(JsonValueKind.Null, line.GetProperty("eventStartTimeUtc").ValueKind);
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_DedupesDuplicatePlatformPropsByLogicalLine()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulSnapshot: false,
            includePaulPlatformProp: true,
            includeDuplicatePaulPlatformProps: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=50");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lines = payload.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Single(lines);
        Assert.Equal(3, payload.GetProperty("audit").GetProperty("rawCurrentProviderLineCount").GetInt32());
        Assert.Equal(2, payload.GetProperty("audit").GetProperty("duplicateCurrentProviderLineCount").GetInt32());
        Assert.Equal("Underdog", lines[0].GetProperty("provider").GetString());
        Assert.Equal("Pitcher Strikeouts", lines[0].GetProperty("normalizedPropType").GetString());
        Assert.Equal(6.5m, lines[0].GetProperty("line").GetDecimal());
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_DedupesAliasEquivalentSnapshotsByIdentityGroup()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulSnapshot: false, includeAliasEquivalentDuplicateSnapshots: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.PaulSkenesId}&take=50");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lines = payload.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal(4, payload.GetProperty("audit").GetProperty("rawCurrentProviderLineCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("audit").GetProperty("duplicateCurrentProviderLineCount").GetInt32());
        Assert.Single(lines, line =>
            line.GetProperty("eventId").GetString() == "alias-duplicate-event"
            && line.GetProperty("provider").GetString() == "PrizePicks"
            && line.GetProperty("normalizedPropType").GetString() == "Total Bases"
            && line.GetProperty("bestSide").GetString() == "Under"
            && line.GetProperty("line").GetDecimal() == 1.5m);
        Assert.Contains(lines, line => line.GetProperty("bestSide").GetString() == "Over");
        Assert.Contains(lines, line => line.GetProperty("eventId").GetString() == "alias-second-event");
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_InvalidPlayerIdReturnsBadRequest()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/player-research/current-lines?playerId=not-a-guid&take=50");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A valid playerId is required.", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PropHistory_LoadsPitcherStrikeoutsForPlatformPropBackedCurrentLine()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulSnapshot: false,
            includePaulPlatformProp: true,
            includePaulPitcherStrikeoutResultsOnSelected: true);

        var response = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.PaulSkenesId}&playerName=Paul%20Skenes&propType=Pitcher%20Strikeouts&side=Over&line=6.5&take=10");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, payload.GetProperty("games").EnumerateArray().Count());
        Assert.Equal(2, payload.GetProperty("summary").GetProperty("cleared").GetInt32());
        Assert.Equal(1, payload.GetProperty("summary").GetProperty("missed").GetInt32());
        Assert.Equal("PITCHER_STRIKEOUTS", payload.GetProperty("diagnostics").GetProperty("propTypeResolved").GetString());
    }

    [Fact]
    public async Task PropHistory_LoadsBatterWalksForPersistedWalkRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includeMookieBatterWalkResults: true);

        var response = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.MookieBettsId}&playerName=Mookie%20Betts&propType=Batter%20Walks&side=Over&line=0.5&take=10");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, payload.GetProperty("games").EnumerateArray().Count());
        Assert.Equal("WALKS", payload.GetProperty("diagnostics").GetProperty("propTypeResolved").GetString());
    }

    [Fact]
    public async Task PropHistory_LoadsBatterStrikeoutsWithoutPitcherStrikeoutRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includeMookieBatterStrikeoutResults: true,
            includeMookiePitcherStrikeoutTrapResults: true);

        var response = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.MookieBettsId}&playerName=Mookie%20Betts&propType=Batter%20Strikeouts&side=Over&line=0.5&take=10");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var games = payload.GetProperty("games").EnumerateArray().ToArray();
        Assert.Equal(2, games.Length);
        Assert.All(games, game => Assert.NotEqual(9m, game.GetProperty("value").GetDecimal()));
        Assert.Equal("STRIKEOUTS", payload.GetProperty("diagnostics").GetProperty("propTypeResolved").GetString());
    }

    [Fact]
    public async Task PropHistory_UsesSideAndLineForOutcomeNotResultLookup()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulPitcherStrikeoutResultsOnSelected: true);

        var highLineResponse = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.PaulSkenesId}&playerName=Paul%20Skenes&propType=PITCHER_STRIKEOUTS&side=Over&line=9.5&take=10");
        var lowLineResponse = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.PaulSkenesId}&playerName=Paul%20Skenes&propType=PITCHER_STRIKEOUTS&side=Over&line=4.5&take=10");

        await EnsureSuccessWithBodyAsync(highLineResponse);
        await EnsureSuccessWithBodyAsync(lowLineResponse);
        var highLinePayload = await highLineResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lowLinePayload = await lowLineResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, highLinePayload.GetProperty("games").EnumerateArray().Count());
        Assert.Equal(3, lowLinePayload.GetProperty("games").EnumerateArray().Count());
        Assert.Equal(0, highLinePayload.GetProperty("summary").GetProperty("cleared").GetInt32());
        Assert.Equal(3, lowLinePayload.GetProperty("summary").GetProperty("cleared").GetInt32());
    }

    [Fact]
    public async Task PropHistory_UsesExactNameHistoryOwnerWhenSelectedDuplicateHasNoHistory()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulAlias: false,
            includePaulSnapshot: false,
            includePaulPitcherStrikeoutResultsOnLiveDuplicate: true);

        var response = await client.GetAsync($"/api/markets/prop-history?sport=MLB&playerId={seeded.PaulSkenesId}&playerName=Paul%20Skenes&propType=Pitcher%20Strikeouts&side=Over&line=6.5&take=10");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(payload.GetProperty("playerId").GetGuid(), new[] { seeded.PaulSkenesId, seeded.PaulSkenesLiveDuplicateId });
        Assert.Equal(3, payload.GetProperty("games").EnumerateArray().Count());
        Assert.True(payload.GetProperty("diagnostics").GetProperty("equivalentPlayerCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task MlbPlayerResultCoverage_ReportsMlbExternalRefAndPitcherStrikeoutRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulMlbRefOnSelected: true,
            includePaulPitcherStrikeoutResultsOnSelected: true);

        var response = await client.GetAsync("/api/debug/mlb/player-result-coverage?q=paul%20skenes");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PitcherStrikeoutResultsAvailableForMatchedIdentity", payload.GetProperty("rootCause").GetString());
        Assert.Contains(payload.GetProperty("knownMlbIds").EnumerateArray(), id => id.GetString() == "694973");
        Assert.True(payload.GetProperty("propResultsCounts").GetProperty("pitcherStrikeoutsForMatchedIdentities").GetInt32() >= 3);
        Assert.Contains(payload.GetProperty("latestPitcherStrikeoutRows").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.PaulSkenesId);
    }

    [Fact]
    public async Task MlbPlayerResultCoverage_ReportsMissingMlbExternalRef()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/debug/mlb/player-result-coverage?q=paul%20skenes");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingMlbExternalRefForMatchedPlayerRows", payload.GetProperty("rootCause").GetString());
        Assert.Empty(payload.GetProperty("knownMlbIds").EnumerateArray());
        Assert.Equal(0, payload.GetProperty("propResultsCounts").GetProperty("pitcherStrikeoutsForMatchedIdentities").GetInt32());
    }

    [Fact]
    public async Task MlbPlayerResultCoverage_ReportsResultsUnderOtherExactNameMlbIdentity()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulAlias: false,
            includePaulMlbRefOnLiveDuplicate: true,
            includePaulPitcherStrikeoutResultsOnLiveDuplicate: true);

        var response = await client.GetAsync("/api/debug/mlb/player-result-coverage?q=paul%20skenes");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PitcherStrikeoutResultsAvailableForMatchedIdentity", payload.GetProperty("rootCause").GetString());
        Assert.Contains(payload.GetProperty("matchingPlayers").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.PaulSkenesLiveDuplicateId);
        Assert.Contains(payload.GetProperty("latestPitcherStrikeoutRows").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.PaulSkenesLiveDuplicateId);
    }

    [Fact]
    public async Task MlbPlayerMappingApprove_CreatesMissingMlbRefForPlayer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory);

        var response = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/approve", new
        {
            playerId = seeded.PaulSkenesId,
            mlbPlayerId = "694973",
            displayName = "Paul Skenes"
        });

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("created").GetBoolean());
        Assert.Equal("mlb", payload.GetProperty("mapping").GetProperty("provider").GetString());
        Assert.Equal("694973", payload.GetProperty("mapping").GetProperty("providerPlayerId").GetString());
    }

    [Fact]
    public async Task MlbPlayerMappingApprove_IsIdempotentWhenMappingAlreadyExists()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulMlbRefOnSelected: true);

        var response = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/approve", new
        {
            playerId = seeded.PaulSkenesId,
            mlbPlayerId = "694973",
            displayName = "Paul Skenes"
        });

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.GetProperty("created").GetBoolean());
        Assert.Equal("694973", payload.GetProperty("mapping").GetProperty("providerPlayerId").GetString());
    }

    [Fact]
    public async Task MlbPlayerMappingApprove_RefusesMlbIdMappedToAnotherPlayer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulMlbRefOnLiveDuplicate: true);

        var response = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/approve", new
        {
            playerId = seeded.PaulSkenesId,
            mlbPlayerId = "694973",
            displayName = "Paul Skenes"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MLB player is already mapped to a different canonical player.", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MlbPlayerMappingApprove_RefusesDifferentMlbIdWhenPlayerAlreadyMapped()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includePaulMlbRefOnSelected: true);

        var response = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/approve", new
        {
            playerId = seeded.PaulSkenesId,
            mlbPlayerId = "999999",
            displayName = "Paul Skenes"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Canonical player already has a different MLB player mapping. This endpoint does not replace mappings.", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PropHistory_LoadsAfterMlbRefExistsAndResultsAreImportedForMappedPlayer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(
            factory,
            includePaulPlatformProp: true,
            includePaulMlbRefOnSelected: true,
            includePaulPitcherStrikeoutResultsOnSelected: true);

        var coverageResponse = await client.GetAsync("/api/debug/mlb/player-result-coverage?q=paul%20skenes");
        await EnsureSuccessWithBodyAsync(coverageResponse);
        var coverage = await coverageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PitcherStrikeoutResultsAvailableForMatchedIdentity", coverage.GetProperty("rootCause").GetString());

        var response = await client.GetAsync($"/api/debug/player-research/history-audit?playerId={seeded.PaulSkenesId}&propType=Pitcher%20Strikeouts&side=Over&line=6.5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, payload.GetProperty("latestMatchingRows").EnumerateArray().Count());
        Assert.Equal(3, payload.GetProperty("propResultsCounts").GetProperty("sameEffectiveEquivalentIdsAndStatKey").GetInt32());
    }

    [Fact]
    public async Task ProviderLineRepairCandidates_ReportAccentInsensitiveDuplicateMlbIdentityConflict()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAccentVariantPlayersAsync(factory);

        var response = await client.GetAsync("/api/debug/mlb-player-mappings/provider-line-repair-candidates");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var candidate = payload.GetProperty("candidates").EnumerateArray()
            .Single(row => row.GetProperty("playerId").GetGuid() == seeded.JulioProviderLinePlayerId);
        Assert.Equal("JULIORODRIGUEZ", candidate.GetProperty("accentInsensitiveNormalizedName").GetString());
        Assert.False(candidate.GetProperty("isSafeAutoApproval").GetBoolean());
        Assert.Contains("already mapped", candidate.GetProperty("conflictReason").GetString());
        Assert.Contains(candidate.GetProperty("candidateMlbIdentities").EnumerateArray(), row =>
            row.GetProperty("displayName").GetString() == "Julio Rodríguez");
    }

    [Fact]
    public async Task ProviderLineRepairAutoApprove_DryRunDefaultsTrueAndCanCreateUnclaimedLegacyMlbRef()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAccentVariantPlayersAsync(factory, includeLegacyMlbIdOnProviderLinePlayer: true, includeAccentedMlbRef: false);

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/provider-line-repair/auto-approve", new
        {
            approvedBy = "test",
            reason = "safe local repair"
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunPayload = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunPayload.GetProperty("dryRun").GetBoolean());
        Assert.Contains(dryRunPayload.GetProperty("rows").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.JulioProviderLinePlayerId
            && row.GetProperty("action").GetString() == "WouldCreate");

        var executeResponse = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/provider-line-repair/auto-approve", new
        {
            dryRun = false,
            approvedBy = "test",
            reason = "safe local repair"
        });

        await EnsureSuccessWithBodyAsync(executeResponse);
        var executePayload = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(executePayload.GetProperty("rows").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.JulioProviderLinePlayerId
            && row.GetProperty("action").GetString() == "Created");

        var mappingResponse = await client.GetAsync($"/api/debug/player-mappings/{seeded.JulioProviderLinePlayerId}");
        await EnsureSuccessWithBodyAsync(mappingResponse);
        var mappingPayload = await mappingResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(mappingPayload.GetProperty("results").EnumerateArray(), row =>
            row.GetProperty("provider").GetString() == "mlb"
            && row.GetProperty("providerPlayerId").GetString() == "677594");
    }

    [Fact]
    public async Task ProviderLineRepairAutoApprove_DoesNotApproveAmbiguousAccentCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAccentVariantPlayersAsync(factory, includeSecondAccentedDuplicate: true);

        var response = await client.PostAsJsonAsync("/api/debug/mlb-player-mappings/provider-line-repair/auto-approve", new
        {
            dryRun = false,
            approvedBy = "test",
            reason = "should not approve"
        });

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(payload.GetProperty("rows").EnumerateArray(), row =>
            row.GetProperty("playerId").GetGuid() == seeded.JulioProviderLinePlayerId
            && row.GetProperty("action").GetString() == "Created");
    }

    [Fact]
    public async Task PlayerResearchCurrentLines_DoesNotCombineAmbiguousSameNameLineOwnersWithoutAlias()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedPlayersAsync(factory, includeAmbiguousSameNameRows: true);

        var response = await client.GetAsync($"/api/player-research/current-lines?playerId={seeded.AmbiguousNoLinePlayerId}&take=5");

        await EnsureSuccessWithBodyAsync(response);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.AmbiguousNoLinePlayerId, payload.GetProperty("effectivePlayerId").GetGuid());
        Assert.Equal("AmbiguousExactNormalizedNameLineOwners", payload.GetProperty("audit").GetProperty("resolutionMode").GetString());
        Assert.Empty(payload.GetProperty("lines").EnumerateArray());
    }

    [Fact]
    public async Task PlayerSearch_LimitIsCappedAtTen()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPlayersAsync(factory, extraPaulCount: 14);

        var response = await client.GetAsync("/api/players/search?q=paul&limit=50");

        await EnsureSuccessWithBodyAsync(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, rows.EnumerateArray().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public async Task PlayerSearch_EmptyOrShortQuery_ReturnsEmptySafely(string query)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPlayersAsync(factory);

        var response = await client.GetAsync($"/api/players/search?q={Uri.EscapeDataString(query)}&limit=50");

        await EnsureSuccessWithBodyAsync(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(rows.EnumerateArray());
    }

    [Fact]
    public async Task PlayerSearch_DoesNotCallExternalProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedPlayersAsync(factory);

        var response = await client.GetAsync("/api/players/search?q=mookie&limit=5");

        await EnsureSuccessWithBodyAsync(response);
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, factory.UnderdogAdapter.CallCount);
    }

    private static async Task<SeededPlayers> SeedPlayersAsync(
        SafetyTestApplicationFactory factory,
        int extraPaulCount = 0,
        bool includeAmbiguousSameNameRows = false,
        bool includePaulAlias = true,
        bool includePaulSnapshot = true,
        bool includePaulPlatformProp = false,
        bool includeDuplicatePaulPlatformProps = false,
        bool includePaulPlatformPropGame = false,
        bool includeAliasEquivalentDuplicateSnapshots = false,
        bool includePaulPitcherStrikeoutResultsOnSelected = false,
        bool includePaulPitcherStrikeoutResultsOnLiveDuplicate = false,
        bool includePaulMlbRefOnSelected = false,
        bool includePaulMlbRefOnLiveDuplicate = false,
        bool includeMookieBatterWalkResults = false,
        bool includeMookieBatterStrikeoutResults = false,
        bool includeMookiePitcherStrikeoutTrapResults = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Major League Baseball" };
        var league = new League { Id = Guid.NewGuid(), Code = "MLB", Name = "MLB", SportId = sport.Id, Sport = sport };
        var liveLeague = new League { Id = Guid.NewGuid(), Code = "MLBLIVE", Name = "MLB Live", SportId = sport.Id, Sport = sport };
        var team = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, League = league, Name = "New York Yankees", Abbreviation = "NYY" };
        var pirates = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, League = league, Name = "Pittsburgh Pirates", Abbreviation = "PIT" };
        var propType = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Sport = sport, Name = "Total Bases", StatKey = "TOTAL_BASES" };
        var pitcherStrikeouts = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Sport = sport, Name = "Pitcher Strikeouts", StatKey = "PITCHER_STRIKEOUTS" };
        var batterStrikeouts = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Sport = sport, Name = "Strikeouts", StatKey = "STRIKEOUTS" };
        var batterWalks = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Sport = sport, Name = "Walks", StatKey = "WALKS" };
        var paulGoldschmidt = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Paul Goldschmidt",
            FirstName = "Paul",
            LastName = "Goldschmidt",
            LeagueId = league.Id,
            League = league,
            TeamId = team.Id,
            Team = team,
            Position = "1B"
        };
        var mookieBetts = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Mookie Betts",
            FirstName = "Mookie",
            LastName = "Betts",
            LeagueId = league.Id,
            League = league,
            TeamId = team.Id,
            Team = team,
            Position = "OF"
        };
        var paulSkenes = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Paul Skenes",
            FirstName = "Paul",
            LastName = "Skenes",
            LeagueId = league.Id,
            League = league,
            TeamId = pirates.Id,
            Team = pirates,
            Position = "P"
        };
        var paulSkenesLiveDuplicate = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Paul Skenes",
            FirstName = "Paul",
            LastName = "Skenes",
            LeagueId = liveLeague.Id,
            League = liveLeague,
            Position = "P"
        };
        var ambiguousNoLinePlayer = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Jordan Smith",
            FirstName = "Jordan",
            LastName = "Smith",
            LeagueId = league.Id,
            League = league,
            Position = "OF"
        };
        var ambiguousLinePlayer = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Jordan Smith",
            FirstName = "Jordan",
            LastName = "Smith",
            LeagueId = liveLeague.Id,
            League = liveLeague,
            Position = "P"
        };
        var secondAmbiguousLinePlayer = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Jordan Smith",
            FirstName = "Jordan",
            LastName = "Smith",
            LeagueId = liveLeague.Id,
            League = liveLeague,
            Position = "P"
        };

        var players = new List<Player> { paulGoldschmidt, mookieBetts, paulSkenes, paulSkenesLiveDuplicate };
        if (includeAmbiguousSameNameRows)
        {
            players.AddRange([ambiguousNoLinePlayer, ambiguousLinePlayer, secondAmbiguousLinePlayer]);
        }
        for (var index = 0; index < extraPaulCount; index++)
        {
            players.Add(new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = $"Paul Test {index:00}",
                LeagueId = league.Id,
                League = league
            });
        }

        dbContext.Sports.Add(sport);
        dbContext.Leagues.AddRange(league, liveLeague);
        dbContext.Teams.AddRange(team, pirates);
        dbContext.PropTypes.AddRange(propType, pitcherStrikeouts, batterStrikeouts, batterWalks);
        dbContext.Players.AddRange(players);
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Succeeded = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS"
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.Props.Add(new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = paulGoldschmidt.Id,
            Player = paulGoldschmidt,
            LeagueId = league.Id,
            League = league,
            PropTypeId = propType.Id,
            PropType = propType,
            NormalizedKey = "paul-goldschmidt-total-bases",
            StartsAt = DateTimeOffset.UtcNow
        });
        var paulSkenesProp = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = paulSkenes.Id,
            Player = paulSkenes,
            LeagueId = league.Id,
            League = league,
            PropTypeId = pitcherStrikeouts.Id,
            PropType = pitcherStrikeouts,
            NormalizedKey = "paul-skenes-pitcher-strikeouts",
            StartsAt = DateTimeOffset.UtcNow
        };
        if (includePaulPlatformPropGame)
        {
            var game = new Game
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                League = league,
                HomeTeamId = team.Id,
                HomeTeam = team,
                AwayTeamId = pirates.Id,
                AwayTeam = pirates,
                ScheduledStartAt = DateTimeOffset.UtcNow.AddHours(4),
                Status = "scheduled"
            };
            dbContext.Games.Add(game);
            paulSkenesProp.GameId = game.Id;
            paulSkenesProp.Game = game;
            paulSkenesProp.StartsAt = game.ScheduledStartAt;
        }
        dbContext.Props.Add(paulSkenesProp);
        if (includePaulPlatformProp)
        {
            var platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = "Underdog",
                Slug = "underdog",
                IsActive = true
            };
            dbContext.Platforms.Add(platform);
            var platformPropRows = new List<PlatformProp>
            {
                BuildPlatformProp(platform, paulSkenesProp, $"underdog:{paulSkenes.Id:N}:pitcher-strikeouts:6.5", DateTimeOffset.UtcNow)
            };
            if (includeDuplicatePaulPlatformProps)
            {
                platformPropRows.Add(BuildPlatformProp(platform, paulSkenesProp, $"underdog:{paulSkenes.Id:N}:pitcher-strikeouts:6.5:duplicate-1", DateTimeOffset.UtcNow.AddMinutes(-1)));
                platformPropRows.Add(BuildPlatformProp(platform, paulSkenesProp, $"underdog:{paulSkenes.Id:N}:pitcher-strikeouts:6.5:duplicate-2", DateTimeOffset.UtcNow.AddMinutes(-2)));
            }

            dbContext.PlatformProps.AddRange(platformPropRows);
        }
        if (includePaulAlias)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = paulSkenes.Id,
                Player = paulSkenes,
                Provider = "mlb-alias",
                ProviderPlayerId = $"player:{paulSkenes.Id:N}:target:{paulSkenesLiveDuplicate.Id:N}",
                ProviderDisplayName = "Paul Skenes",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        if (includePaulMlbRefOnSelected)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = paulSkenes.Id,
                Player = paulSkenes,
                Provider = "mlb",
                ProviderPlayerId = "694973",
                ProviderDisplayName = "Paul Skenes",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        if (includePaulMlbRefOnLiveDuplicate)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = paulSkenesLiveDuplicate.Id,
                Player = paulSkenesLiveDuplicate,
                Provider = "mlb",
                ProviderPlayerId = "694973",
                ProviderDisplayName = "Paul Skenes",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        if (includePaulSnapshot)
        {
            dbContext.MarketLineShoppingSnapshots.Add(BuildSnapshot(refreshRun.Id, paulSkenesLiveDuplicate.Id, "Paul Skenes", "Pitcher Strikeouts", "Under", 6.5m));
        }
        if (includeAliasEquivalentDuplicateSnapshots)
        {
            var duplicateStart = DateTimeOffset.UtcNow.AddHours(4);
            dbContext.MarketLineShoppingSnapshots.AddRange(
                BuildSnapshot(refreshRun.Id, paulSkenes.Id, "Paul Skenes", "Total Bases", "Under", 1.5m, "alias-duplicate-event", duplicateStart),
                BuildSnapshot(refreshRun.Id, paulSkenesLiveDuplicate.Id, "Paul Skenes", "Total Bases", "Under", 1.5m, "alias-duplicate-event", duplicateStart),
                BuildSnapshot(refreshRun.Id, paulSkenesLiveDuplicate.Id, "Paul Skenes", "Total Bases", "Over", 1.5m, "alias-duplicate-event", duplicateStart),
                BuildSnapshot(refreshRun.Id, paulSkenesLiveDuplicate.Id, "Paul Skenes", "Total Bases", "Under", 1.5m, "alias-second-event", duplicateStart.AddDays(1)));
        }
        if (includePaulPitcherStrikeoutResultsOnSelected)
        {
            AddPitcherStrikeoutResults(dbContext, paulSkenes.Id, league.Id, pitcherStrikeouts.Id);
        }
        if (includePaulPitcherStrikeoutResultsOnLiveDuplicate)
        {
            AddPitcherStrikeoutResults(dbContext, paulSkenesLiveDuplicate.Id, league.Id, pitcherStrikeouts.Id);
        }
        if (includeMookieBatterWalkResults)
        {
            AddStatResults(dbContext, mookieBetts.Id, league.Id, batterWalks.Id, [0m, 1m, 2m], "mookie-walks");
        }
        if (includeMookieBatterStrikeoutResults)
        {
            AddStatResults(dbContext, mookieBetts.Id, league.Id, batterStrikeouts.Id, [1m, 2m], "mookie-batter-strikeouts");
        }
        if (includeMookiePitcherStrikeoutTrapResults)
        {
            AddStatResults(dbContext, mookieBetts.Id, league.Id, pitcherStrikeouts.Id, [9m], "mookie-pitcher-strikeout-trap");
        }
        if (includeAmbiguousSameNameRows)
        {
            dbContext.MarketLineShoppingSnapshots.Add(BuildSnapshot(refreshRun.Id, ambiguousLinePlayer.Id, "Jordan Smith", "Total Bases", "Over", 1.5m));
            dbContext.MarketLineShoppingSnapshots.Add(BuildSnapshot(refreshRun.Id, secondAmbiguousLinePlayer.Id, "Jordan Smith", "Total Bases", "Over", 2.5m));
        }
        await dbContext.SaveChangesAsync();

        return new SeededPlayers(
            paulGoldschmidt.Id,
            mookieBetts.Id,
            paulSkenes.Id,
            paulSkenesLiveDuplicate.Id,
            ambiguousNoLinePlayer.Id,
            ambiguousLinePlayer.Id);
    }

    private static PlatformProp BuildPlatformProp(Platform platform, Prop prop, string platformPropId, DateTimeOffset observedAt)
    {
        return new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            Prop = prop,
            PlatformId = platform.Id,
            Platform = platform,
            PlatformPropId = platformPropId,
            Label = $"{prop.Player?.DisplayName ?? "Player"} {prop.PropType?.Name ?? "Prop"}",
            CurrentLine = 6.5m,
            OverAvailable = true,
            UnderAvailable = true,
            Status = "active",
            ObservedAt = observedAt
        };
    }

    private static MarketLineShoppingSnapshot BuildSnapshot(
        Guid refreshRunId,
        Guid playerId,
        string playerName,
        string propType,
        string side,
        decimal line,
        string? eventId = null,
        DateTimeOffset? eventStartTime = null)
    {
        return new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRunId,
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "dfsOnly",
            ProviderCategory = "DFS",
            Provider = "PrizePicks",
            Bookmaker = "PrizePicks",
            Player = playerName,
            EventId = eventId ?? Guid.NewGuid().ToString("N"),
            EventName = "Pittsburgh Pirates @ Chicago Cubs",
            EventStartTime = eventStartTime ?? DateTimeOffset.UtcNow.AddHours(4),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = propType,
            Line = line,
            BestSide = side,
            BestBookmaker = "PrizePicks",
            BestProbability = 0.62m,
            BestImpliedProbability = 0.5m,
            BestProbabilityEdge = 0.12m,
            BestEstimatedEv = 0.24m,
            StrategyClassification = "Candidate",
            StrategyTier = "Conservative",
            IsCandidate = true,
            EntryProvider = "PrizePicks",
            EntryCompatibilityGroup = "PrizePicks",
            EntryProviderCategory = "DFS",
            EntryActionableForUser = true,
            DetailsJson = JsonSerializer.SerializeToDocument(new { playerId, canonicalPlayerId = playerId })
        };
    }

    private static void AddPitcherStrikeoutResults(ApplicationDbContext dbContext, Guid playerId, Guid leagueId, Guid propTypeId)
    {
        AddStatResults(dbContext, playerId, leagueId, propTypeId, [5m, 7m, 8m], "pitcher-strikeouts");
    }

    private static void AddStatResults(ApplicationDbContext dbContext, Guid playerId, Guid leagueId, Guid propTypeId, decimal[] values, string sourcePrefix)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        for (var index = 0; index < values.Length; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                LeagueId = leagueId,
                PropTypeId = propTypeId,
                EventDate = today.AddDays(-values.Length + index),
                ActualResultValue = values[index],
                ExternalSource = "mlb",
                ExternalSourceIdentifier = $"{sourcePrefix}-game-{index + 1}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static async Task<SeededAccentVariantPlayers> SeedAccentVariantPlayersAsync(
        SafetyTestApplicationFactory factory,
        bool includeLegacyMlbIdOnProviderLinePlayer = false,
        bool includeAccentedMlbRef = true,
        bool includeSecondAccentedDuplicate = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Major League Baseball" };
        var league = new League { Id = Guid.NewGuid(), Code = "MLB", Name = "MLB", SportId = sport.Id, Sport = sport };
        var liveLeague = new League { Id = Guid.NewGuid(), Code = "MLBLIVE", Name = "MLB Live", SportId = sport.Id, Sport = sport };
        var mariners = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, League = league, Name = "Seattle Mariners", Abbreviation = "SEA" };
        var propType = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Sport = sport, Name = "Total Bases", StatKey = "TOTAL_BASES" };
        var providerLinePlayer = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Julio Rodriguez",
            FirstName = "Julio",
            LastName = "Rodriguez",
            LeagueId = league.Id,
            League = league,
            TeamId = mariners.Id,
            Team = mariners,
            Position = "OF",
            ExternalRefs = includeLegacyMlbIdOnProviderLinePlayer
                ? JsonDocument.Parse("{\"mlb\":{\"player_id\":\"677594\"}}")
                : null
        };
        var accentedMlbPlayer = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Julio Rodríguez",
            FirstName = "Julio",
            LastName = "Rodríguez",
            LeagueId = league.Id,
            League = league,
            TeamId = mariners.Id,
            Team = mariners,
            Position = "OF"
        };
        var secondAccentedDuplicate = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Julio Rodríguez",
            FirstName = "Julio",
            LastName = "Rodríguez",
            LeagueId = liveLeague.Id,
            League = liveLeague,
            Position = "Center Field"
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.AddRange(league, liveLeague);
        dbContext.Teams.Add(mariners);
        dbContext.PropTypes.Add(propType);
        dbContext.Players.AddRange(providerLinePlayer, accentedMlbPlayer);
        if (includeSecondAccentedDuplicate)
        {
            dbContext.Players.Add(secondAccentedDuplicate);
        }

        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "PrizePicks",
            Slug = "prizepicks",
            IsActive = true
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = providerLinePlayer.Id,
            Player = providerLinePlayer,
            LeagueId = league.Id,
            League = league,
            PropTypeId = propType.Id,
            PropType = propType,
            NormalizedKey = "julio-rodriguez-total-bases",
            StartsAt = DateTimeOffset.UtcNow.AddHours(4)
        };
        dbContext.Platforms.Add(platform);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            Prop = prop,
            PlatformId = platform.Id,
            Platform = platform,
            PlatformPropId = $"prizepicks:{providerLinePlayer.Id:N}:total-bases:1.5",
            Label = "Julio Rodriguez Total Bases",
            CurrentLine = 1.5m,
            OverAvailable = true,
            UnderAvailable = true,
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow
        });
        if (includeAccentedMlbRef)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = accentedMlbPlayer.Id,
                Player = accentedMlbPlayer,
                Provider = "mlb",
                ProviderPlayerId = "677594",
                ProviderDisplayName = "Julio Rodríguez",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (includeSecondAccentedDuplicate)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = secondAccentedDuplicate.Id,
                Player = secondAccentedDuplicate,
                Provider = "mlb",
                ProviderPlayerId = "677595",
                ProviderDisplayName = "Julio Rodríguez",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = accentedMlbPlayer.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)),
            ActualResultValue = 2m,
            ExternalSource = "mlb",
            ExternalSourceIdentifier = $"mlb:{DateTime.UtcNow:yyyyMMdd}:{accentedMlbPlayer.Id:N}:{propType.Id:N}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        return new SeededAccentVariantPlayers(providerLinePlayer.Id, accentedMlbPlayer.Id, secondAccentedDuplicate.Id);
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected success status code but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private sealed record SeededPlayers(
        Guid PaulGoldschmidtId,
        Guid MookieBettsId,
        Guid PaulSkenesId,
        Guid PaulSkenesLiveDuplicateId,
        Guid AmbiguousNoLinePlayerId,
        Guid AmbiguousLinePlayerId);

    private sealed record SeededAccentVariantPlayers(
        Guid JulioProviderLinePlayerId,
        Guid JulioAccentedMlbPlayerId,
        Guid JulioSecondAccentedDuplicateId);
}
