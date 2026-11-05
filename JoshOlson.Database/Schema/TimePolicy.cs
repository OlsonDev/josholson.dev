namespace JoshOlson.Database.Locks;

// How a `timestamptz` column is written in a seed: as an offset from the database's anchor, or as the real instant.
// Chosen per column, never per value (a column opts out with `@seed absolute` in its comment).
public enum TimePolicy {
  Relative = 1,
  Absolute = 2,
}
