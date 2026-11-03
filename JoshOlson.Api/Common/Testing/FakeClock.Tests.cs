using NodaTime;

namespace JoshOlson.Api.Common.Testing;

// A clock a test moves by hand, for expiration. It starts at `now` or, by default, at the current time rounded down
// to the microsecond, which is what Postgres keeps, so a time that makes the round trip compares equal. `Startup`
// registers it as the scoped `IClock`, so a service under test and the test share one.
public sealed class FakeClock : IClock {
  private Instant _now;

  public FakeClock(Instant? now = null)
    => _now = now ?? Instant.FromUnixTimeTicks(SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks() / 10 * 10);

  public Instant GetCurrentInstant()
    => _now;

  public void Advance(Duration duration)
    => _now += duration;
}
