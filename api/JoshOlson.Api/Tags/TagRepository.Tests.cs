using JoshOlson.Api.Common.Testing;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Tags;

public class TagRepositoryTests {
  private static TagRequest NewRequest() {
    var slug = $"test-{Guid.NewGuid():N}";
    return new() {
      Name = slug,
      Slug = slug,
      Description = null,
      Color = null,
    };
  }

  [Fact]
  public async Task Lifecycle_CreatesReadsUpdatesAndDeletes() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    var tagRepository = new TagRepository(connection);
    var request = NewRequest();

    var tagId = await tagRepository.CreateAsync(request, cancellationToken);
    try {
      var created = await tagRepository.GetAsync(tagId, cancellationToken);
      Assert.NotNull(created);
      Assert.Equal(request.Slug, created.Slug);
      Assert.Null(created.Description);
      Assert.Contains(await tagRepository.GetAllAsync(cancellationToken), t => t.TagId == tagId);

      Assert.True(await tagRepository.UpdateAsync(tagId, request with { Description = "foo" }, cancellationToken));
      Assert.Equal("foo", (await tagRepository.GetAsync(tagId, cancellationToken))?.Description);
    } finally {
      Assert.True(await tagRepository.DeleteAsync(tagId, CancellationToken.None));
    }

    Assert.Null(await tagRepository.GetAsync(tagId, cancellationToken));
    Assert.False(await tagRepository.UpdateAsync(tagId, request, cancellationToken));
    Assert.False(await tagRepository.DeleteAsync(tagId, cancellationToken));
  }

  [Fact]
  public async Task CheckTakenAsync_ReportsEachFieldIndependently() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    var tagRepository = new TagRepository(connection);
    var request = NewRequest();
    var tagId = await tagRepository.CreateAsync(request, cancellationToken);
    try {
      var other = $"test-{Guid.NewGuid():N}";

      Assert.Equal((true, true), await tagRepository.CheckTakenAsync(request.Slug, request.Name, null, cancellationToken));
      Assert.Equal((true, false), await tagRepository.CheckTakenAsync(request.Slug, other, null, cancellationToken));
      Assert.Equal((false, true), await tagRepository.CheckTakenAsync(other, request.Name.ToUpperInvariant(), null, cancellationToken));
      Assert.Equal((false, false), await tagRepository.CheckTakenAsync(other, other, null, cancellationToken));
      Assert.Equal((true, false), await tagRepository.CheckTakenAsync(request.Slug, null, null, cancellationToken));
      Assert.Equal((false, true), await tagRepository.CheckTakenAsync(null, request.Name, null, cancellationToken));
      Assert.Equal((false, false), await tagRepository.CheckTakenAsync(null, null, null, cancellationToken));
      // Excluding the tag itself, for each combination of fields checked.
      Assert.Equal((false, false), await tagRepository.CheckTakenAsync(request.Slug, request.Name, tagId, cancellationToken));
      Assert.Equal((false, false), await tagRepository.CheckTakenAsync(request.Slug, null, tagId, cancellationToken));
      Assert.Equal((false, false), await tagRepository.CheckTakenAsync(null, request.Name, tagId, cancellationToken));
      // 0 is an id like any other, not "none".
      Assert.Equal((true, true), await tagRepository.CheckTakenAsync(request.Slug, request.Name, 0, cancellationToken));
    } finally {
      await tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public void BuildCheckTakenSql_LeavesOutWhatIsNotRequested() {
    var slugOnly = TagRepository.BuildCheckTakenSql(checkSlug: true, checkName: false, exceptTag: false);
    var nameExcept = TagRepository.BuildCheckTakenSql(checkSlug: false, checkName: true, exceptTag: true);
    var both = TagRepository.BuildCheckTakenSql(checkSlug: true, checkName: true, exceptTag: true);

    Assert.Contains("is_slug_taken", slugOnly);
    Assert.DoesNotContain("is_name_taken", slugOnly);
    Assert.DoesNotContain("@exceptTagId", slugOnly);
    Assert.Contains("is_name_taken", nameExcept);
    Assert.DoesNotContain("is_slug_taken", nameExcept);
    Assert.Contains("AND t.tag_id", nameExcept);
    Assert.Equal(2, both.Split("EXISTS").Length - 1);
  }

  // Repositories sharing one open connection take part in whatever transaction is open on it,
  // and a transaction that is disposed without CommitAsync rolls back.
  [Fact]
  public async Task UncommittedTransaction_RollsBack() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    var tagRepository = new TagRepository(connection);
    int tagId;

    await using (await connection.BeginTransactionAsync(cancellationToken)) {
      tagId = await tagRepository.CreateAsync(NewRequest(), cancellationToken);
      Assert.NotNull(await tagRepository.GetAsync(tagId, cancellationToken));
      await using var otherConnection = new NpgsqlConnection(TestDatabase.ConnectionString);
      await otherConnection.OpenAsync(cancellationToken);
      Assert.Null(await new TagRepository(otherConnection).GetAsync(tagId, cancellationToken));
    }

    Assert.Null(await tagRepository.GetAsync(tagId, cancellationToken));
  }

  [Fact]
  public async Task CommittedTransaction_Persists() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    var tagRepository = new TagRepository(connection);
    int tagId;

    await using (var transaction = await connection.BeginTransactionAsync(cancellationToken)) {
      tagId = await tagRepository.CreateAsync(NewRequest(), cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }

    try {
      Assert.NotNull(await tagRepository.GetAsync(tagId, cancellationToken));
    } finally {
      await tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }
}
