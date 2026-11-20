using System.ComponentModel;
using System.Diagnostics;

// What `bun fmt` would change in the web files a commit stages (ADR 0045, Tailwind CSS and shadcn-svelte: the formatter
// also sorts imports). It checks the staged content, not the working file, so a commit that stages only some of a file's
// changes is judged by what it commits.
internal static class WebFormatCheck {
  private static readonly string[] _Extensions = [
    ".ts",
    ".js",
    ".svelte",
    ".css",
    ".html",
    ".json",
  ];

  // The staged paths `bun fmt` formats, relative to web/.
  public static List<string> Sources(IEnumerable<string> stagedPaths) {
    var sources = new List<string>();
    foreach (var path in stagedPaths) {
      if (!path.StartsWith("web/", StringComparison.Ordinal)) continue;
      if (_Extensions.Contains(Path.GetExtension(path), StringComparer.Ordinal)) sources.Add(path["web/".Length..]);
    }
    return sources;
  }

  // The paths, relative to web/, whose staged content `bun fmt` would change. Throws if bun cannot be run.
  public static List<string> Unformatted(IEnumerable<string> sources) {
    var unformatted = new List<string>();
    foreach (var source in sources) {
      var staged = Run("git", ".", input: null, "show", $":web/{source}");
      var formatted = Run("bun", "web", staged, "fmt", "--no-script", "--stdin-filepath", source);
      if (!string.Equals(staged, formatted, StringComparison.Ordinal)) unformatted.Add(source);
    }
    return unformatted;
  }

  private static string Run(string command, string workingDirectory, string? input, params string[] arguments) {
    var startInfo = new ProcessStartInfo(command) {
      WorkingDirectory = workingDirectory,
      RedirectStandardInput = input is not null,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    try {
      using var process = Process.Start(startInfo)!;
      var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
      var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
      if (input is not null) {
        process.StandardInput.Write(input);
        process.StandardInput.Close();
      }
      process.WaitForExit();
      if (process.ExitCode is not 0) {
        throw new InvalidOperationException($"{command} {string.Join(' ', arguments)} failed: {error.GetAwaiter().GetResult()}");
      }
      return output.GetAwaiter().GetResult();
    } catch (Win32Exception exception) {
      throw new InvalidOperationException($"{command} is not on the PATH, so the formatting of staged web files cannot be checked.", exception);
    }
  }
}
