using Dapper;
using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.Extensions;
using Npgsql;

namespace JoshOlson.Api.Tags;

public sealed class TagRepository {
  private readonly NpgsqlConnection _connection;

  public TagRepository(NpgsqlConnection connection)
    => _connection = connection;

  public async Task<IReadOnlyList<TagResponse>> GetAllAsync(CancellationToken cancellationToken) {
    var sql = """
      SELECT
          t.tag_id       AS tag_id
        , t.name         AS name
        , t.slug         AS slug
        , t.description  AS description
        , t.color        AS color
      FROM (SELECT)  AS _
      JOIN tag.tag   AS t  ON TRUE
      ORDER BY t.name, t.tag_id;
      """;
    var tags = await _connection.QueryAsync<TagResponse>(sql, cancellationToken);
    return tags.AsList();
  }

  public async Task<TagResponse?> GetAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      SELECT
          t.tag_id       AS tag_id
        , t.name         AS name
        , t.slug         AS slug
        , t.description  AS description
        , t.color        AS color
      FROM (SELECT)  AS _
      JOIN tag.tag   AS t  ON t.tag_id = @tagId;
      """;
    var parameters = new { tagId };
    return await _connection.QuerySingleOrDefaultAsync<TagResponse>(sql, parameters, cancellationToken);
  }

  // Both uniqueness checks in one round trip: up to two statements, each an equality lookup on its own unique index
  // (uq_tag_slug, ux_tag_name_lower). Pass null for a field that is not worth checking and its statement is left out.
  // exceptTagId is the tag being updated, so it does not collide with itself; null means no tag is excluded (a create).
  // Names are unique ignoring case.
  public async Task<(bool IsSlugTaken, bool IsNameTaken)> CheckTakenAsync(
    string? slugToCheck,
    string? nameToCheck,
    int? exceptTagId,
    CancellationToken cancellationToken
  ) {
    if (slugToCheck is null && nameToCheck is null) return (false, false);

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
    return (isSlugTaken, isNameTaken);
  }

  private static readonly SqlMemoizer<bool, bool, bool> _CheckTakenSql = new((checkSlug, checkName, exceptTag) => {
    List<string> statements = [];
    if (checkSlug) statements.Add(BuildTakenSql("slug", ignoreCase: false, exceptTag));
    if (checkName) statements.Add(BuildTakenSql("name", ignoreCase: true, exceptTag));
    return string.Join('\n', statements);
  });

  // The field is one of our own column names, never request data, so it is safe in the SQL text. The AND line is
  // right-aligned with the ON and the operators are aligned, as sql.md lays out a handwritten query.
  private static string BuildTakenSql(string field, bool ignoreCase, bool exceptTag) {
    const string ExceptColumn = "t.tag_id";
    const int AndIndent = 26;
    var column = $"t.{field}";
    var parameter = $"@{field}ToCheck";
    var left = ignoreCase ? $"lower({column}::text)" : column;
    var right = ignoreCase ? $"lower({parameter}::text)" : parameter;
    var width = exceptTag ? Math.Max(left.Length, ExceptColumn.Length) : left.Length;
    var conditions = $"{left.PadRight(width)}  = {right}";
    if (exceptTag) conditions += $"\n{new string(' ', AndIndent)}AND {ExceptColumn.PadRight(width)} != @exceptTagId";
    return $"""
      SELECT
          EXISTS (
            SELECT 1
            FROM (SELECT)  AS _
            JOIN tag.tag   AS t  ON {conditions}
          )  AS is_{field}_taken;
      """;
  }

  public async Task<int> CreateAsync(TagRequest request, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO tag.tag (
          name
        , slug
        , description
        , color
      ) VALUES (
          @name
        , @slug
        , @description
        , @color
      )
      RETURNING tag_id;
      """;
    var parameters = new {
      request.Name,
      request.Slug,
      request.Description,
      request.Color,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }

  public async Task<bool> UpdateAsync(int tagId, TagRequest request, CancellationToken cancellationToken) {
    var sql = """
      UPDATE tag.tag SET
          name         = @name
        , slug         = @slug
        , description  = @description
        , color        = @color
      WHERE tag_id = @tagId;
      """;
    var parameters = new {
      tagId,
      request.Name,
      request.Slug,
      request.Description,
      request.Color,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  public async Task<bool> DeleteAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM tag.tag
      WHERE tag_id = @tagId;
      """;
    var parameters = new { tagId };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }
}
