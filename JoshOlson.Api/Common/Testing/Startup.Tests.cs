using System.Reflection;
using JoshOlson.Api.Auth;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NodaTime;

namespace JoshOlson.Api.Common.Testing;

// Xunit.DependencyInjection finds this class and builds one container for the run. Each test gets its own scope, so
// its constructor can take any service below: scoped ones (the open connection, every `*Service` and `*Repository`)
// are fresh per test and disposed when it ends. The registrations are the API's own, against the dev database.
public static class Startup {
  public static void ConfigureServices(IServiceCollection services) {
    // Shared by the container below and the API under test. Plain HTTP on localhost cannot carry a `Secure` cookie, so
    // the cookie drops that flag and, with it, the `__Secure-` name prefix. The rest of `Auth` is at its defaults.
    var settings = new Dictionary<string, string?> {
      ["ConnectionStrings:Default"] = TestDatabase.ConnectionString,
      ["Auth:CookieName"] = "session",
      ["Auth:CookieSecure"] = "false",
    };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    services
      .AddLogging()
      .AddDatabase(configuration)
      .AddServicesAndRepositories()
      .AddPayloadValidation()
      .AddAuth(configuration);

    // A clock the test moves by hand, shared with the services in its scope, where the API uses the system clock. The
    // stores that hold ceremony state are per scope too, so they see that clock and one test cannot see another's.
    services.AddScoped<FakeClock>();
    services.AddScoped<IClock>(provider => provider.GetRequiredService<FakeClock>());
    services.AddScoped(typeof(SingleUseStore<>));

    // Helpers that build and remove a feature's test data are named `*TestData` and registered the same way.
    var testDataTypes = Assembly.GetExecutingAssembly().GetTypes().Where(IsTestData);
    foreach (var type in testDataTypes) services.AddScoped(type);

    // The whole API in memory, started on first use, so a test that never asks for it pays nothing. `UseSetting`, not
    // `ConfigureAppConfiguration`: `Program` reads some settings before that callback runs.
    services.AddScoped(_ => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
      foreach (var (key, value) in settings) builder.UseSetting(key, value);
    }));

    // A browser that has not signed in. It sends the allowed `Origin` the way the site's pages do, which every request
    // that changes state needs.
    services.AddScoped(provider => {
      var client = provider.GetRequiredService<WebApplicationFactory<Program>>().CreateClient();
      var options = provider.GetRequiredService<IOptions<AuthOptions>>().Value;
      client.DefaultRequestHeaders.Add(HeaderNames.Origin, options.AllowedOrigin);
      return client;
    });
  }

  private static bool IsTestData(Type type)
    => type is { IsClass: true, IsAbstract: false, IsNested: false }
    && type.Name.EndsWith("TestData", StringComparison.Ordinal);
}
