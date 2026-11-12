using JoshOlson.Database.Schema;
using JoshOlson.Shared.Extensions;
using Xunit;

namespace JoshOlson.Database.Locks;

public class SchemaLockTests {
  private static readonly string _Original = new('a', 64);
  private static readonly string _Edited = new('b', 64);
  private static readonly string _Other = new('c', 64);

  private static string FileName(int version, string name)
    => $"V{version.ToInvariantString().PadLeft(3, '0')}__{name}.sql";

  private static LockedMigration Locked(int version, string name, string checksum, params string[] acceptedChecksums)
    => new() {
      Version = version,
      File = FileName(version, name),
      Checksum = checksum,
      AcceptedChecksums = [.. acceptedChecksums],
    };

  private static MigrationFile OnDisk(int version, string name, string checksum)
    => new() {
      Version = version,
      File = FileName(version, name),
      Checksum = checksum,
    };

  // Seeds migrated to version 2; migrations 1 and 2 are locked.
  private static SchemaLock NewLock()
    => new() {
      Migrations = [Locked(1, "foo", _Original), Locked(2, "bar", _Other)],
      Schemas = [],
      Domains = [],
      Tables = [],
    };

  [Fact]
  public void Check_SameHistory_IsConsistent_AndListsLaterMigrationsAsPending() {
    var check = NewLock().Check([OnDisk(1, "foo", _Original), OnDisk(2, "bar", _Other), OnDisk(3, "baz", _Edited)]);

    Assert.True(check.IsConsistent);
    Assert.Equal([3], check.Pending.Select(migration => migration.Version));
  }

  [Fact]
  public void Check_NothingPending_WhenSeedsAreAtTheNewestMigration() {
    var check = NewLock().Check([OnDisk(1, "foo", _Original), OnDisk(2, "bar", _Other)]);

    Assert.True(check.IsConsistent);
    Assert.Empty(check.Pending);
  }

