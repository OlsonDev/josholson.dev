namespace JoshOlson.Database.Schema;

// A CHECK constraint by name, with its definition as the catalog prints it (`pg_get_constraintdef`), kept as text and
// never interpreted: the lock records it so that a change to a rule is noticed.
public sealed record CheckConstraint {
  public required string Name { get; init; }
  public required string Expression { get; init; }
}
