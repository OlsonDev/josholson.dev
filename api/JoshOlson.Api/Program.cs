using Dapper;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Errors;
using JoshOlson.Api.Common.Validation;

namespace JoshOlson.Api;

public sealed class Program {
#if INCLUDE_TESTS
  public static async Task<int> Main(string[] args) {
    if (IsTestRun(args)) {
      if (args.Any(arg => arg is "--server" or "--internal-msbuild-node"))
        return await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(
          args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
      return await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args);
    }
    RunApp(args);
    return 0;
  }
#else
  public static void Main(string[] args)
    => RunApp(args);
#endif

  // `dotnet test` and IDE test runners pass Microsoft.Testing.Platform arguments the web host never gets.
  private static bool IsTestRun(string[] args)
    => Environment.GetEnvironmentVariable("JOSHOLSON_RUN_TESTS") is "1"
    || args.Any(arg => arg is "--server" or "--internal-msbuild-node" or "--list-tests"
      || arg.StartsWith("--dotnet-test-pipe", StringComparison.Ordinal)
      || arg.StartsWith("--results-directory", StringComparison.Ordinal));

  private static void RunApp(string[] args) {
    DefaultTypeMap.MatchNamesWithUnderscores = true;

    var builder = WebApplication.CreateBuilder(args);

    // Validation attributes run once, in PayloadValidator (a failure is 422). MVC's own validation would reject the
    // same request with 400 before the action runs. Binding errors (bad JSON) are still 400.
    builder.Services.AddControllers(options => options.ModelValidatorProviders.Clear());
    builder.Services.AddPayloadValidation();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<PostgresExceptionHandler>();
    builder.Services.AddDatabase(builder.Configuration);
    builder.Services.AddServicesAndRepositories();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.MapControllers();

    app.Run();
  }
}
