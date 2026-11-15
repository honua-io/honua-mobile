# Migration Guide: Moving Field Workflows to Honua

**Plan a move of existing field forms, records and app code to Honua's
open-source mobile runtime and SDK-backed workflows.**

This guide is a migration planning aid. It includes target-state examples and
future roadmap items; it is not a guarantee that every described capability is
fully shipped and validated in this repository. For source-backed status, use
[docs/features/README.md](../features/README.md),
[docs/guides/validation-strategy.md](validation-strategy.md), and
[docs/guides/mobile-sdk-backlog-roadmap.md](mobile-sdk-backlog-roadmap.md).

---

## 🎯 Migration Overview

### Migration Timeline

- **🚀 Quick Start**: 1-2 hours to working prototype
- **📱 Basic App**: 1-2 days to a field workflow prototype
- **🎨 Customization**: 1 week to fully branded solution
- **📊 Full Migration**: Plan as a project with data migration, acceptance testing, and training

---

## 📱 From Fulcrum to Honua

The steps below export Fulcrum forms and records and map them to a Honua form
schema and feature records.

### Migration Steps

#### 1. **Export Your Fulcrum Data** (30 minutes)

Use Fulcrum's supported export workflow to download the form definition and
records as `fulcrum_forms.json` and `fulcrum_records.json`. Keep attachments in
the export directory so the later import can preserve their relative paths.

`honua-migrate` does not currently claim a Fulcrum source adapter. Do not send
Fulcrum tokens through hand-written raw HTTP commands; use Fulcrum's product
export or an approved Fulcrum SDK until that adapter is available.

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

#### 5. **Add Map and Form Controls** (1 hour)

Add the Honua map and form controls:

```xml
<!-- Enhanced data collection -->
<honua:HonuaFeatureForm FormId="site_inspection">
    <!-- Form fields from the migrated schema -->
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

---

## 📋 From Survey123 to Honua

Survey123 forms are authored as XLSForm. The steps below export each survey
definition and convert it to a Honua form schema.

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

        // Add Honua-specific fields
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

#### 3. **Handle Submissions** (1 hour)

Handle submissions in the app:

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
            // Notify subscribers after a successful submission
            await SendRealTimeNotificationAsync(e.FormData);

            // Automatic report generation
            await GenerateAutomaticReportAsync(e.FormData);
        }
    }
}
```

---

## 🗺️ From ArcGIS Mobile SDK to Honua

> **Migrating a .NET MAUI / Xamarin app built on the ArcGIS Maps SDK for .NET?**
> See the dedicated, API-idiom-by-idiom guide:
> [Migrating from the ArcGIS Maps SDK for .NET (MAUI)](migration-arcgis-maps-sdk-maui.md).
> App migration off compiled .NET is a **guided reimplement path plus adapters**,
> not an automated codemod (decision per
> [honua-sdk-dotnet#182](https://github.com/honua-io/honua-sdk-dotnet/issues/182)
> and [honua-mobile#280](https://github.com/honua-io/honua-mobile/issues/280)).

### Migration Steps

#### 1. **Port Map Setup** (2 hours)

**Before (ArcGIS Mobile):**
```csharp
// ArcGIS Maps SDK setup
public class ArcGISMapPage : ContentPage
{
    private MapView _mapView;
    private Map _map;
    private GraphicsOverlay _graphicsOverlay;
    private FeatureLayer _featureLayer;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Map initialization
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
            // Error handling
        }
    }
}
```

**After (Honua):**
```csharp
// Honua setup
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

#### 2. **Port Data Access** (1 hour)

**Before (ArcGIS):**
```csharp
// ArcGIS Maps SDK data access
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
// Honua data access
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

## 🆘 Migration Support

### Self-Service Resources
- **[Mobile SDK documentation](../README.md)**
- **[Feature status](../features/README.md)**
- **[Validation strategy](validation-strategy.md)**

### Migration Acceptance Checklist

Before replacing an existing field platform, validate the pilot against explicit
acceptance criteria:

- Define required feature parity against your current platform
- Validate data migration accuracy with sample exports before production cutover
- Measure performance on representative field workflows
- Train the team until the pilot acceptance checklist passes
