using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Projects;

namespace Honua.Mobile.FieldCollection.Services.Assignments;

public interface ILocalFieldAssignmentService
{
    Task<IReadOnlyList<LocalFieldAssignmentInfo>> GetAssignmentsAsync(
        LocalFieldAssignmentFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateStatusAsync(
        string assignmentId,
        FieldAssignmentStatus status,
        CancellationToken cancellationToken = default);
}

public sealed class LocalFieldAssignmentService : ILocalFieldAssignmentService
{
    private readonly GeoPackageStorageService _storage;

    public LocalFieldAssignmentService(GeoPackageStorageService storage)
    {
        _storage = storage;
    }

    public async Task<IReadOnlyList<LocalFieldAssignmentInfo>> GetAssignmentsAsync(
        LocalFieldAssignmentFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _storage.GetFieldAssignmentsAsync(filter).ConfigureAwait(false);
    }

    public async Task<bool> UpdateStatusAsync(
        string assignmentId,
        FieldAssignmentStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _storage.UpdateFieldAssignmentStatusAsync(assignmentId, status).ConfigureAwait(false);
    }
}
