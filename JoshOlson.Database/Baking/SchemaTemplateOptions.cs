namespace JoshOlson.Database.Baking;

public sealed record SchemaTemplateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string ProvisioningScript { get; init; }

  // Creates the `seed` schema (the hydration anchor). It runs after the provisioning script, so the API role is never
  // granted anything in it.
  public required string SeedSchemaScript { get; init; }

  // The repository's own files, under its root.
  public static SchemaTemplateOptions ForRepository(string root) {
    var database = Path.Combine(root, "JoshOlson.Database");
    return new() {
      MigrationsDirectory = Path.Combine(database, "migrations"),
      ProvisioningScript = Path.Combine(database, "provisioning", "api_role.sql"),
      SeedSchemaScript = Path.Combine(database, "Baking", "seed_schema.sql"),
    };
  }
}
