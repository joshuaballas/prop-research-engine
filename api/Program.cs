using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Configuration;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Ingestion.Players;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Integrations.Underdog;
using SportsBetting.Api.Predictions;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddCommandLine(args)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .SetIsOriginAllowed(IsAllowedFrontendDevOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.Configure<DebugAdminOptions>(builder.Configuration.GetSection(DebugAdminOptions.SectionName));
builder.Services.Configure<UnderdogIngestionOptions>(builder.Configuration.GetSection(UnderdogIngestionOptions.SectionName));
builder.Services.Configure<UnderdogOptions>(builder.Configuration.GetSection(UnderdogOptions.SectionName));
builder.Services.Configure<MlbResultsOptions>(builder.Configuration.GetSection(MlbResultsOptions.SectionName));
builder.Services.Configure<ApiSportsOptions>(builder.Configuration.GetSection(ApiSportsOptions.SectionName));
builder.Services.Configure<TheOddsApiOptions>(builder.Configuration.GetSection(TheOddsApiOptions.SectionName));
builder.Services.Configure<TheOddsIngestionWorkerOptions>(builder.Configuration.GetSection(TheOddsIngestionWorkerOptions.SectionName));
builder.Services.Configure<TheOddsScheduledRefreshOptions>(builder.Configuration.GetSection(TheOddsScheduledRefreshOptions.SectionName));
builder.Services.Configure<TheOddsRefreshPlannerOptions>(builder.Configuration.GetSection(TheOddsRefreshPlannerOptions.SectionName));
builder.Services.Configure<ProviderLineLifecycleOptions>(builder.Configuration.GetSection(ProviderLineLifecycleOptions.SectionName));
builder.Services.Configure<MlbResultImportWorkerOptions>(builder.Configuration.GetSection(MlbResultImportWorkerOptions.SectionName));
builder.Services.Configure<SoccerResultImportWorkerOptions>(builder.Configuration.GetSection(SoccerResultImportWorkerOptions.SectionName));
builder.Services.Configure<SoccerIdentityResolutionOptions>(builder.Configuration.GetSection(SoccerIdentityResolutionOptions.SectionName));
builder.Services.Configure<SoccerPredictionsOptions>(builder.Configuration.GetSection(SoccerPredictionsOptions.SectionName));
builder.Services.Configure<SoccerGradingOptions>(builder.Configuration.GetSection(SoccerGradingOptions.SectionName));
builder.Services.Configure<SoccerTopPicksOptions>(builder.Configuration.GetSection(SoccerTopPicksOptions.SectionName));
builder.Services.Configure<TopPicksModelOptions>(builder.Configuration.GetSection(TopPicksModelOptions.SectionName));
builder.Services.Configure<PredictionGenerationOptions>(builder.Configuration.GetSection(PredictionGenerationOptions.LegacySectionName));
builder.Services.Configure<PredictionGenerationOptions>(builder.Configuration.GetSection(PredictionGenerationOptions.SectionName));
builder.Services.AddSingleton<AppConfigService>();
builder.Services.AddSingleton<IAppConfigService>(services => services.GetRequiredService<AppConfigService>());
builder.Services.AddSingleton<IAppConfigAuditService>(services => services.GetRequiredService<AppConfigService>());
builder.Services.AddSingleton<IConfigureOptions<ApiSportsOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<TheOddsApiOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<TheOddsIngestionWorkerOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<TheOddsScheduledRefreshOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<TheOddsRefreshPlannerOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<ProviderLineLifecycleOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<MlbResultImportWorkerOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<SoccerResultImportWorkerOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<SoccerIdentityResolutionOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<SoccerPredictionsOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<SoccerGradingOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<SoccerTopPicksOptions>, AppConfigOptionsOverrides>();
builder.Services.AddSingleton<IConfigureOptions<UnderdogIngestionOptions>, AppConfigOptionsOverrides>();
builder.Services.AddHttpClient<IUnderdogAdapter, UnderdogAdapter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SportsBettingAnalyzer/0.1 underdog-spike");
});
builder.Services.AddHttpClient<IMlbResultsProvider, MlbResultsProvider>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MlbResultsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SportsBettingAnalyzer/0.1 mlb-results-provider");
});
builder.Services.AddHttpClient<IApiSportsFootballClient, ApiSportsFootballClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiSportsOptions>>().Value;
    client.BaseAddress = new Uri(options.Football.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 180));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SportsBettingAnalyzer/0.1 api-sports-football-spike");
});
builder.Services.AddHttpClient<ITheOddsApiMlbPlayerPropsAdapter, TheOddsApiMlbPlayerPropsAdapter>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TheOddsApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SportsBettingAnalyzer/0.1 the-odds-api-spike");
});
builder.Services.AddScoped<IUnderdogIngestionService, UnderdogIngestionService>();
builder.Services.AddScoped<IMlbResultImportService, MlbResultImportService>();
builder.Services.AddScoped<IMlbResultCoverageService, MlbResultCoverageService>();
builder.Services.AddScoped<ITheOddsApiMlbEventIngestionService, TheOddsApiMlbEventIngestionService>();
builder.Services.AddScoped<ITheOddsMlbIngestionPlanner, TheOddsMlbIngestionPlanner>();
builder.Services.AddScoped<ITheOddsRefreshPlannerService, TheOddsRefreshPlannerService>();
builder.Services.AddScoped<IProviderDomainConfigService, ProviderDomainConfigService>();
builder.Services.AddScoped<IPropResultRepository, PropResultRepository>();
builder.Services.AddScoped<IPredictionRepository, PredictionRepository>();
builder.Services.AddScoped<IPlayerIdentityAliasService, PlayerIdentityAliasService>();
builder.Services.AddScoped<IPredictionFeatureService, PredictionFeatureService>();
builder.Services.AddScoped<IPredictionGenerationService, PredictionGenerationService>();
builder.Services.AddScoped<IPredictionProbabilityService, PredictionProbabilityService>();
builder.Services.AddScoped<ILikelyHitModelService, LikelyHitModelService>();
builder.Services.AddScoped<IPlayerExternalRefRepository, PlayerExternalRefRepository>();
builder.Services.AddScoped<IPlayerResolutionService, PlayerResolutionService>();
builder.Services.AddScoped<IApplicationErrorLogger, ApplicationErrorLogger>();
builder.Services.AddScoped<IProviderLineLifecycleService, ProviderLineLifecycleService>();
builder.Services.AddScoped<ISoccerPlayerIdentityResolutionService, SoccerPlayerIdentityResolutionService>();
builder.Services.AddScoped<ISoccerWorldCupPredictionService, SoccerWorldCupPredictionService>();
builder.Services.AddScoped<IApiSportsSoccerSeasonResolver, ApiSportsSoccerSeasonResolver>();
builder.Services.AddSingleton<ITheOddsLiveRefreshGate, TheOddsLiveRefreshGate>();
builder.Services.AddScoped<ITheOddsMlbRefreshService, TheOddsApiDebugEndpoints.TheOddsMlbRefreshService>();
builder.Services.AddSingleton<IPlayerResolutionStats, PlayerResolutionStats>();
builder.Services.AddSingleton<IPlayerMergeExecutionState, PlayerMergeExecutionState>();
builder.Services.AddSingleton<IUnderdogIngestionWorkerState, UnderdogIngestionWorkerState>();
builder.Services.AddSingleton<ITheOddsMlbIngestionWorkerState, TheOddsMlbIngestionWorkerState>();
builder.Services.AddSingleton<ITheOddsScheduledRefreshState, TheOddsScheduledRefreshState>();
builder.Services.AddHostedService<UnderdogIngestionWorker>();
builder.Services.AddHostedService<MlbResultImportWorker>();
builder.Services.AddHostedService<SoccerResultImportWorker>();
builder.Services.AddHostedService<TheOddsMlbIngestionWorker>();
builder.Services.AddHostedService<TheOddsMlbScheduledRefreshWorker>();

