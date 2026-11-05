namespace JoshOlson.Database.Schema;

public static class DomainResolver {
  // Follows a type through the domains it is built on until it reaches a base type. A domain may be built on an array
  // (`CREATE DOMAIN foo AS smallint[]`) or on another domain, and a column may be an array of a domain. Returns null for
  // text that is not a supported type, a domain that is not in the map, a loop of domains, and an array of a domain that
  // is itself an array, because the seed tool does not handle more than one dimension.
  public static ResolvedType? Resolve(string type, Dictionary<string, Domain> domains) {
    if (!ColumnType.TryParse(type, out var current)) {
      return null;
    }
    var isArray = current.IsArray;
    HashSet<string> seen = new(StringComparer.Ordinal);
    while (current.Seed is null) {
      if (!seen.Add(current.Name)
          || !domains.TryGetValue(current.Name, out var domain)
          || !ColumnType.TryParse(domain.Type, out var next)
          || isArray && next.IsArray) {
        return null;
      }
      isArray |= next.IsArray;
      current = next;
    }
    return new(current.Seed.Value, isArray);
  }
}
