using JoshOlson.Api.Tags;
using Xunit;

namespace JoshOlson.Api.Common.DependencyInjection;

public class ConventionRegistrationTests {
  // Fixtures the convention scan registers or rejects. They have no members, so they are not meant to be static.
#pragma warning disable MA0036
  public interface IFooService;
  public sealed class FooService : IFooService;
  public sealed class BarRepository;
  public sealed class BazHelper;
  internal sealed class QuxService;
  public class QuuxRepository;
  public abstract class CorgeService;
  public sealed class GraultService<T>;
  public static class GarplyService;
#pragma warning restore MA0036

  [Fact]
  public void RegistersMatchingClassesAsScopedSelf() {
    var services = new ServiceCollection().AddServicesAndRepositories([typeof(FooService), typeof(BarRepository)]);

    Assert.Contains(services, d => d.ServiceType == typeof(BarRepository) && d.Lifetime is ServiceLifetime.Scoped);
    Assert.Contains(services, d => d.ServiceType == typeof(FooService) && d.Lifetime is ServiceLifetime.Scoped);
  }

  [Fact]
  public void IgnoresOtherSuffixes()
    => Assert.Empty(new ServiceCollection().AddServicesAndRepositories([typeof(BazHelper)]));

  [Fact]
  public void RegistersMatchingInterfaceSharingTheInstance() {
    using var provider = new ServiceCollection()
      .AddServicesAndRepositories([typeof(FooService)])
      .BuildServiceProvider();
    using var scope = provider.CreateScope();

    var viaInterface = scope.ServiceProvider.GetRequiredService<IFooService>();

    Assert.Same(viaInterface, scope.ServiceProvider.GetRequiredService<FooService>());
  }

  [Theory]
  [InlineData(typeof(QuxService))]
  [InlineData(typeof(QuuxRepository))]
  [InlineData(typeof(CorgeService))]
  [InlineData(typeof(GraultService<>))]
  [InlineData(typeof(GarplyService))]
  public void ThrowsForNonConformingClasses(Type type)
    => Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddServicesAndRepositories([type]));

  [Fact]
  public void ScansTheAssembly() {
    var services = new ServiceCollection().AddServicesAndRepositories();

    Assert.Contains(services, d => d.ServiceType == typeof(TagService));
    Assert.Contains(services, d => d.ServiceType == typeof(TagRepository));
  }
}
