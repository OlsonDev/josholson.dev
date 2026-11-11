using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JoshOlson.Seed.Composition;

public sealed class ServiceProviderResolverTests {
  private static ServiceProviderResolver Resolver(Action<IServiceCollection> add) {
    var services = new ServiceCollection();
    add(services);
    return new(services.BuildServiceProvider());
  }

  [Fact]
  public void Resolve_NullType_ReturnsNull()
    => Assert.Null(Resolver(_ => { }).Resolve(null));

  [Fact]
  public void Resolve_UnregisteredType_ReturnsNull()
    => Assert.Null(Resolver(_ => { }).Resolve(typeof(Foo)));

  [Fact]
  public void Resolve_RegisteredType_ReturnsTheSingleton() {
    var resolver = Resolver(services => services.AddSingleton<Foo>());

    Assert.Same(resolver.Resolve(typeof(Foo)), resolver.Resolve(typeof(Foo)));
  }

  [Fact]
  public void Dispose_DisposesWhatItMade() {
    var resolver = Resolver(services => services.AddSingleton<Foo>());
    var foo = (Foo)resolver.Resolve(typeof(Foo))!;

    resolver.Dispose();

    Assert.True(foo.Disposed);
  }

  private sealed class Foo : IDisposable {
    public bool Disposed { get; private set; }

    public void Dispose()
      => Disposed = true;
  }
}
