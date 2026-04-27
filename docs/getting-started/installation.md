# Installation Guide

This guide will help you install and configure the Honua Mobile SDK for .NET development.

## Prerequisites

### Development Environment
- **Visual Studio 2022** version 17.8 or later with .NET MAUI workload
- **.NET 8.0** SDK or later
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
- `honua-photosurvey` - Simple photo survey application
- `honua-iotmonitor` - IoT sensor monitoring app
- `honua-assetinspection` - Asset inspection with AR
- `honua-minimal` - Minimal SDK integration

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
<PackageReference Include="Honua.Mobile.Core" Version="1.0.0" />
<PackageReference Include="Honua.Mobile.Storage" Version="1.0.0" />
<PackageReference Include="Honua.Mobile.IoT" Version="1.0.0" />
<PackageReference Include="Honua.Mobile.Maui" Version="1.0.0" />
```

### Method 3: .NET CLI

```bash
# Create new MAUI project
dotnet new maui -n MyHonuaApp
cd MyHonuaApp

# Add Honua packages
dotnet add package Honua.Mobile.Core
dotnet add package Honua.Mobile.Storage
dotnet add package Honua.Mobile.IoT
dotnet add package Honua.Mobile.Maui

# Restore packages
dotnet restore
```

### Method 4: Package Manager Console

```powershell
# In Visual Studio Package Manager Console
Install-Package Honua.Mobile.Core
Install-Package Honua.Mobile.Storage
Install-Package Honua.Mobile.IoT
Install-Package Honua.Mobile.Maui
```

## Configuration

### 1. Update MauiProgram.cs

```csharp
using Honua.Mobile.Core;
using Honua.Mobile.Storage;
using Honua.Mobile.IoT;
using Honua.Mobile.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseHonuaMobileSDK(config =>
            {
                // Required: Your Honua server endpoint
                config.ServerEndpoint = "https://your-honua-server.com";

                // Required: API key for authentication
                config.ApiKey = "your-api-key-here";

                // Optional: Enable offline storage (recommended)
                config.EnableOfflineStorage = true;

                // Optional: Enable IoT sensor support
                config.EnableIoTSensors = true;

                // Optional: Configure logging
                config.LogLevel = LogLevel.Information;

                // Optional: Sync settings
                config.AutoSyncInterval = TimeSpan.FromMinutes(5);
                config.MaxRetryAttempts = 3;
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

    <!-- IoT sensor permissions -->
    <uses-permission android:name="android.permission.BLUETOOTH" />
    <uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
    <uses-permission android:name="android.permission.BLUETOOTH_SCAN" />

    <!-- Network permissions -->
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />

    <!-- Feature declarations -->
    <uses-feature android:name="android.hardware.camera" android:required="false" />
    <uses-feature android:name="android.hardware.location.gps" android:required="false" />
    <uses-feature android:name="android.hardware.bluetooth_le" android:required="false" />

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

    <!-- Bluetooth permissions -->
    <key>NSBluetoothAlwaysUsageDescription</key>
    <string>This app needs Bluetooth access for IoT sensor connectivity.</string>

    <key>NSBluetoothPeripheralUsageDescription</key>
    <string>This app needs Bluetooth access for sensor communication.</string>

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
                <string>TLSv1.0</string>
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
    <DeviceCapability Name="bluetooth" />
    <DeviceCapability Name="bluetoothLEDevice" />
  </Capabilities>

</Package>
```

### 3. Application Configuration

Create `appsettings.json` in your project root:

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
    "IoT": {
      "Enabled": true,
      "BluetoothLE": {
        "ScanTimeout": "00:00:30",
        "ConnectionTimeout": "00:00:15",
        "AutoReconnect": true
      },
      "WiFi": {
        "DiscoveryPort": 8080,
        "Timeout": "00:00:10"
      }
    },
    "Camera": {
      "DefaultQuality": "High",
      "EnablePrivacyBlur": true,
      "IncludeLocation": true,
      "CompressionLevel": 0.8
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

## Verification

### Test Your Installation

Create a simple test page to verify everything is working:

```csharp
// TestPage.xaml.cs
using Honua.Mobile.Core;

namespace MyHonuaApp.Pages;

public partial class TestPage : ContentPage
{
    private readonly IHonuaClient _client;

    public TestPage(IHonuaClient client)
    {
        InitializeComponent();
        _client = client;
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = "Testing connection...";

            // Test basic connectivity
            var isConnected = await _client.TestConnectionAsync();

            if (isConnected)
            {
                StatusLabel.Text = "✅ Connection successful!";
                StatusLabel.TextColor = Colors.Green;
            }
            else
            {
                StatusLabel.Text = "❌ Connection failed";
                StatusLabel.TextColor = Colors.Red;
            }
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
dotnet build -t:Run -f net8.0-android     # Android
dotnet build -t:Run -f net8.0-ios         # iOS (macOS only)
dotnet build -t:Run -f net8.0-windows     # Windows
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
2. **Search existing issues**: [GitHub Issues](https://github.com/honua/honua-mobile-sdk/issues)
3. **Community support**: [Discord Channel](https://discord.gg/honua)
4. **Professional support**: [Enterprise Support](https://enterprise.honua.com)

## Next Steps

Once installation is complete:

1. 📖 **[Follow the Tutorial](tutorial.md)** - Build your first app
2. 🎯 **[Explore Examples](../../examples/)** - See real-world implementations
3. 📚 **[Read API Documentation](../api/core.md)** - Learn the SDK APIs
4. 🎬 **[Watch Videos](https://youtube.com/honuaproject)** - Visual learning resources

---

**Ready to start building? Let's create your first Honua mobile app!**

**[➡️ Next: Quick Start Tutorial](tutorial.md)**