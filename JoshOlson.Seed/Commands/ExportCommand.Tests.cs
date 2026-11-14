using JoshOlson.Database;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Exports a database built from the fixture root into a workspace of the test's own, so the real `seeds/` is never
// written to. The command writes a layer file and records the export in the database, so the test walks one database
// through the states an export can find it in. The round trips are `ExportCommandRoundTripTests`, so the two classes
// run side by side.
public sealed class ExportCommandTests : IAsyncLifetime {
  private static readonly string[] _FooBar = [
    "--name",
    "foo-bar",
    "--description",
    "Foo bar",
  ];

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ExportCommandHarness _harness = new();

  public async ValueTask InitializeAsync()
    => await _harness.InitializeAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _harness.DisposeAsync();

  // The states, in order:
  //   1. as built from the root: nothing to export
  //   2. a tag added and one renamed: a new layer, which needs a name
  //   3. exported as `foo-bar`: the database records it, so it is now the layer being edited
  //   4. another tag added: an export with no name replaces `foo-bar` and keeps its description
  //   5. a second database with a tag of its own: `foo-bar` is taken, unless it may be overwritten
  [Fact]
  public async Task Execute_ADatabaseBuiltFromALayerAsItChanges_WritesTheChildReplacesItAndRefusesItsNameToAnother() {
    var database = await _harness.DatabaseFromAsync(FixtureSeeds.Root);
    var (unchangedCode, unchanged) = await _harness.ExportAsync(database, _FooBar);
    var unchangedWroteAFile = File.Exists(_harness.Workspace.Layer("foo-bar"));

    var rename = "UPDATE tag.tag SET name = 'Databases' WHERE tag_id = 1;";
    await _harness.RunSqlAsync(database, $"{FixtureSeeds.InsertTag("Zed")} {rename}");
    var (unnamedCode, unnamed) = await _harness.ExportAsync(database, "--description", "Foo bar");
    var (namedCode, named) = await _harness.ExportAsync(database, _FooBar);
    var written = await _harness.ReadLayerAsync("foo-bar");

    await _harness.RunSqlAsync(database, FixtureSeeds.InsertTag("Quux"));
    var (againCode, again) = await _harness.ExportAsync(database);
    var replaced = await _harness.ReadLayerAsync("foo-bar");

    var second = await _harness.DatabaseFromAsync(FixtureSeeds.Root);
    await _harness.RunSqlAsync(second, FixtureSeeds.InsertTag("Plugh"));
    var (refusedCode, refused) = await _harness.ExportAsync(second, _FooBar);
    var (overwrittenCode, overwritten) = await _harness.ExportAsync(second, [.. _FooBar, "--overwrite"]);
    var overwrote = await _harness.ReadLayerAsync("foo-bar");

    Assert.Equal(1, unchangedCode);
    Assert.Contains("There is nothing to export", unchanged, StringComparison.Ordinal);
    Assert.False(unchangedWroteAFile);

    Assert.Equal(1, unnamedCode);
    Assert.Contains("Give the new layer a --name", unnamed, StringComparison.Ordinal);

    Assert.True(namedCode is 0, named);
    Assert.Contains("tag.tag", named, StringComparison.Ordinal);
    Assert.Contains("Wrote", named, StringComparison.Ordinal);
    Assert.Equal(
      $$"""
      description: Foo bar
      extends: {{FixtureSeeds.Root}}
      tables:
        tag.tag:
          insert:
            - {{FixtureSeeds.TagRow(1001, "Zed")}}
          update:
            - { tag_id: 1, name: Databases }

      """,
      written);

    Assert.True(againCode is 0, again);
    Assert.Contains("Replaced", again, StringComparison.Ordinal);
    Assert.StartsWith($"description: Foo bar\nextends: {FixtureSeeds.Root}\n", replaced, StringComparison.Ordinal);
    Assert.Contains("zed", replaced, StringComparison.Ordinal);
    Assert.Contains("quux", replaced, StringComparison.Ordinal);

    Assert.Equal(1, refusedCode);
    Assert.Contains("already a layer named 'foo-bar'", refused, StringComparison.Ordinal);
    Assert.True(overwrittenCode is 0, overwritten);
    Assert.Contains("plugh", overwrote, StringComparison.Ordinal);
  }
}
