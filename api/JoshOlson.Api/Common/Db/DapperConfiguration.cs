using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using NodaTime;

namespace JoshOlson.Api.Common.Db;

// Dapper settings every query needs. A module initializer runs once when the assembly loads, so the API and the
// repository tests (which never run Program.cs) get the same settings in any test order.
public static class DapperConfiguration {
  [ModuleInitializer]
  public static void Configure() {
    // `bar_id` fills `BarId`.
    DefaultTypeMap.MatchNamesWithUnderscores = true;
    // Npgsql.NodaTime reads and writes Instant, but Dapper rejects a type it does not know as a parameter.
    SqlMapper.AddTypeHandler(new InstantHandler());
  }

  private sealed class InstantHandler : SqlMapper.TypeHandler<Instant> {
    public override Instant Parse(object value)
      => (Instant)value;

    public override void SetValue(IDbDataParameter parameter, Instant value)
      => parameter.Value = value;
  }
}
