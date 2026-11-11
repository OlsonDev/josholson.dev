using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;
using Xunit;

namespace JoshOlson.Seed;

public sealed class SeedAppTests {
  [Fact]
  public void AddServices_RegistersTheRepositoryPaths() {
    var services = new ServiceCollection();

    SeedApp.AddServices(services);

    using var provider = services.BuildServiceProvider();
    Assert.Same(SeedPaths.ForRepository(), provider.GetRequiredService<SeedPaths>());
  }

  [Fact]
  public void AddServices_RegistersWhatTheCommandsTake() {
    var services = new ServiceCollection();

    SeedApp.AddServices(services);

    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    Assert.NotNull(provider.GetRequiredService<SeedToolFactory>());
    Assert.NotNull(provider.GetRequiredService<OwnerConnections>());
    Assert.Same(provider.GetRequiredService<ServerDatabases>(), provider.GetRequiredService<ServerDatabases>());
    Assert.Same(provider.GetRequiredService<ComposeTools>(), provider.GetRequiredService<ComposeTools>());
  }

  [Fact]
  public async Task Run_UnknownCommand_SaysSoAndExitsTwoWithoutAStackTrace() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "foo");

    Assert.Equal(2, exitCode);
    Assert.Contains("Unknown command 'foo'", output, StringComparison.Ordinal);
    Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(typeof(SeedFormatException), false)]
  [InlineData(typeof(SchemaCatalogException), false)]
  [InlineData(typeof(InvalidOperationException), true)]
  public void HandleException_Exception_ExitsOneAndShowsAStackTraceOnlyForABug(Type type, bool isBug) {
    var exception = (Exception)Activator.CreateInstance(type, "foo")!;
    var console = new TestConsole();
    console.Profile.Width = 200;

    var exitCode = SeedApp.HandleException(exception, console);

    Assert.Equal(1, exitCode);
    Assert.Contains("foo", console.Output, StringComparison.Ordinal);
    Assert.Equal(isBug, console.Output.Contains("InvalidOperationException", StringComparison.Ordinal));
  }
}
