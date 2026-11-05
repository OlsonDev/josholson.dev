namespace JoshOlson.Database.Schema;

// A foreign key's ON DELETE or ON UPDATE action.
public enum ReferentialAction {
  NoAction = 1,
  Restrict = 2,
  Cascade = 3,
  SetNull = 4,
  SetDefault = 5,
}
