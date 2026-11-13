using Honua.Mobile.Maui.Location;
using Honua.Sdk.Field.Records;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace Honua.Mobile.Maui.Tests;

public sealed class FieldLocationMetadataMapperTests
{
    [Fact]
    public void FromMauiLocation_MapsExternalGnssEvidenceAndRecordAttributes()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 23, 9, 15, 0, TimeSpan.Zero);
        var location = new MauiLocation(21.3069, -157.8583, capturedAt)
        {
            Accuracy = 0.8,
            VerticalAccuracy = 1.6,
            Altitude = 12.5,
            Speed = 0.4,
            Course = 45
        };

        var fix = FieldLocationMetadataMapper.FromMauiLocation(
            location,
            new FieldLocationCaptureMetadata
            {
                SourceKind = FieldLocationSourceKind.ExternalGnss,
                Provider = "bluetooth-nmea",
                Receiver = new FieldLocationReceiverMetadata
                {
                    Name = "External receiver",
                    Manufacturer = "Receiver manufacturer",
                    Model = "Receiver model",
                    FirmwareVersion = "6.25",
                    SerialNumber = "receiver-42",
                    IsExternal = true
                },
                Properties =
                {
                    ["correction"] = "rtk-fixed",
                    ["datum"] = "WGS84"
                }
            });

        var evidence = fix.ToEvidence();
        var attributes = evidence.ToAttributes("gps");

        Assert.Equal(FieldLocationSourceKind.ExternalGnss, fix.SourceKind);
        Assert.Equal(FieldLocationSourceKind.ExternalGnss, evidence.SourceKind);
        Assert.Equal(0.8, evidence.HorizontalAccuracyMeters);
        Assert.Equal(1.6, evidence.VerticalAccuracyMeters);
        Assert.Equal("bluetooth-nmea", evidence.Provider);
        Assert.Equal("External receiver", evidence.Receiver?.Name);
        Assert.Equal("ExternalGnss", attributes["gps_source"]);
        Assert.Equal("External GNSS", attributes["gps_source_label"]);
        Assert.Equal(0.8, attributes["gps_accuracy_m"]);
        Assert.Equal("External receiver", attributes["gps_receiver_name"]);
        Assert.Contains("External GNSS", FieldLocationMetadataMapper.FormatEvidence(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public void FromMauiLocation_UsesBuiltInGpsFallbackWhenNoProviderHintsExist()
    {
        var fix = FieldLocationMetadataMapper.FromMauiLocation(new MauiLocation(21.3, -157.8)
        {
            Accuracy = 9
        });

        Assert.Equal(FieldLocationSourceKind.BuiltInGps, fix.SourceKind);
        Assert.Equal("Built-in GPS", FieldLocationMetadataMapper.FormatSource(fix.SourceKind));
    }

    [Fact]
    public void PersistedEvidence_FromFieldAppRemainsReadableAfterSdkMove()
    {
        // Existing record attributes contain JSON without CLR namespace metadata.
        const string persisted = """
            {"latitude":21.3069,"longitude":-157.8583,"horizontalAccuracyMeters":0.8,
             "capturedAtUtc":"2026-05-23T09:15:00+00:00","sourceKind":"externalGnss",
             "provider":"bluetooth-nmea","receiver":{"name":"External receiver",
             "serialNumber":"receiver-42","isExternal":true,"properties":{"antenna":"pole"}},
             "properties":{"correction":"rtk-fixed","datum":"WGS84"}}
            """;

        var evidence = Assert.IsType<FieldLocationCaptureEvidence>(
            FieldLocationMetadataMapper.DeserializeEvidence(persisted));

        Assert.Equal(new FieldGeoPoint(21.3069, -157.8583, 0.8), evidence.ToFieldGeoPoint());
        Assert.Equal(new DateTimeOffset(2026, 5, 23, 9, 15, 0, TimeSpan.Zero), evidence.CapturedAtUtc);
        Assert.Equal(FieldLocationSourceKind.ExternalGnss, evidence.SourceKind);
        Assert.Equal("receiver-42", evidence.Receiver?.SerialNumber);
        Assert.Equal("pole", evidence.Receiver?.Properties["antenna"]);
        Assert.Equal("rtk-fixed", evidence.Properties["correction"]);

        var restored = Assert.IsType<FieldLocationCaptureEvidence>(
            FieldLocationMetadataMapper.DeserializeEvidence(FieldLocationMetadataMapper.SerializeEvidence(evidence)));
        Assert.Equal(evidence.ToFieldGeoPoint(), restored.ToFieldGeoPoint());
        Assert.Equal(evidence.CapturedAtUtc, restored.CapturedAtUtc);
        Assert.Equal(evidence.SourceKind, restored.SourceKind);
        Assert.Equal(evidence.Provider, restored.Provider);
        Assert.Equal(evidence.Receiver?.SerialNumber, restored.Receiver?.SerialNumber);
        Assert.Equal(evidence.Receiver?.Properties["antenna"], restored.Receiver?.Properties["antenna"]);
        Assert.Equal(evidence.Properties["datum"], restored.Properties["datum"]);
    }

    [Fact]
    public void MatchesFieldLocation_RequiresCapturedPointAndAccuracyToMatch()
    {
        var evidence = new FieldLocationCaptureEvidence
        {
            Latitude = 21.3069,
            Longitude = -157.8583,
            HorizontalAccuracyMeters = 0.8,
            SourceKind = FieldLocationSourceKind.ExternalGnss
        };

        Assert.True(FieldLocationMetadataMapper.MatchesFieldLocation(
            new FieldGeoPoint(21.3069, -157.8583, 0.8),
            evidence));
        Assert.False(FieldLocationMetadataMapper.MatchesFieldLocation(
            new FieldGeoPoint(21.3070, -157.8583, 0.8),
            evidence));
        Assert.False(FieldLocationMetadataMapper.MatchesFieldLocation(
            new FieldGeoPoint(21.3069, -157.8583, null),
            evidence));
    }
}
