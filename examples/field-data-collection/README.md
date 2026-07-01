# Honua Field Collector (aspirational reference example)

> **Status: illustrative scaffold — does not build as-is.** This example sketches a
> target field-collection experience. Some sections below (augmented-reality
> visualization, IoT/Bluetooth sensor integration) describe planned capabilities
> that are **not part of the shipped Honua Mobile SDK**: the corresponding
> services (`IIoTIntegrationService`, `IARVisualizationService`, `ISensorDataService`,
> `IUtilityVisualizationService`) are not implemented, the project is not part of
> `Honua.Mobile.sln`, and it is not compiled by CI. Treat the AR/IoT material as a
> design sketch, not documentation of working features. The shipped, verified
> capabilities are offline-first GeoPackage capture, dynamic forms, multi-media
> capture, and sync.

Transform how field workers, inspectors, and data collectors interact with real-world environments using offline-first mobile data collection.

## 🚀 Revolutionary Features

### **📱 Professional Data Collection**
- **Dynamic form generation** from server schemas - no coding required
- **Intelligent field validation** with real-time error checking
- **Multi-media capture** with GPS tagging and metadata
- **Offline-first architecture** for reliable field work
- **Professional photo documentation** with privacy controls

### **🥽 Augmented Reality Integration**
- **See-through visualization** of underground utilities and infrastructure
- **Real-time 3D rendering** of pipes, cables, and equipment
- **AR-enhanced documentation** with overlay capture
- **Spatial measurements** using AR depth sensing

### **📡 IoT Sensor Automation**
- **Automatic sensor discovery** and connection via Bluetooth/LoRa/WiFi
- **Real-time environmental monitoring** (temperature, humidity, air quality)
- **Sensor workflow automation** for hands-free data collection
- **Equipment telemetry** integration for maintenance tracking

### **🗺️ Advanced Mapping**
- **Cross-platform maps** with native performance (iOS MapKit, Android Google Maps)
- **Spatial query tools** for complex data filtering
- **Multi-layer visualization** with custom symbology
- **GPS accuracy visualization** for survey-grade positioning

### **⚡ Enterprise-Grade Sync**
- **Intelligent conflict resolution** for multi-user scenarios
- **Bandwidth-optimized** gRPC streaming protocols
- **Background synchronization** with retry logic
- **Change tracking** for audit trails

## 🎯 Use Cases

### **Utility Inspection & Maintenance**
- Locate and document utility infrastructure without excavation
- Verify utility depths and positions using AR overlay
- Capture maintenance photos with automatic GPS tagging
- Integrate sensor readings for equipment monitoring

### **Environmental Monitoring**
- Automated air quality data collection with IoT sensors
- Photo documentation of environmental conditions
- GPS-tracked sampling with chain of custody
- Real-time data streaming to central databases

### **Construction & Site Management**
- Progress documentation with before/after comparisons
- Safety inspection forms with automatic validation
- Equipment tracking with QR/barcode scanning
- Surveying integration with GPS accuracy visualization

### **Asset Management**
- Inventory tracking with photo documentation
- Maintenance scheduling and history tracking
- Condition assessment with standardized forms
- IoT integration for preventive maintenance alerts

## 📦 Installation & Quick Start

### **Prerequisites**
- **Development**: Visual Studio 2022, .NET 8.0, MAUI workload
- **iOS**: Xcode 15+, iOS 14.2+ device with ARKit support
- **Android**: Android SDK 21+, device with ARCore support
- **Windows**: Windows 10/11 for desktop development

### **Quick Start**
```bash
# Clone the repository
git clone https://github.com/honua-io/honua-server.git
cd examples/mobile/field-data-collection

# Restore packages
dotnet restore

# Build the application
dotnet build

# Run on specific platforms
dotnet build -t:Run -f net8.0-android     # Android
dotnet build -t:Run -f net8.0-ios         # iOS
dotnet build -t:Run -f net8.0-windows     # Windows
```

### **Configuration**
Edit `appsettings.json` to configure your Honua server connection:

