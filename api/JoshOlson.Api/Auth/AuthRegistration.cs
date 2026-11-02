using Fido2NetLib;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

public static class AuthRegistration {
  extension(IServiceCollection services) {
    public IServiceCollection AddAuth(IConfiguration configuration) {
      services.AddOptions<AuthOptions>()
        .Bind(configuration.GetSection(AuthOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

      services.AddSingleton<IFido2>(provider => {
        var options = provider.GetRequiredService<IOptions<AuthOptions>>().Value;
        return new Fido2(new Fido2Configuration {
          RPID = options.RelyingPartyId,
          RPName = options.RelyingPartyName,
          Origins = new HashSet<string>(StringComparer.Ordinal) { options.AllowedOrigin },
        });
      });
      services.AddSingleton(typeof(SingleUseStore<>));
      services.AddSingleton<AaguidLookup>();
      return services;
    }
  }
}
