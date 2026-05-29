using System.Buffers;
using System.Diagnostics;
using System.Text;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Infrastructure.Processes;

public sealed class DefaultProcessRunner : IProcessRunner
{
    private const int MaxCapturedOutputChars = 4 * 1024 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
    var stdout = ReadBoundedToEndAsync(process.StandardOutput, MaxCapturedOutputChars, cancellationToken);
    var stderr = ReadBoundedToEndAsync(process.StandardError, MaxCapturedOutputChars, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process may exit between the HasExited check and Kill.
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static async Task<string> ReadBoundedToEndAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            var builder = new StringBuilder(Math.Min(maxChars, 4096));
            long truncatedChars = 0;
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (builder.Length < maxChars)
                {
                    var copyCount = Math.Min(read, maxChars - builder.Length);
                    builder.Append(buffer, 0, copyCount);
                    truncatedChars += read - copyCount;
                    continue;
                }

                truncatedChars += read;
            }

            if (truncatedChars == 0)
            {
                return builder.ToString();
            }

            builder.AppendLine();
            builder.Append($"[truncated {truncatedChars} chars]");
            return builder.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
