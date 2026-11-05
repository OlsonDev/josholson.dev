using System.Globalization;
using JoshOlson.Database.Schema;
using JoshOlson.Database.Yaml;
using JoshOlson.Shared.Extensions;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Locks;

// Reads the lock back (the shape `SchemaLockWriter` documents), refusing anything the tool would not have written:
// unknown keys, versions out of order, a checksum that is not 64 lowercase hex digits, a type the seeds do not support,
// a primary key that names a missing, repeated or nullable column, a `timestamptz` column without a policy, or a foreign
// key to a table or column that is not there. A hand edit is noticed here, not later.
public static class SchemaLockParser {
  private const string _Context = "schema.lock.yaml";
  private static readonly string[] _LockKeys = [
    "version",
    "migrations",
    "domains",
    "tables",
  ];
  private static readonly string[] _DomainKeys = [
    "type",
    "checks",
  ];
  private static readonly string[] _CheckKeys = [
    "name",
    "expression",
  ];
  private static readonly string[] _MigrationKeys = [
    "version",
    "file",
    "checksum",
    "accepted_checksums",
  ];
  private static readonly string[] _TableKeys = [
    "columns",
    "primary_key",
    "foreign_keys",
  ];
  private static readonly string[] _ColumnKeys = [
    "name",
    "type",
    "nullable",
    "generated_as",
    "policy",
  ];
  private static readonly string[] _ForeignKeyKeys = [
    "name",
    "columns",
    "references",
    "referenced_columns",
    "on_delete",
    "on_update",
    "deferrable",
    "deferred",
  ];

  public static SchemaLock Parse(string yaml) {
    var root = YamlNodes.LoadRoot(_Context, yaml);
    YamlNodes.CheckKeys(_Context, "the lock", root, _LockKeys);
    var version = WholeNumber("version", YamlNodes.RequiredText(_Context, root, "version"), 0);
    var migrations = ParseMigrations(version, YamlNodes.Required(_Context, root, "migrations"));
    var domains = YamlNodes.Find(root, "domains") is { } domainsNode ? ParseDomains(domainsNode) : [];
    var tables = ParseTables(YamlNodes.Required(_Context, root, "tables"));
    CheckTypes(domains, tables);
    CheckReferences(tables);
    return new() {
      Version = version,
      Migrations = migrations,
      Domains = domains,
      Tables = tables,
    };
  }

