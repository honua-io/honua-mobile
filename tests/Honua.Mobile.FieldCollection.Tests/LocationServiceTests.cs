using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Microsoft.Maui.Devices.Sensors;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocationServiceTests
{
    [Fact]
    public async Task BuildLocationFixAsync_PreservesGpsFixWhenMetadataProviderThrows()
    {
        var location = new Location(21.3069, -157.8583, new DateTimeOffset(2026, 5, 23, 8, 0, 0, TimeSpan.Zero))
        {
            Accuracy = 2.5
        };
        var service = new LocationService(new ThrowingMetadataProvider(new InvalidOperationException("nmea parse failed")));

        var fix = await service.BuildLocationFixAsync(location, CancellationToken.None);

        Assert.NotNull(fix);
        Assert.Same(location, fix.Location);
        Assert.Equal(FieldLocationSourceKind.BuiltInGps, fix.SourceKind);
        Assert.Equal(2.5, fix.ToEvidence().HorizontalAccuracyMeters);
    }

    [Fact]
    public async Task BuildLocationFixAsync_DoesNotSwallowMetadataCancellation()
    {
        var location = new Location(21.3069, -157.8583);
        var service = new LocationService(new ThrowingMetadataProvider(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.BuildLocationFixAsync(location, CancellationToken.None));
    }

    private sealed class ThrowingMetadataProvider : IHighAccuracyLocationMetadataProvider
    {
        private readonly Exception _exception;

        public ThrowingMetadataProvider(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<FieldLocationCaptureMetadata?> GetMetadataAsync(
            Location location,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }
}
