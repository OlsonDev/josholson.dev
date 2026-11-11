using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Composition;

// Makes what `ServiceCollectionRegistrar` registered. Spectre.Console.Cli disposes a resolver that is disposable when the
// run ends, which is what closes the singletons that hold a connection.
public sealed class ServiceProviderResolver : ITypeResolver, IDisposable {
  private readonly ServiceProvider _provider;

  public ServiceProviderResolver(ServiceProvider provider)
    => _provider = provider;

  public object? Resolve(Type? type)
    => type is null ? null : _provider.GetService(type);

  public void Dispose()
    => _provider.Dispose();
}
