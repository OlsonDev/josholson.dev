using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Yaml;

// Helpers for reading the tool's YAML files (layers and the lock) through YamlDotNet's representation model. A value is
// read as the text that was written, never through YAML's own type inference. `context` names the file in messages.
internal static class YamlNodes {
  public static YamlMappingNode LoadRoot(string context, string yaml) {
    var stream = new YamlStream();
    try {
      using StringReader reader = new(yaml);
      stream.Load(reader);
    } catch (YamlException exception) {
      throw new SeedFormatException($"{context}: {exception.Message}", exception);
    }
    return stream.Documents.Count is 1 && stream.Documents[0].RootNode is YamlMappingNode root
      ? root
      : throw new SeedFormatException($"{context}: the file must hold one YAML mapping.");
  }

  public static void CheckKeys(string context, string where, YamlMappingNode map, string[] allowed) {
    foreach (var key in map.Children.Keys) {
      var text = Key(context, key);
      if (!allowed.Contains(text, StringComparer.Ordinal)) {
        throw new SeedFormatException($"{context}: unknown key '{text}' in {where} (expected {string.Join(", ", allowed)}).");
      }
    }
  }

  public static YamlNode? Find(YamlMappingNode map, string key)
    => map.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: { } value } && string.Equals(value, key, StringComparison.Ordinal)).Value;

  public static YamlNode Required(string context, YamlMappingNode map, string key)
    => Find(map, key) ?? throw new SeedFormatException($"{context}: {key} is required.");

  public static string Key(string context, YamlNode node)
    => node is YamlScalarNode { Value: { Length: > 0 } value }
      ? value
      : throw new SeedFormatException($"{context}: a mapping key must be plain text.");

  // A single value as written. Null, `~`, and empty text are not special here: they are the text they look like.
  public static string Text(string context, string where, YamlNode node)
    => node is YamlScalarNode { Value: { } value }
      ? value
      : throw new SeedFormatException($"{context}: {where} must be a single value.");

  public static YamlSequenceNode Sequence(string context, string where, YamlNode node)
    => node as YamlSequenceNode ?? throw new SeedFormatException($"{context}: {where} must be a list.");

  public static YamlMappingNode Mapping(string context, string where, YamlNode node)
    => node as YamlMappingNode ?? throw new SeedFormatException($"{context}: {where} must be a mapping.");
}
