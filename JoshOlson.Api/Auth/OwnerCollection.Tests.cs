using Xunit;

namespace JoshOlson.Api.Auth;

// "The owner" is the lowest user id, and other tests create and delete users all the time. A test that mints a token
// for the owner runs alone, after the parallel ones, so the owner it picks does not vanish before the token is stored.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OwnerCollection {
  public const string Name = "Owner";
}
