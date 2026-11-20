using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Api.Tags;

// Rows in other features that refer to a tag, which the tag tests make and remove. A test takes this in its
// constructor: `Startup` registers every `*TestData` class as scoped.
public sealed class TagTestData {
  private readonly NpgsqlConnection _connection;

  public TagTestData(NpgsqlConnection connection)
    => _connection = connection;

  // A post tagged with the tag: the cheapest reference to make, since a post needs only a slug, a title, and a body.
  public async Task<int> CreatePostWithTagAsync(int tagId, CancellationToken cancellationToken) {
    var postSql = """
      INSERT INTO blog.post (
          slug
        , title
        , body
      ) VALUES (
          @slug
        , 'foo'
        , 'bar'
      )
      RETURNING post_id;
      """;
    var postParameters = new { slug = $"test-{Guid.NewGuid():N}" };
    var postId = await _connection.QuerySingleAsync<int>(postSql, postParameters, cancellationToken);

    var linkSql = """
      INSERT INTO blog.post__tag__tag (
          post_id
        , tag_id
      ) VALUES (
          @postId
        , @tagId
      );
      """;
    var linkParameters = new {
      postId,
      tagId,
    };
    await _connection.ExecuteAsync(linkSql, linkParameters, cancellationToken);
    return postId;
  }

  public async Task DeletePostAsync(int postId) {
    var sql = """
      DELETE FROM blog.post AS p
      WHERE p.post_id = @postId;
      """;
    var parameters = new { postId };
    await _connection.ExecuteAsync(sql, parameters, CancellationToken.None);
  }
}
