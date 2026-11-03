using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NodaTime;

namespace JoshOlson.Api.Auth;

// Short-lived, single-use state kept in memory (ADR 0031, pending passkey changes): a challenge, or a passkey change
// waiting for its confirmation. It suits one API instance; a restart mid-flow just fails and is retried. `Add` returns
// a random token that redeems the entry. It is the key itself, unhashed, because nothing is persisted and it lives
// minutes. An entry is bound to a key (the session id, or empty for an anonymous flow), so another session cannot
// take it.
public sealed class SingleUseStore<T> where T : class {
  private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
  private readonly IClock _clock;
  private readonly TimeSpan _lifetime;

  public SingleUseStore(IClock clock, IOptions<AuthOptions> options) {
    _clock = clock;
    _lifetime = options.Value.SingleUseLifetime;
  }

  public string Add(T value, string bindingKey) {
    var now = _clock.GetCurrentInstant();
    Prune(now);
    var token = OpaqueToken.Create();
    _entries[token] = new(value, bindingKey, now + Duration.FromTimeSpan(_lifetime));
    return token;
  }

  // Reads without using it up.
  public T? Peek(string token, string bindingKey)
    => _entries.TryGetValue(token, out var entry) && IsUsable(entry, bindingKey) ? entry.Value : null;

  // Uses it up. An entry taken with the wrong key is gone too, so a guess cannot be retried.
  public T? Take(string token, string bindingKey)
    => _entries.TryRemove(token, out var entry) && IsUsable(entry, bindingKey) ? entry.Value : null;

  private bool IsUsable(Entry entry, string bindingKey)
    => entry.ExpiresUtc > _clock.GetCurrentInstant()
    && string.Equals(entry.BindingKey, bindingKey, StringComparison.Ordinal);

  private void Prune(Instant now) {
    foreach (var (token, entry) in _entries) {
      if (entry.ExpiresUtc <= now) _entries.TryRemove(token, out _);
    }
  }

  private sealed record Entry(T Value, string BindingKey, Instant ExpiresUtc);
}
