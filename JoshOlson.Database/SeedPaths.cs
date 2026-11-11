using JoshOlson.Shared.IO;

namespace JoshOlson.Database;

// Where the seed tool's files are, spelled out once: the migrations, the lock beside them, the seed layers, and the two
// scripts that finish a template. A test or the migrator that works on a copy builds its options by hand instead.
public sealed class SeedPaths {
  private static readonly Lazy<SeedPaths> _Repository = new(() => new SeedPaths(RepositoryRoot.Find()));

  public SeedPaths(string root) {
    Root = root;
    Database = Path.Combine(root, "JoshOlson.Database");
    Migrations = Path.Combine(Database, "migrations");
    Seeds = Path.Combine(Database, "seeds");
    ProvisioningScript = Path.Combine(Database, "provisioning", "api_role.sql");
    SeedSchemaScript = Path.Combine(Database, "Baking", "seed_schema.sql");
  }

  public string Root { get; }
  public string Database { get; }
  public string Migrations { get; }
  public string Seeds { get; }
  public string ProvisioningScript { get; }
  public string SeedSchemaScript { get; }

  public string LockFile
    => Path.Combine(Migrations, "schema.lock.yaml");

  // The repository the process runs in (`RepositoryRoot`), found once: the root does not move while a process runs.
  public static SeedPaths ForRepository()
    => _Repository.Value;
}
