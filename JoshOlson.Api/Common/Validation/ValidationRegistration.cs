namespace JoshOlson.Api.Common.Validation;

public static class ValidationRegistration {
  extension(IServiceCollection services) {
    public IServiceCollection AddPayloadValidation() {
      services.AddValidation();
      services.AddScoped<PayloadValidator>();
      return services;
    }
  }
}
