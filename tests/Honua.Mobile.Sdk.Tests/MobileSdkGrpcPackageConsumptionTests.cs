using System.Xml.Linq;

namespace Honua.Mobile.Sdk.Tests;

public sealed class MobileSdkGrpcPackageConsumptionTests
{
    [Fact]
    public void Project_ConsumesSdkGrpcPackageAndDoesNotCompileLocalProto()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Honua.Mobile.Sdk", "Honua.Mobile.Sdk.csproj"));
        var packageIds = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("Honua.Sdk.Grpc", packageIds);
        Assert.DoesNotContain("Google.Protobuf", packageIds);
        Assert.DoesNotContain("Grpc.Tools", packageIds);
        Assert.Empty(project.Descendants("Protobuf"));
        Assert.False(File.Exists(Path.Combine(root, "proto", "honua", "v1", "feature_service.proto")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Mobile.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Honua.Mobile.sln from the test output directory.");
    }
}
