using Dapper;
using JoshOlson.Api.Common.Db;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Api.Tags;

public sealed class TagRepository {
  private readonly NpgsqlConnection _connection;

  public TagRepository(NpgsqlConnection connection)
    => _connection = connection;

  public Task<List<TagResponse>> GetAllAsync(CancellationToken cancellationToken)
    => _connection.QueryAsync<TagResponse>(BuildSelectSql("TRUE"), cancellationToken);

  public async Task<TagResponse?> GetAsync(int tagId, CancellationToken cancellationToken) {
    var parameters = new { tagId };
    return await _connection.QuerySingleOrDefaultAsync<TagResponse>(
      BuildSelectSql("t.tag_id = @tagId"),
      parameters,
      cancellationToken);
  }

  // The columns are written once. The condition is SQL this file chose, never request data.
  private static string BuildSelectSql(string tagCondition)
    => $"""
      SELECT
          t.tag_id            AS tag_id
        , s.slug              AS slug
        , t.name              AS name
        , t.description       AS description
        , t.description_html  AS description_html
        , t.color             AS color
        , t.version           AS version
        , ARRAY (
            SELECT a.name::text
            FROM (SELECT)   AS _
            JOIN tag.alias  AS a  ON a.tag_id = t.tag_id
            ORDER BY lower(a.name::text), a.alias_id
          )                   AS aliases
        , ARRAY (
            SELECT p.slug::text
            FROM (SELECT)  AS _
            JOIN tag.slug  AS p  ON p.tag_id = t.tag_id
                                AND NOT p.is_canonical
            ORDER BY p.created_utc, p.slug_id
          )                   AS previous_slugs
      FROM (SELECT)  AS _
      JOIN tag.tag   AS t  ON {tagCondition}
      JOIN tag.slug  AS s  ON s.tag_id = t.tag_id
                          AND s.is_canonical
      ORDER BY t.name, t.tag_id;
      """;

