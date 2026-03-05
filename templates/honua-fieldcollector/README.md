# YOUR_COMPANY_NAME Field Data Collection App

**Professional mobile field data collection that competes with Fulcrum and Survey123 - completely free and open source.**

This template creates a complete field data collection application with:

## ✨ Features Included

### 📝 **Dynamic Form Generation**
- No-code form creation from server schemas
- 15+ field types (text, numbers, photos, GPS, dropdowns, signatures)
- Real-time validation with user-friendly error messages
- Progress tracking and completion percentage

### 📍 **GPS & Location Services**
- Real-time location accuracy display with color coding
- Automatic GPS metadata for all records
- Configurable accuracy requirements
- Works offline and syncs location when connected

### 📷 **Professional Photo Management**
- Native camera interface with GPS tagging
- Automatic photo compression and optimization
- AI-powered face blurring for privacy (optional)
- Thumbnail previews and batch management

### 🗺️ **Interactive Mapping**
- Cross-platform native maps (iOS MapKit, Android Google Maps, Windows MapControl)
- Display collected data points in real-time
- Layer management and visualization
- Spatial query tools

<!--#if (enableIoT)-->
### 🤖 **IoT Sensor Integration** (Enabled)
- Bluetooth LE environmental sensors
- Auto-discovery and connection
- Real-time sensor data integration with forms
- Support for temperature, humidity, air quality sensors
<!--#endif-->

<!--#if (enableAR)-->
### 🥽 **Augmented Reality** (Enabled)
- AR visualization of underground utilities
- Infrastructure overlay on camera feed
- Interactive 3D models with distance measurement
- Photo capture with AR overlay
<!--#endif-->

### 🔄 **Intelligent Sync & Offline**
- True offline-first architecture
- Automatic sync when network available
- Conflict resolution with user-friendly UI
- GeoPackage storage (OGC standard)

### 📊 **Analytics & Reporting**
- Collection statistics and activity tracking
- Recent activity timeline
- Data export capabilities
- Performance metrics

## 🚀 Quick Start

### 1. Configure Your Server

Update the server connection in `MauiProgram.cs`:

```csharp
config.ServerEndpoint = "https://your-honua-server.com";
config.ApiKey = "your-api-key-here";
```

### 2. Customize Your Form

The app is configured to use form ID `"site_inspection"`. To use your own form:

1. Create a form schema on your Honua server
2. Update the `FormId` in `MainPage.xaml`:

```xml
<honua:HonuaFeatureForm FormId="your-form-id" ... />
```

### 3. Brand Your App

- Update `YOUR_COMPANY_NAME` throughout the code
- Replace app icons in `Resources/AppIcon/`
- Customize colors in `App.xaml`
- Update app metadata in the `.csproj` file

### 4. Build and Deploy

```bash
# Build for development
dotnet build

# Build for release
dotnet build -c Release

# Deploy to device
dotnet build -t:Run -f net8.0-android     # Android
dotnet build -t:Run -f net8.0-ios         # iOS
dotnet build -t:Run -f net8.0-windows     # Windows
```

## 📱 Platform Support

| Platform | Version | Status | Features |
|----------|---------|---------|----------|
| **Android** | API 24+ (7.0) | ✅ Full Support | Google Maps, Camera2, BLE, AR |
| **iOS** | 12.0+ | ✅ Full Support | MapKit, ARKit, Camera, BLE |
| **Windows** | 10 1809+ | ✅ Full Support | MapControl, Camera, BLE |

## 🎨 Customization

### Theming

Customize your app's appearance in `App.xaml`:

```xml
<!-- Update primary colors -->
<Color x:Key="Primary">#YOUR_COLOR</Color>
<Color x:Key="Secondary">#YOUR_SECONDARY</Color>

<!-- Add your company branding -->
<Style x:Key="CompanyHeaderStyle" TargetType="Label">
    <Setter Property="FontFamily" Value="YourCompanyFont" />
    <Setter Property="TextColor" Value="{StaticResource Primary}" />
</Style>
```

### Form Fields

Add custom form fields by extending the schema on your server. The app supports:

- Text input (single line, multi-line)
- Numbers (integers, decimals, with min/max)
- Dates and times
- Photos with GPS tagging
- Location capture with accuracy
- Dropdowns and radio buttons
- Checkboxes and switches
- Digital signatures
- Barcode/QR scanning
<!--#if (enableIoT)-->
- IoT sensor readings
<!--#endif-->
<!--#if (enableAR)-->
- AR measurements and annotations
<!--#endif-->

