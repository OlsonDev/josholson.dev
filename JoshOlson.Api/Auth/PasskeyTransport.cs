namespace JoshOlson.Api.Auth;

// How an authenticator can be reached. Stored as SMALLINT[] in auth.passkey.transports, so the numbers are the
// database values: they start at 1, are never reused, and are mirrored in docs/database.md and the table's range check.
public enum PasskeyTransport : short {
  Usb = 1,
  Nfc = 2,
  Ble = 3,
  Hybrid = 4,
  Internal = 5,
  SmartCard = 6,
}
