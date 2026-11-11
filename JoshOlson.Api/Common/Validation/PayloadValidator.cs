using JoshOlson.Shared.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;

namespace JoshOlson.Api.Common.Validation;

// Runs the DataAnnotations attributes on a payload and collects every error, including nested objects and list
// items. The payload type must be marked [ValidatableType], which makes a source generator emit its rules.
// Wraps Microsoft.Extensions.Validation, which changed between .NET 10 and 11, so a change there stays in this file.
public sealed class PayloadValidator {
  private readonly ValidationOptions _options;
  private readonly IServiceProvider _services;

  public PayloadValidator(IOptions<ValidationOptions> options, IServiceProvider services) {
    _options = options.Value;
    _services = services;
  }

  public async Task<ValidationErrors> ValidateAsync(object payload, CancellationToken cancellationToken) {
    // A type without [ValidatableType] would otherwise pass every time.
    if (!_options.TryGetValidatableTypeInfo(payload.GetType(), out var info))
      throw new InvalidOperationException($"{payload.GetType().FullName} is not marked [ValidatableType].");

    var context = new ValidateContext {
      ValidationOptions = _options,
      ServiceProvider = _services,
    };
    await info.ValidateAsync(payload, context, cancellationToken);

    var errors = new ValidationErrors();
    if (context.ValidationErrors is { } found)
      foreach (var (path, validationErrors) in found)
        foreach (var validationError in validationErrors)
          errors.Add(path.ToCamelCasePath(), validationError.ErrorMessage);
    return errors;
  }
}
