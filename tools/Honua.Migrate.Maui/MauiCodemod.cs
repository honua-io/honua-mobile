using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Honua.Migrate.Maui;

/// <summary>Options controlling a codemod run.</summary>
public sealed record MauiCodemodOptions
{
    /// <summary>Root directory (or single .cs file) to scan.</summary>
    public required string RootDir { get; init; }

    /// <summary>When true, transformed sources are written back to disk.</summary>
    public bool Write { get; init; }

    /// <summary>
    /// When true, recognized-but-not-auto-migratable constructs get an inline
    /// <c>// TODO(honua-migrate)</c> comment in addition to being reported.
    /// </summary>
    public bool AnnotateTodos { get; init; }
}

/// <summary>
/// Entry point for the ArcGIS Maps SDK for .NET (MAUI) → Honua codemod. Mirrors
/// the JS <c>runEsriCompatCodemod</c> contract: collect source files, rewrite
/// recognized constructs, optionally write changes, and return a structured
/// parity report with auto/manual metrics and <c>TODO(honua-migrate)</c> markers.
/// </summary>
public static class MauiCodemod
{
    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    public static MauiCodemodResult Run(MauiCodemodOptions options)
    {
        var rootDir = Path.GetFullPath(options.RootDir);
        var files = CollectSourceFiles(rootDir);

        var byKind = CreateEmptyByKind();
        var fileResults = new List<CodemodFileResult>();
        var allTodos = new List<MigrationTodo>();
        var totalAuto = 0;
        var totalManual = 0;

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var fileResult = CodemodFile(file, source, options.AnnotateTodos);

            foreach (var todo in fileResult.ManualTodos)
            {
                var prev = byKind[todo.Kind];
                byKind[todo.Kind] = prev with { Total = prev.Total + 1, Manual = prev.Manual + 1 };
                totalManual++;
            }

            // Auto-migrated call sites are not individually kinded on the file
            // result, so attribute them via the rewriter's per-run tally below.
            foreach (var kind in fileResult.AutoMigratedKinds)
            {
                var prev = byKind[kind];
                byKind[kind] = prev with { Total = prev.Total + 1, AutoMigrated = prev.AutoMigrated + 1 };
                totalAuto++;
            }

            allTodos.AddRange(fileResult.ManualTodos);

            if (fileResult.Changed && options.Write)
            {
                File.WriteAllText(file, fileResult.TransformedSource);
            }

            if (fileResult.Changed || fileResult.ManualTodos.Count > 0)
            {
                fileResults.Add(fileResult.ToPublic());
            }
        }

        var metrics = new CodemodMetrics(
            TotalCodemodScopedCallSites: totalAuto + totalManual,
            AutoMigratedCallSites: totalAuto,
            ManualCallSites: totalManual,
            ByKind: byKind);