  private static List<LockedMigration> ParseMigrations(int lockVersion, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, "migrations", node);
    List<LockedMigration> migrations = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, "a migration", item);
      YamlNodes.CheckKeys(_Context, "a migration", map, _MigrationKeys);
      var version = WholeNumber("a migration's version", YamlNodes.RequiredText(_Context, map, "version"), 1);
      var file = YamlNodes.RequiredText(_Context, map, "file");
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
        Checksum = Checksum(file, YamlNodes.RequiredText(_Context, map, "checksum")),
        AcceptedChecksums = acceptedChecksums,
      });
    }
    return migrations;
  }

  private static List<Domain> ParseDomains(YamlNode node) {
    var map = YamlNodes.Mapping(_Context, "domains", node);
    List<Domain> domains = new(map.Children.Count);
    foreach (var (key, value) in map.Children) {
      var name = YamlNodes.Key(_Context, key);
      if (!ColumnType.IsDomainName(name)) {
        throw new SeedFormatException($"{_Context}: '{name}' is not a schema.name domain name.");
      }
      if (domains.Any(domain => string.Equals(domain.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: domain {name} is listed twice.");
      }
      var domainMap = YamlNodes.Mapping(_Context, $"domain {name}", value);
      YamlNodes.CheckKeys(_Context, $"domain {name}", domainMap, _DomainKeys);
      var typeText = YamlNodes.RequiredText(_Context, domainMap, "type");
      if (!ColumnType.TryParse(typeText, out var type)) {
        throw new SeedFormatException($"{_Context}: domain {name} has type '{typeText}', which the seed tool does not support.");
      }
      domains.Add(new() {
        Name = name,
        Type = type,
        Checks = YamlNodes.Find(domainMap, "checks") is { } checksNode ? ParseChecks(name, checksNode) : [],
      });
    }

    var byName = domains.ToDictionary(domain => domain.Name, StringComparer.Ordinal);
    foreach (var domain in domains.Where(domain => DomainResolver.Resolve(domain.Type, byName) is null)) {
      throw new SeedFormatException(
        $"{_Context}: domain {domain.Name} is built on {domain.Type}, which is not a supported type, a domain in the lock, an array of a domain that is itself an array, or part of a loop of domains.");
    }
    return domains;
  }

  private static List<CheckConstraint> ParseChecks(string domain, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{domain} checks", node);
    List<CheckConstraint> checks = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a check of {domain}", item);
      YamlNodes.CheckKeys(_Context, $"a check of {domain}", map, _CheckKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      if (checks.Any(check => string.Equals(check.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: domain {domain} has two checks named {name}.");
      }
      var expression = YamlNodes.RequiredText(_Context, map, "expression");
      if (expression.Length is 0) {
        throw new SeedFormatException($"{_Context}: check {name} of domain {domain} has no expression.");
      }
      checks.Add(new() {
        Name = name,
        Expression = expression,
      });
    }
    return checks;
  }

  private static List<Table> ParseTables(YamlNode node) {
    var map = YamlNodes.Mapping(_Context, "tables", node);
    List<Table> tables = new(map.Children.Count);
    foreach (var (key, value) in map.Children) {
      var name = YamlNodes.Key(_Context, key);
      if (!IsQualifiedName(name)) {
        throw new SeedFormatException($"{_Context}: '{name}' is not a schema.table name.");
      }
      if (tables.Any(table => string.Equals(table.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: table {name} is listed twice.");
      }
      tables.Add(ParseTable(name, value));
    }
    return tables;
  }

  private static Table ParseTable(string name, YamlNode node) {
    var map = YamlNodes.Mapping(_Context, $"table {name}", node);
    YamlNodes.CheckKeys(_Context, $"table {name}", map, _TableKeys);
    var columns = ParseColumns(name, YamlNodes.Required(_Context, map, "columns"));
    var primaryKey = YamlNodes.Find(map, "primary_key") is { } primaryKeyNode
      ? ParsePrimaryKey(name, columns, primaryKeyNode)
      : [];
    var foreignKeys = YamlNodes.Find(map, "foreign_keys") is { } foreignKeysNode
      ? ParseForeignKeys(name, columns, foreignKeysNode)
      : [];
    return new() {
      Name = name,
      Columns = columns,
      PrimaryKey = primaryKey,
      ForeignKeys = foreignKeys,
    };
  }

  // The key's columns must exist, appear once, and be NOT NULL (which Postgres enforces for a primary key).
  private static List<string> ParsePrimaryKey(string table, List<Column> columns, YamlNode node) {
    var where = $"{table} primary key";
    var names = Names(where, "primary_key", node);
    for (var index = 0; index < names.Count; index++) {
      var name = names[index];
      var column = columns.Find(known => string.Equals(known.Name, name, StringComparison.Ordinal))
        ?? throw new SeedFormatException($"{_Context}: {where} uses column '{name}', which {table} does not have.");
      if (column.Nullable) {
        throw new SeedFormatException($"{_Context}: {where} uses column '{name}', which is nullable.");
      }
      if (names.IndexOf(name) != index) {
        throw new SeedFormatException($"{_Context}: {where} uses column '{name}' twice.");
      }
    }
    return names;
  }

  private static List<Column> ParseColumns(string table, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{table} columns", node);
    if (sequence.Children.Count is 0) {
      throw new SeedFormatException($"{_Context}: table {table} has no columns.");
    }
    List<Column> columns = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a column of {table}", item);
      YamlNodes.CheckKeys(_Context, $"a column of {table}", map, _ColumnKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      var where = $"{table}.{name}";
      if (columns.Any(column => string.Equals(column.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: column {where} is listed twice.");
      }
      var typeText = YamlNodes.RequiredText(_Context, map, "type");
      if (!ColumnType.TryParse(typeText, out var type)) {
        throw new SeedFormatException($"{_Context}: {where} has type '{typeText}', which the seed tool does not support.");
      }

      columns.Add(new() {
        Name = name,
        Type = type,
        Nullable = OptionalFlag(where, map, "nullable"),
        GeneratedAs = OptionalWord<GeneratedAs>(where, map, "generated_as"),
        Policy = OptionalWord<TimePolicy>(where, map, "policy"),
      });
    }
    return columns;
  }

  private static List<ForeignKey> ParseForeignKeys(string table, List<Column> columns, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{table} foreign_keys", node);
    List<ForeignKey> keys = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a foreign key of {table}", item);
      YamlNodes.CheckKeys(_Context, $"a foreign key of {table}", map, _ForeignKeyKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      var where = $"{table} foreign key {name}";
      if (keys.Any(key => string.Equals(key.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: {where} is listed twice.");
      }

      var keyColumns = Names(where, "columns", YamlNodes.Required(_Context, map, "columns"));
      foreach (var column in keyColumns.Where(column => !columns.Any(known => string.Equals(known.Name, column, StringComparison.Ordinal)))) {
        throw new SeedFormatException($"{_Context}: {where} uses column '{column}', which {table} does not have.");
      }
      var referencedTable = YamlNodes.RequiredText(_Context, map, "references");
      if (!IsQualifiedName(referencedTable)) {
        throw new SeedFormatException($"{_Context}: {where} references '{referencedTable}', which is not a schema.table name.");
      }
      var referencedColumns = Names(where, "referenced_columns", YamlNodes.Required(_Context, map, "referenced_columns"));
      if (referencedColumns.Count != keyColumns.Count) {
        throw new SeedFormatException($"{_Context}: {where} has {keyColumns.Count.ToInvariantString()} column(s) but {referencedColumns.Count.ToInvariantString()} referenced column(s).");
      }

      var deferrable = OptionalFlag(where, map, "deferrable");
      var deferred = OptionalFlag(where, map, "deferred");
      if (deferred && !deferrable) {
        throw new SeedFormatException($"{_Context}: {where} is deferred but not deferrable.");
      }
      keys.Add(new() {
        Name = name,
        Columns = keyColumns,
        ReferencedTable = referencedTable,
        ReferencedColumns = referencedColumns,
        OnDelete = OptionalWord<ReferentialAction>(where, map, "on_delete") ?? ReferentialAction.NoAction,
        OnUpdate = OptionalWord<ReferentialAction>(where, map, "on_update") ?? ReferentialAction.NoAction,
        Deferrable = deferrable,
        InitiallyDeferred = deferred,
      });
    }
    return keys;
  }

  // Every column's type must come to a supported base type once its domains are followed, and a policy belongs to exactly
  // the columns that come to `timestamptz`.
  private static void CheckTypes(List<Domain> domains, List<Table> tables) {
    var byName = domains.ToDictionary(domain => domain.Name, StringComparer.Ordinal);
    foreach (var table in tables) {
      foreach (var column in table.Columns) {
        var where = $"{table.Name}.{column.Name}";
        var resolved = DomainResolver.Resolve(column.Type, byName)
          ?? throw new SeedFormatException($"{_Context}: {where} has type '{column.Type}', which is not a supported type or a domain in the lock.");
        var isTimestamp = resolved.Seed is SeedType.TimestampTz;
        if (isTimestamp != column.Policy.HasValue) {
          throw new SeedFormatException($"{_Context}: {where} must have a policy if and only if it is a timestamptz column.");
        }
      }
    }
  }

  // A foreign key must point at a table in the lock, at columns that exist there.
  private static void CheckReferences(List<Table> tables) {
    var byName = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
    foreach (var table in tables) {
      foreach (var key in table.ForeignKeys) {
        if (!byName.TryGetValue(key.ReferencedTable, out var referenced)) {
          throw new SeedFormatException($"{_Context}: {table.Name} foreign key {key.Name} references table {key.ReferencedTable}, which is not in the lock.");
        }
        foreach (var column in key.ReferencedColumns.Where(column => !referenced.Columns.Any(known => string.Equals(known.Name, column, StringComparison.Ordinal)))) {
          throw new SeedFormatException($"{_Context}: {table.Name} foreign key {key.Name} references column '{column}', which {referenced.Name} does not have.");
        }
      }
    }
  }

  private static List<string> Names(string where, string key, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{where} {key}", node);
    if (sequence.Children.Count is 0) {
      throw new SeedFormatException($"{_Context}: {where} {key} is empty.");
    }
    return [.. sequence.Children.Select(child => YamlNodes.Text(_Context, $"{where} {key}", child))];
  }

  private static bool IsQualifiedName(string name)
    => name.Split('.') is [{ Length: > 0 }, { Length: > 0 }];

  private static int WholeNumber(string what, string text, int minimum)
    => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum
      ? number
      : throw new SeedFormatException($"{_Context}: {what} must be a whole number of at least {minimum.ToInvariantString()}, not '{text}'.");

  // An optional key holding `true` or `false`; absent means false.
  private static bool OptionalFlag(string where, YamlMappingNode map, string key)
    => YamlNodes.OptionalText(_Context, map, key) switch {
      null or "false" => false,
      "true" => true,
      var text => throw new SeedFormatException($"{_Context}: {where} {key} must be true or false, not '{text}'."),
    };

  // An optional key holding one of an enum's kebab-case words; absent is null.
  private static T? OptionalWord<T>(string where, YamlMappingNode map, string key) where T : struct, Enum {
    if (YamlNodes.OptionalText(_Context, map, key) is not { } text) {
      return null;
    }
    return Enum.TryParseKebabCase<T>(text, out var value)
      ? value
      : throw new SeedFormatException($"{_Context}: {where} {key} has '{text}', which is not one of the words the lock uses.");
  }

  private static string Checksum(string file, string text)
    => text.Length is 64 && text.All(char.IsAsciiHexDigitLower)
      ? text
      : throw new SeedFormatException($"{_Context}: a checksum of {file} is not 64 lowercase hex digits.");
}
