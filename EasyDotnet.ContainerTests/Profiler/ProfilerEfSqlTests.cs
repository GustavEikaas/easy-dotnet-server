using System.Collections.Concurrent;
using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.ContainerTests.Profiler;

// End-to-end profiler test against a REAL async DB driver (Npgsql → Postgres in a Testcontainer).
//
// Why this lives in ContainerTests and not IntegrationTests: SQLite completes its async APIs
// synchronously — the user frame is always on the stack when EF Core's `CommandExecuted` event
// fires, so the broken case (continuation thread with no user frame at all) cannot be reproduced
// with SQLite. Npgsql does true async I/O on a socket completion thread, which is the only way
// to exercise the CommandExecuting→CommandExecuted correlation path in ProfilerService.
public sealed class ProfilerEfSqlTests : IAsyncLifetime
{
  private static readonly TimeSpan ProfilerDuration = TimeSpan.FromSeconds(8);
  private const string PgUser = "postgres";
  private const string PgPassword = "postgres";
  private const string PgDb = "easydotnet";

  private IContainer _postgres = null!;
  private string _connectionString = null!;

  public async Task InitializeAsync()
  {
    _postgres = new ContainerBuilder("postgres:16-alpine")
      .WithEnvironment("POSTGRES_USER", PgUser)
      .WithEnvironment("POSTGRES_PASSWORD", PgPassword)
      .WithEnvironment("POSTGRES_DB", PgDb)
      .WithPortBinding(5432, assignRandomHostPort: true)
      .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilCommandIsCompleted("pg_isready", "-U", PgUser, "-d", PgDb))
      .Build();
    await _postgres.StartAsync();
    var hostPort = _postgres.GetMappedPublicPort(5432);
    _connectionString =
      $"Host=127.0.0.1;Port={hostPort};Username={PgUser};Password={PgPassword};Database={PgDb}";
  }

  public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

  // Matrix: prove the call-site resolution path works across EF Core majors. EF 8 is the LTS
  // and the most-reported version; EF 10 is the current major. Both must resolve user frames
  // even though Npgsql delivers the CommandExecuted event on a continuation thread with zero
  // user frames on the stack — that is the whole point of subscribing to CommandExecuting.
  [Theory]
  [InlineData("net8.0", "8.0.4")]
  [InlineData("net10.0", "10.0.0")]
  public async Task Profiler_AttachesToRealEfApp_EmitsSqlBucketAttributedToUserCallSite(
    string tfm, string npgsqlEfVersion)
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ProfilerEfTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(workspaceRoot);
    try
    {
      ScaffoldEfApp(workspaceRoot, tfm, npgsqlEfVersion, _connectionString,
                    out var querySourceFile, out _);
      DotnetBuild(workspaceRoot);
      var appBinary = Path.Combine(workspaceRoot, "bin", "Debug", tfm, "EfTarget.dll");
      Assert.True(File.Exists(appBinary), $"Built binary not found at {appBinary}");

      await RunMatrixCaseAsync(appBinary, workspaceRoot, querySourceFile);
    }
    finally
    {
      try { if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, recursive: true); }
      catch { }
    }
  }

  private static async Task RunMatrixCaseAsync(string appBinary, string workspaceRoot, string querySourceFile)
  {
    using var app = SpawnApp(appBinary, workspaceRoot);
    var pid = app.WaitForReady(TimeSpan.FromSeconds(30));

    var notifications = new RecordingNotificationService();
    var logger = new CapturingLogger<ProfilerService>();
    await using var profiler = new ProfilerService(notifications, logger);

    await profiler.StartAsync(pid, durationSeconds: ProfilerDuration.TotalSeconds);
    try
    {
      await WaitForStateAsync(notifications, "stopped", ProfilerDuration + TimeSpan.FromSeconds(20));
    }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException(
        $"{ex.Message}\nApp stdout:\n{app.SnapshotStdout()}\nApp stderr:\n{app.SnapshotStderr()}\nApp alive: {app.IsAlive}");
    }

    app.Kill();

    var diagnostic =
      $"\nApp stdout:\n{app.SnapshotStdout()}" +
      $"\nApp stderr:\n{app.SnapshotStderr()}" +
      $"\nState transitions: {string.Join(" | ", notifications.States.Select(s => $"{s.State}:{s.Message}"))}" +
      $"\nSample notifications: {notifications.SampleNotifications}, SQL notifications: {notifications.SqlNotifications}" +
      $"\nProfiler log:\n{logger.Snapshot()}";

    var buckets = notifications.LastSqlBuckets;
    Assert.True(buckets is not null, "No profiler/sql-queries notification fired." + diagnostic);
    Assert.True(buckets!.Count > 0, "profiler/sql-queries fired with empty bucket list." + diagnostic);

    Assert.Contains(buckets!, b => b.SqlSample.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                                && b.SqlSample.Contains("things", StringComparison.OrdinalIgnoreCase));

    // The critical assertion. With Npgsql delivering CommandExecuted on a continuation thread
    // (no user frame on the stack), this only passes if the CommandExecuting correlation path
    // works. Regressing the correlation makes every bucket attribute to "<unknown>".
    var attributed = buckets!.Where(b => b.File != "<unknown>").ToArray();
    Assert.True(attributed.Length > 0,
      "All SQL buckets are <unknown> — call-site resolution failed for every event. " +
      $"Total buckets: {buckets!.Count}, samples: {string.Join(" | ", buckets!.Take(3).Select(b => b.SqlSample))}" +
      diagnostic);

    Assert.Contains(attributed, b =>
      string.Equals(Path.GetFullPath(b.File), Path.GetFullPath(querySourceFile), StringComparison.OrdinalIgnoreCase));
  }

  private static async Task WaitForStateAsync(RecordingNotificationService notifications, string state, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (notifications.States.Any(s => s.State == state)) return;
      await Task.Delay(200);
    }
    throw new TimeoutException(
      $"Profiler never reached state '{state}' within {timeout.TotalSeconds:F0}s. " +
      $"States seen: {string.Join(", ", notifications.States.Select(s => s.State))}. " +
      $"Sql notifications: {notifications.SqlNotifications}, sample notifications: {notifications.SampleNotifications}.");
  }

  private static SpawnedApp SpawnApp(string appBinary, string workspaceRoot)
  {
    var psi = new ProcessStartInfo("dotnet", $"\"{appBinary}\"")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      WorkingDirectory = workspaceRoot,
    };
    var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start EfTarget");
    var app = new SpawnedApp(process);
    app.StartAsyncDrain();
    return app;
  }

  private static void ScaffoldEfApp(
    string root, string tfm, string npgsqlEfVersion, string connectionString,
    out string querySourceFile, out int queryLine)
  {
    // Only reference Npgsql.EntityFrameworkCore.PostgreSQL — Microsoft.EntityFrameworkCore comes
    // in transitively at the version Npgsql requires, avoiding NU1605 downgrade errors when the
    // two are pinned to mismatched patch levels.
    File.WriteAllText(Path.Combine(root, "EfTarget.csproj"), $"""
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>{tfm}</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
          <RootNamespace>EfTarget</RootNamespace>
          <AssemblyName>EfTarget</AssemblyName>
          <DebugType>portable</DebugType>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="{npgsqlEfVersion}" />
        </ItemGroup>
      </Project>
      """);

    querySourceFile = Path.Combine(root, "Program.cs");
    var src = $$""""
      using Microsoft.EntityFrameworkCore;

      namespace EfTarget;

      public class Thing
      {
          public int Id { get; set; }
          public string Name { get; set; } = "";
      }

      public class Db : DbContext
      {
          public DbSet<Thing> Things => Set<Thing>();
          protected override void OnConfiguring(DbContextOptionsBuilder o) =>
              o.UseNpgsql("{{connectionString}}");
      }

      public static class Runner
      {
          public static async Task RunOneAsync(Db db, CancellationToken ct) =>
              await db.Things.ToListAsync(ct);  // <-- QUERY-LINE
      }

      public static class Program
      {
          public static async Task Main()
          {
              await using var db = new Db();
              await db.Database.EnsureCreatedAsync();
              db.Things.Add(new Thing { Name = "alpha" });
              await db.SaveChangesAsync();

              Console.WriteLine($"READY pid={Environment.ProcessId}");
              Console.Out.Flush();

              using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
              while (!cts.IsCancellationRequested)
              {
                  try { await Runner.RunOneAsync(db, cts.Token); } catch { }
                  await Task.Delay(100, cts.Token);
              }
          }
      }
      """";
    File.WriteAllText(querySourceFile, src);

    queryLine = 0;
    var lines = src.Split('\n');
    for (var i = 0; i < lines.Length; i++)
    {
      if (lines[i].Contains("QUERY-LINE")) { queryLine = i + 1; break; }
    }
    Assert.True(queryLine > 0, "QUERY-LINE marker not found in scaffolded source");
  }

  private static void DotnetBuild(string projectDir)
  {
    var psi = new ProcessStartInfo("dotnet", "build -c Debug --nologo")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      WorkingDirectory = projectDir,
    };
    using var p = Process.Start(psi) ?? throw new InvalidOperationException("dotnet build failed to start");
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0)
      throw new InvalidOperationException($"dotnet build failed (exit {p.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
  }

  private sealed class SpawnedApp(Process process) : IDisposable
  {
    private readonly System.Text.StringBuilder _stdout = new();
    private readonly System.Text.StringBuilder _stderr = new();
    private readonly TaskCompletionSource<int> _readyTcs = new();

    public bool IsAlive { get { try { return !process.HasExited; } catch { return false; } } }
    public string SnapshotStdout() { lock (_stdout) return _stdout.ToString(); }
    public string SnapshotStderr() { lock (_stderr) return _stderr.ToString(); }

    public void StartAsyncDrain()
    {
      process.OutputDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        lock (_stdout) _stdout.AppendLine(e.Data);
        if (e.Data.StartsWith("READY pid=", StringComparison.Ordinal)
            && int.TryParse(e.Data["READY pid=".Length..], out var pid))
          _readyTcs.TrySetResult(pid);
      };
      process.ErrorDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        lock (_stderr) _stderr.AppendLine(e.Data);
      };
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
    }

    public int WaitForReady(TimeSpan timeout)
    {
      if (!_readyTcs.Task.Wait(timeout))
        throw new TimeoutException(
          $"EfTarget never wrote READY line within {timeout.TotalSeconds:F0}s.\nSTDOUT:\n{SnapshotStdout()}\nSTDERR:\n{SnapshotStderr()}");
      return _readyTcs.Task.Result;
    }

    public void Kill()
    {
      try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    public void Dispose() { Kill(); process.Dispose(); }
  }

  private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
  {
    private readonly System.Text.StringBuilder _sb = new();
    public string Snapshot() { lock (_sb) return _sb.ToString(); }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
      var line = $"[{logLevel}] {formatter(state, exception)}";
      if (exception is not null) line += $" :: {exception}";
      lock (_sb) _sb.AppendLine(line);
    }
  }

  private sealed class RecordingNotificationService : INotificationService
  {
    public ConcurrentBag<(string State, string? Message)> States { get; } = new();
    public List<IReadOnlyList<ProfilerSqlBucket>> SqlEmissions { get; } = new();
    public IReadOnlyList<ProfilerSqlBucket>? LastSqlBuckets => SqlEmissions.LastOrDefault();
    public int SqlNotifications => SqlEmissions.Count;
    public int SampleNotifications { get; private set; }

    public Task NotifyProfilerSqlQueries(ProfilerSqlBucket[] buckets)
    {
      lock (SqlEmissions) SqlEmissions.Add(buckets);
      return Task.CompletedTask;
    }
    public Task NotifyProfilerSamples(ProfilerSampleDelta[] deltas) { SampleNotifications++; return Task.CompletedTask; }
    public Task NotifyProfilerState(string state, string? message = null) { States.Add((state, message)); return Task.CompletedTask; }

    public Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => Task.CompletedTask;
    public Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => Task.CompletedTask;
    public Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile) => Task.CompletedTask;
    public Task NotifyRunningProcessesChangedAsync(RunningSessionInfo[] projects) => Task.CompletedTask;
  }
}
