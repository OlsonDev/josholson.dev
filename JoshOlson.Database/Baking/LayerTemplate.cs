using System.Security.Cryptography;
using System.Text;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A layer's template: a clone of the schema template with the layer's chain applied (`SeedHydrator`), which a database
// for a test or for dev is cloned from in turn (ADR 0033, a scoped database per test). Like the schema template it is
// named for a hash of everything that shapes it, here the schema template's name and the source of every layer in the
// chain, so a template for the current files is reused and one for older files is left for `seed:clean`. It is built
// under a temporary name and renamed when finished, and refuses connections.
//
// Each template is built from the schema template and the whole chain, not from its parent's template plus its own
// delta: the result is the same rows, and applying a delta would need an `UPDATE` and `DELETE` writer beside the
// `INSERT` one.
public sealed class LayerTemplate {
  private readonly SchemaTemplate _schemaTemplate;
  private readonly DatabaseCloner _cloner;
  private readonly LayerTree _tree;
  private readonly SeedSchema _schema;
  private readonly string _seedsDirectory;
  private readonly IClock _clock;

  public LayerTemplate(
    SchemaTemplate schemaTemplate,
    DatabaseCloner cloner,
    LayerTree tree,
    SeedSchema schema,
    string seedsDirectory,
    IClock clock
  ) {
    _schemaTemplate = schemaTemplate;
    _cloner = cloner;
    _tree = tree;
    _schema = schema;
    _seedsDirectory = seedsDirectory;
    _clock = clock;
  }

  public async Task<string> NameAsync(string layer, CancellationToken cancellationToken)
    => await NameAsync(await _schemaTemplate.NameAsync(cancellationToken), layer, cancellationToken);

  // Makes sure the template for the layer exists and returns its name. `Built` says whether this call did the work.
  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, string layer, CancellationToken cancellationToken) {
    var (schemaName, _) = await _schemaTemplate.EnsureAsync(owner, cancellationToken);
    var name = await NameAsync(schemaName, layer, cancellationToken);
    if (await SchemaTemplate.ExistsAsync(owner, name, cancellationToken)) {
      return (name, false);
    }
    var building = SeedDatabaseNames.Building(name);
    await SchemaTemplate.DropAsync(owner, building, cancellationToken);
    await _cloner.CloneAsync(owner, schemaName, building, cancellationToken);
    try {
      _ = await SeedHydrator.HydrateAsync(building, _tree, _schema, layer, SeedHydrator.Anchor(_clock), cancellationToken);
      await owner.ExecuteAsync(
        $"ALTER DATABASE {SeedDatabaseNames.Quote(building)} WITH ALLOW_CONNECTIONS false;",
        null,
        cancellationToken);
      await owner.ExecuteAsync(
        $"ALTER DATABASE {SeedDatabaseNames.Quote(building)} RENAME TO {SeedDatabaseNames.Quote(name)};",
        null,
        cancellationToken);
    } catch {
      await SchemaTemplate.DropAsync(owner, building, CancellationToken.None);
      throw;
    }
    return (name, true);
  }

  private async Task<string> NameAsync(string schemaName, string layer, CancellationToken cancellationToken) {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(Encoding.UTF8.GetBytes(schemaName + "\n"));
    foreach (var link in _tree.ChainTo(layer)) {
      hash.AppendData(Encoding.UTF8.GetBytes(link.Name + "\n"));
      hash.AppendData(await File.ReadAllBytesAsync(Path.Combine(_seedsDirectory, link.Name + ".yaml"), cancellationToken));
    }
    return SeedDatabaseNames.Layer(Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }
}
