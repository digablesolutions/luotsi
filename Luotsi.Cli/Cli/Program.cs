namespace Luotsi.Cli.Cli;

/// <summary>
/// Console program entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the CLI.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        using var app = new App();
        return await app.RunAsync(args).ConfigureAwait(false);
    }
}
