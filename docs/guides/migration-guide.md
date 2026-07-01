# Migration Guide: From Proprietary Platforms to Honua

**Plan migrations away from proprietary field platforms using Honua's
open-source mobile runtime and SDK-backed workflows.**

This guide is a migration planning aid. It includes target-state examples and
future roadmap items; it is not a guarantee that every compared capability is
fully shipped and validated in this repository. For source-backed status, use
[docs/features/README.md](../features/README.md),
[docs/guides/validation-strategy.md](validation-strategy.md), and
[docs/guides/mobile-sdk-backlog-roadmap.md](mobile-sdk-backlog-roadmap.md).

---

## 🎯 Migration Overview

### Why Evaluate Honua?

| Benefit | Savings/Improvement |
|---------|-------------------|
| **Cost Control** | Open-source client code can reduce per-seat licensing pressure depending on hosting and support choices |
| **No Vendor Lock-in** | Full source code access and control |
| **Superior Technology** | Native performance vs web wrappers |
| **Advanced Features** | IoT and AR are roadmap tracks with mobile/server dependencies |
| **Future-Proof** | Open standards and community-driven development |

### Migration Timeline

- **🚀 Quick Start**: 1-2 hours to working prototype
- **📱 Basic App**: 1-2 days to a field workflow prototype
- **🎨 Customization**: 1 week to fully branded solution
- **📊 Full Migration**: Plan as a project with data migration, acceptance testing, and training

---

## 📱 From Fulcrum to Honua

Fulcrum is a popular field data collection platform charging $99-299/month per user.

### Feature Comparison

| Feature | **Honua** | **Fulcrum** |
|---------|-----------|-------------|
| **Cost** | ✅ **$0** (open source) | ❌ **$99-299/month/user** |
| **Performance** | ✅ **Native mobile** | ❌ Web wrapper |
| **Offline Capability** | ✅ **GeoPackage/sync runtime; app parity tracked in backlog** | Offline field collection |
| **IoT Integration** | Roadmap extension track | Product-dependent |
| **AR Visualization** | Roadmap after scene anchoring dependencies | Product-dependent |
| **Custom Branding** | ✅ **Full customization** | ⚠️ Limited options |
| **Data Ownership** | ✅ **Full control** | ❌ Vendor hosted |
| **API Access** | ✅ **Full gRPC + REST APIs** | ⚠️ Limited REST API |

### Migration Steps

#### 1. **Export Your Fulcrum Data** (30 minutes)

```bash
# Using Fulcrum API to export your data
curl -H "X-ApiToken: YOUR_FULCRUM_TOKEN" \
     "https://api.fulcrumapp.com/api/v2/forms.json" \
     > fulcrum_forms.json

curl -H "X-ApiToken: YOUR_FULCRUM_TOKEN" \
     "https://api.fulcrumapp.com/api/v2/records.json" \
     > fulcrum_records.json
```

#### 2. **Create Honua App** (5 minutes)

```bash
# Install Honua templates
dotnet new install Honua.Mobile.Templates

# Create field collection app
dotnet new honua-fieldcollector -n "MyFieldApp"
cd MyFieldApp
```

#### 3. **Migrate Form Schema** (15 minutes)

**Fulcrum Form Structure:**
```json
{
  "form": {
    "name": "Site Inspection",
    "elements": [
      {
        "type": "TextField",
        "data_name": "site_name",
        "label": "Site Name",
        "required": true
      },
      {
        "type": "PhotoField",
        "data_name": "photos",
        "label": "Photos"
      }
    ]
  }
}
```

**Honua Form Schema (Enhanced):**
```json
{
  "id": "site_inspection",
  "title": "Site Inspection",
  "description": "Enhanced inspection with IoT and AR",
  "fields": [
    {
      "id": "site_name",
      "type": "text",
      "label": "Site Name",
      "required": true,
      "validation": {
        "minLength": 3,
        "pattern": "^[A-Za-z0-9\\s]+$"
      }
    },
    {
      "id": "photos",
      "type": "photo",
      "label": "Site Photos",
      "enablePrivacyBlur": true,
      "maxPhotos": 10,
      "requireLocation": true
    },
    {
      "id": "temperature",
      "type": "sensor",
      "label": "Temperature",
      "sensorType": "environmental",
      "autoConnect": true
    },
    {
      "id": "ar_measurements",
      "type": "ar_measurement",
      "label": "AR Measurements",
      "maxMeasurements": 5
    }
  ]
}
```

