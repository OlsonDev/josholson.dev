#!/usr/bin/env dotnet
#:package Npgsql@10.0.3
// Generates docs/schema (Markdown with Mermaid diagrams) from the dev database's catalogs.
// Run through mise, which supplies the connection settings from .env (see mise.toml):
//   mise run schema:doc     regenerate docs/schema
//   mise run schema:check   fail if docs/schema differs from what the database would generate
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

const string DocPath = "docs/schema";

var mode = args.FirstOrDefault();
if (mode is not ("doc" or "check")) {
  Console.Error.WriteLine("Usage: dotnet run scripts/schema-docs.cs -- doc|check");
  return 2;
}

var connectionString = new NpgsqlConnectionStringBuilder {
  Host = "127.0.0.1",
  Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432", CultureInfo.InvariantCulture),
  Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "josholson",
  Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
  Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "josholson",
  SslMode = SslMode.Disable,
}.ToString();

Model model;
await using (var connection = new NpgsqlConnection(connectionString)) {
  await connection.OpenAsync(CancellationToken.None);
  model = await Model.LoadAsync(connection);
}

var work = Path.Combine(Path.GetTempPath(), "schema-docs-" + Guid.NewGuid().ToString("N"));
var generated = Path.Combine(work, "generated");
Directory.CreateDirectory(generated);
try {
  foreach (var (name, content) in Renderer.Render(model)) {
    await File.WriteAllTextAsync(
      Path.Combine(generated, name),
      content,
      new UTF8Encoding(false),
      CancellationToken.None);
  }

  if (string.Equals(mode, "doc", StringComparison.Ordinal)) {
    if (Directory.Exists(DocPath)) Directory.Delete(DocPath, true);
    Directory.CreateDirectory(DocPath);
    foreach (var file in Directory.GetFiles(generated)) File.Copy(file, Path.Combine(DocPath, Path.GetFileName(file)));
    Console.WriteLine($"Wrote {DocPath}");
    return 0;
  }

  if (!Directory.Exists(DocPath)) {
    Console.Error.WriteLine($"{DocPath} does not exist. Run: mise run schema:doc");
    return 1;
  }
  var diff = Process.Start(new ProcessStartInfo {
    FileName = "git",
    ArgumentList = { "diff", "--no-index", "--exit-code", DocPath, generated },
    UseShellExecute = false,
  })!;
  await diff.WaitForExitAsync(CancellationToken.None);
  if (diff.ExitCode is 0) Console.WriteLine($"{DocPath} is up to date.");
  else if (diff.ExitCode is 1) Console.Error.WriteLine($"{DocPath} is out of date. Run: mise run schema:doc");
  return diff.ExitCode;
} finally {
  Directory.Delete(work, true);
}

internal record Column(string Name, string Type, bool Nullable, string? Default, string? Comment);

// Kind is the pg_constraint code: p primary key, u unique, c check, f foreign key.
internal record Constraint(
  string Name,
  char Kind,
  string Definition,
  string[] Columns,
  long RefTable,
  string[] RefColumns,
  char OnUpdate,
  char OnDelete,
  bool Deferrable,
  bool Deferred);

internal record Index(string Name, string Definition, bool BacksConstraint);

internal record Table(
  long Id,
  string Schema,
  string Name,
  string? Comment,
  List<Column> Columns,
  List<Constraint> Constraints,
  List<Index> Indexes) {
  public string FullName
    => $"{Schema}.{Name}";
}

internal record Domain(
  string Schema,
  string Name,
  string Base,
  bool NotNull,
  string? Default,
  string? Comment,
  List<(string Name, string Definition)> Checks) {
  public string FullName
    => $"{Schema}.{Name}";
}

