using JoshOlson.Database.Locks;
using JoshOlson.Database.Schema;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

// Seed data for the sample lock (`SchemaLockSample`): its schema, a row of each of its tables as the YAML flow mapping
// an insert takes, and layers parsed and merged against it. A row builder has a default for every column a test does
// not care about, so a change to the sample schema is a change here and not in every test.
internal static class SampleLayers {
  public static readonly SeedSchema Schema = new(SchemaLockSample.Create());

  // The sample schema and `foo.kind`, the table with a column of every seed type.
  public static readonly SeedSchema SchemaWithKind = new(SchemaLockSample.WithKind());

  // `foo.kind` alone, for a database that has none of the sample's other tables.
  public static readonly SeedSchema SchemaOfKind = new(SchemaLockSample.OnlyKind());

  // The sample schema with more tables after its own.
  public static SeedSchema SchemaWith(params Table[] tables)
    => new(SchemaLockSample.With(tables));

  // A `foo.bar` insert, which names every column that is not generated from an expression. The slug is `s{id}` unless
  // one is given.
  public static string Bar(
    int id,
    string name,
    string? slug = null,
    string note = "~",
    string created = "0s",
    string lat = "1.5") {
    var barId = id.ToInvariantString();
    return $"{{ bar_id: {barId}, slug: {slug ?? $"s{barId}"}, name: {name}, note: {note}, "
      + $"created_utc: {created}, published_utc: ~, lat: {lat} }}";
  }

  // A `foo.link` insert.
  public static string Link(int barId, int tagId, string flags = "~", string tags = "~")
    => $"{{ bar_id: {barId.ToInvariantString()}, tag_id: {tagId.ToInvariantString()}, flags: {flags}, tags: {tags} }}";

  // A `foo.tag` insert.
  public static string Tag(int id, string price = "0")
    => $"{{ tag_id: {id.ToInvariantString()}, price: {price} }}";

  // A layer from the text that goes under `tables:`, so each table's name is indented two spaces. Its description is
  // its name.
  public static SeedLayer Layer(string name, string? extends, string tables) {
    var parent = extends is null ? "" : $"extends: {extends}\n";
    return LayerParser.Parse(name, $"description: {name}\n{parent}tables:\n{tables}", Schema);
  }

  public static SeedState Merge(params SeedLayer[] chain)
    => LayerMerger.Merge([.. chain], Schema);
}
