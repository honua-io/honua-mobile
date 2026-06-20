namespace Honua.Migrate.Maui;

/// <summary>How hard a flagged manual migration is expected to be.</summary>
public enum MigrationTodoDifficulty
{
    Trivial,
    Moderate,
    Complex,
}

/// <summary>
/// A recognized ArcGIS construct that the codemod could not safely rewrite
/// automatically and surfaced as a <c>TODO(honua-migrate)</c> review marker.
/// </summary>
public sealed record MigrationTodo(
    MauiConstructKind Kind,
    string File,
    int Line,
    int Column,
    string Reason,
    MigrationTodoDifficulty Difficulty);

/// <summary>Per-kind tallies (mirrors the JS codemod's <c>CodemodKindMetrics</c>).</summary>
public sealed record CodemodKindMetrics(int Total, int AutoMigrated, int Manual);

/// <summary>Aggregate counters across the whole run.</summary>
public sealed record CodemodMetrics(
    int TotalCodemodScopedCallSites,
    int AutoMigratedCallSites,
    int ManualCallSites,
    IReadOnlyDictionary<MauiConstructKind, CodemodKindMetrics> ByKind);

/// <summary>The outcome of running the codemod over a single source file.</summary>
public sealed record CodemodFileResult(
    string File,
    string OriginalSource,
    string TransformedSource,
    int RewrittenConstructors,
    int RewrittenUsings,
    int AnnotatedTodoComments,
    IReadOnlyList<MigrationTodo> ManualTodos)
{
    public bool Changed =>
        RewrittenConstructors > 0 || RewrittenUsings > 0 || AnnotatedTodoComments > 0;
}

/// <summary>The aggregate result of a codemod run over a directory tree.</summary>
public sealed record MauiCodemodResult(
    string RootDir,
    int FilesScanned,
    int FilesChanged,
    CodemodMetrics Metrics,
    IReadOnlyList<CodemodFileResult> FileResults,
    IReadOnlyList<MigrationTodo> ManualTodos);
