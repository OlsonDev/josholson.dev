using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

// A layer name ends up in database names, so it is kebab-case (the database's text.kebab_case) that starts with a
// lowercase letter and is at most 40 characters.
public static class LayerName {
  public static bool IsValid(string name)
    => name.IsKebabCase() && name.Length <= 40 && char.IsAsciiLetterLower(name[0]);
}
