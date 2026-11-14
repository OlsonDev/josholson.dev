using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// A layer made from the difference between two states (`StateDiffer`), with the canonical text of its file and the two
// checks every such layer gets before anybody trusts it: the baseline with the layer applied gives the state back
// (`GivesBackTheState`), and the text reads back as the same text (`ReadsBack`). Either one failing is a bug in the
// tool, not in the data, and the caller says so in its own words: an export stops there, and `seeds:migrate` collects
// it with the other problems.
public sealed record LayerDiff(SeedLayer Layer, string Yaml, bool GivesBackTheState, bool ReadsBack) {
  // The layer that turns `baseline` into `state`: `insert`s for new rows, `update`s of only the columns that changed,
  // and `delete`s. Null when the two hold the same rows, so there is no layer to make.
  public static LayerDiff? Between(
    SeedState baseline,
    SeedState state,
    string name,
    string? extends,
    string description,
    SeedSchema schema
  ) {
    var tables = StateDiffer.Diff(baseline, state, schema);
    if (tables.Count is 0) {
      return null;
    }
    var layer = new SeedLayer {
      Name = name,
      Extends = extends,
      Description = description,
      Tables = tables,
    };
    var rebuilt = LayerMerger.Merge([StateLayer.Of(baseline, "baseline"), layer], schema);
    var yaml = LayerWriter.Write(layer, schema);
    var written = LayerWriter.Write(LayerParser.Parse(name, yaml, schema), schema);
    return new(layer, yaml, SeedValues.SameState(rebuilt, state), string.Equals(written, yaml, StringComparison.Ordinal));
  }
}