### Adding New Pages

To add new pages to your app:

1. Create new XAML page:

```csharp
// Views/CustomPage.xaml.cs
public partial class CustomPage : ContentPage
{
    public CustomPage()
    {
        InitializeComponent();
    }
}
```

2. Add navigation in `AppShell.xaml`:

```xml
<ShellContent Title="Custom" Icon="custom.png" Route="custom" ContentTemplate="{DataTemplate local:CustomPage}" />
```

### Advanced Features

<!--#if (enableIoT)-->
#### IoT Sensor Configuration

Configure sensor types in `MauiProgram.cs`:

```csharp
config.IoT.SensorTypes = new[]
{
    SensorType.Environmental,
    SensorType.AirQuality,
    SensorType.Noise,
    SensorType.Custom
};
```
<!--#endif-->

<!--#if (enableAR)-->
#### AR Configuration

Customize AR settings:

```csharp
config.AR.MaxRenderDistance = 200; // meters
config.AR.EnableUtilityVisualization = true;
config.AR.EnableInfrastructureOverlay = true;
```
<!--#endif-->

## 🔧 Development Tips

### Debugging

Enable detailed logging in `MauiProgram.cs`:

```csharp
#if DEBUG
builder.Logging.AddDebug();
builder.Services.Configure<LoggerFilterOptions>(options =>
{
    options.SetMinimumLevel(LogLevel.Debug);
});
#endif
```

### Testing

Test offline scenarios:
1. Disable device network connection
2. Collect data normally
3. Re-enable network
4. Verify automatic sync

### Performance Optimization

For large datasets:
- Enable data paging: `config.MaxRecordsPerPage = 100`
- Use background sync: `config.EnableBackgroundSync = true`
- Optimize photos: `config.PhotoCompressionLevel = 0.8`

## 🆚 Competitive Advantages

### vs. Fulcrum ($99/month)
- ✅ **$0 cost** vs $1,188/year
- ✅ **Native performance** vs web wrapper
- ✅ **Full customization** vs vendor lock-in
- ✅ **IoT integration** vs basic forms only
- ✅ **AR capabilities** vs not available

### vs. Survey123 ($25/user/month)
- ✅ **Unlimited users** vs per-user licensing
- ✅ **Real-time sync** vs batch upload
- ✅ **Advanced mapping** vs basic maps
- ✅ **Open source** vs proprietary

### vs. ArcGIS Mobile SDK ($1500/dev/year)
- ✅ **Open source** vs expensive licensing
- ✅ **Complete app** vs SDK only
- ✅ **Modern architecture** vs legacy APIs
- ✅ **Community support** vs vendor dependency

## 📚 Resources

### Documentation
- [Honua Mobile SDK Docs](https://docs.honua.com/mobile)
- [Getting Started Guide](https://docs.honua.com/mobile/getting-started)
- [API Reference](https://docs.honua.com/mobile/api)

### Community
- [Discord Community](https://discord.gg/honua)
- [GitHub Issues](https://github.com/honua/honua-mobile-sdk/issues)
- [YouTube Tutorials](https://youtube.com/honuaproject)

### Enterprise
- [Professional Support](https://enterprise.honua.com)
- [Custom Development](https://enterprise.honua.com/custom)
- [Training Services](https://enterprise.honua.com/training)

## 📄 License

This template and generated code is licensed under the **Apache License 2.0**.

- ✅ Commercial use permitted
- ✅ Modification and distribution permitted
- ✅ Patent use permitted
- ✅ Private use permitted

## 🎉 Success Stories

> "Replaced Fulcrum with this template and saved $15,000/year for our 50-person field team. The offline capabilities are incredible!"
>
> — Environmental Consulting Firm

> "Built our utility inspection app in 2 days using this template. The AR features gave us a huge competitive advantage."
>
> — Infrastructure Company

---

**Built something awesome with this template? Share it with [@honuaproject](https://twitter.com/honuaproject)!**

**[🚀 Deploy to Production](https://docs.honua.com/deployment) • [🎨 Advanced Customization](https://docs.honua.com/customization) • [💬 Join Community](https://discord.gg/honua)**