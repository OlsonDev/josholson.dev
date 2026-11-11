using System.Text.RegularExpressions;

namespace JoshOlson.Database.Baking;

// Every database the seed tool makes has a name starting with `seed_`, so it can be told from the dev database and
// dropped by prefix alone (`seed:clean`, ADR 0033). A name is built from lowercase letters, digits, and underscores
// only, which is what lets `Quote` be a plain wrapper. The dev database is the one name outside the prefix
// (`QuoteDevelopment`).
public static partial class SeedDatabaseNames {
  public const string Prefix = "seed_";
  private const string _SchemaPrefix = Prefix + "schema_";
  private const string _LayerPrefix = Prefix + "layer_";
  private const string _TestPrefix = Prefix + "test_";

  // The template for a given hash of the migrations and provisioning script.
  public static string Schema(string hash)
    => _SchemaPrefix + hash;

  // The template for a layer: a hash of the schema template's name and the layer chain's source.
  public static string Layer(string hash)
    => _LayerPrefix + hash;

  // A scoped database for one test (`seed:test <id> <layer>`). The ID is lowercase letters and digits.
  public static string Test(string id)
    => TestId().IsMatch(id)
      ? _TestPrefix + id
      : throw new SeedFormatException($"'{id}' is not a test id (lowercase letters and digits, at most 40).");

  // Where a template is built before it is renamed to its real name, so a bake that dies halfway leaves nothing a
  // later bake would mistake for a finished template.
  public static string Building(string name)
    => name + "_building";

  public static bool IsBuilding(string name)
    => name.EndsWith("_building", StringComparison.Ordinal);

  public static bool IsTemplate(string name)
    => name.StartsWith(_SchemaPrefix, StringComparison.Ordinal) || name.StartsWith(_LayerPrefix, StringComparison.Ordinal);

  public static string Quote(string name)
    => name.StartsWith(Prefix, StringComparison.Ordinal) && Valid().IsMatch(name)
      ? $"\"{name}\""
      : throw new SeedFormatException($"'{name}' is not a seed tool database name (it must be seed_ and lowercase letters, digits, and underscores, at most 63 characters).");

  // The dev database, which the tool makes and drops by a name the caller gives (POSTGRES_DB). It has to be a plain
  // lowercase name like the others, and never one of the server's own databases.
  public static string QuoteDevelopment(string name)
    => Valid().IsMatch(name) && name is not ("postgres" or "template0" or "template1")
      ? $"\"{name}\""
      : throw new SeedFormatException($"'{name}' cannot be the dev database: it must be lowercase letters, digits, and underscores, and not one of the server's own databases.");

  [GeneratedRegex("^[a-z0-9_]{1,63}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
  private static partial Regex Valid();

  [GeneratedRegex("^[a-z0-9]{1,40}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
  private static partial Regex TestId();
}
