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
