using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Migrate;

public class MigrationHistoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  private static MigrationFile Disk(int version, string name)
    => new() {
      Version = version,
      File = $"V{version.ToInvariantString().PadLeft(3, '0')}__{name}.sql",
      Checksum = SchemaLockSample.Aaa,
    };

  private static AppliedMigration Applied(int version, string name, bool success = true)
    => new() {
      Version = version,
      File = $"V{version.ToInvariantString().PadLeft(3, '0')}__{name}.sql",
      Success = success,
    };

  [Fact]
  public void Check_SameMigrations_HasNoProblems()
    => Assert.Empty(MigrationHistory.Check([Disk(1, "foo"), Disk(2, "bar")], [Applied(1, "foo"), Applied(2, "bar")]));

  [Fact]
  public void Check_MigrationNotApplied_SaysToRunWaypoint() {
    var problems = MigrationHistory.Check([Disk(1, "foo"), Disk(2, "bar")], [Applied(1, "foo")]);

    var problem = Assert.Single(problems);
    Assert.Contains("V002__bar.sql", problem, StringComparison.Ordinal);
    Assert.Contains("mise run waypoint migrate", problem, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_AppliedMigrationNotOnDisk_Reports() {
    var problem = Assert.Single(MigrationHistory.Check([Disk(1, "foo")], [Applied(1, "foo"), Applied(2, "bar")]));

    Assert.Contains("V002__bar.sql", problem, StringComparison.Ordinal);
    Assert.Contains("do not have", problem, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SameVersionDifferentFile_NamesBoth() {
    var problem = Assert.Single(MigrationHistory.Check([Disk(2, "baz")], [Applied(2, "bar")]));

    Assert.Contains("V002__baz.sql", problem, StringComparison.Ordinal);
    Assert.Contains("V002__bar.sql", problem, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_FailedMigration_Reports() {
    var problem = Assert.Single(MigrationHistory.Check([Disk(1, "foo")], [Applied(1, "foo", success: false)]));

    Assert.Contains("failed", problem, StringComparison.Ordinal);
  }

  [Fact]
  public void Check_SeveralProblems_AreAllReportedByVersion() {
    var problems = MigrationHistory.Check([Disk(3, "qux"), Disk(1, "foo")], [Applied(2, "bar"), Applied(1, "baz")]);

    Assert.Equal(3, problems.Count);
    Assert.Contains("V001__baz.sql", problems[0], StringComparison.Ordinal);
    Assert.Contains("V002__bar.sql", problems[1], StringComparison.Ordinal);
    Assert.Contains("V003__qux.sql", problems[2], StringComparison.Ordinal);
  }

  [Fact]
  public async Task ReadAsync_DevDatabase_ListsTheVersionedMigrationsInOrder() {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(_cancellationToken);

    var applied = await MigrationHistory.ReadAsync(connection, _cancellationToken);

    Assert.NotEmpty(applied);
    Assert.Equal(applied.Select(migration => migration.Version).Order(), applied.Select(migration => migration.Version));
    Assert.All(applied, migration => Assert.StartsWith("V", migration.File, StringComparison.Ordinal));
  }
}
