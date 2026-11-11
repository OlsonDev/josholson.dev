namespace JoshOlson.Database.Seeding;

// The IDs a layer's new rows take (ADR 0015, seed ID blocks). A layer's depth is how many parents it has (the root is 0),
// and each depth owns a block of 1000 IDs: the root `1..1000`, depth 1 `1001..2000`, depth 2 `2001..3000`, and so on, so
// the block for a depth is `depth * 1000 + 1` up to `(depth + 1) * 1000`, with no special case for the root. Siblings share
// a block because they never coexist. A block is for the rows a layer inserts; an update of a row an ancestor added keeps
// that row's ID.
public static class IdBlocks {
  public const long Size = 1000;

  public static long Start(int depth)
    => depth * Size + 1;

  public static long End(int depth)
    => (depth + 1) * Size;

  public static bool Contains(int depth, long id)
    => id >= Start(depth) && id <= End(depth);
}
