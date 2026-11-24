#if INCLUDE_TESTS
using Npgsql;

namespace JoshOlson.Api.Common.Db;

// Lets a test choose the database a request runs on with a header (ADR 0033, a scoped database per test). It exists only
// in a build that includes the tests, and only does anything when `Testing:ScopedDatabases` is true: the production host
// is published with `-p:IncludeTests=false`, and a build with the tests refuses to start with the setting on in the
// `Production` environment.
public static class ScopedDatabaseRegistration {
  public const string SettingName = "Testing:ScopedDatabases";

  extension(IServiceCollection services) {
    // After `AddDatabase`: the request's connection is then opened on the scope's database, when it names one.
    public IServiceCollection AddScopedDatabases(IConfiguration configuration, IHostEnvironment environment) {
      if (!configuration.GetValue<bool>(SettingName)) return services;
      if (environment.IsProduction()) {
        throw new InvalidOperationException($"{SettingName} is on in the Production environment. It is for tests only.");
      }
      var connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

      _ = services.AddSingleton(provider => new ScopedDatabaseRegistry(
        provider.GetRequiredService<NpgsqlDataSource>(),
        connectionString
      ));
      _ = services.AddScoped<ScopedDatabase>();
      // Registered after `AddDatabase`'s, so it is the one a request gets.
      return services.AddScoped(provider => {
        var dataSource = provider.GetRequiredService<ScopedDatabase>().DataSource
          ?? provider.GetRequiredService<NpgsqlDataSource>();
        return dataSource.OpenConnection();
      });
    }
  }

  extension(WebApplication app) {
    // Before anything that reads from the database, and before the controllers.
    public WebApplication UseScopedDatabases() {
      if (app.Configuration.GetValue<bool>(SettingName)) _ = app.UseMiddleware<ScopedDatabaseMiddleware>();
      return app;
    }
  }
}
#endif