internal record Model(List<Table> Tables, List<Domain> Domains, Dictionary<string, string> SchemaComments) {
  // The tables to document: ordinary and partitioned tables outside the system schemas and public.
  private const string _TablesFrom = """
    FROM (SELECT) AS _
    JOIN pg_class      AS c  ON c.relkind IN ('r', 'p')
    JOIN pg_namespace  AS n  ON n.oid      = c.relnamespace
                            AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'public')
                            AND n.nspname NOT LIKE 'pg\_%'
    """;

  // The same tables, reduced to the oid.
  private const string _TablesCte = $"""
    WITH tables AS (
      SELECT c.oid
      {_TablesFrom}
    )
    """;

  public static async Task<Model> LoadAsync(NpgsqlConnection connection) {
    // With only pg_catalog on the path, format_type qualifies every other type with its schema.
    await new NpgsqlCommand("SET search_path TO pg_catalog", connection).ExecuteNonQueryAsync(CancellationToken.None);

    var tablesSql = $"""
      SELECT
          c.oid::bigint                       AS table_id
        , n.nspname                           AS schema
        , c.relname                           AS name
        , obj_description(c.oid, 'pg_class')  AS comment
      {_TablesFrom}
      ORDER BY n.nspname, c.relname;
      """;
    var tables = await QueryAsync(
      connection,
      tablesSql,
      r => new Table(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), [], [], []));
    var byId = tables.ToDictionary(t => t.Id);

    var columnsSql = $"""
      {_TablesCte}
      SELECT
          a.attrelid::bigint                     AS table_id
        , a.attname::text                        AS name
        , format_type(a.atttypid, a.atttypmod)   AS type
        , NOT a.attnotnull                       AS is_nullable
        , a.attidentity::text                    AS identity
        , a.attgenerated::text                   AS generated
        , pg_get_expr(d.adbin, d.adrelid)        AS default_expression
        , col_description(a.attrelid, a.attnum)  AS comment
      FROM (SELECT) AS _
      JOIN pg_attribute     AS a  ON a.attnum  >  0
                                 AND NOT a.attisdropped
      JOIN tables           AS t  ON t.oid      = a.attrelid
      LEFT JOIN pg_attrdef  AS d  ON d.adrelid  = a.attrelid
                                 AND d.adnum    = a.attnum
      ORDER BY table_id, a.attnum;
      """;
    var columns = await QueryAsync(connection, columnsSql, r => {
      var expression = r.IsDBNull(6) ? null : r.GetString(6);
      var generated = string.Equals(r.GetString(5), "s", StringComparison.Ordinal);
      var identity = r.GetString(4) switch { "a" => "GENERATED ALWAYS AS IDENTITY", "d" => "GENERATED BY DEFAULT AS IDENTITY", _ => null };
      var defaultValue = generated ? $"GENERATED ALWAYS AS ({expression}) STORED" : identity ?? expression;
      return (Table: r.GetInt64(0), Column: new Column(
        r.GetString(1),
        r.GetString(2),
        r.GetBoolean(3),
        defaultValue,
        r.IsDBNull(7) ? null : r.GetString(7)));
    });
    foreach (var (table, column) in columns) byId[table].Columns.Add(column);

