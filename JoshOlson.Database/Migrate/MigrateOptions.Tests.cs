using JoshOlson.Database.Baking;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Migrate;

public sealed class MigrateOptionsTests {
  [Fact]
  public void For_Paths_FillsTheRequiredOnesAndLeavesTheRestAtTheirDefaults() {
    var paths = new SeedPaths("/foo");
    var databases = new ServerDatabases();
    var template = new SchemaTemplate(SchemaTemplateOptions.ForRepository(paths), new(paths.Root), databases);

    var options = MigrateOptions.For(paths, template, databases, SystemClock.Instance);

    Assert.Equal(paths.Migrations, options.MigrationsDirectory);
    Assert.Equal(paths.Seeds, options.SeedsDirectory);
    Assert.Same(template, options.Template);
    Assert.Same(databases, options.Databases);
    Assert.Same(SystemClock.Instance, options.Clock);
    Assert.False(options.Relock);
    Assert.True(options.Fetch);
    Assert.Null(options.Snapshots);
    Assert.Null(options.Base);
    Assert.Null(options.LayerCheck);
  }
}
