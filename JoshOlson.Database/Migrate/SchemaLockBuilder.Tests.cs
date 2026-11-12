using JoshOlson.Database.Catalog;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Xunit;

namespace JoshOlson.Database.Migrate;

public class SchemaLockBuilderTests {
  private static readonly SchemaLock _Sample = SchemaLockSample.Create();

  private static readonly CatalogSchema _Catalog = new() {
    Schemas = _Sample.Schemas,
    Domains = _Sample.Domains,
    Tables = _Sample.Tables,
  };

  private static MigrationFile Disk(int version, string name, string checksum)
    => new() {
      Version = version,
      File = $"V{version.ToInvariantString().PadLeft(3, '0')}__{name}.sql",
      Checksum = checksum,
    };

  [Fact]
  public void Build_NoExistingLock_LocksEveryMigrationAtTheNewestVersion() {
    var built = SchemaLockBuilder.Build(existing: null, [Disk(3, "baz", SchemaLockSample.Bbb), Disk(1, "foo", SchemaLockSample.Aaa)], _Catalog);

    Assert.Equal(3, built.Version);
    Assert.Equal([1, 3], built.Migrations.Select(migration => migration.Version));
    Assert.Equal([SchemaLockSample.Aaa, SchemaLockSample.Bbb], built.Migrations.Select(migration => migration.Checksum));
  }

  [Fact]
  public void Build_CopiesTheCatalog() {
    var built = SchemaLockBuilder.Build(existing: null, [Disk(1, "foo", SchemaLockSample.Aaa)], _Catalog);

    Assert.Equal(_Catalog.Schemas, built.Schemas);
    Assert.Equal(_Catalog.Domains, built.Domains);
    Assert.Equal(_Catalog.Tables, built.Tables);
  }

  [Fact]
  public void Build_ExistingEntryThatStillMatches_KeepsIt() {
    List<MigrationFile> onDisk = [.. _Sample.Migrations.Select(migration => Disk(migration.Version, migration.Version is 1 ? "foo" : "bar", migration.Checksum))];

    var built = SchemaLockBuilder.Build(_Sample, onDisk, _Catalog);

    Assert.Equal(_Sample.Migrations, built.Migrations);
  }

  [Fact]
  public void Build_NewerMigration_IsAddedAndMovesTheVersion() {
    List<MigrationFile> onDisk = [.. _Sample.Migrations.Select(migration => Disk(migration.Version, migration.Version is 1 ? "foo" : "bar", migration.Checksum))];
    onDisk.Add(Disk(4, "qux", SchemaLockSample.Ccc));

    var built = SchemaLockBuilder.Build(_Sample, onDisk, _Catalog);

    Assert.Equal(4, built.Version);
    Assert.Equal(3, built.Migrations.Count);
    Assert.Equal(SchemaLockSample.Ccc, built.Migrations[^1].Checksum);
  }

  [Fact]
  public void Build_NoMigrations_Throws()
    => Assert.Throws<SeedFormatException>(() => SchemaLockBuilder.Build(existing: null, [], _Catalog));
}
