using Honua.Mobile.FieldCollection.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Honua.Mobile.FieldCollection.Services.Sharing;

public sealed class MauiLocalRecordExportShareService : ILocalRecordExportShareService
{
    public async Task ShareExportAsync(LocalRecordExportResult export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        cancellationToken.ThrowIfCancellationRequested();

        var files = new[]
            {
                export.CsvPath,
                export.GeoJsonPath,
                export.AttachmentManifestPath,
                export.EvidenceManifestPath
            }
            .Where(File.Exists)
            .Select(path => new ShareFile(path))
            .ToList();

        if (files.Count == 0)
        {
            throw new FileNotFoundException("No export artifacts are available to share.", export.ExportDirectory);
        }

        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = $"Honua export - {export.LayerName}",
            Files = files
        });
    }
}
