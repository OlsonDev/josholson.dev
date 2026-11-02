using System.Reflection;
using JoshOlson.Api.Auth;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using NodaTime;

namespace JoshOlson.Api.Common.Testing;

// Xunit.DependencyInjection finds this class and builds one container for the run. Each test gets its own scope, so
// its constructor can take any service below: scoped ones (the open connection, every `*Service` and `*Repository`)
// are fresh per test and disposed when it ends. The registrations are the API's own, against the dev database.
public static class Startup {
  public static void ConfigureServices(IServiceCollection services) {
    var settings = new Dictionary<string, string?> {
      ["ConnectionStrings:Default"] = TestDatabase.ConnectionString,
    };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    services
      .AddLogging()
      .AddDatabase(configuration)
      .AddServicesAndRepositories()
      .AddPayloadValidation()
      .AddAuth(configuration);

    // A clock the test moves by hand, shared with the services in its scope, where the API uses the system clock.
    // `Auth` is absent from the test configuration, so the options are at their defaults (`localhost`).
    services.AddScoped<FakeClock>();
    services.AddScoped<IClock>(provider => provider.GetRequiredService<FakeClock>());

    // Helpers that build and remove a feature's test data are named `*TestData` and registered the same way.
    var testDataTypes = Assembly.GetExecutingAssembly().GetTypes().Where(IsTestData);
    foreach (var type in testDataTypes) services.AddScoped(type);

    // The whole API in memory, started on first use, so a test that never asks for it pays nothing.
    services.AddScoped(_ => new WebApplicationFactory<Program>().WithWebHostBuilder(
      builder => builder.UseSetting("ConnectionStrings:Default", TestDatabase.ConnectionString)));
    services.AddScoped(provider => provider.GetRequiredService<WebApplicationFactory<Program>>().CreateClient());
  }

  private static bool IsTestData(Type type)
    => type is { IsClass: true, IsAbstract: false, IsNested: false }
    && type.Name.EndsWith("TestData", StringComparison.Ordinal);
}
