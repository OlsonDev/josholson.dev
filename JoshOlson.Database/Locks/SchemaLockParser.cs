using System.Globalization;
using JoshOlson.Database.Yaml;
using JoshOlson.Shared.Extensions;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Locks;

// Reads the lock back (the shape `SchemaLockWriter` documents), refusing anything the tool would not have written:
// unknown keys, versions out of order, a checksum that is not 64 lowercase hex digits, a type the seeds do not support,
// a primary key with gaps, or a `timestamptz` column without a policy. A hand edit is noticed here, not later.
public static class SchemaLockParser {
  private const string _Context = "schema.lock.yaml";
  private static readonly string[] _LockKeys = [
    "version",
    "migrations",
    "tables",
  ];
  private static readonly string[] _MigrationKeys = [
    "version",
    "file",
    "checksum",
    "accepted_checksums",
  ];
  private static readonly string[] _ColumnKeys = [
    "name",
    "type",
    "nullable",
    "primary_key",
    "policy",
  ];

  public static SchemaLock Parse(string yaml) {
    var root = YamlNodes.LoadRoot(_Context, yaml);
    YamlNodes.CheckKeys(_Context, "the lock", root, _LockKeys);
    var version = WholeNumber("version", YamlNodes.Text(_Context, "version", YamlNodes.Required(_Context, root, "version")), 0);
    var migrations = ParseMigrations(version, YamlNodes.Required(_Context, root, "migrations"));
    var tables = ParseTables(YamlNodes.Required(_Context, root, "tables"));
    return new() {
      Version = version,
      Migrations = migrations,
      Tables = tables,
    };
  }

  private static List<LockedMigration> ParseMigrations(int lockVersion, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, "migrations", node);
    List<LockedMigration> migrations = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, "a migration", item);
      YamlNodes.CheckKeys(_Context, "a migration", map, _MigrationKeys);
      var version = WholeNumber("a migration's version", YamlNodes.Text(_Context, "version", YamlNodes.Required(_Context, map, "version")), 1);
      var file = YamlNodes.Text(_Context, "file", YamlNodes.Required(_Context, map, "file"));
      if (!MigrationFiles.TryParseVersion(file, out var fileVersion) || fileVersion != version) {
        throw new SeedFormatException($"{_Context}: '{file}' is not the file of migration version {version.ToInvariantString()}.");
      }
      if (version > lockVersion) {
        throw new SeedFormatException($"{_Context}: migration {version.ToInvariantString()} is newer than the locked version {lockVersion.ToInvariantString()}.");
      }
      if (migrations.Count > 0 && migrations[^1].Version >= version) {
        throw new SeedFormatException($"{_Context}: migration {version.ToInvariantString()} is out of order or repeated.");
      }

      List<string> acceptedChecksums = [];
      if (YamlNodes.Find(map, "accepted_checksums") is { } acceptedNode) {
        foreach (var entry in YamlNodes.Sequence(_Context, $"{file} accepted_checksums", acceptedNode).Children) {
          var checksum = Checksum(file, YamlNodes.Text(_Context, $"{file} accepted_checksums", entry));
          if (acceptedChecksums.Contains(checksum, StringComparer.Ordinal)) {
            throw new SeedFormatException($"{_Context}: {file} accepts the same checksum twice.");
          }
          acceptedChecksums.Add(checksum);
        }
      }
      migrations.Add(new() {
        Version = version,
        File = file,
        Checksum = Checksum(file, YamlNodes.Text(_Context, "checksum", YamlNodes.Required(_Context, map, "checksum"))),
        AcceptedChecksums = acceptedChecksums,
      });
    }
    return migrations;
  }

  private static List<LockedTable> ParseTables(YamlNode node) {
    var map = YamlNodes.Mapping(_Context, "tables", node);
    List<LockedTable> tables = new(map.Children.Count);
    foreach (var (key, value) in map.Children) {
      var name = YamlNodes.Key(_Context, key);
      if (name.Split('.') is not [{ Length: > 0 }, { Length: > 0 }]) {
        throw new SeedFormatException($"{_Context}: '{name}' is not a schema.table name.");
      }
      if (tables.Any(table => string.Equals(table.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: table {name} is listed twice.");
      }
      tables.Add(new() {
        Name = name,
        Columns = ParseColumns(name, value),
      });
    }
    return tables;
  }

  private static List<LockedColumn> ParseColumns(string table, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{table} columns", node);
    List<LockedColumn> columns = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a column of {table}", item);
      YamlNodes.CheckKeys(_Context, $"a column of {table}", map, _ColumnKeys);
      var name = YamlNodes.Text(_Context, "name", YamlNodes.Required(_Context, map, "name"));
      var where = $"{table}.{name}";
      if (columns.Any(column => string.Equals(column.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: column {where} is listed twice.");
      }
      var typeText = YamlNodes.Text(_Context, "type", YamlNodes.Required(_Context, map, "type"));
      if (!ColumnType.TryParse(typeText, out var type)) {
        throw new SeedFormatException($"{_Context}: {where} has type '{typeText}', which the seed tool does not support.");
      }

      var nullable = YamlNodes.Find(map, "nullable") is { } nullableNode
        && ParseFlag(where, "nullable", YamlNodes.Text(_Context, "nullable", nullableNode));
      int? position = YamlNodes.Find(map, "primary_key") is { } positionNode
        ? WholeNumber($"{where} primary_key", YamlNodes.Text(_Context, "primary_key", positionNode), 1)
        : null;
      TimePolicy? policy = YamlNodes.Find(map, "policy") is { } policyNode
        ? ParsePolicy(where, YamlNodes.Text(_Context, "policy", policyNode))
        : null;
      var isTimestamp = type.Element is SeedType.TimestampTz;
      if (isTimestamp != policy.HasValue) {
        throw new SeedFormatException($"{_Context}: {where} must have a policy if and only if it is a timestamptz column.");
      }
      columns.Add(new() {
        Name = name,
        Type = type,
        Nullable = nullable,
        PrimaryKeyPosition = position,
        Policy = policy,
      });
    }

    var positions = columns.Where(column => column.PrimaryKeyPosition.HasValue).Select(column => column.PrimaryKeyPosition!.Value).Order().ToList();
    if (!positions.SequenceEqual(Enumerable.Range(1, positions.Count))) {
      throw new SeedFormatException($"{_Context}: the primary key positions of {table} must be 1 to {positions.Count.ToInvariantString()}, each once.");
    }
    return columns;
  }

  private static int WholeNumber(string what, string text, int minimum)
    => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum
      ? number
      : throw new SeedFormatException($"{_Context}: {what} must be a whole number of at least {minimum.ToInvariantString()}, not '{text}'.");

  private static bool ParseFlag(string where, string key, string text)
    => text switch {
      "true" => true,
      "false" => false,
      _ => throw new SeedFormatException($"{_Context}: {where} {key} must be true or false, not '{text}'."),
    };

  private static TimePolicy ParsePolicy(string where, string text)
    => text switch {
      "relative" => TimePolicy.Relative,
      "absolute" => TimePolicy.Absolute,
      _ => throw new SeedFormatException($"{_Context}: {where} policy must be relative or absolute, not '{text}'."),
    };

  private static string Checksum(string file, string text)
    => text.Length is 64 && text.All(char.IsAsciiHexDigitLower)
      ? text
      : throw new SeedFormatException($"{_Context}: a checksum of {file} is not 64 lowercase hex digits.");
}
