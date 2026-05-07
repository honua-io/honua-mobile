using System.Diagnostics;

namespace Honua.Mobile.Smoke.Tests;

public sealed class PlatformSmokeScriptTests
{
    [Theory]
    [InlineData("run-android-platform-smoke.sh", "Skipping Android live Honua platform smoke")]
    [InlineData("run-ios-platform-smoke.sh", "Skipping iOS live Honua platform smoke")]
    public void PlatformSmokeScript_SkipsWhenLiveHonuaConfigIsMissing(string scriptName, string expectedMessage)
    {
        if (!IsBashAvailable())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var result = RunScript(root, scriptName);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expectedMessage, result.Output);
    }

    private static ScriptResult RunScript(string root, string scriptName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(root, "scripts", scriptName));
        startInfo.Environment.Remove("HONUA_MOBILE_SMOKE_BASE_URL");
        startInfo.Environment.Remove("HONUA_MOBILE_SMOKE_SERVICE_ID");
        startInfo.Environment.Remove("HONUA_MOBILE_SMOKE_LAYER_ID");
        startInfo.Environment.Remove("HONUA_MOBILE_SMOKE_API_KEY");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {scriptName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, stdout + stderr);
    }

    private static bool IsBashAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var agentsPath = Path.Combine(directory.FullName, "AGENTS.md");
            var docsPath = Path.Combine(directory.FullName, "docs");

            if (File.Exists(agentsPath) && Directory.Exists(docsPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    private sealed record ScriptResult(int ExitCode, string Output);
}
