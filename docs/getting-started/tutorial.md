# 5-Minute Tutorial: Build Your First Field Data Collection App

In just 5 minutes, you'll build a complete field data collection app that competes with expensive platforms like Fulcrum and Survey123 - completely free and open source!

## What You'll Build

A professional mobile app with:
- ✅ Dynamic forms from server schemas
- ✅ GPS location tracking with accuracy
- ✅ Photo capture with metadata
- ✅ Offline data storage and sync
- ✅ Cross-platform (iOS, Android, Windows)

**End Result**: A production-ready field data collection app in under 5 minutes!

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
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseHonuaMobileSDK(config =>
            {
                // 🔥 Replace with your Honua server
                config.ServerEndpoint = "https://demo.honua.com";

                // 🔑 Get your free API key at demo.honua.com
                config.ApiKey = "your-api-key-here";

                // ✅ Enable powerful features
                config.EnableOfflineStorage = true;
                config.EnableIoTSensors = false; // Start simple
            });

        return builder.Build();
    }
}
```

**💡 No Server Yet?** Use our demo server:
- **Endpoint**: `https://demo.honua.com`
- **API Key**: `demo_key_field_collection_2026`
- **Form ID**: `site_inspection` (pre-configured demo form)

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
                                   FormId="site_inspection"
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
using Honua.Mobile.Core.Events;

namespace MyFieldApp;

public partial class MainPage : ContentPage
{
    private int _recordsCollected = 0;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnDataCollected(object sender, FormSubmittedEventArgs e)
    {
        _recordsCollected++;

        // 🎉 Data automatically includes GPS, photos, sensor readings!
        var formData = e.FormData;

        // Show success message
        await DisplayAlert("Success! 🎉",
            $"Record {_recordsCollected} saved successfully!\n\n" +
            $"📍 Location: {formData.GetValueOrDefault("location", "Not captured")}\n" +
            $"📷 Photos: {GetPhotoCount(formData)} attached\n" +
            $"📊 Total fields: {formData.Count}",
            "Continue Collecting");

        // 🔄 Form automatically syncs to server and clears for next record
    }

    private void OnValidationChanged(object sender, FormValidationEventArgs e)
    {
        // Real-time validation feedback
        if (!e.IsValid)
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
}
```

## Step 5: Run Your App! (60 seconds)

```bash
# Build and run
dotnet build

# Run on your preferred platform
dotnet build -t:Run -f net8.0-android     # Android
dotnet build -t:Run -f net8.0-ios         # iOS (Mac only)
dotnet build -t:Run -f net8.0-windows     # Windows
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

## Advanced 5-Minute Add-Ons

### Add IoT Sensor Integration (2 minutes)

```csharp
// In MauiProgram.cs, enable IoT
config.EnableIoTSensors = true;

// In your form schema, add sensor field:
// { "id": "temperature", "type": "sensor", "sensorType": "environmental" }
```

**Result**: Bluetooth LE environmental sensors automatically discovered and integrated!

### Add Map Visualization (2 minutes)

Add to MainPage.xaml above the form:

```xml
<honua:HonuaMapView Grid.Row="1"
                   HeightRequest="200"
                   ShowToolbar="true"
                   ShowCollectedFeatures="true"
                   EnableSpatialQuery="true" />
```

**Result**: Interactive map showing all collected data points!

### Add Augmented Reality (3 minutes)

```xml
<honua:HonuaARViewer Grid.Row="1"
                    EnableUtilityVisualization="true"
                    MaxRenderDistance="100" />
```

**Result**: AR visualization of underground utilities and infrastructure!

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
# https://demo.honua.com/dashboard

# Or query via API:
curl -H "X-API-Key: your-api-key" \
     https://demo.honua.com/api/v1/features/site_inspection
```

## Compare to Competition

### What You Just Built vs. Fulcrum ($99/month)

| Feature | Your App (Free) | Fulcrum ($99/mo) |
|---------|-----------------|------------------|
| **Dynamic Forms** | ✅ Server-driven schemas | ✅ Web form builder |
| **Offline Capability** | ✅ True offline-first | ⚠️ Limited offline |
| **GPS Accuracy** | ✅ Real-time color-coded | ✅ Basic accuracy |
| **Photo Management** | ✅ AI face blurring | ❌ Basic photos only |
| **IoT Integration** | ✅ Bluetooth LE sensors | ❌ Not available |
| **AR Visualization** | ✅ Native ARKit/ARCore | ❌ Not available |
| **Customization** | ✅ Full source code access | ❌ Vendor lock-in |
| **License Cost** | ✅ **$0 forever** | ❌ **$1,188/year** |

**💰 Savings: $1,188/year per user**

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
- 🐛 **[GitHub Issues](https://github.com/honua/honua-mobile-sdk/issues)** - Bug reports
- 📧 **[Email Support](mailto:support@honua.com)** - Direct assistance

### Share Your Success
- 🐦 **[Twitter](https://twitter.com/honuaproject)** - Tag @honuaproject
- 🎬 **[YouTube](https://youtube.com/honuaproject)** - Featured apps
- 📰 **[Blog](https://blog.honua.com)** - User success stories

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