        return new MauiCodemodResult(
            RootDir: rootDir,
            FilesScanned: files.Count,
            FilesChanged: fileResults.Count(r => r.Changed),
            Metrics: metrics,
            FileResults: fileResults.OrderBy(r => r.File, StringComparer.Ordinal).ToList(),
            ManualTodos: allTodos
                .OrderBy(t => t.File, StringComparer.Ordinal)
                .ThenBy(t => t.Line)
                .ThenBy(t => t.Column)
                .ToList());
    }

    private static InternalFileResult CodemodFile(string file, string source, bool annotateTodos)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: file);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var importedArcGisNames = CollectImportedArcGisSimpleNames(root);

        var rewriter = new MauiCodemodRewriter(file, annotateTodos, importedArcGisNames);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root)!;

        var rewrittenUsings = 0;
        if (rewriter.RewrittenConstructors > 0)
        {
            (newRoot, rewrittenUsings) = RewriteUsings(newRoot, rewriter.RequiredHonuaNamespaces);
        }

        var transformed = newRoot.ToFullString();

        return new InternalFileResult(
            File: file,
            OriginalSource: source,
            TransformedSource: transformed,
            RewrittenConstructors: rewriter.RewrittenConstructors,
            RewrittenUsings: rewrittenUsings,
            AnnotatedTodoComments: rewriter.AnnotatedTodoComments,
            ManualTodos: rewriter.ManualTodos,
            AutoMigratedKinds: rewriter.AutoMigratedKinds);
    }

    /// <summary>
    /// Replaces ArcGIS <c>using</c> directives with the Honua namespaces actually
    /// required by the rewritten constructors, de-duplicating and keeping the
    /// remaining (non-ArcGIS) usings intact.
    /// </summary>
    private static (CompilationUnitSyntax Root, int RewrittenCount) RewriteUsings(
        CompilationUnitSyntax root,
        IReadOnlySet<string> requiredHonuaNamespaces)
    {
        var existingNamespaces = root.Usings
            .Select(u => u.Name?.ToString())
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        var arcGisUsings = root.Usings
            .Where(u => u.Name is not null
                && MauiMappingTable.ArcGisNamespaces.Contains(u.Name.ToString()))
            .ToList();

        if (arcGisUsings.Count == 0 && requiredHonuaNamespaces.Count == 0)
        {
            return (root, 0);
        }

        var keptUsings = root.Usings
            .Where(u => u.Name is null
                || !MauiMappingTable.ArcGisNamespaces.Contains(u.Name.ToString()))
            .ToList();

        var honuaUsings = requiredHonuaNamespaces
            .Where(ns => !existingNamespaces.Contains(ns))
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .Select(ns => SyntaxFactory
                .UsingDirective(SyntaxFactory.ParseName(ns))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
            .ToList();

        var newUsings = SyntaxFactory.List(honuaUsings.Concat(keptUsings));
        var newRoot = root.WithUsings(newUsings);

        return (newRoot, arcGisUsings.Count);
    }

    /// <summary>
    /// Collects the simple type names that an ArcGIS <c>using</c> directive brings
    /// into scope (i.e. every recognized type declared under an imported ArcGIS
    /// namespace). Used to scope bare <c>new TypeName(...)</c> rewrites.
    /// </summary>
    private static IReadOnlySet<string> CollectImportedArcGisSimpleNames(CompilationUnitSyntax root)
    {
        var importedNamespaces = root.Usings
            .Where(u => u.Alias is null && u.Name is not null)
            .Select(u => u.Name!.ToString())
            .Where(MauiMappingTable.ArcGisNamespaces.Contains)
            .ToHashSet(StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in MauiMappingTable.Specs)
        {
            if (spec.ArcGisNamespaces.Any(importedNamespaces.Contains))
            {
                names.Add(spec.ArcGisTypeName);
            }
        }

        return names;
    }

    private static List<string> CollectSourceFiles(string rootDir)
    {
        if (File.Exists(rootDir))
        {
            return rootDir.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? new List<string> { rootDir }
                : new List<string>();
        }

        if (!Directory.Exists(rootDir))
        {
            return new List<string>();
        }

        var results = new List<string>();
        CollectRecursive(rootDir, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void CollectRecursive(string dir, List<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            // Skip Roslyn-style generated files defensively.
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(file);
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (SkipDirs.Contains(name))
            {
                continue;
            }

            CollectRecursive(sub, results);
        }
    }

    private static Dictionary<MauiConstructKind, CodemodKindMetrics> CreateEmptyByKind()
    {
        var map = new Dictionary<MauiConstructKind, CodemodKindMetrics>();
        foreach (MauiConstructKind kind in Enum.GetValues<MauiConstructKind>())
        {
            map[kind] = new CodemodKindMetrics(0, 0, 0);
        }

        return map;
    }

    private sealed record InternalFileResult(
        string File,
        string OriginalSource,
        string TransformedSource,
        int RewrittenConstructors,
        int RewrittenUsings,
        int AnnotatedTodoComments,
        IReadOnlyList<MigrationTodo> ManualTodos,
        IReadOnlyList<MauiConstructKind> AutoMigratedKinds)
    {
        public bool Changed =>
            RewrittenConstructors > 0 || RewrittenUsings > 0 || AnnotatedTodoComments > 0;

        public CodemodFileResult ToPublic() => new(
            File,
            OriginalSource,
            TransformedSource,
            RewrittenConstructors,
            RewrittenUsings,
            AnnotatedTodoComments,
            ManualTodos);
    }
}
