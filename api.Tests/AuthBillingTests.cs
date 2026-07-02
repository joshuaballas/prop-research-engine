using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Auth;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Tests;

public sealed class AuthBillingTests
{
    [Fact]
    public async Task PlansPublicList_AllowsAnonymous()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(2, json.GetProperty("plans").GetArrayLength());
    }

    [Fact]
    public async Task ProtectedMe_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedMarketEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/freshness");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedMe_WithInvalidBearer_ReturnsUnauthorized()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-valid-token");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedAccount_WithTestingAuth_ReachesEndpointAfterCustomAuthorizationMiddleware()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        AddTestAuth(client, "account_user", "account@example.test", "Account Test");

        var response = await client.GetAsync("/api/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal("account_user", json.GetProperty("user").GetProperty("externalSubjectId").GetString());
    }

    [Fact]
    public async Task ProtectedMe_WithTestingAuth_UpsertsSingleAppUser()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        AddTestAuth(client, "user_123", "member@example.test", "Member Test");

        var first = await client.GetAsync("/api/me");
        var second = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.AppUsers.SingleAsync();
        Assert.Equal("Clerk", user.ExternalAuthProvider);
        Assert.Equal("user_123", user.ExternalSubjectId);
        Assert.Equal("member@example.test", user.Email);
        Assert.Equal(AppUserRoles.Member, user.Role);
        Assert.Equal(AppUserStatuses.Active, user.Status);
    }

    [Fact]
    public async Task DisabledUser_ReturnsForbiddenFromMemberEndpoints()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalAuthProvider = "Clerk",
                ExternalSubjectId = "disabled_user",
                Email = "disabled@example.test",
                Role = AppUserRoles.Member,
                Status = AppUserStatuses.Disabled,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        AddTestAuth(client, "disabled_user", "disabled@example.test");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberCannotAccessAdminPlans()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        AddTestAuth(client, "member_user", "member@example.test");

        var response = await client.GetAsync("/api/admin/plans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCanCreatePlan()
    {
        await using var factory = CreateAuthFactory(new Dictionary<string, string?>
        {
            ["Auth:BootstrapOwnerExternalSubjectIds:0"] = "owner_user"
        });
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        AddTestAuth(client, "owner_user", "owner@example.test");

        var response = await client.PostAsJsonAsync("/api/admin/plans", new
        {
            slug = "research-plus",
            name = "Research Plus",
            summary = "Expanded research access.",
            description = "Internal setup plan.",
            status = "Active",
            isPublic = true,
            sortOrder = 30,
            monthlyPriceCents = 4900,
            annualPriceCents = 49000,
            currency = "USD",
            trialDays = (int?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await dbContext.Plans.AnyAsync(plan => plan.Slug == "research-plus"));
    }

    [Fact]
    public async Task PaidCheckoutIntent_UsesServerPlanPriceAndDoesNotActivatePaidPlan()
    {
        await using var factory = CreateAuthFactory();
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient();
        AddTestAuth(client, "checkout_user", "checkout@example.test");

        var response = await client.PostAsJsonAsync("/api/checkout/intents", new
        {
            planSlug = "pro",
            billingInterval = "Monthly",
            returnUrl = "http://localhost:5173/app/billing",
            amountCents = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RequiresExternalProcessor", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("paymentProcessorConnected").GetBoolean());
        Assert.Equal("Stripe", json.GetProperty("externalProviderRequired").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.CheckoutIntents.SingleAsync();
        Assert.Equal(2900, intent.AmountCents);
        var state = await dbContext.UserPlanStates.SingleAsync();
        Assert.Equal(UserPlanStatuses.PendingCheckout, state.Status);
    }

    private static SafetyTestApplicationFactory CreateAuthFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Auth:RequireAuthenticationInTesting"] = "true"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                config[key] = value;
            }
        }

        return new SafetyTestApplicationFactory(config);
    }

    private static void AddTestAuth(HttpClient client, string subject, string? email = null, string? name = null)
    {
        client.DefaultRequestHeaders.Add(TestingAuthHandler.SubjectHeader, subject);
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestingAuthHandler.EmailHeader, email);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            client.DefaultRequestHeaders.Add(TestingAuthHandler.NameHeader, name);
        }
    }
}
