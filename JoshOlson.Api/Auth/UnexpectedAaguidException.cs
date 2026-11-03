namespace JoshOlson.Api.Auth;

// Attestation is `none`, so a browser sends an all-zero AAGUID. A real one means attestation or a browser changed:
// decide then whether `auth.passkey` needs an `aaguid` column (ADR 0031, no AAGUID column).
#pragma warning disable RCS1194 // Only the AAGUID constructor makes sense; the standard ones lack the message
public sealed class UnexpectedAaguidException : Exception {
  public UnexpectedAaguidException(Guid aaguid, string? modelName)
    : base(BuildMessage(aaguid, modelName)) {
  }

  private static string BuildMessage(Guid aaguid, string? modelName) {
    var model = modelName ?? "model unknown";
    return $"""
      The authenticator reported AAGUID {aaguid} ({model}), but attestation is 'none'
      and the AAGUID should be all zeros. Decide whether `auth.passkey` needs an `aaguid` column.
      """;
  }
}
#pragma warning restore RCS1194