    var constraintsSql = $"""
      {_TablesCte}
      SELECT
          con.conrelid::bigint           AS table_id
        , con.conname::text              AS name
        , con.contype::text              AS kind
        , pg_get_constraintdef(con.oid)  AS definition
        , COALESCE((
            SELECT array_agg(a.attname::text ORDER BY k.ord)
            FROM (SELECT) AS _
            JOIN pg_attribute                        AS a                ON a.attrelid  = con.conrelid
            JOIN unnest(con.conkey) WITH ORDINALITY  AS k (attnum, ord)  ON k.attnum    = a.attnum
          ), ARRAY[]::text[])            AS columns
        , con.confrelid::bigint          AS ref_table_id
        , COALESCE((
            SELECT array_agg(a.attname::text ORDER BY k.ord)
            FROM (SELECT) AS _
            JOIN pg_attribute                         AS a                ON a.attrelid  = con.confrelid
            JOIN unnest(con.confkey) WITH ORDINALITY  AS k (attnum, ord)  ON k.attnum    = a.attnum
          ), ARRAY[]::text[])            AS ref_columns
        , con.confupdtype::text          AS on_update
        , con.confdeltype::text          AS on_delete
        , con.condeferrable              AS is_deferrable
        , con.condeferred                AS is_deferred
      FROM (SELECT) AS _
      JOIN pg_constraint  AS con  ON con.contype IN ('p', 'u', 'c', 'f')
      JOIN tables         AS t    ON t.oid        = con.conrelid
      ORDER BY table_id, name;
      """;
    var constraints = await QueryAsync(connection, constraintsSql, r => (
      Table: r.GetInt64(0),
      Constraint: new Constraint(
        r.GetString(1),
        r.GetString(2)[0],
        r.GetString(3),
        r.GetFieldValue<string[]>(4),
        r.GetInt64(5),
        r.GetFieldValue<string[]>(6),
        r.GetString(7)[0],
        r.GetString(8)[0],
        r.GetBoolean(9),
        r.GetBoolean(10))));
    foreach (var (table, constraint) in constraints) byId[table].Constraints.Add(constraint);

    var indexesSql = $"""
      {_TablesCte}
      SELECT
          i.indrelid::bigint             AS table_id
        , ic.relname::text               AS name
        , pg_get_indexdef(i.indexrelid)  AS definition
        , EXISTS (
            SELECT 1
            FROM (SELECT) AS _
            JOIN pg_constraint  AS k  ON k.conindid  = i.indexrelid
                                     AND k.contype  IN ('p', 'u', 'x')
          )                              AS backs_constraint
      FROM (SELECT) AS _
      JOIN pg_index  AS i   ON TRUE
      JOIN tables    AS t   ON t.oid   = i.indrelid
      JOIN pg_class  AS ic  ON ic.oid  = i.indexrelid
      ORDER BY table_id, name;
      """;
    var indexes = await QueryAsync(
      connection,
      indexesSql,
      r => (Table: r.GetInt64(0), Index: new Index(r.GetString(1), r.GetString(2), r.GetBoolean(3))));
    foreach (var (table, index) in indexes) byId[table].Indexes.Add(index);

    var checksSql = """
      SELECT
          con.contypid::bigint           AS domain_id
        , con.conname::text              AS name
        , pg_get_constraintdef(con.oid)  AS definition
      FROM (SELECT) AS _
      JOIN pg_constraint  AS con  ON con.contypid != 0
      ORDER BY name;
      """;
    var checkRows = await QueryAsync(
      connection,
      checksSql,
      r => (Domain: r.GetInt64(0), Name: r.GetString(1), Definition: r.GetString(2)));
    var checks = checkRows.ToLookup(c => c.Domain);
    var domainsSql = """
      SELECT
          t.oid::bigint                            AS domain_id
        , n.nspname                                AS schema
        , t.typname                                AS name
        , format_type(t.typbasetype, t.typtypmod)  AS base
        , t.typnotnull                             AS is_not_null
        , t.typdefault                             AS default_expression
        , obj_description(t.oid, 'pg_type')        AS comment
      FROM (SELECT) AS _
      JOIN pg_type       AS t  ON t.typtype  = 'd'
      JOIN pg_namespace  AS n  ON n.oid      = t.typnamespace
                              AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'public')
      ORDER BY n.nspname, t.typname;
      """;
    var domains = await QueryAsync(connection, domainsSql, r => new Domain(
      r.GetString(1),
      r.GetString(2),
      r.GetString(3),
      r.GetBoolean(4),
      r.IsDBNull(5) ? null : r.GetString(5),
      r.IsDBNull(6) ? null : r.GetString(6),
      [.. checks[r.GetInt64(0)].Select(c => (c.Name, c.Definition))]));