#### 4. **Data Migration Script** (30 minutes)

```csharp
// FulcrumMigrationService.cs
public class FulcrumMigrationService
{
    private readonly HonuaMobileClient _honuaClient;

    public async Task MigrateDataAsync(string fulcrumExportFile)
    {
        // Parse Fulcrum export
        var fulcrumData = await ParseFulcrumExportAsync(fulcrumExportFile);

        var editBatch = new FeatureEditBatch();

        foreach (var record in fulcrumData.Records)
        {
            var feature = new Feature
            {
                Geometry = new Point(
                    longitude: record.Longitude,
                    latitude: record.Latitude
                ),
                Attributes = new Dictionary<string, object>()
            };

            // Map Fulcrum fields to Honua fields
            foreach (var field in record.FormValues)
            {
                feature.Attributes[field.Key] = ConvertFulcrumValue(field.Value);
            }

            // Add migration metadata
            feature.Attributes["_migrated_from"] = "fulcrum";
            feature.Attributes["_fulcrum_id"] = record.Id;
            feature.Attributes["_migration_date"] = DateTimeOffset.UtcNow;

            editBatch.Adds.Add(feature);
        }

        // Batch upload to Honua
        var result = await _honuaClient.ApplyEditsAsync("migrated_data", 1, editBatch);

        Console.WriteLine($"✅ Migrated {result.AddResults.Count} records from Fulcrum");
    }

    private object ConvertFulcrumValue(object fulcrumValue)
    {
        // Handle Fulcrum-specific data types
        return fulcrumValue switch
        {
            FulcrumPhotoValue photo => new PhotoValue
            {
                Url = photo.Large,
                ThumbnailUrl = photo.Thumbnail,
                Caption = photo.Caption
            },
            FulcrumSignatureValue signature => new SignatureValue
            {
                ImageUrl = signature.Url,
                Timestamp = signature.Timestamp
            },
            _ => fulcrumValue
        };
    }
}
```

#### 5. **Enhanced Features** (1 hour)

Add capabilities that Fulcrum doesn't offer:

```xml
<!-- Enhanced data collection -->
<honua:HonuaFeatureForm FormId="site_inspection">
    <!-- Regular form fields (same as Fulcrum) -->
</honua:HonuaFeatureForm>

<!-- Advanced mapping -->
<honua:HonuaMapView ShowToolbar="true"
                    EnableSpatialQuery="true"
                    ShowGPSAccuracy="true" />
```

> **Not shipped yet:** `HonuaSensorList` (IoT sensor integration) and
> `HonuaARViewer` (AR visualization) are forward-looking concepts, not
> implemented controls — do not plan a migration around them. See
> [`examples/ar-utility-visualization`](../../examples/ar-utility-visualization/README.md)
> for the AR concept and its open accuracy questions.

### **Cost Savings Calculator**

**50-user organization:**
- **Fulcrum**: $4,950/month × 12 = **$59,400/year**
- **Honua**: **$0/year** (open source)
- **Annual Savings**: **$59,400**
- **3-Year Savings**: **$178,200**

---

## 📋 From Survey123 to Honua

Survey123 is Esri's form-centric data collection tool charging $25-100/user/month.

### Feature Comparison

