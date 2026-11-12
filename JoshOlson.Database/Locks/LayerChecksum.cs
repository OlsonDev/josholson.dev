using System.Security.Cryptography;
using System.Text;

namespace JoshOlson.Database.Locks;

// The checksum of a layer file, which is the same as of the text `seeds:migrate` writes (UTF-8, no byte order mark), so
// what it recorded can be compared with the file on disk later.
public static class LayerChecksum {
  public static string Of(byte[] bytes)
    => Convert.ToHexStringLower(SHA256.HashData(bytes));

  public static string OfText(string text)
    => Of(Encoding.UTF8.GetBytes(text));
}
