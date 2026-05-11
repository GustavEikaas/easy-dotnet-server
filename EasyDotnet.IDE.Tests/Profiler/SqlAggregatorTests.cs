using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Profiler;

public class SqlAggregatorTests
{
  [Test]
  public async Task NormalizeForKey_StripsNumericLiterals()
  {
    var a = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = 42");
    var b = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = 9001");
    await Assert.That(a).IsEqualTo(b);
  }

  [Test]
  public async Task NormalizeForKey_StripsStringLiterals()
  {
    var a = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Name = 'alice'");
    var b = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Name = 'bob'");
    await Assert.That(a).IsEqualTo(b);
  }

  [Test]
  public async Task NormalizeForKey_PreservesParameterMarkers()
  {
    // Parameterized queries are the canonical EF Core shape — they must round-trip unchanged.
    var key = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = @p0");
    await Assert.That(key).IsEqualTo("SELECT * FROM Users WHERE Id = @p0");
  }

  [Test]
  public async Task NormalizeForKey_CollapsesWhitespace()
  {
    var key = SqlAggregator.NormalizeForKey("SELECT  *\n  FROM\tUsers");
    await Assert.That(key).IsEqualTo("SELECT * FROM Users");
  }

  [Test]
  public async Task Aggregate_CoalescesSameShapeDifferentLiterals()
  {
    var events = new[]
    {
      new ProfilerSqlEvent("/r/Repo.cs", 10, "SELECT * FROM Users WHERE Id = 1", "@p0=1", ElapsedMs: 5),
      new ProfilerSqlEvent("/r/Repo.cs", 10, "SELECT * FROM Users WHERE Id = 2", "@p0=2", ElapsedMs: 8),
      new ProfilerSqlEvent("/r/Repo.cs", 10, "SELECT * FROM Users WHERE Id = 3", "@p0=3", ElapsedMs: 12),
    };

    var buckets = SqlAggregator.Aggregate(events);

    await Assert.That(buckets).Count().IsEqualTo(1);
    await Assert.That(buckets[0].Count).IsEqualTo(3);
    await Assert.That(buckets[0].TotalMs).IsEqualTo(25);
    await Assert.That(buckets[0].MaxMs).IsEqualTo(12);
    await Assert.That(buckets[0].ParametersSample).IsEqualTo("@p0=3");
  }

  [Test]
  public async Task Aggregate_SplitsByCallSite()
  {
    var sql = "SELECT * FROM Users WHERE Id = @p0";
    var events = new[]
    {
      new ProfilerSqlEvent("/r/A.cs", 10, sql, null, 5),
      new ProfilerSqlEvent("/r/B.cs", 20, sql, null, 7),
    };

    var buckets = SqlAggregator.Aggregate(events);

    await Assert.That(buckets).Count().IsEqualTo(2);
  }

  [Test]
  public async Task Aggregate_SplitsByQueryShape()
  {
    var events = new[]
    {
      new ProfilerSqlEvent("/r/A.cs", 10, "SELECT * FROM Users WHERE Id = @p0", null, 5),
      new ProfilerSqlEvent("/r/A.cs", 10, "SELECT * FROM Orders WHERE Id = @p0", null, 7),
    };

    var buckets = SqlAggregator.Aggregate(events);

    await Assert.That(buckets).Count().IsEqualTo(2);
  }

  [Test]
  public async Task Aggregate_EmptyInput_ReturnsEmpty()
  {
    var buckets = SqlAggregator.Aggregate([]);
    await Assert.That(buckets).IsEmpty();
  }

  [Test]
  public async Task IsInfrastructureFrame_DetectsEfAndBcl()
  {
    await Assert.That(SqlAggregator.IsInfrastructureFrame("Microsoft.EntityFrameworkCore.Query.QueryCompiler.Execute")).IsTrue();
    await Assert.That(SqlAggregator.IsInfrastructureFrame("module!Microsoft.EntityFrameworkCore.DbContext.SaveChanges")).IsTrue();
    await Assert.That(SqlAggregator.IsInfrastructureFrame("module!System.Linq.Enumerable.ToList")).IsTrue();
    await Assert.That(SqlAggregator.IsInfrastructureFrame("module!Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader")).IsTrue();
  }

  [Test]
  public async Task IsInfrastructureFrame_AllowsUserCode()
  {
    await Assert.That(SqlAggregator.IsInfrastructureFrame("MyApp!MyApp.Repositories.UserRepository.GetById")).IsFalse();
    await Assert.That(SqlAggregator.IsInfrastructureFrame("MyApp.Program.Main")).IsFalse();
  }

  [Test]
  public async Task ComputeBucketId_IsStableAcrossLiteralAndWhitespaceVariants()
  {
    // The whole point of bucket ids: two events that aggregate to the same bucket must share an
    // id so the server's dedup table can recognize them as the "same" thing across flushes.
    var a = SqlAggregator.ComputeBucketId("/r/Repo.cs", 10,
        SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = 1"));
    var b = SqlAggregator.ComputeBucketId("/r/Repo.cs", 10,
        SqlAggregator.NormalizeForKey("SELECT  *\n  FROM  Users\n  WHERE Id = 999"));
    await Assert.That(a).IsEqualTo(b);
  }

  [Test]
  public async Task ComputeBucketId_DistinguishesCallSites()
  {
    var sql = SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = @p0");
    var a = SqlAggregator.ComputeBucketId("/r/A.cs", 10, sql);
    var b = SqlAggregator.ComputeBucketId("/r/B.cs", 10, sql);
    var c = SqlAggregator.ComputeBucketId("/r/A.cs", 11, sql);
    await Assert.That(a).IsNotEqualTo(b);
    await Assert.That(a).IsNotEqualTo(c);
  }

  [Test]
  public async Task ComputeBucketId_DistinguishesQueryShapes()
  {
    var users = SqlAggregator.ComputeBucketId("/r/A.cs", 10,
        SqlAggregator.NormalizeForKey("SELECT * FROM Users WHERE Id = @p0"));
    var orders = SqlAggregator.ComputeBucketId("/r/A.cs", 10,
        SqlAggregator.NormalizeForKey("SELECT * FROM Orders WHERE Id = @p0"));
    await Assert.That(users).IsNotEqualTo(orders);
  }

  [Test]
  public async Task Aggregate_AssignsBucketId()
  {
    var events = new[]
    {
      new ProfilerSqlEvent("/r/Repo.cs", 10, "SELECT * FROM Users WHERE Id = 1", null, 5),
    };
    var buckets = SqlAggregator.Aggregate(events);
    await Assert.That(buckets).Count().IsEqualTo(1);
    await Assert.That(buckets[0].BucketId).IsNotEmpty();
  }
}
