namespace JoshOlson.Database;

// A seed layer, the lock file, or the migrations are not in the shape the tool expects. The message says which and
// how to fix it.

public sealed class SeedFormatException : Exception {
  public SeedFormatException() { }

  public SeedFormatException(string message)
    : base(message) { }

  public SeedFormatException(string message, Exception innerException)
    : base(message, innerException) { }
}
