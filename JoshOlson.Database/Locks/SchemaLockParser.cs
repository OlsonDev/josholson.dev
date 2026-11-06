using System.Globalization;
using JoshOlson.Database.Schema;
using JoshOlson.Database.Yaml;
using JoshOlson.Shared.Extensions;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Locks;

// Reads the lock back (the shape `SchemaLockWriter` documents), refusing anything the tool would not have written:
// unknown keys, versions out of order, a checksum that is not 64 lowercase hex digits, a type the seeds do not support,
// a primary key that names a missing, repeated or nullable column, a `timestamptz` column without a policy, or a foreign
// key to a table or column that is not there. A hand edit is noticed here, not later. This file reads the lock itself,
// its migrations, schemas, and domains; `SchemaLockParser.Tables.cs` reads the tables.
public static partial class SchemaLockParser {
  private const string _Context = "schema.lock.yaml";
  private static readonly string[] _LockKeys = [
    "version",
    "migrations",
    "schemas",
    "domains",
    "tables",
  ];
  private static readonly string[] _DomainKeys = [
    "type",
    "not_null",
    "default_expression",
    "comment",
    "checks",
  ];
  private static readonly string[] _CheckKeys = [
    "name",
    "expression",
  ];
  private static readonly string[] _MigrationKeys = [
    "version",
    "file",
    "checksum",
    "accepted_checksums",
  ];

  public static SchemaLock Parse(string yaml) {
    var root = YamlNodes.LoadRoot(_Context, yaml);
    YamlNodes.CheckKeys(_Context, "the lock", root, _LockKeys);
    var version = WholeNumber("version", YamlNodes.RequiredText(_Context, root, "version"), 0);
    var migrations = ParseMigrations(version, YamlNodes.Required(_Context, root, "migrations"));
    var schemas = YamlNodes.Find(root, "schemas") is { } schemasNode ? ParseSchemas(schemasNode) : [];
    var domains = YamlNodes.Find(root, "domains") is { } domainsNode ? ParseDomains(domainsNode) : [];
    var tables = ParseTables(YamlNodes.Required(_Context, root, "tables"));
    CheckTypes(domains, tables);
    CheckReferences(tables);
    return new() {
      Version = version,
      Migrations = migrations,
      Schemas = schemas,
      Domains = domains,
      Tables = tables,
    };
  }

  private static List<LockedMigration> ParseMigrations(int lockVersion, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, "migrations", node);
    var migrations = new List<LockedMigration>(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, "a migration", item);
      YamlNodes.CheckKeys(_Context, "a migration", map, _MigrationKeys);
      var version = WholeNumber("a migration's version", YamlNodes.RequiredText(_Context, map, "version"), 1);
      var file = YamlNodes.RequiredText(_Context, map, "file");
      if (!MigrationFiles.TryParseVersion(file, out var fileVersion) || fileVersion != version) {
        throw new SeedFormatException($"{_Context}: '{file}' is not the file of migration version {version.ToInvariantString()}.");
      }
      if (version > lockVersion) {
        throw new SeedFormatException($"{_Context}: migration {version.ToInvariantString()} is newer than the locked version {lockVersion.ToInvariantString()}.");
      }
      if (migrations.Count > 0 && migrations[^1].Version >= version) {
        throw new SeedFormatException($"{_Context}: migration {version.ToInvariantString()} is out of order or repeated.");
      }