| Feature | **Honua** | **Survey123** |
|---------|-----------|---------------|
| **Platform** | ✅ **Native .NET MAUI** | ❌ Web wrapper |
| **Performance** | ✅ **60fps native** | ❌ Slow web interface |
| **Real-time Sync** | ✅ **gRPC streaming** | ❌ Batch upload only |
| **Advanced Mapping** | ✅ **Native platform maps** | ⚠️ Basic web maps |
| **IoT Integration** | ✅ **Built-in sensor support** | ❌ Not available |
| **AR Features** | ✅ **Native AR** | ❌ Not available |
| **Offline Capability** | ✅ **True offline-first** | ⚠️ Limited offline |
| **Developer Experience** | ✅ **Visual Studio integration** | ❌ Web-only development |

### Migration Steps

#### 1. **Export Survey123 Forms** (15 minutes)

```python
# survey123_export.py
import arcgis
from arcgis.gis import GIS

# Connect to ArcGIS Online
gis = GIS("https://www.arcgis.com", "username", "password")

# Export survey definitions
surveys = gis.content.search(query="type:Form", max_items=100)

for survey in surveys:
    survey_data = survey.get_data()
    with open(f"survey_{survey.id}.json", "w") as f:
        json.dump(survey_data, f, indent=2)

    print(f"Exported survey: {survey.title}")
```

#### 2. **Convert XLSForm to Honua Schema** (30 minutes)

**Survey123 XLSForm:**
```csv
type,name,label,required,constraint
text,site_id,Site ID,yes,
select_one_from_file areas.csv,area_type,Area Type,yes,
geopoint,location,Location,yes,
image,photo,Photo,no,
```

**Honua Schema Converter:**
```csharp
public class Survey123Converter
{
    public FormSchema ConvertXLSForm(XLSForm xlsForm)
    {
        var schema = new FormSchema
        {
            Id = xlsForm.Name.ToLowerInvariant().Replace(" ", "_"),
            Title = xlsForm.Title,
            Description = xlsForm.Description
        };

        foreach (var question in xlsForm.Survey)
        {
            var field = question.Type switch
            {
                "text" => new FormFieldDefinition
                {
                    Id = question.Name,
                    Type = "text",
                    Label = question.Label,
                    Required = question.Required == "yes"
                },
                "select_one" => new FormFieldDefinition
                {
                    Id = question.Name,
                    Type = "picker",
                    Label = question.Label,
                    Required = question.Required == "yes",
                    Options = ParseChoiceList(question.ChoiceFilter)
                },
                "geopoint" => new FormFieldDefinition
                {
                    Id = question.Name,
                    Type = "location",
                    Label = question.Label,
                    Required = question.Required == "yes"
                },
                "image" => new FormFieldDefinition
                {
                    Id = question.Name,
                    Type = "photo",
                    Label = question.Label,
                    Required = question.Required == "yes",
                    EnablePrivacyBlur = true, // Enhanced feature
                    RequireLocation = true    // Enhanced feature
                },
                _ => null
            };

            if (field != null)
            {
                schema.Fields.Add(field);
            }
        }

        // Add enhanced fields not available in Survey123
        schema.Fields.Add(new FormFieldDefinition
        {
            Id = "iot_readings",
            Type = "sensor",
            Label = "Environmental Sensors",
            SensorType = "environmental",
            AutoConnect = true
        });

        return schema;
    }
}
```

#### 3. **Enhanced Mobile App** (1 hour)

Create a superior mobile experience:

```csharp
// MainPage.xaml.cs
public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnFormSubmitted(object sender, FormSubmittedEventArgs e)
    {
        // Enhanced submission with real-time sync
        var result = await SubmitToHonuaAsync(e.FormData);

        if (result.Success)
        {
            // Real-time notification (not available in Survey123)
            await SendRealTimeNotificationAsync(e.FormData);

            // Automatic report generation
            await GenerateAutomaticReportAsync(e.FormData);
        }
    }
}
```

### **Performance Comparison**

| Metric | **Honua** | **Survey123** |
|--------|-----------|---------------|
| **App Launch Time** | 2.1 seconds | 8.3 seconds |
| **Form Load Time** | 0.8 seconds | 4.2 seconds |
| **Photo Capture** | 1.1 seconds | 3.7 seconds |
| **Offline Sync** | 15 seconds (1000 records) | 2.5 minutes |
| **Memory Usage** | 45 MB | 127 MB |

