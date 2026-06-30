namespace SportsBetting.Api.Predictions;

public sealed class TopPicksModelOptions
{
    public const string SectionName = "TopPicksModel";
    public const string LikelyHitV1 = "LikelyHitV1";
    public const string Legacy = "Legacy";
    public const string DefaultLikelyHitV1ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded";

    public string ActiveModel { get; set; } = LikelyHitV1;
    public string CurrentModelEra { get; set; } = DefaultLikelyHitV1ModelEra;
    public bool EnableDebugBreakdown { get; set; }
    public decimal MlbMinimumHitChance { get; set; } = 0.58m;
    public decimal MlbMinimumReliability { get; set; } = 0.65m;
    public decimal SoccerWorldCupMinimumHitChance { get; set; } = 0.60m;
    public decimal SoccerWorldCupMinimumReliability { get; set; } = 0.70m;
    public decimal MinimumEffectiveSampleSize { get; set; } = 10m;
    public decimal MarketCalibrationGapThreshold { get; set; } = 0.15m;
    public decimal MarketCalibrationShrinkRate { get; set; } = 0.20m;
    public decimal RecentModifierMin { get; set; } = -0.06m;
    public decimal RecentModifierMax { get; set; } = 0.03m;
    public int SportsbookBalancedMaxFavoriteAmericanOdds { get; set; } = -300;
    public int SportsbookBalancedMaxUnderdogAmericanOdds { get; set; } = 250;
    public decimal SportsbookBalancedTargetMinHitChance { get; set; } = 0.60m;
    public decimal SportsbookBalancedPublicMinimumHitChance { get; set; } = 0.65m;
    public decimal SportsbookBalancedTargetMaxHitChance { get; set; } = 0.75m;
    public decimal SportsbookBalancedSuspiciousMarketGap { get; set; } = 0.25m;
    public int SportsbookHeavyFavoritePenaltyStartsAt { get; set; } = -300;
    public bool SportsbookExtremeFavoriteIneligible { get; set; } = true;
    public bool EnableSelectedSideHistoryConsistencyGuard { get; set; } = true;
    public decimal ConsistencyGuardHighHitChance { get; set; } = 0.75m;
    public decimal ConsistencyGuardExtremeHitChance { get; set; } = 0.85m;
    public decimal ConsistencyGuardWeakRecent10HitRate { get; set; } = 0.50m;
    public decimal ConsistencyGuardExtremeWeakRecent10HitRate { get; set; } = 0.60m;
    public decimal ConsistencyGuardHighHitChanceGap { get; set; } = 0.25m;
    public int PublicMaxTopPicksPerSlateSport { get; set; } = 50;
    public int PublicMaxTopPicksPerPlayer { get; set; } = 3;
    public int PublicMaxTopPicksPerPlayerPropFamily { get; set; } = 1;
    public int PublicMaxTopPicksPerGameEvent { get; set; } = 10;
    public int PublicMaxTopPicksPerTeam { get; set; }
    public int PublicMaxTopPicksPerPropType { get; set; }

    public bool IsLikelyHitV1Enabled =>
        string.Equals(ActiveModel, LikelyHitV1, StringComparison.OrdinalIgnoreCase);
}
