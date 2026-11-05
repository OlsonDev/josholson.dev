using JoshOlson.Database.Schema;
using JoshOlson.Database.Yaml;
using JoshOlson.Shared.Extensions;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Locks;

// The tables section of the lock: columns, keys, constraints, and indexes.
public static partial class SchemaLockParser {
  private static readonly string[] _TableKeys = [
    "comment",
    "columns",
    "primary_key",
    "primary_key_name",
    "unique",
    "checks",
    "foreign_keys",
    "indexes",
  ];
  private static readonly string[] _ColumnKeys = [
    "name",
    "type",
    "nullable",
    "declared_nullable",
    "generated_as",
    "generated_expression",
    "default_expression",
    "policy",
    "comment",
  ];
  private static readonly string[] _UniqueKeys = [
    "name",
    "columns",
    "definition",
  ];
  private static readonly string[] _IndexKeys = [
    "name",
    "definition",
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
    var primaryKeyName = OptionalNonEmptyText($"table {name}", map, "primary_key_name");
    var hasPrimaryKey = primaryKey.Count > 0;
    var hasPrimaryKeyName = primaryKeyName is not null;
    if (hasPrimaryKey != hasPrimaryKeyName) {
      throw new SeedFormatException($"{_Context}: table {name} must have primary_key and primary_key_name together or neither.");
    }
    return new() {
      Name = name,
      Comment = OptionalNonEmptyText($"table {name}", map, "comment"),
      Columns = columns,
      PrimaryKey = primaryKey,
      PrimaryKeyName = primaryKeyName,
      UniqueConstraints = YamlNodes.Find(map, "unique") is { } uniqueNode ? ParseUniqueConstraints(name, columns, uniqueNode) : [],
      Checks = YamlNodes.Find(map, "checks") is { } checksNode ? ParseChecks($"table {name}", checksNode) : [],
      ForeignKeys = YamlNodes.Find(map, "foreign_keys") is { } foreignKeysNode ? ParseForeignKeys(name, columns, foreignKeysNode) : [],
      Indexes = YamlNodes.Find(map, "indexes") is { } indexesNode ? ParseIndexes(name, indexesNode) : [],
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
      var column = ParseColumn(table, item);
      if (columns.Any(known => string.Equals(known.Name, column.Name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: column {table}.{column.Name} is listed twice.");
      }
      columns.Add(column);
    }
    return columns;
  }

  private static Column ParseColumn(string table, YamlNode node) {
    var map = YamlNodes.Mapping(_Context, $"a column of {table}", node);
    YamlNodes.CheckKeys(_Context, $"a column of {table}", map, _ColumnKeys);
    var name = YamlNodes.RequiredText(_Context, map, "name");
    var where = $"{table}.{name}";
    var type = YamlNodes.RequiredText(_Context, map, "type");
    if (!ColumnType.TryParse(type, out _)) {
      throw new SeedFormatException($"{_Context}: {where} has type '{type}', which the seed tool does not support.");
    }
    var nullable = OptionalFlag(where, map, "nullable");
    var declaredOnly = OptionalFlag(where, map, "declared_nullable");
    if (nullable && declaredOnly) {
      throw new SeedFormatException($"{_Context}: {where} says declared_nullable, which is only written for a column that is not nullable (it is already declared nullable when nullable is true).");
    }

    var generatedAs = OptionalWord<GeneratedAs>(where, map, "generated_as");
    var generatedExpression = OptionalNonEmptyText(where, map, "generated_expression");
    var defaultExpression = OptionalNonEmptyText(where, map, "default_expression");
    var isExpression = generatedAs is GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual;
    var hasExpression = generatedExpression is not null;
    if (isExpression != hasExpression) {
      throw new SeedFormatException($"{_Context}: {where} must have a generated_expression if and only if its generated_as is expression-stored or expression-virtual.");
    }
    if (generatedAs is not null && defaultExpression is not null) {
      throw new SeedFormatException($"{_Context}: {where} is generated, so it cannot also have a default.");
    }
    return new() {
      Name = name,
      Type = type,
      DeclaredNullable = nullable || declaredOnly,
      Nullable = nullable,
      GeneratedAs = generatedAs,
      GeneratedExpression = generatedExpression,
      DefaultExpression = defaultExpression,
      Policy = OptionalWord<TimePolicy>(where, map, "policy"),
      Comment = OptionalNonEmptyText(where, map, "comment"),
    };
  }

  private static List<UniqueConstraint> ParseUniqueConstraints(string table, List<Column> columns, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{table} unique", node);
    List<UniqueConstraint> constraints = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a unique constraint of {table}", item);
      YamlNodes.CheckKeys(_Context, $"a unique constraint of {table}", map, _UniqueKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      var where = $"{table} unique constraint {name}";
      if (constraints.Any(known => string.Equals(known.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: {where} is listed twice.");
      }
      var constraintColumns = Names(where, "columns", YamlNodes.Required(_Context, map, "columns"));
      foreach (var column in constraintColumns.Where(column => !columns.Any(known => string.Equals(known.Name, column, StringComparison.Ordinal)))) {
        throw new SeedFormatException($"{_Context}: {where} uses column '{column}', which {table} does not have.");
      }
      constraints.Add(new() {
        Name = name,
        Columns = constraintColumns,
        Definition = OptionalNonEmptyText(where, map, "definition") ?? throw new SeedFormatException($"{_Context}: {where} needs a definition."),
      });
    }
    return constraints;
  }

  private static List<TableIndex> ParseIndexes(string table, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{table} indexes", node);
    List<TableIndex> indexes = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"an index of {table}", item);
      YamlNodes.CheckKeys(_Context, $"an index of {table}", map, _IndexKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      var where = $"{table} index {name}";
      if (indexes.Any(known => string.Equals(known.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: {where} is listed twice.");
      }
      indexes.Add(new() {
        Name = name,
        Definition = OptionalNonEmptyText(where, map, "definition") ?? throw new SeedFormatException($"{_Context}: {where} needs a definition."),
      });
    }
    return indexes;
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
}
