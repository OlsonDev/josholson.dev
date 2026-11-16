using System.Security.Cryptography;
using System.Text;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Results;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A layer's template: a clone of the schema template with the layer's chain applied (`SeedHydrator`), which a database
// for a test or for dev is cloned from in turn (ADR 0033, a scoped database per test). Like the schema template it is
// named for a hash of everything that shapes it, here the schema template's name and the source of every layer in the
// chain, so a template for the current files is reused and one for older files is left for `seed:clean`. Like the schema
// template it is built and closed by `ServerDatabases.EnsureTemplateAsync`.
//
// Each template is built from the schema template and the whole chain, not from its parent's template plus its own
// delta: the result is the same rows, and applying a delta would need an `UPDATE` and `DELETE` writer beside the
// `INSERT` one.
public sealed class LayerTemplate {
  private readonly SchemaTemplate _schemaTemplate;
  private readonly ServerDatabases _databases;
  private readonly LayerTree _tree;
  private readonly string _seedsDirectory;
  private readonly IClock _clock;

  public LayerTemplate(
    SchemaTemplate schemaTemplate,
    ServerDatabases databases,
    LayerTree tree,
    string seedsDirectory,
    IClock clock
  ) {
    _schemaTemplate = schemaTemplate;
    _databases = databases;
    _tree = tree;
    _seedsDirectory = seedsDirectory;
    _clock = clock;
  }

  public async Task<string> NameAsync(string layer, CancellationToken cancellationToken)
    => await NameAsync(await _schemaTemplate.NameAsync(cancellationToken), layer, cancellationToken);

  // Makes sure the template for the layer exists and returns its name. `Built` says whether this call did the work.
  // A layer whose chain does not go into a database comes back as its problems, and nothing is left behind.
  public async Task<Result<BuiltTemplate, SeedProblems>> EnsureAsync(
    NpgsqlConnection owner,
    string layer,
    CancellationToken cancellationToken
  ) {
    var (schemaName, _) = await _schemaTemplate.EnsureAsync(owner, cancellationToken);
    var name = await NameAsync(schemaName, layer, cancellationToken);
    // The build is a callback, and the only way out of it that drops the half-built database is to throw. The problems
    // are kept as they are and handed back once it has.
    SeedProblems? refused = null;
    try {
      var built = await _databases.EnsureTemplateAsync(
        owner,
        name,
        async building => {
          await _databases.CloneAsync(owner, schemaName, building, cancellationToken);
          var hydrated = await SeedHydrator.HydrateAsync(building, _tree, layer, editing: false, SeedHydrator.Anchor(_clock), cancellationToken);
          refused = hydrated.Value as SeedProblems;
          _ = hydrated.OrThrow();
        },
        cancellationToken);
      return new BuiltTemplate(name, built);
    } catch (SeedFormatException) when (refused is not null) {
      return refused;
    }
  }

  private async Task<string> NameAsync(string schemaName, string layer, CancellationToken cancellationToken) {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(Encoding.UTF8.GetBytes(schemaName + "\n"));
    foreach (var link in _tree.ChainTo(layer)) {
      hash.AppendData(Encoding.UTF8.GetBytes(link.Name + "\n"));
      hash.AppendData(await File.ReadAllBytesAsync(LayerLoader.PathOf(_seedsDirectory, link.Name), cancellationToken));
    }
    return SeedDatabaseNames.Layer(Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }
}
