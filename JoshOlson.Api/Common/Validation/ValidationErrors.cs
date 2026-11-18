using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Common.Validation;

// Every validation error for one request, keyed by the JSON name of the field (`slug`, `children[0].label`). An error
// about the request as a whole, such as a rule across fields (the end must be after the start), is added under
// `WholeRequest`, so a client can tell it from every field's.
public sealed class ValidationErrors {
  // ASP.NET's own key for an error that belongs to no property.
  public const string WholeRequest = "";

  private readonly Dictionary<string, List<string>> _errors = [];

  public bool Any
    => _errors.Count > 0;

  public bool Has(string field)
    => _errors.ContainsKey(field);

  public void Add(string field, string message) {
    if (!_errors.TryGetValue(field, out var messages)) _errors[field] = messages = [];
    messages.Add(message);
  }

  public ValidationProblemDetails ToProblemDetails() {
    var errors = new Dictionary<string, string[]>(_errors.Count);
    foreach (var (field, messages) in _errors) errors[field] = [.. messages];
    return new(errors) {
      Status = StatusCodes.Status422UnprocessableEntity,
    };
  }
}