  [Fact]
  public void Check_EditedMigration_IsChanged_AndPointsToSeedsAccept() {
    var check = NewLock().Check([OnDisk(1, "foo", _Edited), OnDisk(2, "bar", _Other)]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Changed, problem.Kind);
    Assert.Equal(1, problem.Version);
    Assert.Contains("seeds:accept 1", problem.Message, StringComparison.Ordinal);
    Assert.Contains("V001__foo.sql", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_BranchTookTheNumber_IsDifferent_AndExplainsTheFix() {
    var check = NewLock().Check([OnDisk(1, "foo", _Original), OnDisk(2, "mine", _Edited)]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Different, problem.Kind);
    Assert.Equal(2, problem.Version);
    Assert.Contains("Your version 2 is V002__mine.sql", problem.Message, StringComparison.Ordinal);
    Assert.Contains("migrated against V002__bar.sql", problem.Message, StringComparison.Ordinal);
    Assert.Contains("seeds:migrate", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SameNumberAndNameButDifferentContent_IsChangedNotDifferent() {
    var check = NewLock().Check([OnDisk(1, "foo", _Original), OnDisk(2, "bar", _Edited)]);

    Assert.Equal(MigrationProblemKind.Changed, Assert.Single(check.Problems).Kind);
  }

  [Fact]
  public void Check_MigrationGone_IsMissing() {
    var check = NewLock().Check([OnDisk(1, "foo", _Original)]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Missing, problem.Kind);
    Assert.Equal(2, problem.Version);
  }

  [Fact]
  public void Check_MigrationInsertedBelowTheSeedsVersion_IsUnlocked() {
    var schemaLock = NewLock() with {
      Migrations = [Locked(1, "foo", _Original), Locked(3, "baz", _Other)],
    };

    var check = schemaLock.Check([OnDisk(1, "foo", _Original), OnDisk(2, "bar", _Edited), OnDisk(3, "baz", _Other)]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Unlocked, problem.Kind);
    Assert.Equal(2, problem.Version);
    Assert.Contains("renumber it above 3", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SeveralProblems_AreOrderedByVersion() {
    var check = NewLock().Check([OnDisk(2, "bar", _Edited), OnDisk(1, "foo", _Edited)]);

    Assert.Equal([1, 2], check.Problems.Select(problem => problem.Version));
  }

  [Fact]
  public void WithAcceptedChecksum_EditedMigrationThenPassesAndTheOriginalStillDoes() {
    var edited = NewLock().Check([OnDisk(1, "foo", _Edited), OnDisk(2, "bar", _Other)]);
    Assert.False(edited.IsConsistent);

    var accepted = NewLock().WithAcceptedChecksum(1, _Edited);

    Assert.True(accepted.Check([OnDisk(1, "foo", _Edited), OnDisk(2, "bar", _Other)]).IsConsistent);
    Assert.True(accepted.Check([OnDisk(1, "foo", _Original), OnDisk(2, "bar", _Other)]).IsConsistent);
    Assert.False(accepted.Check([OnDisk(1, "foo", _Other), OnDisk(2, "bar", _Other)]).IsConsistent);
  }

  [Fact]
  public void WithAcceptedChecksum_KeepsChecksumsOrderedAndWithoutDuplicates() {
    var schemaLock = NewLock().WithAcceptedChecksum(1, _Other).WithAcceptedChecksum(1, _Edited).WithAcceptedChecksum(1, _Other).WithAcceptedChecksum(1, _Original);

    Assert.Equal([_Edited, _Other], schemaLock.Migrations[0].AcceptedChecksums);
  }

  [Fact]
  public void WithAcceptedChecksum_DoesNotChangeTheOriginalLock() {
    var schemaLock = NewLock();

    _ = schemaLock.WithAcceptedChecksum(1, _Edited);

    Assert.Empty(schemaLock.Migrations[0].AcceptedChecksums);
  }

  [Fact]
  public void WithAcceptedChecksum_UnknownVersion_Throws()
    => Assert.Throws<SeedFormatException>(() => NewLock().WithAcceptedChecksum(9, _Edited));

  private static SchemaLock WithTables(params Table[] tables)
    => NewLock() with { Tables = [.. tables] };

  private static ForeignKey References(string referencedTable, bool deferrable = false, bool deferred = false)
    => SchemaLockSample.ForeignKey($"fk_{referencedTable}", ["id"], referencedTable, ["id"], deferrable: deferrable, deferred: deferred);

  [Fact]
  public void InsertOrder_PutsEveryTableAfterTheTablesItReferences() {
    var order = SchemaLockSample.Create().InsertOrder();

    Assert.Equal(["foo.bar", "foo.tag", "foo.link"], order.Select(table => table.Name));
  }

  [Fact]
  public void DeleteOrder_IsTheReverse() {
    var order = SchemaLockSample.Create().DeleteOrder();

    Assert.Equal(["foo.link", "foo.tag", "foo.bar"], order.Select(table => table.Name));
  }

  [Fact]
  public void InsertOrder_UnrelatedTablesAreOrderedByName() {
    var order = WithTables(SchemaLockSample.Table("foo.c"), SchemaLockSample.Table("foo.a"), SchemaLockSample.Table("foo.b")).InsertOrder();

    Assert.Equal(["foo.a", "foo.b", "foo.c"], order.Select(table => table.Name));
  }

  [Fact]
  public void InsertOrder_ChainsFollowTheReferences() {
    var order = WithTables(
      SchemaLockSample.Table("foo.a", References("foo.b")),
      SchemaLockSample.Table("foo.b", References("foo.c")),
      SchemaLockSample.Table("foo.c")).InsertOrder();

    Assert.Equal(["foo.c", "foo.b", "foo.a"], order.Select(table => table.Name));
  }

  [Fact]
  public void InsertOrder_ATableReferencingItselfIsNotACycle() {
    var order = WithTables(SchemaLockSample.Table("foo.a", References("foo.a")), SchemaLockSample.Table("foo.b")).InsertOrder();

    Assert.Equal(["foo.a", "foo.b"], order.Select(table => table.Name));
  }

  [Fact]
  public void InsertOrder_AnInitiallyDeferredKeyBreaksALoop() {
    var order = WithTables(
      SchemaLockSample.Table("foo.a", References("foo.b")),
      SchemaLockSample.Table("foo.b", References("foo.a", deferrable: true, deferred: true))).InsertOrder();

    Assert.Equal(["foo.b", "foo.a"], order.Select(table => table.Name));
  }

  [Fact]
  public void InsertOrder_ADeferrableKeyThatIsNotDeferredStillConstrains() {
    var exception = Assert.Throws<SeedFormatException>(() => WithTables(
      SchemaLockSample.Table("foo.a", References("foo.b")),
      SchemaLockSample.Table("foo.b", References("foo.a", deferrable: true))).InsertOrder());

    Assert.Contains("foo.a, foo.b", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void InsertOrder_ALoopWithNoDeferredKey_ThrowsNamingTheTables() {
    var exception = Assert.Throws<SeedFormatException>(() => WithTables(
      SchemaLockSample.Table("foo.a", References("foo.b")),
      SchemaLockSample.Table("foo.b", References("foo.c")),
      SchemaLockSample.Table("foo.c", References("foo.a")),
      SchemaLockSample.Table("foo.d")).InsertOrder());

    Assert.Contains("foo.a, foo.b, foo.c", exception.Message, StringComparison.Ordinal);
    Assert.DoesNotContain("foo.d", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void InsertOrder_ReferenceToATableNotInTheLock_Throws()
    => Assert.Throws<SeedFormatException>(() => WithTables(SchemaLockSample.Table("foo.a", References("foo.gone"))).InsertOrder());

  [Fact]
  public void InsertOrder_DoesNotChangeTheLock() {
    var schemaLock = SchemaLockSample.Create();

    _ = schemaLock.InsertOrder();

    Assert.Equal(["foo.bar", "foo.link", "foo.tag"], schemaLock.Tables.Select(table => table.Name));
  }
}
