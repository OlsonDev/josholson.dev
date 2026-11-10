namespace JoshOlson.Database.Baking;

// Test classes that build or clone the real templates share this collection so they run one after another. Two bakes
// of the same template at once would both build under the same temporary name and drop each other's work.
internal static class TemplateCollection {
  public const string Name = "seed templates";
}
