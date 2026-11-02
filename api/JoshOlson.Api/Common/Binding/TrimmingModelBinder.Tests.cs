using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace JoshOlson.Api.Common.Binding;

public class TrimmingModelBinderTests {
  private static async Task<ModelBindingResult> BindAsync(string? value) {
    var query = new Dictionary<string, StringValues>();
    if (value is not null) query["foo"] = value;
    var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(string));
    var context = new DefaultModelBindingContext {
      ModelName = "foo",
      ModelMetadata = metadata,
      ModelState = new(),
      ValueProvider = new QueryStringValueProvider(BindingSource.Query, new QueryCollection(query), CultureInfo.InvariantCulture),
    };

    await new TrimmingModelBinder().BindModelAsync(context);

    return context.Result;
  }

  [Theory]
  [InlineData("bar", "bar")]
  [InlineData("  bar\t", "bar")]
  [InlineData("", null)]
  [InlineData("   ", null)]
  public async Task Bind_TrimsAndTurnsBlankIntoNull(string value, string? expected) {
    var result = await BindAsync(value);

    Assert.True(result.IsModelSet);
    Assert.Equal(expected, result.Model);
  }

  [Fact]
  public async Task Bind_AbsentValue_SetsNothing()
    => Assert.False((await BindAsync(null)).IsModelSet);
}
