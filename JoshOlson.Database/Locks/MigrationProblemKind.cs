namespace JoshOlson.Database.Locks;

public enum MigrationProblemKind {
  // The lock has a version your migrations do not.
  Missing = 1,

  // The version exists in both, but as a different file name: another branch's migration took the number.
  Different = 2,

  // Same file name, but its content is not one the lock knows.
  Changed = 3,

  // Your migrations have a version at or below the locked one that the lock has no record of.
  Unlocked = 4,
}
