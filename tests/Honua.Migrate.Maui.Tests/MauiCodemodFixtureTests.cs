using Honua.Migrate.Maui;

namespace Honua.Migrate.Maui.Tests;

/// <summary>
/// End-to-end before/after fixture coverage. Each pair under Fixtures/ is a
/// realistic MAUI source file (<c>*.before.cs.txt</c>) and its expected Honua
/// output (<c>*.after.cs.txt</c>). The codemod is run over the "before" file and
/// the result must match the "after" file exactly (line endings normalized).
/// </summary>
public sealed class MauiCodemodFixtureTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static IEnumerable<object[]> FixturePairs()
    {
        foreach (var before in Directory.EnumerateFiles(FixturesDir, "*.before.cs.txt"))
        {
            var name = Path.GetFileName(before).Replace(".before.cs.txt", string.Empty);
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(FixturePairs))]
    public void Fixture_Before_Transforms_To_After(string fixtureName)
    {
        var before = File.ReadAllText(Path.Combine(FixturesDir, $"{fixtureName}.before.cs.txt"));
        var expectedAfter = File.ReadAllText(Path.Combine(FixturesDir, $"{fixtureName}.after.cs.txt"));

        var dir = Path.Combine(Path.GetTempPath(), "honua-maui-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, $"{fixtureName}.cs");
            File.WriteAllText(file, before);

            MauiCodemod.Run(new MauiCodemodOptions { RootDir = dir, Write = true });

            var actual = File.ReadAllText(file);
            Assert.Equal(Normalize(expectedAfter), Normalize(actual));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n');
}
