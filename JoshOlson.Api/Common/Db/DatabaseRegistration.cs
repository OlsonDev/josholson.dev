using Npgsql;

namespace JoshOlson.Api.Common.Db;

public static class DatabaseRegistration {
  // Pre-warmed pool: a burst of visitors (a hiring team opening the site together) never waits on a cold connect.
  private const int _MinPoolSize = 10;

  extension(IServiceCollection services) {
    public IServiceCollection AddDatabase(IConfiguration configuration) {
      var connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
      var builder = new NpgsqlConnectionStringBuilder(connectionString) {
        MinPoolSize = _MinPoolSize,
      };
      services.AddNpgsqlDataSource(builder.ConnectionString, dataSource => dataSource.UseNodaTime());

      // One open connection per request, shared by every repository and any transaction a service begins.
      // The container disposes it at the end of the request, which rolls back an uncommitted transaction.
      services.AddScoped(provider => provider.GetRequiredService<NpgsqlDataSource>().OpenConnection());
      return services;
    }
  }
}