    var schemaCommentsSql = """
      SELECT
          n.nspname::text                         AS name
        , obj_description(n.oid, 'pg_namespace')  AS comment
      FROM (SELECT) AS _
      JOIN pg_namespace  AS n  ON obj_description(n.oid, 'pg_namespace') IS NOT NULL;
      """;
    var schemaCommentRows = await QueryAsync(
      connection,
      schemaCommentsSql,
      r => (Name: r.GetString(0), Comment: r.GetString(1)));
    var schemaComments = schemaCommentRows.ToDictionary(c => c.Name, c => c.Comment);

    return new Model(tables, domains, schemaComments);
  }

  private static async Task<List<T>> QueryAsync<T>(NpgsqlConnection connection, string sql, Func<NpgsqlDataReader, T> map) {
    await using var reader = await new NpgsqlCommand(sql, connection).ExecuteReaderAsync(CancellationToken.None);
    var rows = new List<T>();
    while (await reader.ReadAsync(CancellationToken.None)) rows.Add(map(reader));
    return rows;
  }
}

// One output file. Links are written as reference-style definitions at the bottom, labelled:
//   pg:{type}        the PostgreSQL documentation for a built-in type
//   domain:{name}    a domain's section in domains.md
//   table:{name}     a table's page
internal class Page {
  private readonly List<string> _lines = [];
  private readonly SortedDictionary<string, string> _links = new(StringComparer.Ordinal);

  public void Add(params string[] added)
    => _lines.AddRange(added);

  public string Link(string text, string label, string url) {
    _links[label] = url;
    return $"[{text}][{label}]";
  }

  public string Render() {
    var result = new List<string>(_lines) { "", "---", "", "> Generated by [scripts/schema-docs.cs](../../scripts/schema-docs.cs). Do not edit by hand." };
    if (_links.Count > 0) {
      result.Add("");
      result.AddRange(_links.Select(l => $"[{l.Key}]: {l.Value}"));
    }
    return string.Join('\n', result) + "\n";
  }
}

internal static class Renderer {
  // Documentation for the version we run; bump with ADR 0003.
  private const string _Docs = "https://www.postgresql.org/docs/19/";

  private static readonly Dictionary<string, string> _TypeDocs = new() {
    ["smallint"] = "datatype-numeric.html#DATATYPE-INT",
    ["integer"] = "datatype-numeric.html#DATATYPE-INT",
    ["bigint"] = "datatype-numeric.html#DATATYPE-INT",
    ["numeric"] = "datatype-numeric.html#DATATYPE-NUMERIC-DECIMAL",
    ["real"] = "datatype-numeric.html#DATATYPE-FLOAT",
    ["double precision"] = "datatype-numeric.html#DATATYPE-FLOAT",
    ["text"] = "datatype-character.html",
    ["character varying"] = "datatype-character.html",
    ["character"] = "datatype-character.html",
    ["boolean"] = "datatype-boolean.html",
    ["bytea"] = "datatype-binary.html",
    ["date"] = "datatype-datetime.html",
    ["time without time zone"] = "datatype-datetime.html",
    ["time with time zone"] = "datatype-datetime.html",
    ["timestamp without time zone"] = "datatype-datetime.html",
    ["timestamp with time zone"] = "datatype-datetime.html",
    ["interval"] = "datatype-datetime.html",
    ["uuid"] = "datatype-uuid.html",
    ["json"] = "datatype-json.html",
    ["jsonb"] = "datatype-json.html",
    ["inet"] = "datatype-net-types.html",
    ["cidr"] = "datatype-net-types.html",
    ["xml"] = "datatype-xml.html",
  };

  private static readonly Dictionary<string, string> _ShortNames = new() {
    ["timestamp with time zone"] = "TIMESTAMPTZ",
    ["timestamp without time zone"] = "TIMESTAMP",
    ["time with time zone"] = "TIMETZ",
    ["time without time zone"] = "TIME",
    ["character varying"] = "VARCHAR",
    ["character"] = "CHAR",
    ["double precision"] = "FLOAT8",
  };

