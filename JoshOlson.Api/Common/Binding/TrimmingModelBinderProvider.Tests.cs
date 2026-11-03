using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace JoshOlson.Api.Common.Binding;

public class TrimmingModelBinderProviderTests {
  private sealed class FakeContext : ModelBinderProviderContext {
    public FakeContext(Type modelType, BindingSource? source) {
      MetadataProvider = new EmptyModelMetadataProvider();
      Metadata = MetadataProvider.GetMetadataForType(modelType);
      BindingInfo = new() { BindingSource = source };
    }

    public override BindingInfo BindingInfo { get; }
    public override ModelMetadata Metadata { get; }
    public override IModelMetadataProvider MetadataProvider { get; }

    public override IModelBinder CreateBinder(ModelMetadata metadata)
      => throw new NotSupportedException();
  }

  public static TheoryData<BindingSource> TextSources
    => [
      BindingSource.Path,
      BindingSource.Query,
      BindingSource.Form,
      BindingSource.Header,
    ];

  private static IModelBinder? GetBinder(Type modelType, BindingSource? source)
    => new TrimmingModelBinderProvider().GetBinder(new FakeContext(modelType, source));

  [Theory]
  [MemberData(nameof(TextSources))]
  public void GetBinder_StringFromTheUrlFormOrHeader_ReturnsTheTrimmingBinder(BindingSource source)
    => Assert.IsType<TrimmingModelBinder>(GetBinder(typeof(string), source));

  [Fact]
  public void GetBinder_StringWithNoDeclaredSource_ReturnsTheTrimmingBinder()
    => Assert.IsType<TrimmingModelBinder>(GetBinder(typeof(string), null));

  [Fact]
  public void GetBinder_StringFromTheBody_ReturnsNull()
    => Assert.Null(GetBinder(typeof(string), BindingSource.Body));

  [Fact]
  public void GetBinder_AnotherType_ReturnsNull()
    => Assert.Null(GetBinder(typeof(int), BindingSource.Query));
}
