namespace Honua.Mobile.FieldCollection.Services;

public interface ILocalRecordExportShareService
{
    Task ShareExportAsync(LocalRecordExportResult export, CancellationToken cancellationToken = default);
}

public sealed class NoOpLocalRecordExportShareService : ILocalRecordExportShareService
{
    public Task ShareExportAsync(LocalRecordExportResult export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
