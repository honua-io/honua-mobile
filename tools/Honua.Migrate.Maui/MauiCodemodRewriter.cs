using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Honua.Migrate.Maui;

/// <summary>
/// Roslyn syntax rewriter that translates ArcGIS Maps SDK for .NET (MAUI) call
/// sites into their Honua mobile SDK equivalents. This is the .NET analogue of
/// the TypeScript-compiler rewriter used by the JS honua-migrate codemod: it
/// operates purely on syntax (no compilation / type resolution required) so it
/// runs on any platform without the MAUI workload.
///
/// Recognition is import-scoped: a bare <c>new FeatureLayer(...)</c> is only
/// rewritten when an ArcGIS <c>using</c> brings that simple name into scope, and
/// fully-qualified <c>new Esri.ArcGISRuntime.Mapping.FeatureLayer(...)</c> is
/// always recognized. This avoids touching unrelated user types.
/// </summary>
internal sealed class MauiCodemodRewriter : CSharpSyntaxRewriter
{
    internal const string TodoMarker = "TODO(honua-migrate)";

    private readonly string _file;
    private readonly bool _annotateTodos;
    private readonly IReadOnlySet<string> _importedArcGisSimpleNames;

    private readonly List<MigrationTodo> _manualTodos = new();
    private readonly List<MauiConstructKind> _autoMigratedKinds = new();
    private readonly HashSet<string> _requiredHonuaNamespaces = new(StringComparer.Ordinal);

    private int _rewrittenConstructors;
    private int _annotatedTodoComments;

    public MauiCodemodRewriter(
        string file,
        bool annotateTodos,
        IReadOnlySet<string> importedArcGisSimpleNames)
    {
        _file = file;
        _annotateTodos = annotateTodos;
        _importedArcGisSimpleNames = importedArcGisSimpleNames;
    }

    public IReadOnlyList<MigrationTodo> ManualTodos => _manualTodos;
    public IReadOnlyList<MauiConstructKind> AutoMigratedKinds => _autoMigratedKinds;
    public IReadOnlySet<string> RequiredHonuaNamespaces => _requiredHonuaNamespaces;
    public int RewrittenConstructors => _rewrittenConstructors;
    public int AnnotatedTodoComments => _annotatedTodoComments;

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var (simpleName, isQualified) = ExtractTypeName(node.Type);
        if (simpleName is null)
        {
            return base.VisitObjectCreationExpression(node);
        }

        var spec = MauiMappingTable.TryGetByArcGisTypeName(simpleName);
        if (spec is null)
        {
            return base.VisitObjectCreationExpression(node);
        }

        // Bare names must have been brought in by an ArcGIS using directive.
        // Fully-qualified Esri.ArcGISRuntime.* names are always recognized.
        if (!isQualified && !_importedArcGisSimpleNames.Contains(simpleName))
        {
            return base.VisitObjectCreationExpression(node);
        }

        // Recurse first so nested constructions (e.g. graphics inside an
        // overlay) are still visited.
        var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;

        if (spec.Mode == MauiRewriteMode.GuidedManual)
        {
            RecordManualTodo(node, spec, MigrationTodoDifficulty.Moderate);
            return _annotateTodos ? AttachTodoTrivia(visited, spec) : visited;
        }

        // Deterministic 1:1 constructor rewrite.
        _rewrittenConstructors++;
        _autoMigratedKinds.Add(spec.Kind);
        _requiredHonuaNamespaces.Add(spec.HonuaNamespace);

        var newTypeSyntax = SyntaxFactory
            .ParseTypeName(spec.HonuaTypeName)
            .WithTriviaFrom(visited.Type);

        return visited.WithType(newTypeSyntax);
    }

    private void RecordManualTodo(
        ObjectCreationExpressionSyntax node,
        MauiRewriteSpec spec,
        MigrationTodoDifficulty difficulty)
    {
        var pos = node.GetLocation().GetLineSpan().StartLinePosition;
        _manualTodos.Add(new MigrationTodo(
            spec.Kind,
            _file,
            pos.Line + 1,
            pos.Character + 1,
            spec.Note ?? $"{spec.ArcGisTypeName} requires manual migration to {spec.HonuaTypeName}.",
            difficulty));
    }

    private ObjectCreationExpressionSyntax AttachTodoTrivia(
        ObjectCreationExpressionSyntax node,
        MauiRewriteSpec spec)
    {
        var reason = spec.Note ?? $"{spec.ArcGisTypeName} requires manual migration.";
        var commentText = $"// {TodoMarker}[{spec.Kind}]: {reason}";

        var leading = node.GetLeadingTrivia();

        // Avoid double-annotating an already-marked call site.
        if (leading.ToFullString().Contains(TodoMarker, StringComparison.Ordinal))
        {
            return node;
        }

        _annotatedTodoComments++;

        var newLeading = leading
            .Add(SyntaxFactory.Comment(commentText))
            .Add(SyntaxFactory.ElasticCarriageReturnLineFeed);

        return node.WithLeadingTrivia(newLeading);
    }

    /// <summary>
    /// Returns the simple type name of an <c>ObjectCreationExpression</c> and
    /// whether it was written fully qualified under an ArcGIS namespace.
    /// </summary>
    private static (string? SimpleName, bool IsQualified) ExtractTypeName(TypeSyntax type)
    {
        switch (type)
        {
            case IdentifierNameSyntax id:
                return (id.Identifier.Text, false);

            case QualifiedNameSyntax qualified:
                {
                    var simple = qualified.Right.Identifier.Text;
                    var leftText = qualified.Left.ToString();
                    var isArcGis = MauiMappingTable.ArcGisNamespaces.Contains(leftText);
                    return (simple, isArcGis);
                }

            case GenericNameSyntax generic:
                return (generic.Identifier.Text, false);

            default:
                return (null, false);
        }
    }
}