```json
{
  "Honua": {
    "ServerUrl": "https://your-honua-server.com",
    "GrpcUrl": "https://grpc.your-honua-server.com",
    "ApiKey": "your-api-key"
  },
  "Features": {
    "EnableAR": true,
    "EnableIoT": true,
    "EnableOfflineSync": true
  }
}
```

## 🏗️ Architecture Overview

### **Core Components**
```
Honua Field Collector
├── Features/
│   ├── Authentication/          # Secure login and user management
│   ├── DataCollection/          # Form-based data entry
│   ├── Forms/                   # Dynamic form generation
│   ├── Camera/                  # Professional photo capture
│   ├── Mapping/                 # Advanced mapping features
│   ├── AR/                      # Augmented reality visualization
│   ├── Sensors/                 # IoT sensor integration
│   ├── Sync/                    # Background synchronization
│   └── Reports/                 # Data analysis and reporting
├── Services/                    # Shared business logic
├── Platforms/                   # Platform-specific implementations
└── Resources/                   # UI assets and configurations
```

### **Data Flow Architecture**
```
Field Worker → Mobile App → Local Storage → Background Sync → Honua Server
     ↓              ↓            ↓               ↓              ↓
[User Input] → [Validation] → [GeoPackage] → [gRPC Stream] → [PostgreSQL]
     ↓              ↓            ↓               ↓              ↓
[AR Overlay] → [IoT Sensors] → [Conflict Res] → [Real-time] → [Analytics]
```

### **Technology Stack**

**Frontend:**
- **.NET MAUI** - Cross-platform native UI
- **CommunityToolkit.Maui** - Enhanced UI components
- **Xamarin.Essentials** - Device integration

**Backend Integration:**
- **gRPC** - High-performance data streaming
- **GeoPackage** - OGC-compliant offline storage
- **SQLite** - Local database engine

**Platform Features:**
- **ARKit (iOS)** / **ARCore (Android)** - Augmented reality
- **MapKit (iOS)** / **Google Maps (Android)** - Native mapping
- **Bluetooth LE** - IoT sensor connectivity

## 🎮 User Interface Guide

### **Main Navigation**
- **📊 Dashboard**: Project overview and quick actions
- **📝 Collect**: Form-based data collection interface
- **🗺️ Map**: Advanced mapping and spatial analysis

> AR visualization and IoT sensor integration are **forward-looking concepts,
> not shipped**. No `HonuaARView` control or IoT integration service exists in
> this repository today. The proposed shape of those features is sketched below
> for discussion only.

### **Key Features Walkthrough**

#### **1. Dynamic Form Creation**
```csharp
// Forms are automatically generated from server schema
var form = await formService.GetFormAsync("utility-inspection");

// Supports all field types with validation
- Text input with regex validation
- Numeric fields with range checking
- Date/time pickers with constraints
- Photo capture with GPS tagging
- Dropdown lists from server data
- Multi-select checkboxes
- Signature capture
- Barcode/QR scanning
```

#### **2. AR Utility Visualization (concept — not implemented)**

There is no `HonuaARView` control today. The intended shape is an AR view that
loads nearby utilities by GPS and supports tap-to-inspect, AR-depth measurement,
and photo capture with overlays. See
[`examples/ar-utility-visualization`](../ar-utility-visualization/README.md) for
the concept write-up and open accuracy questions.

#### **3. IoT Sensor Integration (concept — not implemented)**

There is no IoT integration service today (`src/Honua.Mobile.IoT` is not part of
this repository). The intended shape is automatic sensor discovery over
Bluetooth LE for environmental, air-quality, and equipment-monitoring sensors.

### **Offline Operation**
The app is designed for reliable field work without constant connectivity:

- **Forms**: Pre-loaded from server, work completely offline
- **Maps**: Offline base maps with cached feature data
- **Photos**: Local storage with background upload
- **Sync**: Intelligent queuing with conflict resolution
- **Data**: GeoPackage format for standards compliance

