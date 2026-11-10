namespace JoshOlson.Database.Baking;

public sealed record SchemaTemplateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string ProvisioningScript { get; init; }
}
