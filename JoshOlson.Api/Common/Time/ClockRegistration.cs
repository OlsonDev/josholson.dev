using NodaTime;

namespace JoshOlson.Api.Common.Time;

public static class ClockRegistration {
  extension(IServiceCollection services) {
    // The one clock, so a test can replace it (ADR 0029, NodaTime for time).
    public IServiceCollection AddClock()
      => services.AddSingleton<IClock>(SystemClock.Instance);
  }
}
