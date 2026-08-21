# Installation Guide

This guide will help you install and configure the Honua Mobile SDK for .NET development.

## Prerequisites

### Development Environment
- **Visual Studio** or a current IDE with the .NET MAUI workload
- **.NET 10.0** SDK or later
- **Git** for version control

### Platform Requirements

#### For iOS Development
- **macOS** (required for iOS development)
- **Xcode 15.0** or later
- **iOS Simulator** or physical iOS device
- **Apple Developer Account** (for device testing and App Store)

#### For Android Development
- **Android SDK** with API Level 24 (Android 7.0) or higher
- **Android Emulator** or physical Android device
- **Java Development Kit (JDK)** 11 or later

#### For Windows Development
- **Windows 10** version 1809 or later
- **Windows 11 SDK** (latest version recommended)

## Installation Methods

The package commands below apply to versions visible on nuget.org. Honua's
release gate restores the complete package set and its `Honua.Sdk.*`
dependencies anonymously before declaring a mobile release complete. Do not
add the private GitHub Packages feed to work around a version that is absent
from nuget.org; use a source checkout until that public cut completes.

### Method 1: Project Templates (Recommended)

The fastest way to get started is using our project templates:

```bash
# Install Honua project templates
dotnet new install Honua.Mobile.Templates

# Verify installation
dotnet new list | grep honua
```

Available templates:
- `honua-fieldcollector` - Complete field data collection app

Create a new project:
```bash
dotnet new honua-fieldcollector -n MyFieldApp
cd MyFieldApp
dotnet build
```

### Method 2: NuGet Package Manager (Visual Studio)

1. **Create new MAUI project**:
   - File → New → Project
   - Select ".NET MAUI App" template
   - Configure project settings

2. **Install Honua packages**:
   - Right-click project → Manage NuGet Packages
   - Search for "Honua.Mobile"
   - Install these packages:

```xml
<PackageReference Include="Honua.Mobile.Maui" Version="0.1.0-alpha.1" />
<PackageReference Include="Honua.Mobile.Offline" Version="0.1.0-alpha.1" />
<PackageReference Include="Honua.Mobile.Sdk" Version="0.1.0-alpha.1" />
```

### Method 3: .NET CLI

```bash
# Create new MAUI project
dotnet new maui -n MyHonuaApp
cd MyHonuaApp

# Add Honua packages
dotnet add package Honua.Mobile.Maui --version 0.1.0-alpha.1
dotnet add package Honua.Mobile.Offline --version 0.1.0-alpha.1
dotnet add package Honua.Mobile.Sdk --version 0.1.0-alpha.1

# Restore packages
dotnet restore
```

### Method 4: Package Manager Console

```powershell
# In Visual Studio Package Manager Console
Install-Package Honua.Mobile.Maui -Version 0.1.0-alpha.1
Install-Package Honua.Mobile.Offline -Version 0.1.0-alpha.1
Install-Package Honua.Mobile.Sdk -Version 0.1.0-alpha.1
```

## Configuration

### 1. Update MauiProgram.cs

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var offlineDb = Path.Combine(FileSystem.Current.AppDataDirectory, "fielddata.gpkg");

        builder.UseMauiApp<App>();

        builder.Services
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://your-honua-server.com"),
                ApiKey = "your-api-key-here",
            })
            .AddHonuaMobileFieldCollection()
            .AddHonuaSdkGeoPackageOfflineSync(
                new GeoPackageSyncStoreOptions { DatabasePath = offlineDb },
                new OfflinePackageManifest
                {
                    PackageId = "mobile-offline-field-ops-v1",
                    Sources =
                    [
                        new OfflineSourceDescriptor
                        {
                            SourceId = "mobile_offline_demo/FeatureServer/68910",
                            Source = new SourceDescriptor
                            {
                                Id = "mobile-offline-field-sites",
                                Protocol = FeatureProtocolIds.GeoServicesFeatureService,
                                Locator = new SourceLocator { ServiceId = "mobile_offline_demo", LayerId = 68910 },
                            },
                            Where = "1=1",
                            OutFields = ["objectid", "globalid", "site_name", "status", "priority", "assigned_to", "inspection_date", "sync_version", "offline_action", "notes"],
                            ReturnGeometry = true,
                            PageSize = 100,
                        },
                    ],
                })
            .AddHonuaBackgroundSync(new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMinutes(5),
            });

        // Register additional services if needed
        builder.Services.AddScoped<IMyCustomService, MyCustomService>();

        return builder.Build();
    }
}
```

### 2. Platform-Specific Configuration

#### Android Configuration

**AndroidManifest.xml** (`Platforms/Android/AndroidManifest.xml`):
```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">

    <!-- Required permissions -->
    <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
    <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />

    <!-- Network permissions -->
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />

    <!-- Feature declarations -->
    <uses-feature android:name="android.hardware.camera" android:required="false" />
    <uses-feature android:name="android.hardware.location.gps" android:required="false" />

    <application android:allowBackup="true" android:icon="@mipmap/appicon" android:supportsRtl="true">
        <!-- Additional configuration -->
    </application>

