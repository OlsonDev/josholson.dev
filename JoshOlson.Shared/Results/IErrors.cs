namespace JoshOlson.Shared.Results;

// What a `Result` holds when there is no value: a plain value that says what went wrong, never an exception. It knows
// the exception it becomes, which is built only when a caller gives up (`Result.OrThrow`), so nothing is made that may
// never be thrown and the stack trace starts where it was (ADR 0041, records and union results).
public interface IErrors<TSelf> where TSelf : IErrors<TSelf> {
  public static abstract Exception ToException(TSelf errors);
}
