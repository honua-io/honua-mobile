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

Update the server connection and offline package manifest in `MauiProgram.cs`:

```csharp
builder.Services
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://api.honua.io"),
        ApiKey = "your-api-key-here",
    })
    .AddHonuaSdkGeoPackageOfflineSync(
        new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-fieldcollector.gpkg"),
            DefaultFeatureCacheTtl = TimeSpan.FromDays(7),
        },
        CreateOfflinePackageManifest());
```

The template uses the SDK-backed offline path by default. `Honua.Sdk.Offline`
owns the portable package manifest and sync engine; the mobile app still owns
GeoPackage storage, native file placement, connectivity, permissions, and
background scheduling.

The checked-in manifest targets the cloud/staging fixture from
`honua-server#895`: service `mobile_offline_demo`, editable layer `68910`, and
readonly context layer `68920`.

### 2. Customize Your Form

The app is configured to use form ID `"field-site-inspection"`. To use your own form:

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
dotnet build -t:Run -f net10.0-android     # Android
dotnet build -t:Run -f net10.0-ios         # iOS
dotnet build -t:Run -f net10.0-windows10.0.19041.0 # Windows
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

2. Register and navigate to the page from your existing view:

```csharp
builder.Services.AddTransient<CustomPage>();

await Navigation.PushAsync(
    Handler.MauiContext.Services.GetRequiredService<CustomPage>());
```

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
- [Honua Mobile SDK Docs](https://github.com/honua-io/honua-mobile/tree/trunk/docs)
- [Getting Started Guide](https://github.com/honua-io/honua-mobile/tree/trunk/docs/getting-started)
- [API Reference](https://github.com/honua-io/honua-mobile/tree/trunk/docs/api)

### Community
- [Discord Community](https://discord.gg/honua)
- [GitHub Issues](https://github.com/honua-io/honua-mobile/issues)
- [YouTube Tutorials](https://youtube.com/honuaproject)

### Enterprise
<!-- TODO: replace with canonical enterprise/support links once published (placeholder enterprise.honua.com is not live) -->
- Professional Support, Custom Development, and Training Services — contact via the repository maintainers

## 📄 License

This template and generated code is licensed under the **Apache License 2.0**.

- ✅ Commercial use permitted
- ✅ Modification and distribution permitted
- ✅ Patent use permitted
- ✅ Private use permitted

**Built something with this template? Share it with [@honuaproject](https://twitter.com/honuaproject)!**

**[📚 Documentation](https://github.com/honua-io/honua-mobile/tree/trunk/docs) • [💬 Join Community](https://discord.gg/honua)**
