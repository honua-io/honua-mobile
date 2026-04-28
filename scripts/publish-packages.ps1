# Honua Mobile SDK - Package Publishing Script
# Automates building and publishing NuGet packages for the SDK

param(
    [string]$Version = "0.1.0-alpha.1",
    [string]$NuGetApiKey = "",
    [string]$NuGetSource = "https://nuget.pkg.github.com/honua-io/index.json",
    [switch]$DryRun = $false,
    [switch]$IncludeTemplates = $false,
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Honua Mobile SDK Package Publishing" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Source: $NuGetSource" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "🔍 DRY RUN MODE - No packages will be published" -ForegroundColor Yellow
}

# Package definitions
$packages = @(
    @{
        Name = "Honua.Mobile.Sdk"
        Path = "src/Honua.Mobile.Sdk/Honua.Mobile.Sdk.csproj"
        Description = "Core mobile client, gRPC transport, routing, and scene metadata"
    },
    @{
        Name = "Honua.Mobile.Field"
        Path = "src/Honua.Mobile.Field/Honua.Mobile.Field.csproj"
        Description = "Field data collection forms, validation, and workflow"
    },
    @{
        Name = "Honua.Mobile.Offline"
        Path = "src/Honua.Mobile.Offline/Honua.Mobile.Offline.csproj"
        Description = "Offline GeoPackage storage and sync capabilities"
    },
    @{
        Name = "Honua.Mobile.Maui"
        Path = "src/Honua.Mobile.Maui/Honua.Mobile.Maui.csproj"
        Description = "MAUI dependency injection and platform integration helpers"
    }
)

$templatePackage = @{
    Name = "Honua.Mobile.Templates"
    Path = "templates/Honua.Mobile.Templates.nuspec"
    Description = "Visual Studio project templates"
}

function Test-Prerequisites {
    Write-Host "🔍 Checking prerequisites..." -ForegroundColor Green

    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green
    }
    catch {
        Write-Error "❌ .NET SDK not found. Please install .NET 10.0+ SDK"
        exit 1
    }

    # Check NuGet CLI
    try {
        $nugetVersion = nuget help | Select-String "NuGet Version"
        Write-Host "✅ NuGet CLI: $nugetVersion" -ForegroundColor Green
    }
    catch {
        Write-Warning "⚠️ NuGet CLI not found. Installing via dotnet tool..."
        dotnet tool install -g nuget.commandline
    }

    # Validate API key if not dry run
    if (-not $DryRun -and [string]::IsNullOrEmpty($NuGetApiKey)) {
        Write-Error "❌ GitHub Packages token is required for publishing. Use -NuGetApiKey or pass GITHUB_TOKEN/PAT through this parameter"
        exit 1
    }

    Write-Host "✅ Prerequisites check completed" -ForegroundColor Green
}

function Run-Tests {
    if ($SkipTests) {
        Write-Host "⏭️ Skipping tests" -ForegroundColor Yellow
        return
    }

    Write-Host "🧪 Running tests..." -ForegroundColor Green

    # Run all tests
    dotnet test --configuration Release --logger "console;verbosity=minimal"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Tests failed. Stopping package publishing."
        exit 1
    }

    Write-Host "✅ All tests passed" -ForegroundColor Green
}

function Build-Package {
    param($Package)

    Write-Host "📦 Building package: $($Package.Name)" -ForegroundColor Cyan

    if ($Package.Path.EndsWith(".csproj")) {
        # Build .NET project
        dotnet pack $Package.Path `
            --configuration Release `
            --output "./dist" `
            -p:PackageVersion=$Version `
            -p:Version=$Version `
            --include-symbols `
            --include-source
    }
    else {
        # Build NuGet package from nuspec
        nuget pack $Package.Path `
            -OutputDirectory "./dist" `
            -Version $Version `
            -Properties "version=$Version"
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Failed to build package: $($Package.Name)"
        exit 1
    }

    Write-Host "✅ Built package: $($Package.Name)" -ForegroundColor Green
}

function Publish-Package {
    param($PackageName)

    $packageFile = Get-ChildItem "./dist" -Filter "$PackageName.$Version.nupkg" | Select-Object -First 1

    if (-not $packageFile) {
        Write-Error "❌ Package file not found: $PackageName.$Version.nupkg"
        return
    }

    Write-Host "🚀 Publishing package: $PackageName" -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "🔍 DRY RUN: Would publish $($packageFile.FullName)" -ForegroundColor Yellow
        return
    }

    dotnet nuget push $packageFile.FullName `
        --api-key $NuGetApiKey `
        --source $NuGetSource `
        --skip-duplicate

    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Failed to publish package: $PackageName"
        exit 1
    }

    Write-Host "✅ Published package: $PackageName" -ForegroundColor Green
}

function Update-PackageVersions {
    Write-Host "🔄 Updating package versions in project files..." -ForegroundColor Green

    # Update version in all .csproj files
    Get-ChildItem -Recurse -Filter "*.csproj" | ForEach-Object {
        $content = Get-Content $_.FullName
        $updated = $content -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
        $updated = $updated -replace '<PackageVersion>[^<]+</PackageVersion>', "<PackageVersion>$Version</PackageVersion>"
        Set-Content $_.FullName $updated
    }

    # Update version in template nuspec
    if (Test-Path $templatePackage.Path) {
        $content = Get-Content $templatePackage.Path
        $updated = $content -replace '<version>[^<]+</version>', "<version>$Version</version>"
        Set-Content $templatePackage.Path $updated
    }

    Write-Host "✅ Updated package versions to $Version" -ForegroundColor Green
}

