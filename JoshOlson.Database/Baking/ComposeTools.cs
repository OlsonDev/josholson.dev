using System.Diagnostics;
using System.Globalization;

namespace JoshOlson.Database.Baking;

// Runs commands in the dev compose file's containers, from the repository root: Waypoint in `tools` (it has no Windows
// build, so it does not run on the host) and `psql` in `postgres`. Both read the owner's password from the POSTGRES_*
// settings in `.env`, which the mise tasks load.
public sealed class ComposeTools {
  private readonly string _root;

  public ComposeTools(string root)
    => _root = root;

  // Applies the migrations to a database that already exists. The wrapper in the image builds its connection URL from
  // POSTGRES_DB, so the variable names the database.
  public async Task MigrateAsync(string database, CancellationToken cancellationToken)
    => _ = await RunAsync(["exec", "-T", "-e", $"POSTGRES_DB={database}", "tools", "waypoint", "migrate"], stdin: null, cancellationToken);

  // The same, from a directory of migrations inside the container (`PutMigrationsAsync`) instead of the mounted ones: the
  // wrapper reads WAYPOINT_LOCATIONS.
  public async Task MigrateAsync(string database, string locations, CancellationToken cancellationToken)
    => _ = await RunAsync(
      ["exec", "-T", "-e", $"POSTGRES_DB={database}", "-e", $"WAYPOINT_LOCATIONS={locations}", "tools", "waypoint", "migrate"],
      stdin: null,
      cancellationToken);

  // Writes the files into a new directory in the `tools` container and returns its path there. The bytes go through
  // base64, so a file arrives exactly as it is on disk (Waypoint's checksum of it is the same as of the mounted copy).
  public async Task<string> PutMigrationsAsync(List<(string Name, byte[] Content)> files, CancellationToken cancellationToken) {
    var directory = $"/tmp/seed-migrations-{Guid.NewGuid():N}";
    var lines = string.Join('\n', files.Select(file => $"{file.Name} {Convert.ToBase64String(file.Content)}")) + "\n";
    _ = await RunAsync(
      [
        "exec",
        "-T",
        "tools",
        "sh",
        "-c",
        "set -e; mkdir -p \"$1\"; while read -r name data; do printf '%s' \"$data\" | base64 -d > \"$1/$name\"; done",
        "sh",
        directory,
      ],
      lines,
      cancellationToken);
    return directory;
  }

  public async Task RemoveMigrationsAsync(string directory, CancellationToken cancellationToken)
    => _ = await RunAsync(["exec", "-T", "tools", "rm", "-rf", directory], stdin: null, cancellationToken);

  // Runs a SQL file with psql as the owner, stopping at the first error. A psql variable is how the API role's
  // password gets in (`:'api_password'`).
  public async Task RunScriptAsync(
    string database,
    string script,
    Dictionary<string, string> variables,
    CancellationToken cancellationToken
  ) {
    var args = new List<string> {
      "exec",
      "-T",
      "postgres",
      "psql",
      "-U",
      Setting("POSTGRES_USER", "josholson"),
      "-d",
      database,
      "-v",
      "ON_ERROR_STOP=1",
    };
    foreach (var (name, value) in variables) {
      args.Add("-v");
      args.Add($"{name}={value}");
    }
    _ = await RunAsync(args, await File.ReadAllTextAsync(script, cancellationToken), cancellationToken);
  }

  private static string Setting(string name, string fallback)
    => Environment.GetEnvironmentVariable(name) ?? fallback;

  private async Task<string> RunAsync(List<string> compose, string? stdin, CancellationToken cancellationToken) {
    var start = new ProcessStartInfo("podman") {
      WorkingDirectory = _root,
      UseShellExecute = false,
      RedirectStandardInput = stdin is not null,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (var arg in (string[])["compose", "-f", "compose.yaml", "-f", "compose.dev.yaml", .. compose]) {
      start.ArgumentList.Add(arg);
    }
    using var process = Process.Start(start)
      ?? throw new InvalidOperationException("podman did not start.");
    if (stdin is not null) {
      await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
      process.StandardInput.Close();
    }
    var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var error = process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    var text = await output + await error;
    return process.ExitCode is 0
      ? text
      : throw new InvalidOperationException(
        $"podman compose {string.Join(' ', compose)} exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}:\n{text}");
  }
}
