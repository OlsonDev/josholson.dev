using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Common.Validation;

// Every validation error for one request, keyed by the JSON name of the field (`slug`, `children[0].label`).
public sealed class ValidationErrors {
  private readonly Dictionary<string, List<string>> _errors = [];

  public bool Any => _errors.Count > 0;

  public bool Has(string field)
    => _errors.ContainsKey(field);

  public void Add(string field, string message) {
    if (!_errors.TryGetValue(field, out var messages)) _errors[field] = messages = [];
    messages.Add(message);
  }

  public ValidationProblemDetails ToProblemDetails()
    => new(_errors.ToDictionary(error => error.Key, error => error.Value.ToArray())) {
      Status = StatusCodes.Status422UnprocessableEntity,
    };
}
