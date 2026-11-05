namespace JoshOlson.Database.Catalog;

// The database holds things the seed tool does not support (a column type it has no seed type for, a bad `@seed`
// comment), so a lock cannot be made from it. `Problems` lists every one the reader found, each naming the table and
// column (or the domain), and the message is the list.
public sealed class SchemaCatalogException : Exception {
  public List<string> Problems { get; } = [];

  public SchemaCatalogException() { }

  public SchemaCatalogException(string message)
    : base(message)
    => Problems.Add(message);

  public SchemaCatalogException(string message, Exception innerException)
    : base(message, innerException)
    => Problems.Add(message);

  public SchemaCatalogException(List<string> problems)
    : base(problems.Count is 1 ? problems[0] : $"The database has {problems.Count} things the seed tool does not support:\n- {string.Join("\n- ", problems)}")
    => Problems = problems;
}
