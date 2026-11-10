using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.IO;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A database of a test's own: a clone of the real schema template (built the first time, then reused by every test that
// asks), dropped when the test is done. Tests that put rows into one can commit, and leave nothing behind in the dev
// database.
internal sealed class ScratchDatabase : IAsyncDisposable {
  private ScratchDatabase(string name)
    => Name = name;

  public string Name { get; }

  public static async Task<ScratchDatabase> CreateAsync(CancellationToken cancellationToken) {
    var root = RepositoryRoot.Find();
    var template = new SchemaTemplate(SchemaTemplateOptions.ForRepository(root), new(root));
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await owner.OpenAsync(cancellationToken);
    var (templateName, _) = await template.EnsureAsync(owner, cancellationToken);
    var name = $"seed_scratch_{Guid.NewGuid():N}";
    await new DatabaseCloner().CloneAsync(owner, templateName, name, cancellationToken);
    return new(name);
  }

  // The repository's lock as the layer parser reads it.
  public static async Task<SeedSchema> LoadSchemaAsync(CancellationToken cancellationToken) {
    var lockPath = Path.Combine(RepositoryRoot.Find(), "JoshOlson.Database", "migrations", SeedMigrator.LockFileName);
    return new(SchemaLockParser.Parse(await File.ReadAllTextAsync(lockPath, cancellationToken)));
  }

  public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) {
    var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(Name));
    await connection.OpenAsync(cancellationToken);
    return connection;
  }

  public async ValueTask DisposeAsync() {
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await owner.OpenAsync(CancellationToken.None);
    await SchemaTemplate.DropAsync(owner, Name, CancellationToken.None);
  }
}
