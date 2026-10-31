using System.Reflection;
using System.Runtime.CompilerServices;

namespace JoshOlson.Api.Common.DependencyInjection;

// Registers every class whose name ends in `Service` or `Repository` as scoped, so a new one needs no wiring.
// Such a class must be public, sealed, non-abstract, and non-generic, or startup fails: a typo or a forgotten
// `public` would otherwise leave it unregistered until a request hits it.
public static class ConventionRegistration {
  private static readonly string[] _Suffixes = ["Service", "Repository"];

  extension(IServiceCollection services) {
    // Scans the whole assembly. Nested types are skipped, so test fixtures can live inside test classes.
    public IServiceCollection AddServicesAndRepositories()
      => services.AddServicesAndRepositories(Assembly.GetExecutingAssembly().GetTypes().Where(type => !type.IsNested));

    public IServiceCollection AddServicesAndRepositories(IEnumerable<Type> types) {
      foreach (var type in types.Where(HasSuffix)) {
        EnsureConforms(type);
        services.AddScoped(type);
        // `FooService : IFooService` can also be injected as its interface, sharing the same instance.
        var contract = type.GetInterface($"I{type.Name}");
        if (contract is not null) services.AddScoped(contract, provider => provider.GetRequiredService(type));
      }
      return services;
    }
  }

  private static bool HasSuffix(Type type)
    => type.IsClass
    && !type.IsDefined(typeof(CompilerGeneratedAttribute))
    && _Suffixes.Any(suffix => NameWithoutArity(type).EndsWith(suffix, StringComparison.Ordinal));

  // A generic type's name carries its arity (`FooService`1`), which would hide the suffix.
  private static string NameWithoutArity(Type type)
    => type.Name.Split('`')[0];

  private static void EnsureConforms(Type type) {
    if (type is { IsVisible: true, IsSealed: true, IsAbstract: false, IsGenericTypeDefinition: false }) return;
    throw new InvalidOperationException(
      $"{type.FullName} ends in Service or Repository, so it must be a public sealed non-abstract non-generic class.");
  }
}