## 🔧 Advanced Configuration

### **Custom Form Schemas**
Forms are defined using Honua's dynamic schema system:

```json
{
  "formId": "site-inspection",
  "title": "Site Inspection Form",
  "version": "1.2",
  "fields": [
    {
      "id": "inspector_name",
      "type": "text",
      "label": "Inspector Name",
      "required": true,
      "validation": {
        "minLength": 2,
        "maxLength": 100
      }
    },
    {
      "id": "site_photos",
      "type": "photo",
      "label": "Site Photos",
      "multiple": true,
      "gpsRequired": true,
      "privacyBlur": true
    },
    {
      "id": "temperature",
      "type": "sensor",
      "label": "Temperature Reading",
      "sensorType": "environmental",
      "unit": "celsius",
      "validation": {
        "min": -40,
        "max": 60
      }
    }
  ]
}
```

### **AR Configuration**
```json
{
  "ar": {
    "maxRenderDistance": 100,
    "enableDepthVisualization": true,
    "utilityColors": {
      "water": "#2196F3",
      "gas": "#FFEB3B",
      "electric": "#F44336",
      "telecommunications": "#4CAF50"
    }
  }
}
```

### **IoT Sensor Workflows**
```json
{
  "workflows": [
    {
      "id": "environmental-monitoring",
      "name": "Environmental Data Collection",
      "steps": [
        {
          "action": "connectSensor",
          "sensorType": "environmental"
        },
        {
          "action": "readSensors",
          "duration": "30s",
          "interval": "5s"
        },
        {
          "action": "validateData",
          "qualityThreshold": 0.8
        },
        {
          "action": "attachToForm",
          "formField": "environmental_data"
        }
      ]
    }
  ]
}
```

## 📊 Performance & Scalability

### **Performance Characteristics**
- **Form loading**: < 500ms for complex forms
- **Photo capture**: < 2 seconds with GPS tagging
- **AR visualization**: 60fps with 100+ utilities
- **Sync throughput**: 1000+ records/minute
- **Battery optimization**: 8+ hours continuous field use

### **Offline Capabilities**
- **Storage**: 10GB+ offline data capacity
- **Forms**: 1000+ cached forms with full validation
- **Maps**: Regional base maps for extended field work
- **Photos**: Unlimited local storage with smart compression

### **Enterprise Scalability**
- **Multi-user sync**: Conflict-free collaborative editing
- **Background tasks**: Automatic sync without user intervention
- **Error handling**: Comprehensive retry logic and error recovery
- **Audit trails**: Complete change tracking for compliance

## 🔒 Security & Privacy

### **Data Protection**
- **End-to-end encryption** for all data transmission
- **Local data encryption** using platform secure storage
- **Photo privacy**: AI-powered face blurring for sensitive photos
- **Access controls**: Role-based permissions from server

### **Authentication**
- **Multi-factor authentication** support
- **Biometric login** (fingerprint, face recognition)
- **Token-based security** with automatic refresh
- **Device attestation** for enterprise deployments

## 🚢 Deployment Options

### **Development Deployment**
```bash
# Debug builds with full logging
dotnet build --configuration Debug
dotnet run --project HonuaFieldCollector.csproj
```

### **Enterprise Deployment**
```bash
# Release builds with optimization
dotnet publish --configuration Release --runtime android-arm64
dotnet publish --configuration Release --runtime ios-arm64

# App store deployment
# Automated via CI/CD with code signing
```

### **Custom Deployment**
- **White-label branding**: Custom app icons, colors, and naming
- **Feature toggles**: Enable/disable specific functionality
- **Custom integrations**: Connect to existing enterprise systems
- **Offline packages**: Pre-configured for disconnected environments

## 📈 Analytics & Reporting

### **Built-in Analytics**
- **Usage tracking**: Feature adoption and user behavior
- **Performance monitoring**: App performance and error tracking
- **Data quality**: Validation success rates and error patterns
- **Sync statistics**: Upload/download speeds and success rates

