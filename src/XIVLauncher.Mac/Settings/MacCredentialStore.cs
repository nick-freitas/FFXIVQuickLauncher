using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace XIVLauncher.Mac.Settings;

public interface IMacCredentialStore
{
    Task<string?> GetPasswordAsync(string username, CancellationToken cancellationToken = default);

    Task SavePasswordAsync(string username, string password, CancellationToken cancellationToken = default);
}

public sealed class MacKeychainCredentialStore : IMacCredentialStore
{
    private const string ServiceName = "XIVLauncherMac";
    private const string SecurityCommand = "/usr/bin/security";

    public async Task<string?> GetPasswordAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var result = await RunSecurityAsync(
            [
                "find-generic-password",
                "-a",
                username,
                "-s",
                ServiceName,
                "-w",
            ],
            cancellationToken);

        return result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\r', '\n') : null;
    }

    public async Task SavePasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return;

        var result = await RunSecurityAsync(
            [
                "add-generic-password",
                "-a",
                username,
                "-s",
                ServiceName,
                "-w",
                password,
                "-U",
            ],
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Could not save password to macOS Keychain: {result.StandardError.Trim()}");
    }

    private static async Task<SecurityResult> RunSecurityAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(SecurityCommand)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new SecurityResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record SecurityResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed class NullMacCredentialStore : IMacCredentialStore
{
    public Task<string?> GetPasswordAsync(string username, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task SavePasswordAsync(string username, string password, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
