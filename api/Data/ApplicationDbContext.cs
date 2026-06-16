using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Models;
using System.Text.Json;

namespace SportsBetting.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueConfiguration> LeagueConfigurations => Set<LeagueConfiguration>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerExternalRef> PlayerExternalRefs => Set<PlayerExternalRef>();
    public DbSet<PlayerMergeAudit> PlayerMergeAudits => Set<PlayerMergeAudit>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<PropType> PropTypes => Set<PropType>();
    public DbSet<Prop> Props => Set<Prop>();
    public DbSet<PlatformProp> PlatformProps => Set<PlatformProp>();
    public DbSet<PropLineSnapshot> PropLineSnapshots => Set<PropLineSnapshot>();
    public DbSet<PropAnalysis> PropAnalyses => Set<PropAnalysis>();
    public DbSet<LineMovement> LineMovements => Set<LineMovement>();
    public DbSet<PropResult> PropResults => Set<PropResult>();
    public DbSet<MlbResultImportState> MlbResultImportStates => Set<MlbResultImportState>();
    public DbSet<MlbResultImportDateState> MlbResultImportDateStates => Set<MlbResultImportDateState>();
    public DbSet<PredictionRun> PredictionRuns => Set<PredictionRun>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<PredictionRunSkipCount> PredictionRunSkipCounts => Set<PredictionRunSkipCount>();
    public DbSet<LineShoppingStrategyBacktestSnapshot> LineShoppingStrategyBacktestSnapshots => Set<LineShoppingStrategyBacktestSnapshot>();
    public DbSet<TheOddsRefreshRun> TheOddsRefreshRuns => Set<TheOddsRefreshRun>();
    public DbSet<MarketTopPickSnapshot> MarketTopPickSnapshots => Set<MarketTopPickSnapshot>();
    public DbSet<MarketLineShoppingSnapshot> MarketLineShoppingSnapshots => Set<MarketLineShoppingSnapshot>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<RawIngestionPayload> RawIngestionPayloads => Set<RawIngestionPayload>();
    public DbSet<CorrelationPair> CorrelationPairs => Set<CorrelationPair>();
    public DbSet<CorrelationRule> CorrelationRules => Set<CorrelationRule>();
    public DbSet<OptimizedEntry> OptimizedEntries => Set<OptimizedEntry>();
    public DbSet<OptimizedEntryLeg> OptimizedEntryLegs => Set<OptimizedEntryLeg>();
    public DbSet<ApplicationError> ApplicationErrors => Set<ApplicationError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        ConfigureReferenceTables(modelBuilder);
        ConfigurePropTables(modelBuilder);
        ConfigureResultTables(modelBuilder);
        ConfigurePredictionTables(modelBuilder);
        ConfigureIngestionTables(modelBuilder);
        ConfigureCorrelationTables(modelBuilder);
        ConfigureOptimizationTables(modelBuilder);
        ConfigureApplicationErrorTables(modelBuilder);
    }

    private static void ConfigureReferenceTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Platform>(entity =>
        {
            entity.ToTable("platforms");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Sport>(entity =>
        {
            entity.ToTable("sports");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<League>(entity =>
        {
            entity.ToTable("leagues");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SeasonType).HasMaxLength(50);
            entity.HasOne(x => x.Sport).WithMany(x => x.Leagues).HasForeignKey(x => x.SportId);
            entity.HasIndex(x => new { x.SportId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<LeagueConfiguration>(entity =>
        {
            entity.ToTable("league_configurations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LeagueName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.IsEnabledForAnalysis).HasDefaultValue(false);
            entity.Property(x => x.IsEnabledForOptimizer).HasDefaultValue(false);
            entity.Property(x => x.IsEnabledForDisplay).HasDefaultValue(false);
            entity.HasIndex(x => x.LeagueName).IsUnique();
            entity.HasData(
                EnabledLeagueConfiguration("NFL", "11111111-1111-1111-1111-111111111111"),
                EnabledLeagueConfiguration("NBA", "22222222-2222-2222-2222-222222222222"),
                EnabledLeagueConfiguration("WNBA", "33333333-3333-3333-3333-333333333333"),
                EnabledLeagueConfiguration("NHL", "44444444-4444-4444-4444-444444444444"),
                EnabledLeagueConfiguration("MLB", "55555555-5555-5555-5555-555555555555"),
                EnabledLeagueConfiguration("SOCCER", "66666666-6666-6666-6666-666666666666"),
                EnabledLeagueConfiguration("UFC", "77777777-7777-7777-7777-777777777777"));
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Abbreviation).HasMaxLength(25);
            entity.Property(x => x.ExternalRefs).HasColumnType("jsonb");
            entity.HasOne(x => x.League).WithMany(x => x.Teams).HasForeignKey(x => x.LeagueId);
            entity.HasIndex(x => new { x.LeagueId, x.Abbreviation });
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(250).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Position).HasMaxLength(50);
            entity.Property(x => x.ExternalRefs).HasColumnType("jsonb");
            entity.HasOne(x => x.Team).WithMany(x => x.Players).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.League).WithMany(x => x.Players).HasForeignKey(x => x.LeagueId);
            entity.HasIndex(x => new { x.LeagueId, x.DisplayName });
        });

        modelBuilder.Entity<PlayerExternalRef>(entity =>
        {
            entity.ToTable("player_external_refs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProviderPlayerId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProviderDisplayName).HasMaxLength(250).IsRequired();
            entity.HasOne(x => x.Player).WithMany(x => x.ExternalProviderRefs).HasForeignKey(x => x.PlayerId);
            entity.HasIndex(x => new { x.Provider, x.ProviderPlayerId }).IsUnique();
            entity.HasIndex(x => x.PlayerId);
            entity.HasIndex(x => new { x.Provider, x.ProviderDisplayName });
        });

        modelBuilder.Entity<PlayerMergeAudit>(entity =>
        {
            entity.ToTable("player_merge_audits");
            entity.HasKey(x => x.MergeId);
            entity.Property(x => x.MergeId).HasColumnName("merge_id");
            entity.HasIndex(x => x.SurvivorPlayerId);
            entity.HasIndex(x => x.MergedPlayerId).IsUnique();
            entity.HasIndex(x => x.MergedAt);
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("games");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalRefs).HasColumnType("jsonb");
            entity.HasOne(x => x.League).WithMany(x => x.Games).HasForeignKey(x => x.LeagueId);
            entity.HasOne(x => x.HomeTeam).WithMany().HasForeignKey(x => x.HomeTeamId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AwayTeam).WithMany().HasForeignKey(x => x.AwayTeamId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ScheduledStartAt);
        });
    }

    private static LeagueConfiguration EnabledLeagueConfiguration(string leagueName, string id)
    {
        var timestamp = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);

        return new LeagueConfiguration
        {
            Id = Guid.Parse(id),
            LeagueName = leagueName,
            IsEnabledForAnalysis = true,
            IsEnabledForOptimizer = true,
            IsEnabledForDisplay = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static void ConfigurePropTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PropType>(entity =>
        {
            entity.ToTable("prop_types");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StatKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ValueType).HasMaxLength(50).IsRequired();
            entity.HasOne(x => x.Sport).WithMany(x => x.PropTypes).HasForeignKey(x => x.SportId);
            entity.HasIndex(x => new { x.SportId, x.StatKey }).IsUnique();
        });

        modelBuilder.Entity<Prop>(entity =>
        {
            entity.ToTable("props");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NormalizedKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.HasOne(x => x.Player).WithMany(x => x.Props).HasForeignKey(x => x.PlayerId);
            entity.HasOne(x => x.Game).WithMany(x => x.Props).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PropType).WithMany(x => x.Props).HasForeignKey(x => x.PropTypeId);
            entity.HasOne(x => x.League).WithMany(x => x.Props).HasForeignKey(x => x.LeagueId);
            entity.HasIndex(x => x.NormalizedKey).IsUnique();
            entity.HasIndex(x => new { x.PlayerId, x.GameId, x.PropTypeId });
        });

        modelBuilder.Entity<PlatformProp>(entity =>
        {
            entity.ToTable("platform_props");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlatformPropId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ExternalStableKey).HasMaxLength(500);
            entity.Property(x => x.Label).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CurrentLine).HasPrecision(12, 4);
            entity.Property(x => x.PayoutType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Metadata).HasColumnType("jsonb");
            entity.HasOne(x => x.Prop).WithMany(x => x.PlatformProps).HasForeignKey(x => x.PropId);
            entity.HasOne(x => x.Platform).WithMany(x => x.PlatformProps).HasForeignKey(x => x.PlatformId);
            entity.HasOne(x => x.RawPayload).WithMany().HasForeignKey(x => x.RawPayloadId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.PlatformId, x.PlatformPropId }).IsUnique();
            entity.HasIndex(x => x.ExternalStableKey);
            entity.HasIndex(x => x.ObservedAt);
        });

        modelBuilder.Entity<PropLineSnapshot>(entity =>
        {
            entity.ToTable("prop_line_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Line).HasPrecision(12, 4);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceHash).HasMaxLength(128).IsRequired();
            entity.HasOne(x => x.PlatformProp).WithMany(x => x.LineSnapshots).HasForeignKey(x => x.PlatformPropId);
            entity.HasOne(x => x.RawPayload).WithMany().HasForeignKey(x => x.RawPayloadId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.PlatformPropId, x.ObservedAt });
            entity.HasIndex(x => x.ObservedAt);
            entity.HasIndex(x => new { x.PlatformPropId, x.Line, x.ObservedAt });
        });

        modelBuilder.Entity<PropAnalysis>(entity =>
        {
            entity.ToTable("prop_analysis");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Side).HasMaxLength(20).IsRequired();
            entity.Property(x => x.HitProbability).HasPrecision(8, 6);
            entity.Property(x => x.FairLine).HasPrecision(12, 4);
            entity.Property(x => x.ExpectedValue).HasPrecision(12, 6);
            entity.Property(x => x.ConfidenceScore).HasPrecision(8, 4);
            entity.Property(x => x.HistoricalHitRate).HasPrecision(8, 6);
            entity.Property(x => x.LineMovementScore).HasPrecision(10, 4);
            entity.Property(x => x.PlatformEdgeScore).HasPrecision(10, 4);
            entity.Property(x => x.ModelVersion).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Explanation).HasColumnType("jsonb");
            entity.HasOne(x => x.PlatformProp).WithMany(x => x.Analyses).HasForeignKey(x => x.PlatformPropId);
            entity.HasIndex(x => new { x.PlatformPropId, x.Side, x.ModelVersion }).IsUnique();
        });

        modelBuilder.Entity<LineMovement>(entity =>
        {
            entity.ToTable("line_movements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PreviousLine).HasPrecision(12, 4);
            entity.Property(x => x.CurrentLine).HasPrecision(12, 4);
            entity.Property(x => x.Delta).HasPrecision(12, 4);
            entity.Property(x => x.Direction).HasMaxLength(20).IsRequired();
            entity.Property(x => x.MovementPercent).HasPrecision(10, 4);
            entity.Property(x => x.SignificanceScore).HasPrecision(10, 4);
            entity.HasOne(x => x.PlatformProp).WithMany(x => x.LineMovements).HasForeignKey(x => x.PlatformPropId);
            entity.HasIndex(x => x.DetectedAt);
            entity.HasIndex(x => new { x.PlatformPropId, x.DetectedAt });
        });
    }

    private static void ConfigureIngestionTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionRun>(entity =>
        {
            entity.ToTable("ingestion_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ErrorMessage);
            entity.Property(x => x.PayloadHash).HasMaxLength(128);
            entity.Property(x => x.SchemaVersion).HasMaxLength(50);
            entity.HasOne(x => x.Platform).WithMany(x => x.IngestionRuns).HasForeignKey(x => x.PlatformId);
            entity.HasIndex(x => new { x.PlatformId, x.StartedAt });
        });

        modelBuilder.Entity<RawIngestionPayload>(entity =>
        {
            entity.ToTable("raw_ingestion_payloads");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasOne(x => x.IngestionRun).WithMany(x => x.RawPayloads).HasForeignKey(x => x.IngestionRunId);
            entity.HasOne(x => x.Platform).WithMany(x => x.RawPayloads).HasForeignKey(x => x.PlatformId);
            entity.HasIndex(x => x.PayloadHash);
            entity.HasIndex(x => x.CapturedAt);
        });
    }

    private static void ConfigureResultTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PropResult>(entity =>
        {
            entity.ToTable("prop_results");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActualResultValue).HasPrecision(12, 4);
            entity.Property(x => x.ExternalSource).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExternalSourceIdentifier).HasMaxLength(300).IsRequired();
            entity.HasOne(x => x.Player).WithMany(x => x.PropResults).HasForeignKey(x => x.PlayerId);
            entity.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId);
            entity.HasOne(x => x.PropType).WithMany(x => x.PropResults).HasForeignKey(x => x.PropTypeId);
            entity.HasIndex(x => new { x.PlayerId, x.PropTypeId, x.EventDate });
            entity.HasIndex(x => new { x.LeagueId, x.EventDate });
            entity.HasIndex(x => new { x.ExternalSource, x.ExternalSourceIdentifier }).IsUnique();
        });

        modelBuilder.Entity<MlbResultImportState>(entity =>
        {
            entity.ToTable("mlb_result_import_states");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.LastStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.LastErrorMessage);
            entity.HasIndex(x => x.Source).IsUnique();
            entity.HasIndex(x => x.LastSuccessfulImportDate);
        });

        modelBuilder.Entity<MlbResultImportDateState>(entity =>
        {
            entity.ToTable("mlb_result_import_date_states");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.LastErrorMessage);
            entity.HasIndex(x => x.EventDate).IsUnique();
            entity.HasIndex(x => new { x.Status, x.EventDate });
        });
    }

    private static void ConfigurePredictionTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PredictionRun>(entity =>
        {
            entity.ToTable("prediction_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ModelName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ModelVersion).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.ModelName, x.ModelVersion, x.CreatedAt });
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<Prediction>(entity =>
        {
            entity.ToTable("predictions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PredictedProbability).HasPrecision(5, 4);
            entity.Property(x => x.PredictedValue).HasPrecision(18, 4);
            entity.Property(x => x.SportsbookLine).HasPrecision(18, 4);
            entity.Property(x => x.Edge).HasPrecision(18, 4);
            entity.Property(x => x.Recommendation).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Confidence).HasPrecision(5, 4);
            entity.HasOne(x => x.PredictionRun).WithMany(x => x.Predictions).HasForeignKey(x => x.PredictionRunId);
            entity.HasOne(x => x.Prop).WithMany(x => x.Predictions).HasForeignKey(x => x.PropId);
            entity.HasIndex(x => x.PredictionRunId);
            entity.HasIndex(x => x.PropId);
            entity.HasIndex(x => new { x.PropId, x.CreatedAt });
        });

        modelBuilder.Entity<PredictionRunSkipCount>(entity =>
        {
            entity.ToTable("prediction_run_skip_counts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.PredictionRun).WithMany(x => x.SkipCounts).HasForeignKey(x => x.PredictionRunId);
            entity.HasIndex(x => new { x.PredictionRunId, x.Reason }).IsUnique();
        });

        modelBuilder.Entity<LineShoppingStrategyBacktestSnapshot>(entity =>
        {
            entity.ToTable("line_shopping_strategy_backtest_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sport).HasMaxLength(50).IsRequired();
            entity.Property(x => x.StrategyName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.HitRate).HasPrecision(8, 6);
            entity.Property(x => x.SimulatedProfit).HasPrecision(18, 4);
            entity.Property(x => x.SimulatedRoi).HasPrecision(8, 6);
            entity.Property(x => x.AverageBestEstimatedEv).HasPrecision(18, 6);
            entity.Property(x => x.AverageBestProbability).HasPrecision(8, 6);
            entity.Property(x => x.AverageBestImpliedProbability).HasPrecision(8, 6);
            entity.Property(x => x.AverageProbabilityEdge).HasPrecision(8, 6);
            entity.Property(x => x.AverageHistoricalSampleSize).HasPrecision(18, 2);
            entity.Property(x => x.AverageBestPrice).HasPrecision(18, 6);
            entity.HasOne(x => x.PredictionRun).WithMany().HasForeignKey(x => x.RunId);
            entity.HasIndex(x => new { x.RunId, x.StrategyName, x.GeneratedAt }).IsUnique();
            entity.HasIndex(x => new { x.Sport, x.SlateDate });
            entity.HasIndex(x => x.StrategyName);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<TheOddsRefreshRun>(entity =>
        {
            entity.ToTable("the_odds_refresh_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Sport).HasMaxLength(50).IsRequired();
            entity.Property(x => x.BookmakerPreset).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProviderCategoriesIncluded).HasMaxLength(500).IsRequired();
            entity.Property(x => x.SportsbookBookmakersRequested).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DfsBookmakersRequested).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.MarketsRequested).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ErrorMessage);
            entity.Property(x => x.DiagnosticsJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.StartedAtUtc);
            entity.HasIndex(x => x.CompletedAtUtc);
            entity.HasIndex(x => new { x.Sport, x.SlateDate });
            entity.HasIndex(x => new { x.BookmakerPreset, x.StartedAtUtc });
            entity.HasIndex(x => new { x.Succeeded, x.CompletedAtUtc });
        });

        ConfigureMarketSnapshot<MarketTopPickSnapshot>(
            modelBuilder,
            "market_top_pick_snapshots",
            "ix_market_top_pick_snapshots");

        ConfigureMarketSnapshot<MarketLineShoppingSnapshot>(
            modelBuilder,
            "market_line_shopping_snapshots",
            "ix_market_line_shopping_snapshots");
    }

    private static void ConfigureMarketSnapshot<TSnapshot>(
        ModelBuilder modelBuilder,
        string tableName,
        string indexPrefix)
        where TSnapshot : class
    {
        modelBuilder.Entity<TSnapshot>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey("Id");
            entity.Property<Guid>("Id");
            entity.Property<Guid>("TheOddsRefreshRunId");
            entity.Property<Guid?>("PredictionRunId");
            entity.Property<string>("Sport").HasMaxLength(50).IsRequired();
            entity.Property<DateOnly>("SlateDate");
            entity.Property<DateTimeOffset>("GeneratedAtUtc");
            entity.Property<string>("BookmakerPreset").HasMaxLength(100).IsRequired();
            entity.Property<string>("ProviderCategory").HasMaxLength(50).IsRequired();
            entity.Property<string?>("Provider").HasMaxLength(200);
            entity.Property<string?>("Bookmaker").HasMaxLength(200);
            entity.Property<string>("Player").HasMaxLength(300).IsRequired();
            entity.Property<string?>("EventId").HasMaxLength(200);
            entity.Property<string?>("EventName").HasMaxLength(500);
            entity.Property<DateTimeOffset?>("EventStartTime");
            entity.Property<string>("LifecycleStatus").HasMaxLength(50).IsRequired();
            entity.Property<bool>("IsStale");
            entity.Property<bool>("IsExpired");
            entity.Property<string>("PropType").HasMaxLength(200).IsRequired();
            entity.Property<decimal>("Line").HasPrecision(12, 4);
            entity.Property<string>("BestSide").HasMaxLength(20).IsRequired();
            entity.Property<string?>("BestBookmaker").HasMaxLength(200);
            entity.Property<int?>("BestAmericanPrice");
            entity.Property<decimal?>("BestDecimalPrice").HasPrecision(12, 6);
            entity.Property<decimal?>("BestProbability").HasPrecision(8, 6);
            entity.Property<decimal?>("BestImpliedProbability").HasPrecision(8, 6);
            entity.Property<decimal?>("BestProbabilityEdge").HasPrecision(8, 6);
            entity.Property<decimal?>("BestEstimatedEv").HasPrecision(12, 6);
            entity.Property<string>("StrategyClassification").HasMaxLength(50).IsRequired();
            entity.Property<string>("StrategyTier").HasMaxLength(100).IsRequired();
            entity.Property<bool>("IsCandidate");
            entity.Property<bool>("IsWatchlist");
            entity.Property<bool>("IsAvoid");
            entity.Property<bool>("IsResearchOnly");
            entity.Property<string>("EntryProvider").HasMaxLength(200).IsRequired();
            entity.Property<string>("EntryCompatibilityGroup").HasMaxLength(200).IsRequired();
            entity.Property<string>("EntryProviderCategory").HasMaxLength(50).IsRequired();
            entity.Property<bool>("EntryActionableForUser");
            entity.Property<JsonDocument>("DetailsJson").HasColumnType("jsonb");
            entity.HasOne(typeof(TheOddsRefreshRun), "TheOddsRefreshRun")
                .WithMany(typeof(TSnapshot) == typeof(MarketTopPickSnapshot)
                    ? nameof(TheOddsRefreshRun.TopPickSnapshots)
                    : nameof(TheOddsRefreshRun.LineShoppingSnapshots))
                .HasForeignKey("TheOddsRefreshRunId")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(typeof(PredictionRun), "PredictionRun")
                .WithMany()
                .HasForeignKey("PredictionRunId")
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex("TheOddsRefreshRunId").HasDatabaseName($"{indexPrefix}_refresh_run_id");
            entity.HasIndex("PredictionRunId").HasDatabaseName($"{indexPrefix}_prediction_run_id");
            entity.HasIndex("Sport", "SlateDate").HasDatabaseName($"{indexPrefix}_sport_slate_date");
            entity.HasIndex("GeneratedAtUtc").HasDatabaseName($"{indexPrefix}_generated_at_utc");
            entity.HasIndex("ProviderCategory", "IsCandidate").HasDatabaseName($"{indexPrefix}_provider_category_candidate");
            entity.HasIndex("BestEstimatedEv").HasDatabaseName($"{indexPrefix}_best_estimated_ev");
        });
    }

    private static void ConfigureCorrelationTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CorrelationPair>(entity =>
        {
            entity.ToTable("correlation_pairs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RelationshipScope).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CorrelationCoefficient).HasPrecision(8, 6);
            entity.Property(x => x.ConfidenceScore).HasPrecision(8, 4);
            entity.Property(x => x.ModelVersion).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.PropA).WithMany().HasForeignKey(x => x.PropAId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PropB).WithMany().HasForeignKey(x => x.PropBId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PropTypeA).WithMany().HasForeignKey(x => x.PropTypeAId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PropTypeB).WithMany().HasForeignKey(x => x.PropTypeBId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.PropAId, x.PropBId, x.ModelVersion });
            entity.HasIndex(x => new { x.PropTypeAId, x.PropTypeBId, x.RelationshipScope });
        });

        modelBuilder.Entity<CorrelationRule>(entity =>
        {
            entity.ToTable("correlation_rules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Context).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExpectedDirection).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DefaultWeight).HasPrecision(8, 4);
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId);
            entity.HasOne(x => x.PropTypeA).WithMany().HasForeignKey(x => x.PropTypeAId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PropTypeB).WithMany().HasForeignKey(x => x.PropTypeBId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.LeagueId, x.PropTypeAId, x.PropTypeBId, x.Context }).IsUnique();
        });
    }

    private static void ConfigureOptimizationTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OptimizedEntry>(entity =>
        {
            entity.ToTable("optimized_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CombinedProbability).HasPrecision(8, 6);
            entity.Property(x => x.ExpectedValue).HasPrecision(12, 6);
            entity.Property(x => x.ConfidenceScore).HasPrecision(8, 4);
            entity.Property(x => x.CorrelationScore).HasPrecision(8, 4);
            entity.Property(x => x.RiskScore).HasPrecision(8, 4);
            entity.Property(x => x.ModelVersion).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.EntrySize, x.Rank });
            entity.HasIndex(x => x.GeneratedAt);
        });

        modelBuilder.Entity<OptimizedEntryLeg>(entity =>
        {
            entity.ToTable("optimized_entry_legs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Side).HasMaxLength(20).IsRequired();
            entity.Property(x => x.LegProbability).HasPrecision(8, 6);
            entity.Property(x => x.LegExpectedValue).HasPrecision(12, 6);
            entity.HasOne(x => x.OptimizedEntry).WithMany(x => x.Legs).HasForeignKey(x => x.OptimizedEntryId);
            entity.HasOne(x => x.PlatformProp).WithMany().HasForeignKey(x => x.PlatformPropId);
            entity.HasIndex(x => new { x.OptimizedEntryId, x.LegOrder }).IsUnique();
        });
    }

    private static void ConfigureApplicationErrorTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationError>(entity =>
        {
            entity.ToTable("application_errors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Severity).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Component).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Operation).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RequestPath).HasMaxLength(1000);
            entity.Property(x => x.HttpMethod).HasMaxLength(20);
            entity.Property(x => x.Message).IsRequired();
            entity.Property(x => x.ExceptionType).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.Source);
            entity.HasIndex(x => x.Component);
            entity.HasIndex(x => x.Severity);
            entity.HasIndex(x => x.CorrelationId);
        });
    }
}
