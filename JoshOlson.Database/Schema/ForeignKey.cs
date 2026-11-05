namespace JoshOlson.Database.Schema;

// A foreign key, in the order of its columns. A key that is deferrable and initially deferred is not checked until the
// transaction ends, so it does not constrain the order rows are inserted in.
public sealed record ForeignKey : INamed {
  public required string Name { get; init; }
  public required List<string> Columns { get; init; }
  public required string ReferencedTable { get; init; }
  public required List<string> ReferencedColumns { get; init; }
  public required ReferentialAction OnDelete { get; init; }
  public required ReferentialAction OnUpdate { get; init; }
  public required bool Deferrable { get; init; }
  public required bool InitiallyDeferred { get; init; }
}
