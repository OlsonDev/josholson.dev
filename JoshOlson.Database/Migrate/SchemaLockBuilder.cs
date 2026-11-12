using JoshOlson.Database.Catalog;
using JoshOlson.Database.Locks;

namespace JoshOlson.Database.Migrate;

public static class SchemaLockBuilder {
  // The lock for a database built from exactly `onDisk`: the version is the newest migration, each migration is
  // checksummed as it is on disk, and the schema is the catalog's. A migration the existing lock already has, under the
  // same file name and checksum, keeps its entry. `layers` is what the lock says about the layer files (`SchemaLock.Layers`).
  public static SchemaLock Build(SchemaLock? existing, List<MigrationFile> onDisk, CatalogSchema catalog, List<LockedLayer>? layers = null) {
    if (onDisk.Count is 0) {
      throw new SeedFormatException("There are no versioned migrations to lock.");
    }
    var migrations = new List<LockedMigration>(onDisk.Count);
    foreach (var migration in onDisk.OrderBy(migration => migration.Version)) {
      var locked = existing?.Migrations.Find(known => known.Version == migration.Version
        && string.Equals(known.File, migration.File, StringComparison.Ordinal)
        && known.Matches(migration.Checksum));
      migrations.Add(locked ?? new() {
        Version = migration.Version,
        File = migration.File,
        Checksum = migration.Checksum,
      });
    }
    return new() {
      Migrations = migrations,
      Layers = layers,
      Schemas = catalog.Schemas,
      Domains = catalog.Domains,
      Tables = catalog.Tables,
    };
  }
}
