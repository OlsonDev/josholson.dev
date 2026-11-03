namespace JoshOlson.Api.Auth;

// `JoshOlson.Api enroll [display name]` (ADR 0027, bootstrap and recovery): mints a one-time enrollment token on the
// server and prints it. The display name is only used when no account exists yet. Run it again to recover when every
// passkey is lost.
public static class EnrollCommand {
  public const string Name = "enroll";

  public static async Task<int> RunAsync(
    IServiceProvider services,
    string[] arguments,
    TextWriter output,
    CancellationToken cancellationToken
  ) {
    await using var scope = services.CreateAsyncScope();
    var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
    var token = await enrollmentService.CreateTokenAsync(arguments.FirstOrDefault(), cancellationToken);
    await output.WriteLineAsync("Enrollment token (one use, shown once):");
    await output.WriteLineAsync(token);
    return 0;
  }
}
