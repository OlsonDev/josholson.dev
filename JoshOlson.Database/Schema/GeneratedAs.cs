namespace JoshOlson.Database.Schema;

// How the database fills a column itself, named for the `GENERATED ... AS` clause. An identity column is inserted into
// with OVERRIDING SYSTEM VALUE when a seed gives it a value; a column generated from an expression cannot be inserted
// into at all, whether the expression is kept in the row (`STORED`) or computed when read (`VIRTUAL`).
public enum GeneratedAs : short {
  IdentityAlways = 1,
  IdentityByDefault = 2,
  ExpressionStored = 3,
  ExpressionVirtual = 4,
}
