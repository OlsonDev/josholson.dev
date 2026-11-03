namespace JoshOlson.Database.Layers;

public sealed class SeedFormatException : Exception {
  public SeedFormatException() { }

  public SeedFormatException(string message)
    : base(message) { }

  public SeedFormatException(string message, Exception innerException)
    : base(message, innerException) { }
}
