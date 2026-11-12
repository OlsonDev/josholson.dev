using JoshOlson.Database.Schema;
using Xunit;

namespace JoshOlson.Database.Locks;

public class SchemaLockTests {
  // Seeds migrated to version 2; migrations 1 and 2 are locked. On disk, `Aaa` and `Ccc` are their checksums as they
  // were locked, and `Bbb` is a file that was edited or is someone else's.
  private static SchemaLock NewLock()
    => new() {
      Migrations = [
        SchemaLockSample.Locked(1, "foo", SchemaLockSample.Aaa),
        SchemaLockSample.Locked(2, "bar", SchemaLockSample.Ccc),
      ],
      Schemas = [],
      Domains = [],
      Tables = [],
    };

  [Fact]
  public void Check_SameHistory_IsConsistent_AndListsLaterMigrationsAsPending() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(1, "foo"),
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Ccc),
      SchemaLockSample.Disk(3, "baz", SchemaLockSample.Bbb),
    ]);

    Assert.True(check.IsConsistent);
    Assert.Equal([3], check.Pending.Select(migration => migration.Version));
  }

  [Fact]
  public void Check_NothingPending_WhenSeedsAreAtTheNewestMigration() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(1, "foo"),
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Ccc),
    ]);

    Assert.True(check.IsConsistent);
    Assert.Empty(check.Pending);
  }

  [Fact]
  public void Check_EditedMigration_IsChanged_AndSaysToAddANewOne() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(1, "foo", SchemaLockSample.Bbb),
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Ccc),
    ]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Changed, problem.Kind);
    Assert.Equal(1, problem.Version);
    Assert.Contains("Add a new migration", problem.Message, StringComparison.Ordinal);
    Assert.Contains("V001__foo.sql", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_BranchTookTheNumber_IsDifferent_AndExplainsTheFix() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(1, "foo"),
      SchemaLockSample.Disk(2, "mine", SchemaLockSample.Bbb),
    ]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Different, problem.Kind);
    Assert.Equal(2, problem.Version);
    Assert.Contains("Your version 2 is V002__mine.sql", problem.Message, StringComparison.Ordinal);
    Assert.Contains("migrated against V002__bar.sql", problem.Message, StringComparison.Ordinal);
    Assert.Contains("seeds:migrate", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SameNumberAndNameButDifferentContent_IsChangedNotDifferent() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(1, "foo"),
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Bbb),
    ]);

    Assert.Equal(MigrationProblemKind.Changed, Assert.Single(check.Problems).Kind);
  }

  [Fact]
  public void Check_MigrationGone_IsMissing() {
    var check = NewLock().Check([SchemaLockSample.Disk(1, "foo")]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Missing, problem.Kind);
    Assert.Equal(2, problem.Version);
  }

  [Fact]
  public void Check_MigrationInsertedBelowTheSeedsVersion_IsUnlocked() {
    var schemaLock = NewLock() with {
      Migrations = [
        SchemaLockSample.Locked(1, "foo", SchemaLockSample.Aaa),
        SchemaLockSample.Locked(3, "baz", SchemaLockSample.Ccc),
      ],
    };

    var check = schemaLock.Check([
      SchemaLockSample.Disk(1, "foo"),
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Bbb),
      SchemaLockSample.Disk(3, "baz", SchemaLockSample.Ccc),
    ]);

    var problem = Assert.Single(check.Problems);
    Assert.Equal(MigrationProblemKind.Unlocked, problem.Kind);
    Assert.Equal(2, problem.Version);
    Assert.Contains("renumber it above 3", problem.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SeveralProblems_AreOrderedByVersion() {
    var check = NewLock().Check([
      SchemaLockSample.Disk(2, "bar", SchemaLockSample.Bbb),
      SchemaLockSample.Disk(1, "foo", SchemaLockSample.Bbb),
    ]);

    Assert.Equal([1, 2], check.Problems.Select(problem => problem.Version));
  }

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