  // The tag a slug belongs to, current or previous.
  public async Task<int?> GetTagIdBySlugAsync(string slug, CancellationToken cancellationToken) {
    var sql = """
      SELECT
          s.tag_id  AS tag_id
      FROM (SELECT)  AS _
      JOIN tag.slug  AS s  ON s.slug = @slug;
      """;
    var parameters = new { slug };
    return await _connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, cancellationToken);
  }

  // Both uniqueness checks in one round trip: up to two statements, each an equality lookup on its own unique index
  // (uq_slug_slug, ux_tag_name_lower). Pass null for a field that is not worth checking and its statement is left out.
  // exceptTagId is the tag being updated, so it does not collide with itself; null means no tag is excluded (a create).
  // Names are unique ignoring case. A slug is taken by any tag's current or previous slug.
  public async Task<TakenTagFields> CheckTakenAsync(
    string? slugToCheck,
    string? nameToCheck,
    int? exceptTagId,
    CancellationToken cancellationToken
  ) {
    if (slugToCheck is null && nameToCheck is null) return new(IsSlugTaken: false, IsNameTaken: false);

    var sql = _CheckTakenSql.ForValues(slugToCheck is not null, nameToCheck is not null, exceptTagId is not null);
    var parameters = new {
      slugToCheck,
      nameToCheck,
      exceptTagId,
    };
    await using var data = await _connection.QueryMultipleAsync(sql, parameters, cancellationToken);
    // The results come back in the order the statements were built: slug first, then name.
    var isSlugTaken = slugToCheck is not null && await data.ReadSingleAsync<bool>();
    var isNameTaken = nameToCheck is not null && await data.ReadSingleAsync<bool>();
    return new(isSlugTaken, isNameTaken);
  }

  private static readonly SqlMemoizer<bool, bool, bool> _CheckTakenSql = new((checkSlug, checkName, exceptTag) => {
    var statements = new List<string>();
    if (checkSlug) statements.Add(BuildTakenSql("slug", "tag.slug", "s", ignoreCase: false, exceptTag));
    if (checkName) statements.Add(BuildTakenSql("name", "tag.tag", "t", ignoreCase: true, exceptTag));
    return string.Join('\n', statements);
  });

  // The field, table, and alias are our own, never request data, so they are safe in the SQL text. The AND line is
  // right-aligned with the ON and the operators are aligned, as sql.md lays out a handwritten query.
  private static string BuildTakenSql(string field, string table, string alias, bool ignoreCase, bool exceptTag) {
    var column = $"{alias}.{field}";
    var exceptColumn = $"{alias}.tag_id";
    var parameter = $"@{field}ToCheck";
    var left = ignoreCase ? $"lower({column}::text)" : column;
    var right = ignoreCase ? $"lower({parameter}::text)" : parameter;
    var fromSource = "FROM (SELECT)";
    var joinSource = $"JOIN {table}";
    var sourceWidth = Math.Max(fromSource.Length, joinSource.Length);
    // The AND ends under the N of ON, which is 13 columns past the sources, in the text as the raw string strips it.
    var andIndent = sourceWidth + 13;
    var width = exceptTag ? Math.Max(left.Length, exceptColumn.Length) : left.Length;
    var conditions = $"{left.PadRight(width)}  = {right}";
    if (exceptTag) conditions += $"\n{new string(' ', andIndent)}AND {exceptColumn.PadRight(width)} != @exceptTagId";
    return $"""
      SELECT
          EXISTS (
            SELECT 1
            {fromSource.PadRight(sourceWidth)}  AS _
            {joinSource.PadRight(sourceWidth)}  AS {alias}  ON {conditions}
          )  AS is_{field}_taken;
      """;
  }

  // Which of the given names (already lowercase) another tag has as a name or an alias. The tag being saved is left
  // out, so saving it again does not collide with itself.
  public async Task<List<string>> GetTakenAliasesAsync(
    List<string> lowerNames,
    int? exceptTagId,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          lower(t.name::text)  AS name
      FROM (SELECT)  AS _
      JOIN tag.tag   AS t  ON lower(t.name::text)  = ANY (@lowerNames)
                          AND t.tag_id            IS DISTINCT FROM @exceptTagId
      UNION
      SELECT
          lower(a.name::text)  AS name
      FROM (SELECT)   AS _
      JOIN tag.alias  AS a  ON lower(a.name::text)  = ANY (@lowerNames)
                           AND a.tag_id            IS DISTINCT FROM @exceptTagId;
      """;
    var parameters = new {
      lowerNames,
      exceptTagId,
    };
    return await _connection.QueryAsync<string>(sql, parameters, cancellationToken);
  }

  // Null when there is no such tag.
  public async Task<TagReferenceCounts?> GetReferenceCountsAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      SELECT
          (
            SELECT count(*)::int
            FROM (SELECT)               AS _
            JOIN resume.work__tag__tag  AS x  ON x.tag_id = t.tag_id
          )  AS works
        , (
            SELECT count(*)::int
            FROM (SELECT)                  AS _
            JOIN resume.project__tag__tag  AS x  ON x.tag_id = t.tag_id
          )  AS projects
        , (
            SELECT count(*)::int
            FROM (SELECT)                AS _
            JOIN resume.skill__tag__tag  AS x  ON x.tag_id = t.tag_id
          )  AS skills
        , (
            SELECT count(*)::int
            FROM (SELECT)             AS _
            JOIN blog.post__tag__tag  AS x  ON x.tag_id = t.tag_id
          )  AS posts
      FROM (SELECT)  AS _
      JOIN tag.tag   AS t  ON t.tag_id = @tagId;
      """;
    var parameters = new { tagId };
    return await _connection.QuerySingleOrDefaultAsync<TagReferenceCounts>(sql, parameters, cancellationToken);
  }

  public async Task<int> CreateAsync(TagRequest request, string? descriptionHtml, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO tag.tag (
          name
        , description
        , description_html
        , color
      ) VALUES (
          @name
        , @description
        , @descriptionHtml
        , @color
      )
      RETURNING tag_id;
      """;
    var parameters = new {
      request.Name,
      request.Description,
      descriptionHtml,
      request.Color,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }

  // False when there is no such tag, or when the request expects a version of it that is no longer current.
  public async Task<bool> UpdateAsync(
    int tagId,
    TagRequest request,
    string? descriptionHtml,
    CancellationToken cancellationToken
  ) {
    var sql = _UpdateSql.ForValues(request.ExpectedVersion is not null);
    var parameters = new {
      tagId,
      request.Name,
      request.Description,
      descriptionHtml,
      request.Color,
      request.ExpectedVersion,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  private static readonly SqlMemoizer<bool> _UpdateSql = new(checkVersion => {
    var versionCheck = checkVersion ? "\n        AND t.version  = @expectedVersion" : "";
    return $"""
      UPDATE tag.tag AS t SET
          name              = @name
        , description       = @description
        , description_html  = @descriptionHtml
        , color             = @color
        , version           = t.version + 1
      WHERE t.tag_id   = @tagId{versionCheck};
      """;
  });

  public async Task AddSlugAsync(int tagId, string slug, bool isCanonical, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO tag.slug (
          tag_id
        , slug
        , is_canonical
      ) VALUES (
          @tagId
        , @slug
        , @isCanonical
      );
      """;
    var parameters = new {
      tagId,
      slug,
      isCanonical,
    };
    await _connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  // Two statements in one command (one round trip, one implicit transaction), in this order, because the unique
  // index allows one canonical slug per tag at every moment and checks each row as it is written, so one UPDATE that
  // flips both rows would fail whenever it reached the new canonical row first.
  public async Task SetCanonicalSlugAsync(int tagId, string slug, CancellationToken cancellationToken) {
    var sql = """
      UPDATE tag.slug AS s SET
          is_canonical  = FALSE
      WHERE s.tag_id        = @tagId
        AND s.slug         != @slug
        AND s.is_canonical;

      UPDATE tag.slug AS s SET
          is_canonical  = TRUE
      WHERE s.tag_id  = @tagId
        AND s.slug    = @slug;
      """;
    var parameters = new {
      tagId,
      slug,
    };
    await _connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  // Drops the tag's previous slugs that are not in the list. The current slug is never dropped.
  public async Task DeleteSlugsExceptAsync(int tagId, List<string> keep, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM tag.slug AS s
      WHERE s.tag_id       = @tagId
        AND NOT s.is_canonical
        AND s.slug::text  != ALL (@keep);
      """;
    var parameters = new {
      tagId,
      keep,
    };
    await _connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  public async Task DeleteAliasesAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM tag.alias AS a
      WHERE a.tag_id = @tagId;
      """;
    var parameters = new { tagId };
    await _connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  public async Task AddAliasAsync(int tagId, string name, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO tag.alias (
          tag_id
        , name
      ) VALUES (
          @tagId
        , @name
      );
      """;
    var parameters = new {
      tagId,
      name,
    };
    await _connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  public async Task<bool> DeleteAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM tag.tag AS t
      WHERE t.tag_id = @tagId;
      """;
    var parameters = new { tagId };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }
}