  private static readonly HashSet<string> _Unlinked = [];

  private static readonly Dictionary<char, string> _Actions = new() {
    ['a'] = "NO ACTION",
    ['r'] = "RESTRICT",
    ['c'] = "CASCADE",
    ['n'] = "SET NULL",
    ['d'] = "SET DEFAULT",
  };

  public static IEnumerable<(string Name, string Content)> Render(Model model) {
    var tables = model.Tables.ToDictionary(t => t.Id);
    foreach (var table in model.Tables) yield return ($"{table.FullName}.md", TablePage(table, model, tables));
    yield return ("domains.md", DomainsPage(model));
    yield return ("README.md", IndexPage(model, tables));
  }

  private static string IndexPage(Model model, Dictionary<long, Table> tables) {
    var page = new Page();
    page.Add("# josholson", "");
    foreach (var group in model.Tables.GroupBy(t => t.Schema)) {
      page.Add($"## {group.Key}", "");
      if (model.SchemaComments.TryGetValue(group.Key, out var comment)) page.Add(comment, "");
      var showComment = group.Any(t => t.Comment is not null);
      string[] headers = showComment ? ["Table", "Columns", "Comment"] : ["Table", "Columns"];
      var rows = group.Select(t => {
        var row = new List<string> { TableLink(page, t.FullName), t.Columns.Count.ToString() };
        if (showComment) row.Add(t.Comment ?? "");
        return row.ToArray();
      });
      page.Add(MdTable(headers, rows, [1]));
      page.Add("");
    }

    page.Add("## Domains", "");
    page.Add(MdTable(["Domain", "Base type"], model.Domains.Select(d => new[] {
      page.Link(Code(d.FullName), $"domain:{d.FullName}", $"domains.md#{Slug(d.FullName)}"),
      TypeCell(page, d.Base, model),
    })));
    page.Add("", "## Relations", "", "```mermaid", "erDiagram", "");
    foreach (var table in model.Tables) {
      foreach (var fk in table.Constraints.Where(c => c.Kind == 'f')) {
        page.Add(Relationship(table, fk, tables[fk.RefTable]));
      }
    }
    page.Add("```");
    return page.Render();
  }

  private static string DomainsPage(Model model) {
    var page = new Page();
    page.Add("# Domains", "", "Domains hold validation that repeats across tables ([ADR 0017](../adr/0017-domains-by-kind.md)).");
    foreach (var domain in model.Domains) {
      page.Add("", $"## {domain.FullName}", "");
      if (domain.Comment is not null) page.Add(domain.Comment, "");
      page.Add($"- Base type: {TypeCell(page, domain.Base, model)}");
      if (domain.NotNull) page.Add("- `NOT NULL`");
      if (domain.Default is not null) page.Add($"- Default: {Code(domain.Default)}");
      if (domain.Checks.Count > 0) {
        page.Add("", "### Check constraints", "");
        page.Add(MdTable(["Name", "Definition"], domain.Checks.Select(c => new[] { Code(c.Name), Code(CheckExpression(c.Definition)) })));
      }
    }
    return page.Render();
  }

