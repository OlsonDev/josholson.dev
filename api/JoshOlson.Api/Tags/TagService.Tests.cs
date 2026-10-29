using JoshOlson.Api.Common.Db;
using JoshOlson.Api.Common.DependencyInjection;
using JoshOlson.Api.Common.Testing;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JoshOlson.Api.Tags;

public class TagServiceTests {
  private static ServiceProvider CreateProvider() {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = TestDatabase.ConnectionString })
      .Build();
    return new ServiceCollection()
      .AddLogging()
      .AddDatabase(configuration)
      .AddServicesAndRepositories()
      .AddPayloadValidation()
      .BuildServiceProvider(validateScopes: true);
  }

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
  public async Task ValidateCreateAsync_ReportsEveryFieldError() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var provider = CreateProvider();
    using var scope = provider.CreateScope();
    var tagService = scope.ServiceProvider.GetRequiredService<TagService>();
    var request = new TagRequest { Name = " foo", Slug = "Not Kebab", Description = "", Color = "red" };

    var errors = await tagService.ValidateCreateAsync(request, cancellationToken);

    Assert.Equal(["color", "description", "name", "slug"], errors.ToProblemDetails().Errors.Keys.Order());
  }

  [Fact]
  public async Task ValidateCreateAsync_TakenNameAndSlug_AreReported() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var provider = CreateProvider();
    using var scope = provider.CreateScope();
    var tagService = scope.ServiceProvider.GetRequiredService<TagService>();
    var request = NewRequest();
    var tagId = await tagService.CreateAsync(request, cancellationToken);
    try {
      var errors = await tagService.ValidateCreateAsync(request with { Name = request.Name.ToUpperInvariant() }, cancellationToken);

      Assert.Equal(["name", "slug"], errors.ToProblemDetails().Errors.Keys.Order());
    } finally {
      await tagService.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task ValidateUpdateAsync_OwnNameAndSlug_AreNotTaken() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var provider = CreateProvider();
    using var scope = provider.CreateScope();
    var tagService = scope.ServiceProvider.GetRequiredService<TagService>();
    var request = NewRequest();
    var tagId = await tagService.CreateAsync(request, cancellationToken);
    try {
      var own = await tagService.ValidateUpdateAsync(tagId, request with { Description = "foo" }, cancellationToken);
      var other = await tagService.ValidateUpdateAsync(tagId + 1, request, cancellationToken);

      Assert.False(own.Any);
      Assert.True(other.Has("slug"));
    } finally {
      await tagService.DeleteAsync(tagId, CancellationToken.None);
    }
  }
}