      var acceptedChecksums = new List<string>();
      if (YamlNodes.Find(map, "accepted_checksums") is { } acceptedNode) {
        foreach (var entry in YamlNodes.Sequence(_Context, $"{file} accepted_checksums", acceptedNode).Children) {
          var checksum = Checksum(file, YamlNodes.Text(_Context, $"{file} accepted_checksums", entry));
          if (acceptedChecksums.Contains(checksum, StringComparer.Ordinal)) {
            throw new SeedFormatException($"{_Context}: {file} accepts the same checksum twice.");
          }
          acceptedChecksums.Add(checksum);
        }
      }
      migrations.Add(new() {
        Version = version,
        File = file,
        Checksum = Checksum(file, YamlNodes.RequiredText(_Context, map, "checksum")),
        AcceptedChecksums = acceptedChecksums,
      });
    }
    return migrations;
  }

  private static List<SchemaComment> ParseSchemas(YamlNode node) {
    var map = YamlNodes.Mapping(_Context, "schemas", node);
    var schemas = new List<SchemaComment>(map.Children.Count);
    foreach (var (key, value) in map.Children) {
      var name = YamlNodes.Key(_Context, key);
      if (name.Contains('.')) {
        throw new SeedFormatException($"{_Context}: '{name}' is not a schema name.");
      }
      var comment = YamlNodes.Text(_Context, $"the comment of schema {name}", value);
      if (comment.Length is 0) {
        throw new SeedFormatException($"{_Context}: schema {name} has an empty comment, which the catalog would not keep.");
      }
      schemas.Add(new() {
        Name = name,
        Comment = comment,
      });
    }
    return schemas;
  }

  private static List<Domain> ParseDomains(YamlNode node) {
    var map = YamlNodes.Mapping(_Context, "domains", node);
    var domains = new List<Domain>(map.Children.Count);
    foreach (var (key, value) in map.Children) {
      var name = YamlNodes.Key(_Context, key);
      if (!ColumnType.IsDomainName(name)) {
        throw new SeedFormatException($"{_Context}: '{name}' is not a schema.name domain name.");
      }
      if (domains.Any(domain => string.Equals(domain.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: domain {name} is listed twice.");
      }
      domains.Add(ParseDomain(name, value));
    }

    var byName = domains.ToDictionary(domain => domain.Name, StringComparer.Ordinal);
    foreach (var domain in domains.Where(domain => DomainResolver.Resolve(domain.Type, byName) is null)) {
      throw new SeedFormatException(
        $"{_Context}: domain {domain.Name} is built on {domain.Type}, which is not a supported type, a domain in the lock, an array of a domain that is itself an array, or part of a loop of domains.");
    }
    return domains;
  }

  private static Domain ParseDomain(string name, YamlNode node) {
    var map = YamlNodes.Mapping(_Context, $"domain {name}", node);
    YamlNodes.CheckKeys(_Context, $"domain {name}", map, _DomainKeys);
    var type = YamlNodes.RequiredText(_Context, map, "type");
    if (!ColumnType.TryParse(type, out _)) {
      throw new SeedFormatException($"{_Context}: domain {name} has type '{type}', which the seed tool does not support.");
    }
    return new() {
      Name = name,
      Type = type,
      NotNull = OptionalFlag($"domain {name}", map, "not_null"),
      DefaultExpression = OptionalNonEmptyText($"domain {name}", map, "default_expression"),
      Comment = OptionalNonEmptyText($"domain {name}", map, "comment"),
      Checks = YamlNodes.Find(map, "checks") is { } checksNode ? ParseChecks($"domain {name}", checksNode) : [],
    };
  }

  // The checks of a domain or a table; `owner` says which ("domain foo.bar").
  private static List<CheckConstraint> ParseChecks(string owner, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{owner} checks", node);
    var checks = new List<CheckConstraint>(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var map = YamlNodes.Mapping(_Context, $"a check of {owner}", item);
      YamlNodes.CheckKeys(_Context, $"a check of {owner}", map, _CheckKeys);
      var name = YamlNodes.RequiredText(_Context, map, "name");
      if (checks.Any(check => string.Equals(check.Name, name, StringComparison.Ordinal))) {
        throw new SeedFormatException($"{_Context}: {owner} has two checks named {name}.");
      }
      var expression = YamlNodes.RequiredText(_Context, map, "expression");
      if (expression.Length is 0) {
        throw new SeedFormatException($"{_Context}: check {name} of {owner} has no expression.");
      }
      checks.Add(new() {
        Name = name,
        Expression = expression,
      });
    }
    return checks;
  }

  private static List<string> Names(string where, string key, YamlNode node) {
    var sequence = YamlNodes.Sequence(_Context, $"{where} {key}", node);
    if (sequence.Children.Count is 0) {
      throw new SeedFormatException($"{_Context}: {where} {key} is empty.");
    }
    return [.. sequence.Children.Select(child => YamlNodes.Text(_Context, $"{where} {key}", child))];
  }

  private static bool IsQualifiedName(string name)
    => name.Split('.') is [{ Length: > 0 }, { Length: > 0 }];

  private static int WholeNumber(string what, string text, int minimum)
    => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum
      ? number
      : throw new SeedFormatException($"{_Context}: {what} must be a whole number of at least {minimum.ToInvariantString()}, not '{text}'.");

  // An optional key holding `true` or `false`; absent means false.
  private static bool OptionalFlag(string where, YamlMappingNode map, string key)
    => YamlNodes.OptionalText(_Context, map, key) switch {
      null or "false" => false,
      "true" => true,
      var text => throw new SeedFormatException($"{_Context}: {where} {key} must be true or false, not '{text}'."),
    };

  // An optional key holding one of an enum's kebab-case words; absent is null.
  private static T? OptionalWord<T>(string where, YamlMappingNode map, string key) where T : struct, Enum {
    if (YamlNodes.OptionalText(_Context, map, key) is not { } text) {
      return null;
    }
    return Enum.TryParseKebabCase<T>(text, out var value)
      ? value
      : throw new SeedFormatException($"{_Context}: {where} {key} has '{text}', which is not one of the words the lock uses.");
  }

  // An optional key holding text; absent is null, and empty is an error because the catalog never holds an empty comment,
  // default, or expression.
  private static string? OptionalNonEmptyText(string where, YamlMappingNode map, string key) {
    var text = YamlNodes.OptionalText(_Context, map, key);
    return text is { Length: 0 }
      ? throw new SeedFormatException($"{_Context}: {where} {key} is empty; leave the key out instead.")
      : text;
  }

  private static string Checksum(string file, string text)
    => text.Length is 64 && text.All(char.IsAsciiHexDigitLower)
      ? text
      : throw new SeedFormatException($"{_Context}: a checksum of {file} is not 64 lowercase hex digits.");
}