  private static string TablePage(Table table, Model model, Dictionary<long, Table> tables) {
    var page = new Page();
    page.Add($"# {Code(table.FullName)}", "");
    if (table.Comment is not null) page.Add(table.Comment, "");

    var showComment = table.Columns.Any(c => c.Comment is not null);
    string[] headers = showComment ? ["Name", "Type", "Nullable", "Default", "Comment"] : ["Name", "Type", "Nullable", "Default"];
    page.Add("## Columns", "");
    var rows = table.Columns.Select(c => {
      var row = new List<string> {
        Code(c.Name),
        TypeCell(page, c.Type, model),
        Code(c.Nullable ? "NULL" : "NOT NULL"),
        c.Default is null ? "" : Code(c.Default),
      };
      if (showComment) row.Add(c.Comment ?? "");
      return row.ToArray();
    });
    page.Add(MdTable(headers, rows, [2]));

    var groups = new (string Title, char Kind, Func<Constraint, string> Definition)[] {
      ("Primary key", 'p', c => Code(string.Join(", ", c.Columns) + AfterColumns(c.Definition))),
      ("Unique constraints", 'u', c => Code(string.Join(", ", c.Columns) + AfterColumns(c.Definition))),
      ("Check constraints", 'c', c => Code(CheckExpression(c.Definition))),
    };
    var constraintGroups = groups.Where(g => table.Constraints.Any(c => c.Kind == g.Kind)).ToList();
    if (constraintGroups.Count > 0) {
      page.Add("", "## Constraints");
      foreach (var (title, kind, definition) in constraintGroups) {
        page.Add("", $"### {title}", "");
        page.Add(MdTable(["Name", "Definition"], table.Constraints.Where(c => c.Kind == kind).Select(c => new[] { Code(c.Name), definition(c) })));
      }
    }

    var indexes = table.Indexes.Where(i => !i.BacksConstraint).ToList();
    if (indexes.Count > 0) {
      page.Add("", "## Indexes", "", "```sql");
      page.Add(string.Join("\n\n", indexes.Select(i => SplitIndex(i.Definition) + ";")));
      page.Add("```");
    }

    var outgoing = table.Constraints.Where(c => c.Kind == 'f').ToList();
    var incoming = tables.Values.SelectMany(t => t.Constraints.Where(c => c.Kind == 'f' && c.RefTable == table.Id).Select(c => (Table: t, Constraint: c)))
      .OrderBy(x => x.Table.FullName, StringComparer.Ordinal).ThenBy(x => x.Constraint.Name, StringComparer.Ordinal).ToList();
    page.Add("", "## Relations");
    if (outgoing.Count > 0) {
      page.Add("", "### Foreign keys", "");
      page.Add(ForeignKeys("Ref Table", outgoing.Select(c => (tables[c.RefTable], c, true)), page));
    }
    if (incoming.Count > 0) {
      page.Add("", "### Incoming foreign keys", "");
      page.Add(ForeignKeys("Table", incoming.Select(x => (x.Table, x.Constraint, false)), page));
    }
    page.Add("", "### Diagram", "", "```mermaid", "erDiagram", "");
    page.Add(Diagram(table, tables, model));
    page.Add("```");
    return page.Render();
  }

  // For outgoing keys the linked table is the one referenced; for incoming keys it is the referencing one.
  private static string[] ForeignKeys(string tableHeader, IEnumerable<(Table Table, Constraint Constraint, bool Outgoing)> keys, Page page) {
    var list = keys.ToList();
    var deferrable = list.Any(k => k.Constraint.Deferrable);
    List<string> headers = ["Name", "Columns", tableHeader, "Ref Columns", "On Delete", "On Update"];
    if (deferrable) headers.Add("Deferrable");
    return MdTable([.. headers], list.Select(k => {
      var (table, fk, outgoing) = k;
      var row = new List<string> {
        Code(fk.Name),
        Code(string.Join(", ", fk.Columns)),
        TableLink(page, table.FullName),
        Code(string.Join(", ", fk.RefColumns)),
        Code(_Actions[fk.OnDelete]),
        Code(_Actions[fk.OnUpdate]),
      };
      if (deferrable) row.Add(fk.Deferrable ? Code(fk.Deferred ? "INITIALLY DEFERRED" : "DEFERRABLE") : "");
      return row.ToArray();
    }));
  }

