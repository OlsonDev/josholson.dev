using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

// A layer name ends up in database names, so it is kebab-case (the database's text.kebab_case) that starts with a
// lowercase letter and is at most 40 characters.
public static class LayerName {
  // Not a layer: the schema with no rows in it, which a database made by the migrations alone already is. A layer cannot
  // have the name, extend it, or be exported under it, because a parent would put its children one block of IDs up (a
  // layer's depth is how many parents it has), and the point is that what is made from nothing exports as a root, in the
  // first block, with no `extends`. `seed blank`, `seed test <id> blank`, and `seed template blank` ask for it.
  public const string Blank = "blank";

  public static bool IsValid(string name)
    => name.IsKebabCase() && name.Length <= 40 && char.IsAsciiLetterLower(name[0]);

  public static bool IsReserved(string name)
    => string.Equals(name, Blank, StringComparison.Ordinal);
}
