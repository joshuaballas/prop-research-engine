using System.Text;

namespace SportsBetting.Api.Ingestion;

public static class CanonicalLeagueNormalizer
{
    public static string NormalizeCode(string value)
    {
        var normalized = NormalizeRawCode(value);

        return normalized switch
        {
            "MLBLIVE" or "MLB_LIVE" => "MLB",
            "NBALIVE" or "NBA_LIVE" => "NBA",
            "WNBALIVE" or "WNBA_LIVE" => "WNBA",
            "NFLLIVE" or "NFL_LIVE" => "NFL",
            "NHLLIVE" or "NHL_LIVE" => "NHL",
            "UFCLIVE" or "UFC_LIVE" => "UFC",
            _ when normalized.EndsWith("_LIVE", StringComparison.Ordinal) => normalized[..^5],
            _ when normalized.EndsWith("LIVE", StringComparison.Ordinal) => normalized[..^4],
            _ => normalized
        };
    }

    private static string NormalizeRawCode(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }
}