  private static string Diagram(Table table, Dictionary<long, Table> tables, Model model) {
    var related = tables.Values.SelectMany(t => t.Constraints.Where(c => c.Kind == 'f'
        && (t.Id == table.Id || c.RefTable == table.Id)).Select(c => (Table: t, Constraint: c))).ToList();
    var entities = new List<Table> { table };
    foreach (var (child, fk) in related) {
      foreach (var other in new[] { child, tables[fk.RefTable] }) if (!entities.Contains(other)) entities.Add(other);
    }

    var lines = new List<string>();
    lines.AddRange(related.Select(r => Relationship(r.Table, r.Constraint, tables[r.Constraint.RefTable])));
    foreach (var entity in entities) {
      var keys = Keys(entity);
      lines.Add("");
      lines.Add($"\"{entity.FullName}\" {{");
      lines.AddRange(entity.Columns.Select(c => $"  {MermaidType(c.Type, model)} {c.Name}" + (keys.TryGetValue(c.Name, out var k) ? $" {k}" : "")));
      lines.Add("}");
    }
    return string.Join('\n', lines.SkipWhile(l => l is ""));
  }

  private static Dictionary<string, string> Keys(Table table) {
    var keys = new Dictionary<string, SortedSet<string>>();
    foreach (var constraint in table.Constraints) {
      var key = constraint.Kind switch { 'p' => "PK", 'f' => "FK", 'u' when constraint.Columns.Length is 1 => "UK", _ => null };
      if (key is null) continue;
      foreach (var column in constraint.Columns) {
        if (!keys.TryGetValue(column, out var set)) keys[column] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(key);
      }
    }
    return keys.ToDictionary(k => k.Key, k => string.Join(", ", new[] { "PK", "FK", "UK" }.Where(k.Value.Contains)));
  }

