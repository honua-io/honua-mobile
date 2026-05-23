using Honua.Mobile.FieldCollection.Models;
using Microsoft.Maui.Devices.Sensors;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldLocationMetadataMapperTests
{
    [Fact]
    public void FromMauiLocation_MapsExternalGnssEvidenceAndRecordAttributes()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 23, 9, 15, 0, TimeSpan.Zero);
        var location = new Location(21.3069, -157.8583, capturedAt)
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
                    Name = "Trimble R12",
                    Manufacturer = "Trimble",
                    Model = "R12",
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
        Assert.Equal("Trimble R12", evidence.Receiver?.Name);
        Assert.Equal("ExternalGnss", attributes["gps_source"]);
        Assert.Equal("External GNSS", attributes["gps_source_label"]);
        Assert.Equal(0.8, attributes["gps_accuracy_m"]);
        Assert.Equal("Trimble R12", attributes["gps_receiver_name"]);
        Assert.Contains("External GNSS", FieldLocationMetadataMapper.FormatEvidence(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public void FromMauiLocation_UsesBuiltInGpsFallbackWhenNoProviderHintsExist()
    {
        var fix = FieldLocationMetadataMapper.FromMauiLocation(new Location(21.3, -157.8)
        {
            Accuracy = 9
        });

        Assert.Equal(FieldLocationSourceKind.BuiltInGps, fix.SourceKind);
        Assert.Equal("Built-in GPS", FieldLocationMetadataMapper.FormatSource(fix.SourceKind));
    }
}
