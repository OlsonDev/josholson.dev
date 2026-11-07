using Xunit;

namespace JoshOlson.Database.Schema;

public class DomainResolverTests {
  private static readonly Dictionary<string, Domain> _Domains = new[] {
    NewDomain("foo.slug", "text"),
    NewDomain("foo.short_slug", "foo.slug"),
    NewDomain("foo.coords", "decimal(9, 6)"),
    NewDomain("foo.tag_ids", "smallint[]"),
    NewDomain("foo.small_tag_ids", "foo.tag_ids"),
    NewDomain("foo.loop_a", "foo.loop_b"),
    NewDomain("foo.loop_b", "foo.loop_a"),
    NewDomain("foo.gone_child", "foo.gone"),
  }.ToDictionary(domain => domain.Name, StringComparer.Ordinal);

  private static Domain NewDomain(string name, string type)
    => new() {
      Name = name,
      Type = type,
      NotNull = false,
      DefaultExpression = null,
      Comment = null,
      Checks = [],
    };

  [Theory]
  [InlineData("text", SeedType.Text, false, null)]
  [InlineData("decimal(9, 6)[]", SeedType.Decimal, true, "9, 6")]
  [InlineData("foo.slug", SeedType.Text, false, null)]
  [InlineData("foo.slug[]", SeedType.Text, true, null)]
  [InlineData("foo.short_slug", SeedType.Text, false, null)]
  [InlineData("foo.short_slug[]", SeedType.Text, true, null)]
  [InlineData("foo.coords", SeedType.Decimal, false, "9, 6")]
  [InlineData("foo.tag_ids", SeedType.SmallInt, true, null)]
  [InlineData("foo.small_tag_ids", SeedType.SmallInt, true, null)]
  public void Resolve_FollowsDomainsToTheBaseType(string text, SeedType seed, bool isArray, string? modifiers)
    => Assert.Equal(new ResolvedType(seed, isArray, modifiers), DomainResolver.Resolve(text, _Domains));

  [Theory]
  [InlineData("foo.nope")]
  [InlineData("foo.gone_child")]
  [InlineData("foo.loop_a")]
  [InlineData("foo.tag_ids[]")]
  [InlineData("foo.small_tag_ids[]")]
  [InlineData("interval")]
  public void Resolve_UnsupportedTypeUnknownDomainLoopOrSecondDimension_ReturnsNull(string text)
    => Assert.Null(DomainResolver.Resolve(text, _Domains));
}