</manifest>
```

#### iOS Configuration

**Info.plist** (`Platforms/iOS/Info.plist`):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
    <!-- Location permissions -->
    <key>NSLocationWhenInUseUsageDescription</key>
    <string>This app needs location access for GPS-based data collection.</string>

    <key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
    <string>This app needs location access for background sync and tracking.</string>

    <!-- Camera permissions -->
    <key>NSCameraUsageDescription</key>
    <string>This app needs camera access for photo capture and documentation.</string>

    <!-- Photo library -->
    <key>NSPhotoLibraryUsageDescription</key>
    <string>This app needs photo library access for image management.</string>

    <!-- Microphone (if needed for video) -->
    <key>NSMicrophoneUsageDescription</key>
    <string>This app needs microphone access for video capture.</string>

    <!-- App Transport Security -->
    <key>NSAppTransportSecurity</key>
    <dict>
        <key>NSAllowsArbitraryLoads</key>
        <false/>
        <key>NSExceptionDomains</key>
        <dict>
            <key>your-honua-server.com</key>
            <dict>
                <key>NSExceptionRequiresForwardSecrecy</key>
                <false/>
                <key>NSExceptionMinimumTLSVersion</key>
                <string>TLSv1.2</string>
                <key>NSIncludesSubdomains</key>
                <true/>
            </dict>
        </dict>
    </dict>
</dict>
</plist>
```

#### Windows Configuration

**Package.appxmanifest** (`Platforms/Windows/Package.appxmanifest`):
```xml
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">

  <Capabilities>
    <Capability Name="internetClient" />
    <DeviceCapability Name="location" />
    <DeviceCapability Name="webcam" />
  </Capabilities>

</Package>
```

### 3. Application Configuration

The SDK is configured in code through `HonuaMobileClientOptions` (passed to
`AddHonuaMobileSdk`), not by binding an `appsettings.json` section. The snippet
below is an **illustrative** example of how you might keep your own settings in
`appsettings.json` and read them when constructing the options — the keys are
your application's, not a schema the SDK binds automatically.

```json
{
  "HonuaMobile": {
    "ServerEndpoint": "https://your-honua-server.com",
    "ApiKey": "",
    "OfflineStorage": {
      "Enabled": true,
      "DatabasePath": "honua_offline.gpkg",
      "MaxSizeBytes": 104857600,
      "AutoSync": true,
      "SyncInterval": "00:05:00"
    },
    "Logging": {
      "LogLevel": {
        "Default": "Information",
        "Honua": "Debug"
      }
    }
  }
}
```

> The IoT/Bluetooth and camera-pipeline settings that previously appeared here
> were removed: the SDK exposes no Bluetooth/IoT integration and binds no such
> configuration. See `HonuaMobileClientOptions` for the options the SDK actually
> reads.

## Verification

### Test Your Installation

Create a simple test page to verify everything is working:

```csharp
// TestPage.xaml.cs
using Honua.Mobile.Offline.Sync;

namespace MyHonuaApp.Pages;

public partial class TestPage : ContentPage
{
    private readonly IOfflineSyncRunner _syncRunner;

    public TestPage(IOfflineSyncRunner syncRunner)
    {
        InitializeComponent();
        _syncRunner = syncRunner;
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = "Testing connection...";

            var result = await _syncRunner.SyncAsync();
            StatusLabel.Text = $"Sync runner ready: {result.Loaded} queued edits inspected.";
            StatusLabel.TextColor = Colors.Green;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ Error: {ex.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
    }
}
```

```xml
<!-- TestPage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage x:Class="MyHonuaApp.Pages.TestPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             Title="Honua SDK Test">
    <StackLayout Padding="20" Spacing="20">
        <Label Text="Honua Mobile SDK Test"
               FontSize="24"
               FontAttributes="Bold"
               HorizontalOptions="Center" />

        <Button Text="Test Connection"
                Clicked="OnTestConnectionClicked"
                BackgroundColor="#007ACC"
                TextColor="White" />

        <Label x:Name="StatusLabel"
               Text="Click button to test connection"
               FontSize="16"
               HorizontalOptions="Center" />
    </StackLayout>
</ContentPage>
```

### Build and Run

```bash
# Build the project
dotnet build

# Run on specific platform
dotnet build -t:Run -f net10.0-android     # Android
dotnet build -t:Run -f net10.0-ios         # iOS (macOS only)
dotnet build -t:Run -f net10.0-windows10.0.19041.0 # Windows
```

## Troubleshooting

### Common Issues

**Issue: Package not found**
```
Solution: Ensure you're using the latest package source
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
```

**Issue: Build errors on Android**
```
Solution: Update Android SDK and build tools
- Open Android SDK Manager
- Update to latest SDK Platform and Build-Tools
- Update Android Emulator if needed
```

**Issue: iOS simulator not starting**
```
Solution: Reset iOS Simulator
- iOS Simulator → Device → Erase All Content and Settings
- Restart Xcode and Visual Studio
```

**Issue: Bluetooth permissions on Android 12+**
```
Solution: Add runtime permission request
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothConnect)]
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothScan)]
```

### Getting Help

If you encounter issues:

1. **Check the documentation**: Browse our [troubleshooting guide](../guides/troubleshooting.md)
2. **Search existing issues**: [GitHub Issues](https://github.com/honua-io/honua-mobile/issues)
3. **Community support**: [Discord Channel](https://discord.gg/honua)
4. **Professional support**: Enterprise Support <!-- TODO: add canonical Enterprise Support URL once published (placeholder enterprise.honua.com is not live) -->

## Next Steps

Once installation is complete:

1. 📖 **[Follow the Tutorial](tutorial.md)** - Build your first app
2. 🎯 **[Explore Examples](../../examples/)** - See real-world implementations
3. 📚 **[Read API Documentation](../api/core.md)** - Learn the SDK APIs
4. 🎬 **[Watch Videos](https://youtube.com/honuaproject)** - Visual learning resources

---

**Ready to start building? Let's create your first Honua mobile app!**

**[➡️ Next: Quick Start Tutorial](tutorial.md)**
