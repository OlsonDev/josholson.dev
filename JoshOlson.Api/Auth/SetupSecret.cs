using System.Security.Cryptography;
using System.Text;

namespace JoshOlson.Api.Auth;

// The secret that lets the browser register the first passkey (`AuthOptions.SetupSecret`).
public static class SetupSecret {
  // False when setup is off (`configured` is empty). Compares fixed-size hashes, so the time it takes says nothing about
  // how much of a guess was right, or how long the secret is.
  public static bool Matches(string configured, string given)
    => configured.Length > 0
      && CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
        SHA256.HashData(Encoding.UTF8.GetBytes(given)));
}
