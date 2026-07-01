namespace SportsBetting.Api.Configuration;

public sealed class PublicMarketReadinessOptions
{
    public const string SectionName = "PublicMarkets";

    public int ColdStartGraceMinutes { get; set; } = 5;

    public TimeSpan GetColdStartGraceWindow() =>
        TimeSpan.FromMinutes(Math.Clamp(ColdStartGraceMinutes, 0, 60));
}

public interface IApplicationStartupClock
{
    DateTimeOffset StartedAtUtc { get; }
}

public sealed class ApplicationStartupClock : IApplicationStartupClock
{
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
}
