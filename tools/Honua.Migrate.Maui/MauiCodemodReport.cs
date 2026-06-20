using System.Text;

namespace Honua.Migrate.Maui;

/// <summary>Renders a human-readable parity report for a codemod run.</summary>
public static class MauiCodemodReport
{
    public static string Render(MauiCodemodResult result, bool wrote)
    {
        var sb = new StringBuilder();
        sb.AppendLine("honua-migrate maui — ArcGIS Maps SDK for .NET (MAUI) -> Honua");
        sb.AppendLine(new string('=', 62));
        sb.AppendLine($"root:            {result.RootDir}");
        sb.AppendLine($"files scanned:   {result.FilesScanned}");
        sb.AppendLine($"files changed:   {result.FilesChanged}{(wrote ? " (written)" : " (dry run)")}");
        sb.AppendLine();

        var m = result.Metrics;
        sb.AppendLine("call sites");
        sb.AppendLine($"  total recognized: {m.TotalCodemodScopedCallSites}");
        sb.AppendLine($"  auto-migrated:    {m.AutoMigratedCallSites}");
        sb.AppendLine($"  manual review:    {m.ManualCallSites}");
        sb.AppendLine();

        var kinded = m.ByKind
            .Where(kvp => kvp.Value.Total > 0)
            .OrderBy(kvp => kvp.Key.ToString(), StringComparer.Ordinal)
            .ToList();

        if (kinded.Count > 0)
        {
            sb.AppendLine("by construct");
            foreach (var (kind, metrics) in kinded)
            {
                sb.AppendLine(
                    $"  {kind,-22} total={metrics.Total} auto={metrics.AutoMigrated} manual={metrics.Manual}");
            }

            sb.AppendLine();
        }

        if (result.ManualTodos.Count > 0)
        {
            sb.AppendLine($"manual review markers ({result.ManualTodos.Count})");
            foreach (var todo in result.ManualTodos)
            {
                sb.AppendLine(
                    $"  {todo.File}:{todo.Line}:{todo.Column} [{todo.Kind}/{todo.Difficulty}] {todo.Reason}");
            }
        }

        return sb.ToString();
    }
}
