using System.Data;
using System.Net;
using System.Runtime.CompilerServices;
using Dapper;
using NodaTime;

namespace JoshOlson.Shared.Data;

// Dapper settings every query needs. A module initializer runs once when the assembly loads, so every project that goes
// through `NpgsqlConnectionExtensions` (the API, the seed tool, their tests, which never run `Program.cs`) gets the same
// settings in any test order.
public static class DapperConfiguration {
  [ModuleInitializer]
  public static void Configure() {
    // `bar_id` fills `BarId`.
    DefaultTypeMap.MatchNamesWithUnderscores = true;
    // Npgsql.NodaTime reads and writes Instant, but Dapper rejects a type it does not know as a parameter.
    SqlMapper.AddTypeHandler(new InstantHandler());
    // Likewise for the `inet` column that holds a session's IP address.
    SqlMapper.AddTypeHandler(new IpAddressHandler());
    // A `text[]` column fills a `List<string>`. Dapper would otherwise expand a list parameter into one parameter per
    // item, for an `IN` list, which is not what an array parameter wants.
    SqlMapper.AddTypeHandler(new StringListHandler());
  }

  private sealed class IpAddressHandler : SqlMapper.TypeHandler<IPAddress?> {
    public override IPAddress? Parse(object value)
      => value as IPAddress;

    public override void SetValue(IDbDataParameter parameter, IPAddress? value)
      => parameter.Value = value;
  }

  private sealed class InstantHandler : SqlMapper.TypeHandler<Instant> {
    public override Instant Parse(object value)
      => (Instant)value;

    public override void SetValue(IDbDataParameter parameter, Instant value)
      => parameter.Value = value;
  }

  private sealed class StringListHandler : SqlMapper.TypeHandler<List<string>> {
    public override List<string> Parse(object value)
      => [.. (string[])value];

    public override void SetValue(IDbDataParameter parameter, List<string>? value)
      => parameter.Value = value is null ? DBNull.Value : value.ToArray();
  }
}
