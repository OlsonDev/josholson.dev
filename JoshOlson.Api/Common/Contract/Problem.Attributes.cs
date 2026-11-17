using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Common.Contract;

// What an action can answer besides success, for the OpenAPI document. Every error is `application/problem+json`, which
// `[ProducesResponseType<ProblemDetails>(404)]` would not say (it would list the global `application/json`).
public static class ProblemContentType {
  public const string Name = "application/problem+json";
}

// An error with a `ProblemDetails` body: `[ProblemResponse(StatusCodes.Status404NotFound)]`.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProblemResponseAttribute : ProducesResponseTypeAttribute<ProblemDetails> {
  public ProblemResponseAttribute(int statusCode)
    : base(statusCode, ProblemContentType.Name) { }
}

// A `422`, with a `ValidationProblemDetails` body that names each field and what is wrong with it.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ValidationProblemResponseAttribute : ProducesResponseTypeAttribute<ValidationProblemDetails> {
  public ValidationProblemResponseAttribute()
    : base(StatusCodes.Status422UnprocessableEntity, ProblemContentType.Name) { }
}
