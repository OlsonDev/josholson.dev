namespace JoshOlson.Database.Layers;

// A layer name ends up in database names, so it is lower-kebab-case: letters, digits, and single hyphens.
public static class LayerName {
  public static bool IsValid(string name)
    => name.Length is > 0 and <= 40
    && name[0] is >= 'a' and <= 'z'
    && name[^1] is not '-'
    && !name.Contains("--", StringComparison.Ordinal)
    && name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
