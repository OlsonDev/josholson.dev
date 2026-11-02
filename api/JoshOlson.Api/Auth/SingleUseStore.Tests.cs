using JoshOlson.Api.Common.Testing;
using Microsoft.Extensions.Options;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SingleUseStoreTests {
  private static readonly Instant _Start = Instant.FromUtc(2026, 10, 31, 12, 0);

  private static (SingleUseStore<string> Store, FakeClock Clock) Create() {
    var clock = new FakeClock(_Start);
    return (new(clock, Options.Create(new AuthOptions())), clock);
  }

  [Fact]
  public void Take_ReturnsTheValueOnce() {
    var (store, _) = Create();
    var token = store.Add("foo", "bar");

    Assert.Equal("foo", store.Take(token, "bar"));
    Assert.Null(store.Take(token, "bar"));
  }

  [Fact]
  public void Take_WithAnotherBindingKey_ReturnsNullAndConsumesIt() {
    var (store, _) = Create();
    var token = store.Add("foo", "bar");

    Assert.Null(store.Take(token, "baz"));
    Assert.Null(store.Take(token, "bar"));
  }

  [Fact]
  public void Peek_DoesNotConsume() {
    var (store, _) = Create();
    var token = store.Add("foo", "bar");

    Assert.Equal("foo", store.Peek(token, "bar"));
    Assert.Null(store.Peek(token, "baz"));
    Assert.Equal("foo", store.Take(token, "bar"));
  }

  [Fact]
  public void Take_AfterTheLifetime_ReturnsNull() {
    var (store, clock) = Create();
    var token = store.Add("foo", "bar");

    clock.Advance(Duration.FromMinutes(5) + Duration.FromSeconds(1));

    Assert.Null(store.Take(token, "bar"));
  }

  [Fact]
  public void Take_UnknownId_ReturnsNull() {
    var (store, _) = Create();

    Assert.Null(store.Take("foo", "bar"));
  }
}
