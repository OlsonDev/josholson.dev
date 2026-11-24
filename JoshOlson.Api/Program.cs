using JoshOlson.Api.Auth;
using JoshOlson.Api.Common.Binding;
using JoshOlson.Api.Common.Contract;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Errors;
using JoshOlson.Api.Common.Time;
using JoshOlson.Api.Common.Validation;
using Microsoft.AspNetCore.Mvc;
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
  // The API speaks JSON only, so the contract lists one media type, not text/plain and text/json as well.
  options.Filters.Add(new ProducesAttribute("application/json"));
})
  .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions))
  .AddInputTrimming();
// The OpenAPI generator reads these options, not MVC's, so the contract's settings go to both.
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddOpenApi(
  OpenApiCommand.DocumentName,
  options => options
    .AddSchemaTransformer<ContractSchemaTransformer>()
    .AddOperationTransformer<ContractOperationTransformer>()
);
builder.Services.AddPayloadValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<PostgresExceptionHandler>();
builder.Services.AddDatabase(builder.Configuration);
#if INCLUDE_TESTS
builder.Services.AddScopedDatabases(builder.Configuration, builder.Environment);
#endif
builder.Services.AddServicesAndRepositories();
builder.Services.AddClock();
builder.Services.AddAuth(builder.Configuration);

var app = builder.Build();

if (args is [OpenApiCommand.Name, ..]) {
  // The document is the command's output, for a pipe or a file, not a log message.
#pragma warning disable RS0030
  return await OpenApiCommand.RunAsync(app.Services, args[1..], Console.Out, CancellationToken.None);
#pragma warning restore RS0030
}

app.UseExceptionHandler();
#if INCLUDE_TESTS
app.UseScopedDatabases();
#endif
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
