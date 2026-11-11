namespace JoshOlson.Database.Baking;

public sealed record SchemaTemplateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string ProvisioningScript { get; init; }

  // Creates the `seed` schema (the hydration anchor). It runs after the provisioning script, so the API role is never
  // granted anything in it.
  public required string SeedSchemaScript { get; init; }

  public static SchemaTemplateOptions ForRepository(SeedPaths paths)
    => new() {
      MigrationsDirectory = paths.Migrations,
      ProvisioningScript = paths.ProvisioningScript,
      SeedSchemaScript = paths.SeedSchemaScript,
    };
}
