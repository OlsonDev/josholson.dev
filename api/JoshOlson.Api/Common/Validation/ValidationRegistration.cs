namespace JoshOlson.Api.Common.Validation;

public static class ValidationRegistration {
  public static IServiceCollection AddPayloadValidation(this IServiceCollection services) {
    services.AddValidation();
    services.AddScoped<PayloadValidator>();
    return services;
  }
}