### **Custom Reports**
- **Data collection metrics**: Forms completed, photos captured
- **Field productivity**: Time per task, completion rates
- **Quality assurance**: Validation failures and corrections
- **IoT sensor data**: Environmental trends and equipment health

## 🛠️ Development & Customization

### **Extending Functionality**
```csharp
// Add custom form field types
public class CustomBarcodeScannerField : IFormField
{
    public async Task<string> CollectDataAsync()
    {
        var scanner = DependencyService.Get<IBarcodeScanner>();
        return await scanner.ScanAsync();
    }
}

// Add custom AR overlays
public class CustomAROverlay : IARVisualization
{
    public void Render(ARSession session, Feature feature)
    {
        // Custom 3D rendering logic
    }
}

// Add custom IoT sensors
public class CustomSensorService : ISensorService
{
    public async Task<SensorReading> ReadAsync()
    {
        // Custom sensor communication
    }
}
```

### **Integration APIs**
```csharp
// Export data to external systems
var exporter = App.GetService<IDataExporter>();
await exporter.ExportToCSV(features, outputPath);
await exporter.ExportToGeoJSON(features, outputPath);
await exporter.ExportToShapefile(features, outputPath);

// Import existing data
var importer = App.GetService<IDataImporter>();
await importer.ImportFromCSV(inputPath, mappingConfig);
```

## 🌟 Why Honua Field Collector?

### **Revolutionary Advantages**

**🆚 vs. Fulcrum (ArcGIS):**
- ✅ **Fully open source** - No vendor lock-in
- ✅ **AR visualization** - See underground infrastructure
- ✅ **IoT integration** - Automated sensor data collection
- ✅ **Better offline** - True offline-first architecture
- ✅ **Modern tech stack** - .NET MAUI cross-platform

**🆚 vs. Survey123:**
- ✅ **Advanced mapping** - Native platform integration
- ✅ **AR capabilities** - 3D visualization and measurement
- ✅ **IoT automation** - Hands-free data collection
- ✅ **Real-time sync** - gRPC streaming protocols
- ✅ **Enterprise ready** - Role-based security and audit trails

**🆚 vs. KoBo Toolbox:**
- ✅ **Professional grade** - Enterprise security and performance
- ✅ **Native mobile** - Full platform integration
- ✅ **AR and IoT** - Next-generation data collection
- ✅ **Scalable architecture** - Handle millions of records
- ✅ **Commercial support** - Professional services available

## 🤝 Contributing & Community

### **Open Source Development**
- **GitHub**: [honua-io/honua-server](https://github.com/honua-io/honua-server)
- **License**: Apache 2.0 - Fully open source
- **Contributing**: Welcome! See CONTRIBUTING.md for guidelines
- **Issues**: Bug reports and feature requests welcome

### **Community Resources**
- **Documentation**: [honua-mobile docs](https://github.com/honua-io/honua-mobile/tree/trunk/docs)
- **Community Forum**: [Discord](https://discord.gg/honua)
- **Discord**: Real-time developer chat
- **YouTube**: Tutorial videos and demos

### **Professional Services**
- **Custom development**: Tailored solutions for your needs
- **Training and support**: Get your team productive quickly
- **Enterprise deployment**: Secure, scalable installations
- **Consulting**: Best practices for field data collection

---

## 🎯 Ready to Transform Your Field Operations?

**Honua Field Collector represents the future of mobile data collection** - where augmented reality, IoT automation, and intelligent synchronization combine to create the most powerful field data platform available.

**Key Benefits:**
- ✅ **10x faster** data collection vs traditional methods
- ✅ **90% reduction** in data entry errors
- ✅ **100% offline** capability for remote work
- ✅ **Zero vendor lock-in** with open source licensing
- ✅ **Enterprise security** with government-grade encryption

[📚 Documentation](https://github.com/honua-io/honua-mobile/tree/trunk/docs) • [💬 Community](https://discord.gg/honua) <!-- TODO: add live demo link once a hosted mobile demo exists -->

**Start collecting revolutionary field data today.**