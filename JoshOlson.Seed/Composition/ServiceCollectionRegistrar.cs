using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Composition;

// Spectre.Console.Cli ships no container: it asks an `ITypeRegistrar` to register what it needs (the command classes, the
// console) and an `ITypeResolver` to make them, so a command's constructor is filled from the same container as the rest
// of the tool. Everything is a singleton: a run is one command in one process, so the process is the scope.
public sealed class ServiceCollectionRegistrar : ITypeRegistrar {
  private readonly IServiceCollection _services;

  public ServiceCollectionRegistrar(IServiceCollection services)
    => _services = services;

  public ITypeResolver Build()
    => new ServiceProviderResolver(_services.BuildServiceProvider(new ServiceProviderOptions {
      ValidateOnBuild = true,
      ValidateScopes = true,
    }));

  public void Register(Type service, Type implementation)
    => _ = _services.AddSingleton(service, implementation);

  public void RegisterInstance(Type service, object implementation)
    => _ = _services.AddSingleton(service, implementation);

  public void RegisterLazy(Type service, Func<object> factory)
    => _ = _services.AddSingleton(service, _ => factory());
}
