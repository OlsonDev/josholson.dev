using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Catalog;

// The catalog queries, run on the connection (and inside the transaction) that `SchemaCatalog.ReadAsync` opens. Every
// query is read-only. Like the API's repositories it only runs queries: the model types are filled straight from the
// rows (a column alias is the property's name), and joining children to parents and checking the result is
// `SchemaCatalog`'s job.
//
// A query for a child returns `(key, item)` pairs: the first column, `table_id` or `domain_id`, is the catalog oid of the
// parent, used only to group the children in code, and the columns from `name` on fill the item. A table or domain comes
// back with the lists it owns unset (`Columns`, `Checks`, and the rest), which `SchemaCatalog` fills in.
internal sealed class CatalogRepository {
  // The schemas that are never read: `pg_*` is matched by name in the query.
  private static readonly string[] _SystemSchemas = ["information_schema"];

  // The constraints of one kind, `@kind`: 'p' primary key, 'u' unique.
  private const string _KeysSql = """
    SELECT
        con.conrelid::bigint           AS table_id
      , con.conname::text              AS name
      , COALESCE((
          SELECT array_agg(a.attname::text ORDER BY k.ord)
          FROM (SELECT) AS _
          JOIN pg_attribute                        AS a                ON a.attrelid  = con.conrelid
          JOIN unnest(con.conkey) WITH ORDINALITY  AS k (attnum, ord)  ON k.attnum    = a.attnum
        ), ARRAY[]::text[])            AS columns
      , pg_get_constraintdef(con.oid)  AS definition
    FROM (SELECT) AS _
    JOIN pg_constraint  AS con  ON con.contype::text  = @kind
                               AND con.conrelid       = ANY (@tableIds);
    """;

  private readonly NpgsqlConnection _connection;
  private readonly string[] _excludedSchemas;
  private readonly string[] _onlySchemas;
  private readonly bool _restrictToOnly;

  public CatalogRepository(NpgsqlConnection connection, CatalogOptions options) {
    _connection = connection;
    _excludedSchemas = [.. _SystemSchemas, .. options.ExcludedSchemas];
    _onlySchemas = [.. options.OnlySchemas ?? []];
    _restrictToOnly = options.OnlySchemas is not null;
  }

  // The same type, in the spelling the lock uses. Only a type that starts with the long name is touched, so a domain
  // called `numeric_foo.bar` is not.
  internal static string CleanType(string expression)
    => $"""
      regexp_replace(
        regexp_replace(
          regexp_replace({expression}, '^timestamp with time zone(?=\[|$)', 'timestamptz'),
          '^numeric(?=\(|\[|$)', 'decimal'),
        '^(decimal\(\d+),(\d+\))', '\1, \2')
      """;

  // A foreign key's `confdeltype` or `confupdtype`, as the `ReferentialAction` it stands for.
  internal static string ActionCase(string column)
    => $"""
      CASE {column}
        WHEN 'a'  THEN {(short)ReferentialAction.NoAction}
        WHEN 'r'  THEN {(short)ReferentialAction.Restrict}
        WHEN 'c'  THEN {(short)ReferentialAction.Cascade}
        WHEN 'n'  THEN {(short)ReferentialAction.SetNull}
        WHEN 'd'  THEN {(short)ReferentialAction.SetDefault}
      END::smallint
      """;

  public Task<List<SchemaComment>> GetSchemaCommentsAsync(CancellationToken cancellationToken) {
    var sql = """
      SELECT
          n.nspname::text                         AS name
        , obj_description(n.oid, 'pg_namespace')  AS comment
      FROM (SELECT) AS _
      JOIN pg_namespace  AS n  ON obj_description(n.oid, 'pg_namespace') IS NOT NULL
                              AND n.nspname != ALL (@excludedSchemas)
                              AND n.nspname NOT LIKE 'pg\_%'
                              AND (NOT @restrictToOnly OR n.nspname = ANY (@onlySchemas));
      """;
    return _connection.QueryAsync<SchemaComment>(sql, SchemaParameters(), cancellationToken);
  }

