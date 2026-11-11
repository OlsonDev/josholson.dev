namespace JoshOlson.Database.Layers;

// The names of the layers in `seeds/`, so code and tests name one by a constant an IDE can rename. A layer's name is its
// file name, so renaming one means renaming the file and the `extends` line of every layer below it; `SeedLayersTests`
// fails until the constants and the files agree.
public static class SeedLayers {
  public const string Minimal = "minimal";
  public const string MoreTags = "more-tags";
}