function Generate-ReleaseNotes {
    Write-Host "📝 Generating release notes..." -ForegroundColor Green

    $releaseNotes = @"
# Honua Mobile SDK v$Version

## 🚀 New Features
- Revolutionary open-source geospatial mobile SDK
- Complete field data collection capabilities
- Cross-platform support (iOS, Android, Windows)
- Professional UI components that compete with Fulcrum and Survey123

## 📦 Packages Released

### Core Libraries
- **Honua.Mobile.Sdk** v$Version - Core mobile client and gRPC transport
- **Honua.Mobile.Field** v$Version - Field forms, validation, and workflow
- **Honua.Mobile.Offline** v$Version - Offline storage and sync
- **Honua.Mobile.Maui** v$Version - MAUI platform integration

### Development Tools
- **Honua.Mobile.Templates** v$Version - Visual Studio project templates

## 📚 Documentation
- [Getting Started Guide](https://docs.honua.com/mobile/getting-started)
- [API Reference](https://docs.honua.com/mobile/api)
- [Sample Applications](https://github.com/honua/honua-mobile-sdk/tree/main/examples)

## 🆚 Competitive Advantages
- 💰 **$0 cost** vs \$1,200+/year for Fulcrum/Survey123
- 🔓 **Open source** vs vendor lock-in
- 🚀 **Native performance** vs web wrappers
- 🤖 **IoT integration** that no competitor offers
- 🥽 **AR capabilities** (first open source implementation)

## 🔧 Installation

### Quick Start with Templates
```bash
dotnet new install Honua.Mobile.Templates
dotnet new honua-fieldcollector -n MyFieldApp
```

### Manual Package Installation
```bash
dotnet add package Honua.Mobile.Sdk --version $Version
dotnet add package Honua.Mobile.Field --version $Version
dotnet add package Honua.Mobile.Offline --version $Version
dotnet add package Honua.Mobile.Maui --version $Version
```

## 🐛 Bug Reports & Support
- [GitHub Issues](https://github.com/honua/honua-mobile-sdk/issues)
- [Discord Community](https://discord.gg/honua)
- [Enterprise Support](https://enterprise.honua.com)

---

**Built with ❤️ by the Honua Community**
"@

    $releaseNotes | Out-File -FilePath "./dist/release-notes-v$Version.md" -Encoding UTF8
    Write-Host "✅ Generated release notes: ./dist/release-notes-v$Version.md" -ForegroundColor Green
}

function Main {
    # Create dist directory
    if (-not (Test-Path "./dist")) {
        New-Item -ItemType Directory -Path "./dist" | Out-Null
    }

    # Run prerequisite checks
    Test-Prerequisites

    # Update package versions
    Update-PackageVersions

    # Run tests
    Run-Tests

    # Build packages
    Write-Host "📦 Building SDK packages..." -ForegroundColor Cyan
    foreach ($package in $packages) {
        Build-Package $package
    }

    # Build template package if requested
    if ($IncludeTemplates) {
        Write-Host "📦 Building template package..." -ForegroundColor Cyan
        Build-Package $templatePackage
    }

    # Generate release notes
    Generate-ReleaseNotes

    # Publish packages
    if (-not $DryRun) {
        Write-Host "🚀 Publishing packages to $NuGetSource..." -ForegroundColor Cyan

        foreach ($package in $packages) {
            Publish-Package $package.Name
        }

        if ($IncludeTemplates) {
            Publish-Package $templatePackage.Name
        }

        Write-Host "🎉 All packages published successfully!" -ForegroundColor Green
        Write-Host "📖 View packages at: https://github.com/orgs/honua-io/packages" -ForegroundColor Cyan
    }
    else {
        Write-Host "🔍 DRY RUN COMPLETED" -ForegroundColor Yellow
        Write-Host "Packages built in ./dist directory:" -ForegroundColor Yellow
        Get-ChildItem "./dist" -Filter "*.nupkg" | ForEach-Object {
            Write-Host "  - $($_.Name)" -ForegroundColor Gray
        }
    }

    # Display package information
    Write-Host "`n📊 Package Summary:" -ForegroundColor Cyan
    Write-Host "Version: $Version" -ForegroundColor White
    Write-Host "Total Packages: $($packages.Count + $(if($IncludeTemplates){1}else{0}))" -ForegroundColor White
    Write-Host "SDK Size: $(Get-ChildItem './dist' -Filter '*.nupkg' | Measure-Object Length -Sum | ForEach-Object { [math]::Round($_.Sum / 1MB, 2) }) MB" -ForegroundColor White

    if (-not $DryRun) {
        Write-Host "`n🎯 Next Steps:" -ForegroundColor Green
        Write-Host "1. Test packages: dotnet new install Honua.Mobile.Templates --version $Version" -ForegroundColor White
        Write-Host "2. Create sample app: dotnet new honua-fieldcollector -n TestApp" -ForegroundColor White
        Write-Host "3. Share the news: https://twitter.com/honuaproject" -ForegroundColor White
        Write-Host "4. Update documentation: https://docs.honua.com" -ForegroundColor White
    }
}

# Execute main function
try {
    Main
}
catch {
    Write-Error "❌ Package publishing failed: $($_.Exception.Message)"
    exit 1
}
