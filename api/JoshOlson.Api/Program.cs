using JoshOlson.Api.Auth;
using JoshOlson.Api.Common.Binding;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Errors;
using JoshOlson.Api.Common.Time;
using JoshOlson.Api.Common.Validation;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
#if INCLUDE_TESTS
using JoshOlson.Api;
using Xunit.MicrosoftTestingPlatform;
using Xunit.Runner.InProc.SystemConsole;
#endif

#if INCLUDE_TESTS
if (IsTestRun(args)) {
  if (args.Any(arg => arg is "--server" or "--internal-msbuild-node"))
    return await TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
  return await ConsoleRunner.Run(args);
}
#endif

var builder = WebApplication.CreateBuilder(args);

// Validation attributes run once, in PayloadValidator (a failure is 422). MVC's own validation would reject the
// same request with 400 before the action runs. Binding errors (bad JSON) are still 400.
builder.Services.AddControllers(options => {
  options.ModelValidatorProviders.Clear();
  options.Filters.Add<SessionFilter>();
})
  .AddJsonOptions(options => options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb))
  .AddInputTrimming();
builder.Services.AddPayloadValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<PostgresExceptionHandler>();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddServicesAndRepositories();
builder.Services.AddClock();
builder.Services.AddAuth(builder.Configuration);

var app = builder.Build();

if (args is [EnrollCommand.Name, ..]) {
  // The token is a secret meant for the operator's terminal, not for a log sink.
#pragma warning disable RS0030
  return await EnrollCommand.RunAsync(app.Services, args[1..], Console.Out, CancellationToken.None);
#pragma warning restore RS0030
}

app.UseExceptionHandler();
app.UseAuth();
app.MapControllers();

await app.RunAsync();
return 0;

#if INCLUDE_TESTS
// `dotnet test` and IDE test runners pass Microsoft.Testing.Platform arguments the web host never gets.
static bool IsTestRun(string[] args)
  => Environment.GetEnvironmentVariable("JOSHOLSON_RUN_TESTS") is "1"
  || args.Any(arg => arg is "--server" or "--internal-msbuild-node" or "--list-tests"
    || arg.StartsWith("--dotnet-test-pipe", StringComparison.Ordinal)
    || arg.StartsWith("--results-directory", StringComparison.Ordinal));
#endif
