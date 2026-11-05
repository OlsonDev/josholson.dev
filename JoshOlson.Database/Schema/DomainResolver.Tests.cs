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
      Type = ColumnType.Parse(type),
      NotNull = false,
      DefaultExpression = null,
      Comment = null,
      Checks = [],
    };

  [Theory]
  [InlineData("text", SeedType.Text, false)]
  [InlineData("decimal(9, 6)[]", SeedType.Decimal, true)]
  [InlineData("foo.slug", SeedType.Text, false)]
  [InlineData("foo.slug[]", SeedType.Text, true)]
  [InlineData("foo.short_slug", SeedType.Text, false)]
  [InlineData("foo.short_slug[]", SeedType.Text, true)]
  [InlineData("foo.coords", SeedType.Decimal, false)]
  [InlineData("foo.tag_ids", SeedType.SmallInt, true)]
  [InlineData("foo.small_tag_ids", SeedType.SmallInt, true)]
  public void Resolve_FollowsDomainsToTheBaseType(string text, SeedType seed, bool isArray)
    => Assert.Equal(new ResolvedType(seed, isArray), DomainResolver.Resolve(ColumnType.Parse(text), _Domains));

  [Theory]
  [InlineData("foo.nope")]
  [InlineData("foo.gone_child")]
  [InlineData("foo.loop_a")]
  [InlineData("foo.tag_ids[]")]
  [InlineData("foo.small_tag_ids[]")]
  public void Resolve_UnknownDomainLoopOrSecondDimension_ReturnsNull(string text)
    => Assert.Null(DomainResolver.Resolve(ColumnType.Parse(text), _Domains));
}