  public Task<Dictionary<long, Table>> GetTablesAsync(CancellationToken cancellationToken) {
    var sql = """
      SELECT
          c.oid::bigint                          AS table_id
        , (n.nspname || '.' || c.relname)::text  AS name
        , obj_description(c.oid, 'pg_class')     AS comment
      FROM (SELECT) AS _
      JOIN pg_class      AS c  ON c.relkind IN ('r', 'p')
      JOIN pg_namespace  AS n  ON n.oid      = c.relnamespace
                              AND n.nspname != ALL (@excludedSchemas)
                              AND n.nspname NOT LIKE 'pg\_%'
                              AND (NOT @restrictToOnly OR n.nspname = ANY (@onlySchemas));
      """;
    return _connection.QueryDictionaryAsync<long, Table>(sql, SchemaParameters(), "name", cancellationToken);
  }

  public Task<Dictionary<long, Domain>> GetDomainsAsync(CancellationToken cancellationToken) {
    var sql = $"""
      SELECT
          t.oid::bigint                                           AS domain_id
        , (n.nspname || '.' || t.typname)::text                   AS name
        , {CleanType("format_type(t.typbasetype, t.typtypmod)")}  AS type
        , t.typnotnull                                            AS not_null
        , t.typdefault                                            AS default_expression
        , obj_description(t.oid, 'pg_type')                       AS comment
      FROM (SELECT) AS _
      JOIN pg_type       AS t  ON t.typtype  = 'd'
      JOIN pg_namespace  AS n  ON n.oid      = t.typnamespace
                              AND n.nspname != ALL (@excludedSchemas)
                              AND n.nspname NOT LIKE 'pg\_%'
                              AND (NOT @restrictToOnly OR n.nspname = ANY (@onlySchemas));
      """;
    return _connection.QueryDictionaryAsync<long, Domain>(sql, SchemaParameters(), "name", cancellationToken);
  }

  public Task<Dictionary<long, List<CheckConstraint>>> GetDomainChecksAsync(
    Dictionary<long, Domain> domains,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          con.contypid::bigint           AS domain_id
        , con.conname::text              AS name
        , pg_get_constraintdef(con.oid)  AS expression
      FROM (SELECT) AS _
      JOIN pg_constraint  AS con  ON con.contype   = 'c'
                                 AND con.contypid  = ANY (@domainIds);
      """;
    var parameters = new { domainIds = domains.Keys.ToArray() };
    return _connection.QueryGroupedAsync<long, CheckConstraint>(sql, parameters, "name", cancellationToken);
  }

  public Task<Dictionary<long, List<Column>>> GetColumnsAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  ) {
    var sql = $"""
      SELECT
          a.attrelid::bigint                                   AS table_id
        , a.attname::text                                      AS name
        , {CleanType("format_type(a.atttypid, a.atttypmod)")}  AS type
        , NOT a.attnotnull                                     AS declared_nullable
        , CASE
            WHEN a.attidentity  = 'a'  THEN {(short)GeneratedAs.IdentityAlways}
            WHEN a.attidentity  = 'd'  THEN {(short)GeneratedAs.IdentityByDefault}
            WHEN a.attgenerated = 's'  THEN {(short)GeneratedAs.ExpressionStored}
            WHEN a.attgenerated = 'v'  THEN {(short)GeneratedAs.ExpressionVirtual}
          END::smallint                                        AS generated_as
        , pg_get_expr(g.adbin, g.adrelid)                      AS generated_expression
        , pg_get_expr(d.adbin, d.adrelid)                      AS default_expression
        , col_description(a.attrelid, a.attnum)                AS comment
      FROM (SELECT) AS _
      JOIN pg_attribute     AS a  ON a.attrelid      = ANY (@tableIds)
                                 AND a.attnum       > 0
                                 AND NOT a.attisdropped
      LEFT JOIN pg_attrdef  AS d  ON d.adrelid       = a.attrelid
                                 AND d.adnum         = a.attnum
                                 AND a.attgenerated  = ''
      LEFT JOIN pg_attrdef  AS g  ON g.adrelid       = a.attrelid
                                 AND g.adnum         = a.attnum
                                 AND a.attgenerated != ''
      ORDER BY a.attrelid, a.attnum;
      """;
    return _connection.QueryGroupedAsync<long, Column>(sql, TableIds(tables), "name", cancellationToken);
  }

  // A table has at most one primary key. It comes back as a `UniqueConstraint` too, and only its name and columns are used.
  public Task<Dictionary<long, UniqueConstraint>> GetPrimaryKeysAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  )
    => _connection.QueryDictionaryAsync<long, UniqueConstraint>(_KeysSql, KeyParameters(tables, "p"), "name", cancellationToken);

  public Task<Dictionary<long, List<UniqueConstraint>>> GetUniqueConstraintsAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  )
    => _connection.QueryGroupedAsync<long, UniqueConstraint>(_KeysSql, KeyParameters(tables, "u"), "name", cancellationToken);

  public Task<Dictionary<long, List<CheckConstraint>>> GetChecksAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          con.conrelid::bigint           AS table_id
        , con.conname::text              AS name
        , pg_get_constraintdef(con.oid)  AS expression
      FROM (SELECT) AS _
      JOIN pg_constraint  AS con  ON con.contype   = 'c'
                                 AND con.conrelid  = ANY (@tableIds);
      """;
    return _connection.QueryGroupedAsync<long, CheckConstraint>(sql, TableIds(tables), "name", cancellationToken);
  }

