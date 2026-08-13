# 5-Minute Tutorial: Build Your First Field Data Collection App

In just 5 minutes, you'll build an open-source field data collection app that
demonstrates the same core workflow categories used by platforms like Fulcrum
and Survey123.

## What You'll Build

A professional mobile app with:
- ✅ Dynamic forms from server schemas
- ✅ GPS location tracking with accuracy
- ✅ Photo capture with metadata
- ✅ Offline data storage and sync
- ✅ Cross-platform (iOS, Android, Windows)

**End Result**: A field data collection app prototype you can extend and
validate for your own workflow.

## Step 1: Create Project (30 seconds)

```bash
# Install templates (one-time setup)
dotnet new install Honua.Mobile.Templates

# Create your field collection app
dotnet new honua-fieldcollector -n MyFieldApp
cd MyFieldApp
```

**What happened**: You now have a complete .NET MAUI project with all Honua SDK components pre-configured!

## Step 2: Configure Connection (60 seconds)

Open `MauiProgram.cs` and configure your server connection:

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var offlineDb = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-fieldcollector.gpkg");

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://api.honua.io"),
                ApiKey = "your-api-key-here",
            })
            .AddHonuaMobileFieldCollection()
            .AddHonuaSdkGeoPackageOfflineSync(
                new GeoPackageSyncStoreOptions { DatabasePath = offlineDb },
                new OfflinePackageManifest
                {
                    PackageId = "mobile-offline-field-ops-v1",
                    DisplayName = "Mobile Offline Field Operations",
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
                });

        return builder.Build();
    }
}
```

**💡 No Server Yet?** Use our demo server:
- **Endpoint**: `https://api.honua.io`
- **API Key**: `demo_key_field_collection_2026`
- **Service**: `mobile_offline_demo` (pre-configured offline field operations fixture)
- **Editable layer**: `68910`

## Step 3: Build the Data Collection UI (90 seconds)

Replace `MainPage.xaml` content:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage x:Class="MyFieldApp.MainPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:honua="http://schemas.honua.com/mobile/2024"
             Title="Field Data Collection">

    <Grid RowDefinitions="Auto,*,Auto">

        <!-- 📍 GPS Status Header -->
        <honua:HonuaLocationIndicator Grid.Row="0"
                                     ShowAccuracy="true"
                                     RequiredAccuracy="5.0"
                                     BackgroundColor="#E3F2FD"
                                     Padding="15" />

        <!-- 📝 Dynamic Data Collection Form -->
        <ScrollView Grid.Row="1">
            <honua:HonuaFeatureForm x:Name="DataForm"
                                   FormId="field-site-inspection"
                                   AllowDrafts="true"
                                   ShowProgress="true"
                                   FormSubmitted="OnDataCollected"
                                   ValidationChanged="OnValidationChanged"
                                   Padding="20" />
        </ScrollView>

        <!-- 🔄 Sync Status Footer -->
        <honua:HonuaSyncStatus Grid.Row="2"
                              ShowDetails="true"
                              EnableManualSync="true"
                              BackgroundColor="#F5F5F5"
                              Padding="15" />

    </Grid>

</ContentPage>
```

## Step 4: Handle Data Collection (60 seconds)

Update `MainPage.xaml.cs` with event handlers:

```csharp
namespace MyFieldApp;

public partial class MainPage : ContentPage
{
    private int _recordsCollected = 0;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnDataCollected(object sender, EventArgs e)
    {
        _recordsCollected++;

        // 🎉 Data automatically includes GPS, photos, sensor readings!
        var formData = ReadFormData(e);

        // Show success message
        await DisplayAlert("Success! 🎉",
            $"Record {_recordsCollected} saved successfully!\n\n" +
            $"📍 Location: {formData.GetValueOrDefault("location", "Not captured")}\n" +
            $"📷 Photos: {GetPhotoCount(formData)} attached\n" +
            $"📊 Total fields: {formData.Count}",
            "Continue Collecting");

        // 🔄 Form automatically syncs to server and clears for next record
    }

    private void OnValidationChanged(object sender, EventArgs e)
    {
        // Real-time validation feedback
        if (!ReadProperty<bool>(e, "IsValid"))
        {
            // Form automatically shows validation errors
        }
    }

    private int GetPhotoCount(Dictionary<string, object> formData)
    {
        return formData.Values
            .OfType<List<object>>()
            .SelectMany(x => x)
            .Count(x => x.ToString().Contains("photo"));
    }

    private static Dictionary<string, object> ReadFormData(EventArgs args)
    {
        return ReadProperty<Dictionary<string, object>>(args, "FormData") ?? [];
    }

    private static T? ReadProperty<T>(object source, string propertyName)
    {
        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value is T typed ? typed : default;
    }
}
```

## Step 5: Run Your App! (60 seconds)

```bash
# Build and run
dotnet build

