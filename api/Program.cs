using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Ingestion.Players;
using SportsBetting.Api.Integrations.Results.Mlb;
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
builder.Services.Configure<DebugAdminOptions>(builder.Configuration.GetSection(DebugAdminOptions.SectionName));
builder.Services.Configure<UnderdogIngestionOptions>(builder.Configuration.GetSection(UnderdogIngestionOptions.SectionName));
builder.Services.Configure<UnderdogOptions>(builder.Configuration.GetSection(UnderdogOptions.SectionName));
builder.Services.Configure<MlbResultsOptions>(builder.Configuration.GetSection(MlbResultsOptions.SectionName));
builder.Services.Configure<TheOddsApiOptions>(builder.Configuration.GetSection(TheOddsApiOptions.SectionName));
builder.Services.Configure<TheOddsIngestionWorkerOptions>(builder.Configuration.GetSection(TheOddsIngestionWorkerOptions.SectionName));
builder.Services.Configure<TheOddsScheduledRefreshOptions>(builder.Configuration.GetSection(TheOddsScheduledRefreshOptions.SectionName));
builder.Services.Configure<MlbResultImportWorkerOptions>(builder.Configuration.GetSection(MlbResultImportWorkerOptions.SectionName));
builder.Services.Configure<PredictionGenerationOptions>(builder.Configuration.GetSection(PredictionGenerationOptions.SectionName));
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
builder.Services.AddHttpClient<ITheOddsApiMlbPlayerPropsAdapter, TheOddsApiMlbPlayerPropsAdapter>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TheOddsApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SportsBettingAnalyzer/0.1 the-odds-api-spike");
});
builder.Services.AddScoped<IUnderdogIngestionService, UnderdogIngestionService>();
builder.Services.AddScoped<IMlbResultImportService, MlbResultImportService>();
builder.Services.AddScoped<ITheOddsApiMlbEventIngestionService, TheOddsApiMlbEventIngestionService>();
builder.Services.AddScoped<ITheOddsMlbIngestionPlanner, TheOddsMlbIngestionPlanner>();
builder.Services.AddScoped<IPropResultRepository, PropResultRepository>();
builder.Services.AddScoped<IPredictionRepository, PredictionRepository>();
builder.Services.AddScoped<IPlayerIdentityAliasService, PlayerIdentityAliasService>();
builder.Services.AddScoped<IPredictionFeatureService, PredictionFeatureService>();
builder.Services.AddScoped<IPredictionGenerationService, PredictionGenerationService>();
builder.Services.AddScoped<IPredictionProbabilityService, PredictionProbabilityService>();
builder.Services.AddScoped<IPlayerExternalRefRepository, PlayerExternalRefRepository>();
builder.Services.AddScoped<IPlayerResolutionService, PlayerResolutionService>();
builder.Services.AddScoped<IApplicationErrorLogger, ApplicationErrorLogger>();
builder.Services.AddScoped<ITheOddsMlbRefreshService, TheOddsApiDebugEndpoints.TheOddsMlbRefreshService>();
builder.Services.AddSingleton<IPlayerResolutionStats, PlayerResolutionStats>();
builder.Services.AddSingleton<IPlayerMergeExecutionState, PlayerMergeExecutionState>();
builder.Services.AddSingleton<IUnderdogIngestionWorkerState, UnderdogIngestionWorkerState>();
builder.Services.AddSingleton<ITheOddsMlbIngestionWorkerState, TheOddsMlbIngestionWorkerState>();
builder.Services.AddSingleton<ITheOddsScheduledRefreshState, TheOddsScheduledRefreshState>();
builder.Services.AddHostedService<UnderdogIngestionWorker>();
builder.Services.AddHostedService<MlbResultImportWorker>();
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

await ApplyDatabaseMigrationsAsync(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseMiddleware<ApplicationErrorMiddleware>();
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
app.MapSystemHealthDebugEndpoints();
app.MapDfsDebugEndpoints();
app.MapTheOddsApiDebugEndpoints();
app.MapPublicMlbDfsEndpoints();

app.Run();

static bool IsPlaceholderConnectionString(string value)
{
    return string.Equals(value.Trim(), "POSTGRES_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase);
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