  public Task<Dictionary<long, List<ForeignKey>>> GetForeignKeysAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  ) {
    var sql = $"""
      SELECT
          con.conrelid::bigint                     AS table_id
        , con.conname::text                        AS name
        , COALESCE((
            SELECT array_agg(a.attname::text ORDER BY k.ord)
            FROM (SELECT) AS _
            JOIN pg_attribute            AS a                ON a.attrelid  = con.conrelid
            JOIN unnest(con.conkey) WITH ORDINALITY  AS k (attnum, ord)  ON k.attnum    = a.attnum
          ), ARRAY[]::text[])                      AS columns
        , (rn.nspname || '.' || rc.relname)::text  AS referenced_table
        , COALESCE((
            SELECT array_agg(a.attname::text ORDER BY k.ord)
            FROM (SELECT) AS _
            JOIN pg_attribute             AS a                ON a.attrelid  = con.confrelid
            JOIN unnest(con.confkey) WITH ORDINALITY  AS k (attnum, ord)  ON k.attnum    = a.attnum
          ), ARRAY[]::text[])                      AS referenced_columns
        , {ActionCase("con.confdeltype")}          AS on_delete
        , {ActionCase("con.confupdtype")}          AS on_update
        , con.condeferrable                        AS deferrable
        , con.condeferred                          AS initially_deferred
      FROM (SELECT) AS _
      JOIN pg_constraint  AS con  ON con.contype   = 'f'
                                 AND con.conrelid  = ANY (@tableIds)
      JOIN pg_class       AS rc   ON rc.oid        = con.confrelid
      JOIN pg_namespace   AS rn   ON rn.oid        = rc.relnamespace;
      """;
    return _connection.QueryGroupedAsync<long, ForeignKey>(sql, TableIds(tables), "name", cancellationToken);
  }

  // The indexes that are not the index behind a primary key, unique, or exclusion constraint: those are documented as
  // the constraint.
  public Task<Dictionary<long, List<TableIndex>>> GetIndexesAsync(
    Dictionary<long, Table> tables,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          i.indrelid::bigint             AS table_id
        , ic.relname::text               AS name
        , pg_get_indexdef(i.indexrelid)  AS definition
      FROM (SELECT) AS _
      JOIN pg_index  AS i   ON i.indrelid  = ANY (@tableIds)
                           AND NOT EXISTS (
                             SELECT 1
                             FROM (SELECT) AS _
                             JOIN pg_constraint  AS k  ON k.conindid  = i.indexrelid
                                                      AND k.contype  IN ('p', 'u', 'x')
                           )
      JOIN pg_class  AS ic  ON ic.oid      = i.indexrelid;
      """;
    return _connection.QueryGroupedAsync<long, TableIndex>(sql, TableIds(tables), "name", cancellationToken);
  }

  private object SchemaParameters()
    => new {
      excludedSchemas = _excludedSchemas,
      onlySchemas = _onlySchemas,
      restrictToOnly = _restrictToOnly,
    };

  private static object KeyParameters(Dictionary<long, Table> tables, string kind)
    => new {
      tableIds = TableIdArray(tables),
      kind,
    };

  private static object TableIds(Dictionary<long, Table> tables)
    => new { tableIds = TableIdArray(tables) };

  private static long[] TableIdArray(Dictionary<long, Table> tables)
    => [.. tables.Keys];
}