  // The many side is optional unless a unique constraint on the same columns makes it one; the one side is optional
  // when any referencing column is nullable.
  private static string Relationship(Table child, Constraint fk, Table parent) {
    var unique = child.Constraints.Any(c => c.Kind is 'p' or 'u' && c.Columns.Order(StringComparer.Ordinal).SequenceEqual(fk.Columns.Order(StringComparer.Ordinal)));
    var optional = fk.Columns.Any(name => child.Columns.First(c => string.Equals(c.Name, name, StringComparison.Ordinal)).Nullable);
    return $"\"{child.FullName}\" {(unique ? "|o" : "}o")}--{(optional ? "o|" : "||")} \"{parent.FullName}\" : \"{string.Join(", ", fk.Columns)}\"";
  }

  // Built-in types appear in capitals, with the short names Postgres accepts for the multi-word ones. Domains and
  // other types keep the names Postgres prints.
  private static string TypeName(string type, Model model) {
    var name = BaseName(type);
    if (model.Domains.Any(d => string.Equals(d.FullName, name, StringComparison.Ordinal)) || !_TypeDocs.ContainsKey(name)) return type;
    return (_ShortNames.GetValueOrDefault(name) ?? name.ToUpperInvariant()) + type[name.Length..];
  }

  // Mermaid types cannot hold spaces or quotes; dots, brackets, and parentheses are fine.
  private static string MermaidType(string type, Model model)
    => Regex.Replace(TypeName(type, model).Replace("\"", ""), @"\s+", "_");

  // "character varying(20)[]" is "character varying".
  private static string BaseName(string type)
    => Regex.Replace(Regex.Replace(type, @"(\[\])+$", ""), @"\(.*\)$", "").Replace("\"", "");

  // Heading anchors as GitHub makes them: lowercase, punctuation dropped, spaces to hyphens.
  private static string Slug(string heading)
    => Regex.Replace(heading.ToLowerInvariant(), "[^a-z0-9 _-]", "").Replace(' ', '-');

  private static string TableLink(Page page, string name)
    => page.Link(Code(name), $"table:{name}", $"{name}.md");

  // Link a type to its documentation: a domain to its section, a built-in to the PostgreSQL manual.
  private static string TypeCell(Page page, string type, Model model) {
    var name = BaseName(type);
    var text = Code(TypeName(type, model));
    if (model.Domains.Any(d => string.Equals(d.FullName, name, StringComparison.Ordinal))) return page.Link(text, $"domain:{name}", $"domains.md#{Slug(name)}");
    if (_TypeDocs.TryGetValue(name, out var url)) return page.Link(text, $"pg:{name.Replace(' ', '-')}", _Docs + url);
    if (_Unlinked.Add(name)) Console.Error.WriteLine($"No link for type {name}: add it to _TypeDocs in scripts/schema-docs.cs.");
    return text;
  }

  private static string Code(string text) {
    if (text.Length is 0) return text;
    var longest = Regex.Matches(text, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
    var fence = new string('`', longest + 1);
    return longest > 0 ? $"{fence} {text} {fence}" : $"{fence}{text}{fence}";
  }

  // "CHECK ((a >= b))" becomes "a >= b": the keyword and the parentheses Postgres adds around every expression.
  private static string CheckExpression(string definition) {
    var expression = Regex.Replace(definition, "^CHECK ", "");
    var suffix = Regex.Match(expression, @"(?<=\)) (NO INHERIT|NOT VALID)( NO INHERIT| NOT VALID)?$");
    if (suffix.Success) expression = expression[..suffix.Index];
    while (Wrapped(expression)) expression = expression[1..^1];
    return expression + suffix.Value;
  }

  private static bool Wrapped(string text) {
    if (text.Length < 2 || text[0] != '(' || text[^1] != ')') return false;
    var depth = 0;
    for (var i = 0; i < text.Length - 1; i++) {
      if (text[i] == '(') depth++;
      else if (text[i] == ')') depth--;
      if (depth is 0) return false;
    }
    return true;
  }

  // Anything after the column list: DEFERRABLE INITIALLY DEFERRED, and so on.
  private static string AfterColumns(string definition) {
    var rest = definition[(definition.IndexOf(')') + 1)..].Trim();
    return rest.Length is 0 ? "" : " " + rest;
  }

  // One clause per line: CREATE INDEX ..., then ON, USING, INCLUDE, WITH, TABLESPACE, and WHERE indented.
  private static string SplitIndex(string definition) {
    string[] keywords = ["ON ", "USING ", "INCLUDE ", "WITH ", "TABLESPACE ", "WHERE "];
    var result = new StringBuilder();
    var depth = 0;
    var quote = '\0';
    for (var i = 0; i < definition.Length; i++) {
      var c = definition[i];
      if (quote == '\0' && c is '\'' or '"') quote = c;
      else if (c == quote) quote = '\0';
      else if (quote == '\0' && c == '(') depth++;
      else if (quote == '\0' && c == ')') depth--;

      if (c == ' ' && depth is 0 && quote == '\0'
&& keywords.Any(k => string.CompareOrdinal(definition, i + 1, k, 0, k.Length) is 0)) {
        result.Append("\n  ");
      } else {
        result.Append(c);
      }
    }
    return result.ToString();
  }

  // Pads every cell from its final text, so alignment holds whatever markup a cell carries.
  private static string[] MdTable(string[] headers, IEnumerable<string[]> rows, int[]? rightAligned = null) {
    var body = rows.Select(r => r.Select(cell => cell.Replace("|", @"\|")).ToArray()).ToList();
    var right = rightAligned ?? [];
    var widths = Enumerable.Range(0, headers.Length).Select(c => body.Select(r => r[c].Length).Append(headers[c].Length).Max()).ToArray();
    string Cell(string text, int c) => right.Contains(c) ? text.PadLeft(widths[c]) : text.PadRight(widths[c]);
    string Row(string[] cells) => "| " + string.Join(" | ", cells.Select(Cell)) + " |";
    var separator = "| " + string.Join(" | ", widths.Select((w, c) => right.Contains(c) ? new string('-', w - 1) + ":" : new string('-', w))) + " |";
    return [Row(headers), separator, .. body.Select(Row)];
  }
}
