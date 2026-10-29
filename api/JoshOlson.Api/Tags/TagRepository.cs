using Dapper;
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
    var cmd = new CommandDefinition(sql, cancellationToken: cancellationToken);
    var tags = await _connection.QueryAsync<TagResponse>(cmd);
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
    var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
    return await _connection.QuerySingleOrDefaultAsync<TagResponse>(cmd);
  }

  // Both uniqueness checks in one round trip: up to two statements, each an equality lookup on its own unique index
  // (uq_tag_slug, ux_tag_name_lower). Pass null for a field that is not worth checking and its statement is left out.
  // exceptTagId is the tag being updated, so it does not collide with itself; identity ids start at 1, so pass 0 for
  // none. Names are unique ignoring case.
  public async Task<(bool IsSlugTaken, bool IsNameTaken)> CheckTakenAsync(
    string? slugToCheck, string? nameToCheck, int exceptTagId, CancellationToken cancellationToken
  ) {
    const string SlugSql = """
      SELECT
          EXISTS (
            SELECT 1
            FROM (SELECT)  AS _
            JOIN tag.tag   AS t  ON t.slug    = @slugToCheck
                                AND t.tag_id != @exceptTagId
          )  AS is_slug_taken;
      """;
    const string NameSql = """
      SELECT
          EXISTS (
            SELECT 1
            FROM (SELECT)  AS _
            JOIN tag.tag   AS t  ON lower(t.name::text) = lower(@nameToCheck::text)
                                AND t.tag_id           != @exceptTagId
          )  AS is_name_taken;
      """;

    var sql = (slugToCheck, nameToCheck) switch {
      (null, null) => null,
      (not null, null) => SlugSql,
      (null, not null) => NameSql,
      _ => SlugSql + "\n" + NameSql,
    };
    if (sql is null) return (false, false);

    var parameters = new {
      slugToCheck,
      nameToCheck,
      exceptTagId,
    };
    var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
    using var data = await _connection.QueryMultipleAsync(cmd);
    var isSlugTaken = slugToCheck is not null && await data.ReadSingleAsync<bool>();
    var isNameTaken = nameToCheck is not null && await data.ReadSingleAsync<bool>();
    return (isSlugTaken, isNameTaken);
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
      request.Color
    };
    var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
    return await _connection.QuerySingleAsync<int>(cmd);
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
      request.Color
    };
    var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
    return await _connection.ExecuteAsync(cmd) > 0;
  }

  public async Task<bool> DeleteAsync(int tagId, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM tag.tag
      WHERE tag_id = @tagId;
      """;
    var parameters = new { tagId };
    var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
    return await _connection.ExecuteAsync(cmd) > 0;
  }
}
