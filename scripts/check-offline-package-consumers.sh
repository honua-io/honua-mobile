#!/usr/bin/env bash
# Run against the source-built artifacts from the Pack NuGet packages workflow step.
# Passing proves compatibility of the supplied artifacts. Downstream pins require
# separate publication of that package version to the downstream feed.
set -euo pipefail

if [[ $# -ne 2 || ! -d "$1" || ! "$2" =~ ^[0-9][a-zA-Z0-9.+-]*$ ]]; then
    echo "Usage: $0 <nupkg-directory> <package-version>" >&2
    exit 2
fi

package_dir="$(cd "$1" && pwd)"
version="$2"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
export NUGET_PACKAGES="$scratch/packages"
cp -- "$package_dir"/Honua.Mobile.*."$version".nupkg "$scratch/"
cat > "$scratch/NuGet.config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="." />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate">
      <package pattern="Honua.Mobile.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

for mode in stock sqlcipher; do
    consumer="$scratch/$mode"
    mkdir "$consumer"
    cp "$script_dir/package-smoke/OfflineStorage.cs" "$consumer/Program.cs"
    # Run each provider in its own executable. Match the SQLCipher pins in
    # honua-field/Directory.Packages.props and the stock Mobile driver version.
    references='<PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.10" />'
    if [[ "$mode" == sqlcipher ]]; then
        references='<PackageReference Include="Microsoft.Data.Sqlite.Core" Version="11.0.0" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.1.11" />'
    fi
    cat > "$consumer/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Honua.Mobile.Offline" Version="[$version]" />
    $references
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
  </ItemGroup>
</Project>
XML
    dotnet restore "$consumer/Consumer.csproj" --configfile "$scratch/NuGet.config"
    dotnet build "$consumer/Consumer.csproj" --configuration Release --no-restore
    dotnet "$consumer/bin/Release/net11.0/Consumer.dll" "$mode" "$consumer/offline.gpkg"
done
