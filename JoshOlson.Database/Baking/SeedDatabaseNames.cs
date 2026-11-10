using System.Text.RegularExpressions;

namespace JoshOlson.Database.Baking;

// Every database the seed tool makes has a name starting with `seed_`, so it can be told from the dev database and
// dropped by prefix alone (`seed:clean`, ADR 0033). A name is built from lowercase letters, digits, and underscores
// only, which is what lets `Quote` be a plain wrapper.
public static partial class SeedDatabaseNames {
  public const string Prefix = "seed_";
  private const string _SchemaPrefix = Prefix + "schema_";

  // The template for a given hash of the migrations and provisioning script.
  public static string Schema(string hash)
    => _SchemaPrefix + hash;

  // Where a template is built before it is renamed to its real name, so a bake that dies halfway leaves nothing a
  // later bake would mistake for a finished template.
  public static string Building(string name)
    => name + "_building";

  public static string Quote(string name)
    => name.StartsWith(Prefix, StringComparison.Ordinal) && Valid().IsMatch(name)
      ? $"\"{name}\""
      : throw new SeedFormatException($"'{name}' is not a seed tool database name (it must be seed_ and lowercase letters, digits, and underscores, at most 63 characters).");

  [GeneratedRegex("^[a-z0-9_]{1,63}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
  private static partial Regex Valid();
}
