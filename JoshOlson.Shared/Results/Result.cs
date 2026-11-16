namespace JoshOlson.Shared.Results;

// A value, or the errors that kept it from being made, for a method whose caller carries on after a failure (ADR 0041,
// records and union results). A method returns either one as it is (`return foo;`, `return errors;`), and a caller
// branches on the two with a `switch` expression or `is`. A failure that only the top of the program deals with is
// thrown instead.
public readonly union Result<T, TErrors>(T, TErrors)
  where T : notnull
  where TErrors : IErrors<TErrors> {
  // The value, for a caller with nothing to do about a failure: the errors are thrown as their exception, made here.
  public T OrThrow()
    => this switch {
      T value => value,
      TErrors errors => throw TErrors.ToException(errors),
    };
}
