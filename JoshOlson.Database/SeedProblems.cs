using JoshOlson.Shared.Results;

namespace JoshOlson.Database;

// The problems the seed tool finds with a layer, the lock, or the migrations (`Problems`), and the errors of its
// `Result`s. Thrown, they are a `SeedFormatException` holding their text, which is a failed run and not a crash.
public sealed class SeedProblems : Problems, IErrors<SeedProblems> {
  // No subject: several problems are a line each.
  public SeedProblems()
    : base(subject: null, "problems") { }

  // Several problems come under "<subject> <count> <noun>:", as a list.
  public SeedProblems(string subject, string noun = "problems")
    : base(subject, noun) { }

  // One problem by itself.
  public static SeedProblems Of(string message) {
    var problems = new SeedProblems();
    problems.Add(message);
    return problems;
  }

  public static Exception ToException(SeedProblems errors)
    => new SeedFormatException(errors.ToString());

  // For a method that still throws what it collected.
  public void ThrowIfAny() {
    if (Any) {
      throw ToException(this);
    }
  }
}
