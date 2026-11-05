namespace JoshOlson.Database.Schema;

public static class DomainResolver {
  // Follows a type through the domains it is built on until it reaches a base type. A domain may be built on an array
  // (`CREATE DOMAIN foo AS smallint[]`) or on another domain, and a column may be an array of a domain. Returns null for
  // a domain that is not in the map, a loop of domains, and an array of a domain that is itself an array, because the
  // seed tool does not handle more than one dimension.
  public static ResolvedType? Resolve(ColumnType type, Dictionary<string, Domain> domains) {
    var isArray = type.IsArray;
    var current = type;
    HashSet<string> seen = new(StringComparer.Ordinal);
    while (current.Seed is null) {
      if (!seen.Add(current.Name) || !domains.TryGetValue(current.Name, out var domain)) {
        return null;
      }
      if (isArray && domain.Type.IsArray) {
        return null;
      }
      isArray |= domain.Type.IsArray;
      current = domain.Type;
    }
    return new(current.Seed.Value, isArray);
  }
}
