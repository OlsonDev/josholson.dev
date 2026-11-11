using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JoshOlson.Seed.Composition;

public sealed class ServiceCollectionRegistrarTests {
  [Fact]
  public void Register_ServiceAndImplementation_ResolvesTheImplementationOnce() {
    var registrar = new ServiceCollectionRegistrar(new ServiceCollection());
    registrar.Register(typeof(IFoo), typeof(Foo));

    var resolver = registrar.Build();

    Assert.IsType<Foo>(resolver.Resolve(typeof(IFoo)));
    Assert.Same(resolver.Resolve(typeof(IFoo)), resolver.Resolve(typeof(IFoo)));
  }

  [Fact]
  public void RegisterInstance_Instance_ResolvesIt() {
    var foo = new Foo();
    var registrar = new ServiceCollectionRegistrar(new ServiceCollection());
    registrar.RegisterInstance(typeof(IFoo), foo);

    Assert.Same(foo, registrar.Build().Resolve(typeof(IFoo)));
  }

  [Fact]
  public void RegisterLazy_Factory_RunsItOnlyWhenFirstResolved() {
    var made = 0;
    var registrar = new ServiceCollectionRegistrar(new ServiceCollection());
    registrar.RegisterLazy(typeof(IFoo), () => {
      made++;
      return new Foo();
    });
    var resolver = registrar.Build();
    var before = made;

    _ = resolver.Resolve(typeof(IFoo));
    _ = resolver.Resolve(typeof(IFoo));

    Assert.Equal(0, before);
    Assert.Equal(1, made);
  }

  [Fact]
  public void Build_ServiceWithAnUnregisteredDependency_Throws() {
    var registrar = new ServiceCollectionRegistrar(new ServiceCollection());
    registrar.Register(typeof(Bar), typeof(Bar));

    _ = Assert.Throws<AggregateException>(registrar.Build);
  }

  private interface IFoo;

  private sealed class Foo : IFoo;

  private sealed class Bar {
    public Bar(IFoo foo)
      => Foo = foo;

    public IFoo Foo { get; }
  }
}
