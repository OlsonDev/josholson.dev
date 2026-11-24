using JoshOlson.Api.Common.Testing;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public sealed class ScopedDatabaseRegistrationTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ScopedDatabaseTestData _scopedDatabaseTestData;

  public ScopedDatabaseRegistrationTests(ScopedDatabaseTestData scopedDatabaseTestData)
    => _scopedDatabaseTestData = scopedDatabaseTestData;

  private static IConfiguration Configuration(bool scoped)
    => new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:Default"] = TestDatabase.ConnectionString,
        [ScopedDatabaseRegistration.SettingName] = scoped ? "true" : "false",
      })
      .Build();

  private static ServiceProvider Provider(bool scoped, string environment = "Development") {
    var configuration = Configuration(scoped);
    var services = new ServiceCollection();
    _ = services.AddDatabase(configuration);
    _ = services.AddScopedDatabases(configuration, new FakeEnvironment(environment));
    return services.BuildServiceProvider();
  }

  [Fact]
  public void AddScopedDatabases_SettingOff_RegistersNothing() {
    using var provider = Provider(scoped: false);

    Assert.Null(provider.GetService<ScopedDatabaseRegistry>());
    Assert.Null(provider.GetService<ScopedDatabase>());
  }

  [Fact]
  public void AddScopedDatabases_SettingOnInProduction_Throws() {
    var exception = Assert.Throws<InvalidOperationException>(() => Provider(scoped: true, Environments.Production));

    Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Connection_NoScope_IsOnTheDefaultDatabase() {
    using var provider = Provider(scoped: true);
    using var scope = provider.CreateScope();

    var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();

    Assert.Equal(new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString).Database, connection.Database);
  }

  [Fact]
  public async Task Connection_ScopeWithADatabase_IsOnThatDatabase() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);
    await using var provider = Provider(scoped: true);
    await using var scope = provider.CreateAsyncScope();
    var registry = scope.ServiceProvider.GetRequiredService<ScopedDatabaseRegistry>();
    scope.ServiceProvider.GetRequiredService<ScopedDatabase>().DataSource = await registry.FindAsync(id, _cancellationToken);

    var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();

    Assert.Equal(ScopedDatabaseRegistry.DatabaseName(id), connection.Database);
  }

  private sealed class FakeEnvironment : IHostEnvironment {
    public FakeEnvironment(string environmentName)
      => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "Foo";
    public string ContentRootPath { get; set; } = "/";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
  }
}