# Run on your preferred platform
dotnet build -t:Run -f net10.0-android     # Android
dotnet build -t:Run -f net10.0-ios         # iOS (Mac only)
dotnet build -t:Run -f net10.0-windows10.0.19041.0 # Windows
```

**🎉 Congratulations!** You now have a professional field data collection app!

## What You Just Built

### 🏆 Professional Features (Out of the Box)

**Dynamic Form Generation**:
- Form fields automatically generated from server schema
- 15+ field types: text, numbers, photos, GPS, dropdowns, signatures
- Real-time validation with user-friendly error messages
- Progress tracking and completion percentage

**GPS Integration**:
- Real-time location accuracy display with color coding
- Automatic GPS metadata for all records
- Configurable accuracy requirements
- Works offline and syncs location when connected

**Photo Management**:
- Professional camera interface with GPS tagging
- Automatic photo compression and optimization
- AI-powered face blurring for privacy (if enabled)
- Thumbnail previews and batch management

**Offline-First Architecture**:
- All data stored locally in GeoPackage format (OGC standard)
- Intelligent sync when network available
- Conflict resolution with user-friendly UI
- Works completely offline for days/weeks

**Cross-Platform Native Performance**:
- iOS: MapKit, ARKit, CoreLocation integration
- Android: Google Maps, ARCore, Location Services
- Windows: MapControl, Camera, Geolocation APIs
- Consistent UX across all platforms

## Testing Your App

### Demo Data Collection

1. **Launch the app** - GPS accuracy indicator appears
2. **Fill out the form** - Watch progress bar update in real-time
3. **Take photos** - GPS metadata automatically added
4. **Submit data** - Instant sync to server
5. **Go offline** - Continue collecting, auto-sync when online

### Verify Sync

```bash
# Check your server dashboard at:
# https://api.honua.io/dashboard

# Or query via API:
curl -H "X-API-Key: your-api-key" \
     "https://api.honua.io/rest/services/mobile_offline_demo/FeatureServer/68910/query?where=1%3D1&outFields=*&f=json"
```

## Compare to Competition

This table is a planning comparison for the template and SDK surface, not a
claim that every item is fully shipped and validated in this repository. For
source-backed status, use the [feature map](../features/README.md), the
[validation strategy](../guides/validation-strategy.md), and the
[mobile SDK backlog roadmap](../guides/mobile-sdk-backlog-roadmap.md).

### Template Direction vs. Fulcrum ($99/month)

| Feature | Honua template direction | Fulcrum ($99/mo) |
|---------|-----------------|------------------|
| **Dynamic Forms** | SDK-backed schemas and rendering are active mobile scope | Web form builder |
| **Offline Capability** | GeoPackage and sync runtime are implemented; app workflow parity is tracked in backlog | Offline field collection |
| **GPS Accuracy** | Mobile location services and validation presentation are implemented; advanced field UX is backlog | Accuracy capture |
| **Photo Management** | Media capture/local path handling is mobile scope; advanced AI hooks are backlog | Photo capture |
| **IoT Integration** | Future extension track | Product-dependent |
| **AR Visualization** | Future track after scene anchoring dependencies close | Not a core field form feature |
| **Customization** | Source and template customization | Vendor-managed customization |
| **License Cost** | Open-source client repository | Commercial subscription |

Commercial savings depend on hosting, support, and implementation choices.

## Next Steps (Choose Your Adventure)

### 🎯 **Perfect for Beginners**: Explore Components
- [📷 Camera Integration Guide](../guides/camera-integration.md)
- [🔄 Offline Sync Guide](../guides/offline-sync.md)

### 🚀 **Ready for More**: Advanced Features
- [🚀 Advanced Features](../guides/advanced-features.md)
- [🔐 Security & Authentication](../guides/security.md)

### 🏗️ **Going Production**: Enterprise Features
- [⚡ Performance Optimization](../guides/performance.md)
- [🔀 Migration Guide](../guides/migration-guide.md)

### 👨‍💻 **Developer Deep Dive**: Technical Details
- [📚 Core API Reference](../api/core.md)
- [🔧 Troubleshooting](../guides/troubleshooting.md)

## Troubleshooting

### App Won't Start
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

### GPS Not Working
- **Android**: Check location permissions in Settings
- **iOS**: Allow location access when prompted
- **All**: Ensure device has GPS enabled

### Photos Not Saving
- **Android**: Grant camera and storage permissions
- **iOS**: Allow camera access when prompted
- **Check**: Device has sufficient storage space

### Sync Issues
- **Check**: Internet connectivity
- **Verify**: API key is correct
- **Test**: Server endpoint is accessible

## Community & Support

### Get Help
- 💬 **[Discord Community](https://discord.gg/honua)** - Real-time help
- 📖 **[Documentation](../README.md)** - Comprehensive guides
- 🐛 **[GitHub Issues](https://github.com/honua-io/honua-mobile/issues)** - Bug reports
- 📧 **[Email Support](mailto:support@honua.com)** - Direct assistance

### Share Your Success
- 🐦 **[Twitter](https://twitter.com/honuaproject)** - Tag @honuaproject
- 🎬 **[YouTube](https://youtube.com/honuaproject)** - Featured apps
- 📰 **Blog** - User success stories <!-- TODO: add canonical blog URL once published (placeholder blog.honua.com is not live) -->

---

## 🎉 Congratulations!

**You just built a professional field data collection app in 5 minutes that competes with platforms costing $1,200+ per year!**

### What's Next?
- ✅ **Customize the form** for your specific use case
- ✅ **Add more fields** (sensors, signatures, calculations)
- ✅ **Deploy to app stores** (iOS App Store, Google Play)
- ✅ **Scale to your team** with unlimited users
- ✅ **Add enterprise features** (SSO, audit logs, analytics)

### Key Benefits You've Unlocked:
- 💰 **$0 cost** vs $1,200+/year for alternatives
- 🔓 **No vendor lock-in** - you own the code
- 🚀 **Professional grade** - enterprise-ready features
- 🌍 **Cross-platform** - iOS, Android, Windows support
- 🔄 **Modern architecture** - gRPC, offline-first, real-time sync

**Ready to revolutionize your data collection workflow?**

**[🚀 Deploy to Production](../guides/deployment.md) • [🎨 Customize Your App](../guides/customization.md) • [👥 Join the Community](https://discord.gg/honua)**

---

*Built something awesome? Share it with [@honuaproject](https://twitter.com/honuaproject) and tag #HonuaMobile!*
