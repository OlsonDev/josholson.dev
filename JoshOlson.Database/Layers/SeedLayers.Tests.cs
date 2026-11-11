using Xunit;

namespace JoshOlson.Database.Layers;

public sealed class SeedLayersTests {
  [Fact]
  public void Constants_AreExactlyTheLayerFilesInTheSeedsDirectory() {
    var constants = typeof(SeedLayers)
      .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
      .Where(field => field.IsLiteral)
      .Select(field => (string)field.GetRawConstantValue()!)
      .Order(StringComparer.Ordinal);
    var files = LayerLoader.Files(SeedPaths.ForRepository().Seeds).Select(Path.GetFileNameWithoutExtension);

    Assert.Equal(files, constants);
  }
}
