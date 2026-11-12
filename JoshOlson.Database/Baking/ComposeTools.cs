using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace JoshOlson.Database.Baking;

// Runs commands in the dev compose file's containers: Waypoint in `tools` (it has no Windows build, so it does not run on
// the host) and `psql` in `postgres`. Both read the owner's password from the POSTGRES_* settings in `.env`, which the
// mise tasks load.
//
// A command goes straight to the container with `podman exec`, which takes about half as long as `podman compose exec`
// (the compose provider is a Python program that works out the project again on every call). The container is found once,
// by the labels compose gives it, and remembered. Every call is a round trip, so a step that is several commands runs as
// one script inside the container where it can (`MigrateAsync` with files, `RunScriptsAsync`).
public sealed class ComposeTools {
  // Writes the files it is sent (a name and base64 per line, so each arrives exactly as it is on disk and Waypoint's
  // checksum of it is the same as of the mounted copy) into a directory of its own, runs Waypoint on it, and removes the
  // directory whether Waypoint succeeded or not. The arguments after the script are Waypoint's own (`--transaction`).
  // Waypoint's wrapper builds its connection URL from POSTGRES_DB and reads the migrations from WAYPOINT_LOCATIONS.
  private const string _MigrateFilesScript = """
    set -e
    d=$(mktemp -d /tmp/seed-migrations-XXXXXX)
    trap 'rm -rf "$d"' EXIT
    while read -r name data; do printf '%s' "$data" | base64 -d > "$d/$name"; done
    WAYPOINT_LOCATIONS="$d" waypoint migrate "$@"
    """;

  private readonly string _root;
  private readonly ConcurrentDictionary<string, string> _containers = [with(StringComparer.Ordinal)];

  public ComposeTools(string root)
    => _root = root;

  // Applies the mounted migrations to a database that already exists. The wrapper in the image builds its connection URL
  // from POSTGRES_DB, so the variable names the database.
  public async Task MigrateAsync(string database, CancellationToken cancellationToken)
    => _ = await ExecAsync("tools", [$"POSTGRES_DB={database}"], ["waypoint", "migrate"], stdin: null, cancellationToken);

  // The same, from files the caller has instead of the mounted ones, in one round trip. With `transaction` everything
  // pending is applied in one transaction, so a migration that fails leaves nothing of itself, not even a failed row in
  // Waypoint's history (which it would otherwise want `waypoint repair` for).
  public async Task MigrateAsync(
    string database,
    List<(string Name, byte[] Content)> files,
    bool transaction,
    CancellationToken cancellationToken
  ) {
    List<string> command = [
      "sh",
      "-c",
      _MigrateFilesScript,
      "sh",
    ];
    if (transaction) {
      command.Add("--transaction");
    }
    var lines = string.Join('\n', files.Select(file => $"{file.Name} {Convert.ToBase64String(file.Content)}")) + "\n";
    _ = await ExecAsync("tools", [$"POSTGRES_DB={database}"], command, lines, cancellationToken);
  }

  // Runs a SQL file with psql as the owner, stopping at the first error. A psql variable is how the API role's
  // password gets in (`:'api_password'`).
  public async Task RunScriptAsync(
    string database,
    string script,
    Dictionary<string, string> variables,
    CancellationToken cancellationToken
  )
    => await RunScriptsAsync(database, [script], variables, cancellationToken);

  // Several SQL files in one psql session, in order, so they are one round trip and share the variables.
  public async Task RunScriptsAsync(
    string database,
    List<string> scripts,
    Dictionary<string, string> variables,
    CancellationToken cancellationToken
  ) {
    List<string> command = [
      "psql",
      "-U",
      Setting("POSTGRES_USER", "josholson"),
      "-d",
      database,
      "-v",
      "ON_ERROR_STOP=1",
    ];
    foreach (var (name, value) in variables) {
      command.Add("-v");
      command.Add($"{name}={value}");
    }
    var texts = new List<string>(scripts.Count);
    foreach (var script in scripts) {
      texts.Add(await File.ReadAllTextAsync(script, cancellationToken));
    }
    _ = await ExecAsync("postgres", [], command, string.Join('\n', texts), cancellationToken);
  }

  private static string Setting(string name, string fallback)
    => Environment.GetEnvironmentVariable(name) ?? fallback;

  private async Task<string> ExecAsync(
    string service,
    List<string> environment,
    List<string> command,
    string? stdin,
    CancellationToken cancellationToken
  ) {
    var container = await ContainerAsync(service, cancellationToken);
    List<string> args = ["exec"];
    if (stdin is not null) {
      args.Add("-i");
    }
    foreach (var variable in environment) {
      args.Add("-e");
      args.Add(variable);
    }
    args.Add(container);
    args.AddRange(command);
    return await RunPodmanAsync(args, stdin, cancellationToken);
  }

  // The running container of a compose service, which the compose file's project gave labels. Found by the repository's
  // directory first, and by the service alone if that matches nothing (a checkout reached through a link).
  private async Task<string> ContainerAsync(string service, CancellationToken cancellationToken) {
    if (_containers.TryGetValue(service, out var known)) {
      return known;
    }
    var byService = $"label=com.docker.compose.service={service}";
    var found = await FindAsync([byService, $"label=com.docker.compose.project.working_dir={_root}"], cancellationToken);
    if (found.Count is 0) {
      found = await FindAsync([byService], cancellationToken);
    }
    var container = found.Count is 1
      ? found[0]
      : throw new InvalidOperationException(
        found.Count is 0
          ? $"The {service} container is not running. Start it with mise run tools:up."
          : $"More than one {service} container is running, and none is the repository's. Stop the others.");
    _containers[service] = container;
    return container;
  }

  private async Task<List<string>> FindAsync(List<string> filters, CancellationToken cancellationToken) {
    List<string> args = [
      "ps",
      "-q",
      "--filter",
      "status=running",
    ];
    foreach (var filter in filters) {
      args.Add("--filter");
      args.Add(filter);
    }
    var output = await RunPodmanAsync(args, stdin: null, cancellationToken);
    return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
  }

  private async Task<string> RunPodmanAsync(List<string> args, string? stdin, CancellationToken cancellationToken) {
    var start = new ProcessStartInfo("podman") {
      WorkingDirectory = _root,
      UseShellExecute = false,
      RedirectStandardInput = stdin is not null,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (var arg in args) {
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
        $"podman {string.Join(' ', Shown(args))} exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}:\n{text}");
  }

  // The command as it is reported when it fails, without a psql variable's value (`-v api_password=...` is the API role's
  // password, which belongs in no log or test output).
  private static List<string> Shown(List<string> args) {
    var shown = new List<string>(args.Count);
    for (var i = 0; i < args.Count; i++) {
      shown.Add(i > 0 && args[i - 1] is "-v" && args[i].Contains('=', StringComparison.Ordinal) ? $"{args[i][..args[i].IndexOf('=', StringComparison.Ordinal)]}=***" : args[i]);
    }
    return shown;
  }
}
