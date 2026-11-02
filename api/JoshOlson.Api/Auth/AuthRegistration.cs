using Fido2NetLib;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

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

      // Only the `www` origin, an exact match and never `*`, with credentials (ADR 0027, direct browser-to-API calls).
      // CORS adds `Vary: Origin`.
      services.AddCors(cors => cors.AddDefaultPolicy(policy => {
        var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
        policy.WithOrigins(options.AllowedOrigin)
          .AllowCredentials()
          .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
          .WithHeaders(HeaderNames.ContentType)
          .SetPreflightMaxAge(TimeSpan.FromHours(1));
      }));
      return services;
    }
  }

  extension(WebApplication app) {
    public WebApplication UseAuth() {
      // ASP.NET adds `Vary: Origin` only for a policy with several origins; the response differs by origin here too.
      app.Use(async (context, next) => {
        context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Origin);
        await next(context);
      });
      app.UseCors();
      app.UseMiddleware<OriginCheckMiddleware>();
      return app;
    }
  }
}
