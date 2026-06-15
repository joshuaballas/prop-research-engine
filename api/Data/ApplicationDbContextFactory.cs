using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SportsBetting.Api.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddCommandLine(args)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SportsBettingDb")
            ?? throw new InvalidOperationException("Connection string 'SportsBettingDb' is not configured.");
        if (string.Equals(connectionString.Trim(), "POSTGRES_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Connection string 'SportsBettingDb' still contains the POSTGRES_CONNECTION_STRING placeholder. Copy api/appsettings.Local.example.json to api/appsettings.Local.json and set a real local connection string, or set ConnectionStrings__SportsBettingDb.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
