using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JoshOlson.Database.Migrate;

// Reads the base out of git with `fetch`, `rev-parse`, `show`, and `ls-tree`, so nothing is checked out and the working tree
// and the branch you are on are never touched. The base is the remote's main unless a ref is given.
//
// `git fetch origin main` downloads what is new and moves only `origin/main`, the copy of the remote's branch that the
// repository keeps, never a local branch. It runs without a prompt and with a time limit; if it fails (offline, or a
// credential it would have asked for) the base is `origin/main` as it was last fetched, and the warning says so.
public sealed class GitSnapshotSource : ISnapshotSource {
  private const string _Remote = "origin";
  private const string _Branch = "main";
  private static readonly TimeSpan _FetchTimeout = TimeSpan.FromSeconds(60);

  private readonly SeedPaths _paths;

  public GitSnapshotSource(SeedPaths paths)
    => _paths = paths;

  public async Task<SnapshotRead> ReadAsync(string? baseRef, bool fetch, CancellationToken cancellationToken) {
    var warnings = new List<string>();
    var reference = baseRef ?? $"{_Remote}/{_Branch}";
    try {
      if (baseRef is null && fetch) {
        var (fetchExit, fetchOutput) = await RunAsync(["fetch", _Remote, _Branch], _FetchTimeout, cancellationToken);
        if (fetchExit is not 0) {
          warnings.Add($"Could not fetch {_Remote} {_Branch} ({FirstLine(fetchOutput)}), so {reference} is as it was last fetched.");
        }
      }
      var (resolveExit, resolved) = await RunAsync(["rev-parse", "--verify", "--quiet", $"{reference}^{{commit}}"], timeout: null, cancellationToken);
      if (resolveExit is not 0) {
        return baseRef is not null
          ? throw new SeedFormatException($"'{baseRef}' is not a commit in this repository.")
          : new(
            Snapshot: null,
            Warnings: [.. warnings, $"There is no {reference}, so there is no base to derive the seeds from. Fetch it, or give --base <ref>."]);
      }
      return new(await ReadCommitAsync(reference, resolved.Trim(), cancellationToken), warnings);
    } catch (Win32Exception exception) {
      return baseRef is not null
        ? throw new SeedFormatException($"git could not be run: {exception.Message}", exception)
        : new(Snapshot: null, Warnings: [.. warnings, $"git could not be run ({exception.Message}), so there is no base to derive the seeds from."]);
    }
  }

  private async Task<SeedSnapshot> ReadCommitAsync(string reference, string commit, CancellationToken cancellationToken) {
    var (_, shown) = await RunAsync(["show", "-s", "--format=%h %cs", commit], timeout: null, cancellationToken);
    var info = shown.Trim().Split(' ');
    var lockText = await ShowAsync(commit, Relative(_paths.LockFile), cancellationToken);
    var (_, listing) = await RunAsync(["ls-tree", "--name-only", commit, Relative(_paths.Seeds) + "/"], timeout: null, cancellationToken);
    var layers = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      if (path.EndsWith(".yaml", StringComparison.Ordinal) && await ShowAsync(commit, path, cancellationToken) is { } text) {
        layers[Path.GetFileNameWithoutExtension(path)] = text;
      }
    }
    return new($"{reference} at {info[0]} ({info[^1]})", commit, lockText, layers);
  }

  // A file as it is at the commit; null when the commit has no such file.
  private async Task<string?> ShowAsync(string commit, string path, CancellationToken cancellationToken) {
    var (exitCode, output) = await RunAsync(["show", $"{commit}:{path}"], timeout: null, cancellationToken);
    return exitCode is 0 ? output : null;
  }

  // The path from the repository root, with the slashes git uses.
  private string Relative(string path)
    => Path.GetRelativePath(_paths.Root, path).Replace('\\', '/');

  private static string FirstLine(string output)
    => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no output";

  // Git's own words go in the output when it fails (its error stream), and only then.
  private async Task<(int ExitCode, string Output)> RunAsync(List<string> args, TimeSpan? timeout, CancellationToken cancellationToken) {
    var start = new ProcessStartInfo("git") {
      WorkingDirectory = _paths.Root,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("core.quotepath=false");
    foreach (var arg in args) {
      start.ArgumentList.Add(arg);
    }
    start.Environment["GIT_TERMINAL_PROMPT"] = "0";
    using var process = Process.Start(start)
      ?? throw new InvalidOperationException("git did not start.");
    using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    if (timeout is { } after) {
      limit.CancelAfter(after);
    }
    var output = process.StandardOutput.ReadToEndAsync(limit.Token);
    var error = process.StandardError.ReadToEndAsync(limit.Token);
    try {
      await process.WaitForExitAsync(limit.Token);
    } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
      process.Kill(entireProcessTree: true);
      return (-1, "it did not finish in time");
    }
    return (process.ExitCode, process.ExitCode is 0 ? await output : await error);
  }
}
