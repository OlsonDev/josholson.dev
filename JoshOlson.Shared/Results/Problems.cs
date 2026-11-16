using JoshOlson.Shared.Extensions;

namespace JoshOlson.Shared.Results;

// The problems found with some input, each a sentence, collected so they can all be reported together. A project has
// its own sealed kind, which is the errors of its `Result`s (`IErrors`) and says which exception they become.
//
// `subject` is how the text introduces several: "The foos have" gives "The foos have 2 problems:" and a list. Without
// one, they are a line each. One problem is always just its sentence.
public abstract class Problems {
  private readonly string? _subject;
  private readonly string _noun;

  protected Problems(string? subject, string noun) {
    _subject = subject;
    _noun = noun;
  }

  public List<string> Messages { get; } = [];

  public int Count
    => Messages.Count;

  public bool Any
    => Messages.Count > 0;

  public void Add(string message)
    => Messages.Add(message);

  public void AddRange(IEnumerable<string> messages)
    => Messages.AddRange(messages);

  public override string ToString()
    => Messages.Count < 2 || _subject is null
      ? string.Join("\n", Messages)
      : $"{_subject} {Messages.Count.ToInvariantString()} {_noun}:\n- {string.Join("\n- ", Messages)}";
}
