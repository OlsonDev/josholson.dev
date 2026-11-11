using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Catalog;

// Reads the schema the seed tool works against out of a database's catalogs: the domains, and the tables with their
// columns, constraints, foreign keys, and indexes. Postgres spells types its own way (`timestamp with time zone`,
// `numeric(9,6)`), so the queries print them in the form the lock uses (`timestamptz`, `decimal(9, 6)`) and the C# side
// only ever sees that one spelling. Types stay text in the model; reading checks that each is one the seed tool
// supports, and a column or domain of a type it has no seed type for, or a bad `@seed` comment, is a problem. Every
// problem is collected, with the table and column named, and they are thrown together once the whole schema is checked.
public static class SchemaCatalog {
  // A snapshot, so the queries agree with each other; and only pg_catalog on the search path, so that format_type
  // qualifies every other type (a domain) with its schema. The transaction is read-only in effect: nothing here writes.
  public static async Task<CatalogSchema> ReadAsync(NpgsqlConnection connection, CatalogOptions options, CancellationToken cancellationToken) {
    await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
    _ = await connection.ExecuteAsync("SET LOCAL search_path TO pg_catalog", parameters: null, cancellationToken);

    var catalogRepository = new CatalogRepository(connection, options);
    var tables = await catalogRepository.GetTablesAsync(cancellationToken);
    var domains = await catalogRepository.GetDomainsAsync(cancellationToken);
    var rows = new CatalogRows {
      Schemas = await catalogRepository.GetSchemaCommentsAsync(cancellationToken),
      Tables = tables,
      Domains = domains,
      DomainChecks = await catalogRepository.GetDomainChecksAsync(domains, cancellationToken),
      Columns = await catalogRepository.GetColumnsAsync(tables, cancellationToken),
      PrimaryKeys = await catalogRepository.GetPrimaryKeysAsync(tables, cancellationToken),
      UniqueConstraints = await catalogRepository.GetUniqueConstraintsAsync(tables, cancellationToken),
      Checks = await catalogRepository.GetChecksAsync(tables, cancellationToken),
      ForeignKeys = await catalogRepository.GetForeignKeysAsync(tables, cancellationToken),
      Indexes = await catalogRepository.GetIndexesAsync(tables, cancellationToken),
    };
    await transaction.CommitAsync(cancellationToken);

    return Assemble(rows);
  }

  private static CatalogSchema Assemble(CatalogRows rows) {
    var problems = new List<string>();
    var domains = BuildDomains(rows, problems);
    var domainsByName = domains.ToDictionary(domain => domain.Name, StringComparer.Ordinal);
    var tables = new List<Table>(rows.Tables.Count);
    foreach (var (key, table) in rows.Tables) {
      tables.Add(BuildTable(key, table, rows, domainsByName, problems));
    }
    if (problems.Count > 0) {
      problems.Sort(StringComparer.Ordinal);
      throw new SchemaCatalogException(problems);
    }
    return new() {
      Schemas = SortByName(rows.Schemas),
      Domains = domains,
      Tables = SortByName(tables),
    };
  }

  private static Table BuildTable(
    long key,
    Table table,
    CatalogRows rows,
    Dictionary<string, Domain> domains,
    List<string> problems
  ) {
    var primaryKey = rows.PrimaryKeys.GetValueOrDefault(key);
    var (ignored, problem) = SeedDirectives.TableIgnoreOf(table.Comment, table.Name);
    if (problem is not null) {
      problems.Add(problem);
    }
    return table with {
      Ignored = ignored,
      Columns = [.. rows.Columns.GetValueOrDefault(key, []).Select(column => FinishColumn(table.Name, column, domains, problems))],
      PrimaryKey = primaryKey?.Columns ?? [],
      PrimaryKeyName = primaryKey?.Name,
      UniqueConstraints = ByName(rows.UniqueConstraints, key),
      Checks = ByName(rows.Checks, key),
      ForeignKeys = ByName(rows.ForeignKeys, key),
      Indexes = ByName(rows.Indexes, key),
    };
  }

  private static List<Domain> BuildDomains(CatalogRows rows, List<string> problems) {
    var domains = new List<Domain>(rows.Domains.Count);
    foreach (var (key, domain) in rows.Domains) {
      if (!ColumnType.TryParse(domain.Type, out _)) {
        problems.Add($"Domain {domain.Name} is built on '{domain.Type}', which the seed tool does not support.");
      }
      domains.Add(domain with { Checks = ByName(rows.DomainChecks, key) });
    }
    return SortByName(domains);
  }

  // One parent's children, by name; none is an empty list.
  private static List<T> ByName<T>(Dictionary<long, List<T>> children, long key) where T : INamed
    => children.TryGetValue(key, out var list) ? SortByName(list) : [];

  // Sorts the list where it is and hands it back, so a list a query built is not copied. Names are unique within a
  // parent, so the sort being unstable does not matter.
  private static List<T> SortByName<T>(List<T> items) where T : INamed {
    items.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
    return items;
  }

  // The two things the query cannot say: the effective nullability and the time policy, which follow the type through
  // its domains. A problem is added for a type or a `@seed` comment the seed tool cannot use, and the column is returned
  // as it is, since the result is thrown away.
  private static Column FinishColumn(string table, Column column, Dictionary<string, Domain> domains, List<string> problems) {
    var where = $"{table}.{column.Name}";
    if (!ColumnType.TryParse(column.Type, out _)) {
      problems.Add($"{where} has type '{column.Type}', which the seed tool does not support.");
      return column;
    }
    if (DomainResolver.Resolve(column.Type, domains) is not { } resolved) {
      problems.Add($"{where} has type '{column.Type}', which is not a supported type or a domain built on one.");
      return column;
    }
    var (directive, problem) = SeedDirectives.TimePolicyOf(column.Comment, where);
    if (problem is not null) {
      problems.Add(problem);
    } else if (resolved.Seed is not SeedType.TimestampTz && directive is not null) {
      problems.Add($"{where}: a @seed time directive belongs on a timestamptz column, and this one is {column.Type}.");
    }
    return column with {
      Nullable = IsNullable(column.DeclaredNullable, column.Type, domains),
      Policy = resolved.Seed is SeedType.TimestampTz ? directive ?? TimePolicy.Relative : null,
    };
  }

  // A column can take NULL unless it says NOT NULL itself or its type is a domain (or a domain built on one) that does.
  // An array of a domain is a different matter: the domain's rule is about the elements. The type must be one that
  // `DomainResolver` has resolved.
  internal static bool IsNullable(bool declaredNullable, string type, Dictionary<string, Domain> domains) {
    if (!declaredNullable) {
      return false;
    }
    var current = ColumnType.Parse(type);
    while (current is { IsArray: false, IsDomain: true }) {
      var domain = domains[current.Name];
      if (domain.NotNull) {
        return false;
      }
      current = ColumnType.Parse(domain.Type);
    }
    return true;
  }
}
