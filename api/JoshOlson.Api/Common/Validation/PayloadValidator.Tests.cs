using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Xunit;

namespace JoshOlson.Api.Common.Validation;

public class PayloadValidatorTests {
  [ValidatableType]
  public sealed class Foo {
    [Required, StringLength(5, MinimumLength = 2)]
    public string? Name { get; init; }

    [Range(1, 3)]
    public int Level { get; init; }

    public Bar? Bar { get; init; }

    public List<Bar> Bars { get; init; } = [];
  }

  [ValidatableType]
  public sealed class Bar {
    [Required]
    public string? Label { get; init; }
  }

  public sealed class Baz;

  private static ServiceProvider CreateProvider()
    => new ServiceCollection().AddPayloadValidation().BuildServiceProvider();

  [Fact]
  public async Task ValidateAsync_ReportsEveryErrorUnderTheJsonNames() {
    await using var provider = CreateProvider();
    var validator = provider.GetRequiredService<PayloadValidator>();
    var foo = new Foo {
      Name = "x",
      Level = 9,
      Bar = new() { Label = null },
      Bars = [new() { Label = null }, new() { Label = "ok" }],
    };

    var errors = await validator.ValidateAsync(foo, TestContext.Current.CancellationToken);

    var details = errors.ToProblemDetails();
    Assert.Equal(["bar.label", "bars[0].label", "level", "name"], details.Errors.Keys.Order(StringComparer.Ordinal));
    Assert.Contains("minimum length of 2", Assert.Single(details.Errors["name"]));
  }

  [Fact]
  public async Task ValidateAsync_ValidPayload_HasNoErrors() {
    await using var provider = CreateProvider();
    var validator = provider.GetRequiredService<PayloadValidator>();

    var errors = await validator.ValidateAsync(new Foo { Name = "ok", Level = 1 }, TestContext.Current.CancellationToken);

    Assert.False(errors.Any);
  }

  [Fact]
  public async Task ValidateAsync_UnmarkedType_Throws() {
    await using var provider = CreateProvider();
    var validator = provider.GetRequiredService<PayloadValidator>();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => validator.ValidateAsync(new Baz(), TestContext.Current.CancellationToken));
  }
}
