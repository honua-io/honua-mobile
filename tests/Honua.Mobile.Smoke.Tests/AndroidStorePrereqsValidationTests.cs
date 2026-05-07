using System.Diagnostics;

namespace Honua.Mobile.Smoke.Tests;

public sealed class AndroidStorePrereqsValidationTests
{
    [Fact]
    public void ValidationScript_PassesCurrentRepo()
    {
        if (!IsBashAvailable())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var result = RunValidationScript(root);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("workflow mapping", result.Output);
    }

    [Fact]
    public void ValidationScript_RejectsSwappedWorkflowMapping()
    {
        if (!IsBashAvailable())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "android-internal-distribution.yml");
        var workflow = File.ReadAllText(workflowPath);
        var swappedWorkflow = workflow.Replace(
            "package_name=\"io.honua.mobile.fieldcollection\"",
            "package_name=\"io.honua.mobile.app\"",
            StringComparison.Ordinal);

        Assert.NotEqual(workflow, swappedWorkflow);

        var tempWorkflowPath = Path.Combine(Path.GetTempPath(), $"honua-android-workflow-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(tempWorkflowPath, swappedWorkflow);

            var result = RunValidationScript(root, new Dictionary<string, string?>
            {
                ["HONUA_ANDROID_STORE_WORKFLOW"] = tempWorkflowPath,
            });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("maps 'field-collection' without package 'io.honua.mobile.fieldcollection'", result.Output);
        }
        finally
        {
            if (File.Exists(tempWorkflowPath))
            {
                File.Delete(tempWorkflowPath);
            }
        }
    }

    private static ScriptResult RunValidationScript(
        string root,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var scriptPath = Path.Combine(root, "scripts", "validate-android-store-prereqs.sh");
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(scriptPath);

        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Android store prereq validation script.");
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