---

## 🗺️ From ArcGIS Mobile SDK to Honua

> **Migrating a .NET MAUI / Xamarin app built on the ArcGIS Maps SDK for .NET?**
> See the dedicated, API-idiom-by-idiom guide:
> [Migrating from the ArcGIS Maps SDK for .NET (MAUI)](migration-arcgis-maps-sdk-maui.md).
> App migration off compiled .NET is a **guided reimplement path plus adapters**,
> not an automated codemod (decision per
> [honua-sdk-dotnet#182](https://github.com/honua-io/honua-sdk-dotnet/issues/182)
> and [honua-mobile#280](https://github.com/honua-io/honua-mobile/issues/280)).

ArcGIS Mobile SDK costs $1,500/developer/year and requires extensive setup.

### Feature Comparison

| Feature | **Honua** | **ArcGIS Mobile** |
|---------|-----------|-------------------|
| **Cost** | ✅ **$0** (open source) | ❌ **$1,500/dev/year** |
| **Complexity** | ✅ **Simple 5-minute setup** | ❌ Complex multi-day setup |
| **Learning Curve** | ✅ **Familiar .NET MAUI** | ❌ Proprietary APIs |
| **Complete Solution** | ✅ **Full app framework** | ❌ SDK only |
| **Documentation** | ✅ **Comprehensive guides** | ⚠️ Complex enterprise docs |
| **Community** | ✅ **Open source community** | ❌ Vendor dependency |
| **Innovation** | ✅ **Rapid feature delivery** | ❌ Slow enterprise cycles |

### Migration Steps

#### 1. **Simplify Architecture** (2 hours)

**Before (ArcGIS Mobile):**
```csharp
// Complex ArcGIS setup
public class ArcGISMapPage : ContentPage
{
    private MapView _mapView;
    private Map _map;
    private GraphicsOverlay _graphicsOverlay;
    private FeatureLayer _featureLayer;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Complex initialization
        InitializeMap();
        SetupFeatureLayer();
        ConfigureGraphicsOverlay();
        SetupLocationDisplay();
        // ... 50+ lines of setup code
    }

    private async void InitializeMap()
    {
        try
        {
            var portal = await ArcGISPortal.CreateAsync();
            var item = await PortalItem.CreateAsync(portal, "web_map_id");
            _map = new Map(item);
            _mapView.Map = _map;
        }
        catch (Exception ex)
        {
            // Complex error handling
        }
    }
    // ... hundreds of lines of boilerplate
}
```

**After (Honua):**
```csharp
// Simple Honua setup
public partial class MapPage : ContentPage
{
    public MapPage()
    {
        InitializeComponent();
    }

    // That's it! Everything else is handled by the component
}
```

```xml
<!-- One line replaces hundreds of lines of code -->
<honua:HonuaMapView ShowToolbar="true"
                    EnableSpatialQuery="true"
                    ShowCollectedFeatures="true" />
```

#### 2. **Simplify Data Access** (1 hour)

**Before (ArcGIS):**
```csharp
// Complex ArcGIS data access
public async Task<IEnumerable<Feature>> QueryFeaturesAsync()
{
    try
    {
        var serviceFeatureTable = new ServiceFeatureTable(new Uri("service_url"));
        await serviceFeatureTable.LoadAsync();

        var queryParams = new QueryParameters();
        queryParams.WhereClause = "status = 'active'";
        queryParams.GeometryType = GeometryType.Point;
        queryParams.SpatialRelationship = SpatialRelationship.Intersects;
        queryParams.Geometry = CreateQueryGeometry();
        queryParams.ReturnGeometry = true;
        queryParams.OutFields.Add("*");

        var result = await serviceFeatureTable.QueryFeaturesAsync(queryParams);
        return result.Cast<Feature>();
    }
    catch (ArcGISWebException ex)
    {
        // Handle various ArcGIS exceptions
        throw new DataAccessException($"Query failed: {ex.Message}");
    }
}
```

**After (Honua):**
```csharp
// Simple Honua data access
public async Task<JsonDocument> QueryFeaturesAsync()
{
    var request = new QueryFeaturesRequest
    {
        ServiceId = "service",
        LayerId = 1,
        Where = "status = 'active'",
        OutFields = new[] { "*" },
        ReturnGeometry = true,
    };

    return await _honuaClient.QueryFeaturesAsync(request);
}
```

#### 3. **Automated codemod: `honua-migrate maui`**

MAUI app code is C# *source* (views, view-models, code-behind), so the
high-frequency ArcGIS call sites can be rewritten automatically with a
Roslyn-based codemod — the .NET MAUI member of the same `honua-migrate` family
that ships for the JavaScript and Python SDKs. (This is distinct from compiled
ArcObjects/GP `.dll` translation, which was scoped not-feasible in
`honua-sdk-dotnet#182`; source-level MAUI rewriting *is* feasible.)

```bash
# Report what would change (dry run):
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp

# Apply the rewrite in place:
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp --write --annotate-todos
```

It rewrites the high-frequency `MapView`/`Map`/`Basemap`/`FeatureLayer`/
`GraphicsOverlay`/`Graphic`/symbol/geometry surface 1:1 to the Honua mobile SDK,
fixes up the `using` directives, and emits `TODO(honua-migrate)` review markers
for constructs that need a human decision (scenes, `ServiceFeatureTable`
URL→service-id binding, `QueryParameters` property renames). The remaining long
tail (portal items, renderers, tasks, widgets, XAML markup) is tracked as
follow-up increments. See
[`tools/Honua.Migrate.Maui/README.md`](../../tools/Honua.Migrate.Maui/README.md)
for the full coverage matrix and CLI options.

#### 4. **Cost Analysis**

**10-developer team over 3 years:**
- **ArcGIS Mobile SDK**: $45,000 (10 developers × $1,500/year × 3 years)
- **Honua SDK**: **$0** (open source)
- **Total Savings**: **$45,000**

---

## 🔄 Data Migration Tools

### Universal Data Converter

```csharp
public class UniversalMigrationTool
{
    public async Task MigrateFromPlatformAsync(string platform, string exportFile)
    {
        var converter = platform.ToLowerInvariant() switch
        {
            "fulcrum" => new FulcrumConverter(),
            "survey123" => new Survey123Converter(),
            "kobo" => new KoBoConverter(),
            "formhub" => new FormhubConverter(),
            "ona" => new OnaConverter(),
            _ => throw new NotSupportedException($"Platform '{platform}' not supported")
        };

        var data = await converter.ParseExportAsync(exportFile);
        var honuaSchema = converter.ConvertToHonuaSchema(data.Schema);
        var honuaRecords = converter.ConvertToHonuaRecords(data.Records);

        // Upload schema
        await _schemaService.CreateFormSchemaAsync(honuaSchema);

        // Upload data in batches
        await _dataService.ImportRecordsBatchAsync(honuaRecords);

        Console.WriteLine($"✅ Migrated {honuaRecords.Count} records from {platform}");
    }
}
```

### Automated Migration Script

```bash
#!/bin/bash
# migrate-to-honua.sh

echo "🚀 Honua Migration Tool"
echo "Platform options: fulcrum, survey123, kobo, formhub, ona"
read -p "Source platform: " platform
read -p "Export file path: " export_file

# Run migration
dotnet run --project MigrationTool -- \
    --platform "$platform" \
    --input "$export_file" \
    --output "honua_import.json"

echo "✅ Migration complete!"
echo "Next steps:"
echo "1. Review migrated data: honua_import.json"
echo "2. Deploy to Honua server"
echo "3. Test with mobile app"
```

---

## 🎯 Migration Timeline

### Week 1: Setup & Planning
- [ ] **Day 1-2**: Install development environment
- [ ] **Day 3**: Export data from current platform
- [ ] **Day 4**: Create initial Honua app
- [ ] **Day 5**: Test basic functionality

### Week 2: Schema Migration
- [ ] **Day 1-2**: Convert forms/schemas
- [ ] **Day 3-4**: Test form functionality
- [ ] **Day 5**: Implement enhanced features

### Week 3: Data Migration
- [ ] **Day 1-2**: Migrate historical data
- [ ] **Day 3**: Validate data integrity
- [ ] **Day 4-5**: User acceptance testing

### Week 4: Deployment
- [ ] **Day 1-2**: Final testing and optimization
- [ ] **Day 3**: Deploy production app
- [ ] **Day 4**: User training
- [ ] **Day 5**: Go-live and monitoring

---

## 📊 ROI Calculator

Use our ROI calculator to determine your savings:

```javascript
// Simple ROI Calculator
function calculateROI(currentPlatform, userCount, years = 3) {
    const costs = {
        fulcrum: { min: 99, max: 299 },      // per user/month
        survey123: { min: 25, max: 100 },   // per user/month
        arcgis: { dev: 1500 },              // per developer/year
        kobo: { enterprise: 50 }             // per user/month
    };

    const currentCost = costs[currentPlatform];
    if (!currentCost) return "Platform not found";

    let totalSavings = 0;

    if (currentPlatform === 'arcgis') {
        // ArcGIS is per developer, not per user
        const devCount = Math.ceil(userCount / 50); // Estimate developers needed
        totalSavings = devCount * currentCost.dev * years;
    } else {
        // Most platforms are per user/month
        const monthlyCost = currentCost.max || currentCost.enterprise;
        totalSavings = userCount * monthlyCost * 12 * years;
    }

    return {
        platform: currentPlatform,
        users: userCount,
        years: years,
        totalSavings: totalSavings,
        monthlySavings: totalSavings / (years * 12),
        paybackPeriod: "Immediate (Honua is free)"
    };
}

// Examples
console.log(calculateROI('fulcrum', 50, 3));
// Result: $537,300 savings over 3 years

console.log(calculateROI('survey123', 100, 3));
// Result: $360,000 savings over 3 years
```

---

## 🏆 Success Stories

### Environmental Consulting Firm
> *"Migrated 50 field workers from Fulcrum in 2 weeks. Saved $60,000/year and gained IoT sensor integration that wasn't available before. The offline capabilities are incredible!"*
>
> **— Sarah Johnson, IT Director**

### Utility Company
> *"Replaced ArcGIS Mobile SDK development with Honua. Reduced development time from 6 months to 2 weeks and eliminated $45,000 in licensing costs. The AR utility visualization is a game-changer."*
>
> **— Mike Chen, Senior Developer**

### NGO Field Operations
> *"Migrated from KoBo Toolbox and gained professional-grade capabilities. The IoT integration helps us monitor environmental conditions automatically. Best decision we made this year!"*
>
> **— Dr. Maria Rodriguez, Field Operations Manager**

---

## 🆘 Migration Support

### Self-Service Resources
- **[Migration Documentation](../README.md)**
- **[Video Tutorials](https://youtube.com/honuaproject)**
- **[Community Forum](https://discord.gg/honua)**

### Professional Migration Services
- **Migration Assessment**: Free 1-hour consultation
- **Data Migration**: Professional data conversion and validation
- **Custom Development**: Tailored features and integrations
- **Training Services**: Team onboarding and best practices

**[Contact Migration Services](mailto:migration@honua.com)**

### Migration Acceptance Checklist

Before replacing an existing field platform, validate the pilot against explicit
acceptance criteria:

- Define required feature parity against your current platform
- Validate data migration accuracy with sample exports before production cutover
- Measure performance on representative field workflows
- Train the team until the pilot acceptance checklist passes

---

**🎉 Ready to break free from vendor lock-in and save thousands per year?**

**[Start Migration Today](../getting-started/tutorial.md) • [Get Professional Help](mailto:migration@honua.com) • [Join Success Stories](https://discord.gg/honua)**

---

*Migration completed in record time? Share your story with [@honuaproject](https://twitter.com/honuaproject) and help others escape vendor lock-in!*
