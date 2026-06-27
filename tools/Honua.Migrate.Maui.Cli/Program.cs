using Honua.Migrate.Maui;

// honua-migrate maui — CLI front-end over the MAUI codemod core.
//
// Usage:
//   honua-migrate-maui <path> [--write] [--annotate-todos] [--fail-on-manual]
//
//   <path>            Directory tree or single .cs file to scan.
//   --write           Apply rewrites to disk (default: dry run / report only).
//   --annotate-todos  Insert inline // TODO(honua-migrate) comments for
//                     recognized-but-manual constructs.
//   --fail-on-manual  Exit non-zero if any manual-review markers were emitted
//                     (useful as a CI gate during a staged migration).

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "usage: honua-migrate-maui <path> [--write] [--annotate-todos] [--fail-on-manual]");
    return 2;
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(
        "usage: honua-migrate-maui <path> [--write] [--annotate-todos] [--fail-on-manual]");
    Console.WriteLine();
    Console.WriteLine("Scans a .NET MAUI project for ArcGIS Maps SDK call sites and rewrites");
    Console.WriteLine("the high-frequency MapView/Map/layer/graphics/geometry surface to the");
    Console.WriteLine("Honua mobile SDK, emitting TODO(honua-migrate) markers for the rest.");
    return args.Length == 0 ? 2 : 0;
}

var path = args[0];
var write = false;
var annotateTodos = false;
var failOnManual = false;

foreach (var arg in args.Skip(1))
{
    switch (arg)
    {
        case "--write":
            write = true;
            break;
        case "--annotate-todos":
            annotateTodos = true;
            break;
        case "--fail-on-manual":
            failOnManual = true;
            break;
        default:
            return Fail($"unknown argument '{arg}'");
    }
}

if (!File.Exists(path) && !Directory.Exists(path))
{
    return Fail($"path not found: {path}");
}

var result = MauiCodemod.Run(new MauiCodemodOptions
{
    RootDir = path,
    Write = write,
    AnnotateTodos = annotateTodos,
});

Console.WriteLine(MauiCodemodReport.Render(result, wrote: write));

if (failOnManual && result.Metrics.ManualCallSites > 0)
{
    return 1;
}

return 0;
