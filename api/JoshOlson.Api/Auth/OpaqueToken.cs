using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace JoshOlson.Api.Auth;

// A random bearer secret (session cookie, enrollment token). Only its SHA-256 hash is stored.
public static class OpaqueToken {
  private const int _ByteCount = 32;

  public static string Create()
    => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(_ByteCount));

  public static byte[] Hash(string token)
    => SHA256.HashData(Encoding.UTF8.GetBytes(token));

  public static byte[] NewUserHandle()
    => RandomNumberGenerator.GetBytes(_ByteCount);
}