var connectionString = builder.Configuration.GetConnectionString("SportsBettingDb")
    ?? throw new InvalidOperationException("Connection string 'SportsBettingDb' is not configured.");
if (IsPlaceholderConnectionString(connectionString))
{
    throw new InvalidOperationException("Connection string 'SportsBettingDb' still contains the POSTGRES_CONNECTION_STRING placeholder. Copy api/appsettings.Local.example.json to api/appsettings.Local.json and set a real local connection string, or set ConnectionStrings__SportsBettingDb.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options
        .UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.CommandTimeout(180))
        .UseSnakeCaseNamingConvention());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await ApplyDatabaseMigrationsAsync(app);
    await SeedAppConfigsAsync(app);
    await SeedProviderDomainConfigsAsync(app);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<ApplicationErrorMiddleware>();
}
app.UseMiddleware<DebugAdminProtectionMiddleware>();
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/health"))
    .ExcludeFromDescription();

app.MapGet("/research", () => Results.Redirect("/research/index.html"))
    .WithName("ResearchDashboard");

app.MapHealthChecks("/health")
    .WithName("Health");

app.MapGet("/health/ready", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

    return canConnect
        ? Results.Ok(new { status = "ready", database = "available" })
        : Results.Problem("Database connection is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Readiness");

app.MapUnderdogDebugEndpoints();
app.MapUnderdogIngestionHealthEndpoints();
app.MapLineHistoryDebugEndpoints();
app.MapResultsDebugEndpoints();
app.MapPredictionDebugEndpoints();
app.MapFeatureDebugEndpoints();
app.MapPlayerMappingDebugEndpoints();
app.MapMarketClassificationDebugEndpoints();
app.MapApplicationErrorDebugEndpoints();
app.MapAppConfigDebugEndpoints();
app.MapProviderDomainConfigDebugEndpoints();
app.MapSystemHealthDebugEndpoints();
app.MapDfsDebugEndpoints();
app.MapTheOddsApiDebugEndpoints();
app.MapProviderLineLifecycleDebugEndpoints();
app.MapRecommendationLedgerDebugEndpoints();
app.MapPublicMarketsEndpoints();
app.MapPlayersEndpoints();

app.Run();

static bool IsPlaceholderConnectionString(string value)
{
    return string.Equals(value.Trim(), "POSTGRES_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase);
}

static bool IsAllowedFrontendDevOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var isLocalHost = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    var isVitePort = uri.Port is >= 5173 and <= 5179;

    return isLocalHost && isVitePort && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseMigration");
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
    var pendingMigrationList = pendingMigrations.ToArray();

    if (pendingMigrationList.Length == 0)
    {
        logger.LogInformation("Database schema is up to date.");
        return;
    }

    logger.LogInformation(
        "Applying {PendingMigrationCount} pending database migration(s): {PendingMigrations}.",
        pendingMigrationList.Length,
        string.Join(", ", pendingMigrationList));

    await dbContext.Database.MigrateAsync();

    logger.LogInformation("Database migrations applied successfully.");
}

static async Task SeedAppConfigsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("AppConfigSeeder");

    try
    {
        var result = await AppConfigSeeder.SeedAsync(scope.ServiceProvider);
        logger.LogInformation(
            "AppConfigs seed completed. Inserted: {Inserted}; metadata updated: {MetadataUpdated}.",
            result.Inserted,
            result.MetadataUpdated);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "AppConfigs seed failed; appsettings/default fallbacks remain active.");
    }
}

static async Task SeedProviderDomainConfigsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("ProviderDomainConfigSeeder");

    try
    {
        var result = await ProviderDomainConfigSeeder.SeedAsync(scope.ServiceProvider);
        logger.LogInformation(
            "Provider domain config seed completed. Competitions inserted: {CompetitionsInserted}; markets inserted: {MarketsInserted}; bookmakers inserted: {BookmakersInserted}; refresh targets inserted: {RefreshTargetsInserted}; metadata updated: {MetadataUpdated}.",
            result.CompetitionsInserted,
            result.MarketsInserted,
            result.BookmakersInserted,
            result.RefreshTargetsInserted,
            result.MetadataUpdated);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Provider domain config seed failed; structured appsettings fallbacks remain active.");
    }
}

public partial class Program
{
}
