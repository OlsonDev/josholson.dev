using System.Collections.Concurrent;

namespace JoshOlson.Api.Common.Db;

// Builds SQL that varies with a few flags (which optional parts to include) once per combination of values, then
// hands back the same string every time, so repeated calls neither rebuild it nor defeat prepared-statement reuse.
// The arguments are passed through to the builder in order. They must be value types (bools, enums), so the set of
// combinations stays small: every distinct combination is cached for the life of the process, so never key it on
// request data.
public sealed class SqlMemoizer<T1> where T1 : struct {
  private readonly Func<T1, string> _build;
  private readonly ConcurrentDictionary<T1, string> _sql = [];

  public SqlMemoizer(Func<T1, string> build)
    => _build = build;

  public string ForValues(T1 value1)
    => _sql.GetOrAdd(value1, _build);
}

public sealed class SqlMemoizer<T1, T2> where T1 : struct where T2 : struct {
  private readonly Func<(T1, T2), string> _build;
  private readonly ConcurrentDictionary<(T1, T2), string> _sql = [];

  public SqlMemoizer(Func<T1, T2, string> build)
    => _build = key => build(key.Item1, key.Item2);

  public string ForValues(T1 value1, T2 value2)
    => _sql.GetOrAdd((value1, value2), _build);
}

public sealed class SqlMemoizer<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct {
  private readonly Func<(T1, T2, T3), string> _build;
  private readonly ConcurrentDictionary<(T1, T2, T3), string> _sql = [];

  public SqlMemoizer(Func<T1, T2, T3, string> build)
    => _build = key => build(key.Item1, key.Item2, key.Item3);

  public string ForValues(T1 value1, T2 value2, T3 value3)
    => _sql.GetOrAdd((value1, value2, value3), _build);
}

public sealed class SqlMemoizer<T1, T2, T3, T4> where T1 : struct where T2 : struct where T3 : struct where T4 : struct {
  private readonly Func<(T1, T2, T3, T4), string> _build;
  private readonly ConcurrentDictionary<(T1, T2, T3, T4), string> _sql = [];

  public SqlMemoizer(Func<T1, T2, T3, T4, string> build)
    => _build = key => build(key.Item1, key.Item2, key.Item3, key.Item4);

  public string ForValues(T1 value1, T2 value2, T3 value3, T4 value4)
    => _sql.GetOrAdd((value1, value2, value3, value4), _build);
}
