using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Storage;
using StorageSyncSession = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncSession;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

namespace Honua.Mobile.FieldCollection.ViewModels;

public interface IRouteAwareViewModel
{
    void ApplyQueryAttributes(IDictionary<string, object> query);
    Task OnNavigatedToAsync();
}

public sealed class AttributeDisplayItem
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed partial class EditableAttributeItem : ObservableObject
{
    [ObservableProperty]
    private string key = string.Empty;

    [ObservableProperty]
    private string valueText = string.Empty;
}

public partial class RecordDetailViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;

    [ObservableProperty]
    private string featureId = string.Empty;

    [ObservableProperty]
    private int layerId;

    [ObservableProperty]
    private Feature? feature;

    [ObservableProperty]
    private string geometrySummary = "No geometry";

    public ObservableCollection<AttributeDisplayItem> Attributes { get; } = [];

    public RecordDetailViewModel(INavigationService navigationService, IFeatureService featureService)
        : base(navigationService)
    {
        _featureService = featureService;
        Title = "Record Detail";
    }

    public virtual void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId == 0 ? 1 : LayerId);
        FeatureId = RouteQuery.GetString(query, "featureId", FeatureId);
    }

    public virtual Task OnNavigatedToAsync() => LoadRecord();

    [RelayCommand]
    protected async Task LoadRecord()
    {
        if (LayerId <= 0 || string.IsNullOrWhiteSpace(FeatureId))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            Feature = await _featureService.GetFeatureAsync(LayerId, FeatureId);
            Attributes.Clear();

            if (Feature == null)
            {
                GeometrySummary = "Record not found";
                return;
            }

            foreach (var attribute in Feature.Attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
            {
                Attributes.Add(new AttributeDisplayItem
                {
                    Key = attribute.Key,
                    Value = FormatValue(attribute.Value)
                });
            }

            GeometrySummary = FormatGeometry(Feature.Geometry);
        });
    }

    [RelayCommand]
    private async Task EditRecord()
    {
        if (Feature == null)
        {
            return;
        }

        await NavigationService.NavigateToAsync(
            "record-edit",
            new Dictionary<string, object>
            {
                ["layerId"] = Feature.LayerId,
                ["featureId"] = Feature.Id,
                ["isEdit"] = true
            });
    }

    [RelayCommand]
    private async Task DeleteRecord()
    {
        if (Feature == null)
        {
            return;
        }

        var confirmed = await ShowConfirmation(
            "Delete Record",
            $"Delete {Feature.DisplayTitle}?",
            "Delete",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _featureService.DeleteFeatureAsync(Feature.LayerId, Feature.Id);
            await NavigationService.GoBackAsync();
        });
    }

    private static string FormatGeometry(Geometry? geometry)
    {
        return geometry switch
        {
            FieldPoint point => $"Point {point.Latitude:F6}, {point.Longitude:F6}",
            LineString line => $"Line with {line.Coordinates.Count} vertices",
            Polygon polygon => $"Polygon with {polygon.Coordinates.Sum(ring => ring.Count)} vertices",
            null => "No geometry",
            _ => geometry.Type
        };
    }

    internal static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("u"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("u"),
            bool boolean => boolean ? "Yes" : "No",
            _ => value.ToString() ?? string.Empty
        };
    }
}

public sealed partial class FeatureDetailViewModel : RecordDetailViewModel
{
    public FeatureDetailViewModel(INavigationService navigationService, IFeatureService featureService)
        : base(navigationService, featureService)
    {
        Title = "Feature Detail";
    }
}

public sealed partial class RecordEditViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;

    [ObservableProperty]
    private int layerId = 1;

    [ObservableProperty]
    private string featureId = string.Empty;

    [ObservableProperty]
    private bool isNew = true;

    [ObservableProperty]
    private string pageTitle = "Create Record";

    [ObservableProperty]
    private string geometrySummary = "No geometry";

    private FieldPoint? _location;
    private Feature? _existingFeature;

    public ObservableCollection<EditableAttributeItem> Attributes { get; } = [];

    public RecordEditViewModel(INavigationService navigationService, IFeatureService featureService)
        : base(navigationService)
    {
        _featureService = featureService;
        Title = "Create Record";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId);
        FeatureId = RouteQuery.GetString(query, "featureId", FeatureId);
        IsNew = RouteQuery.GetBool(query, "isNew", string.IsNullOrWhiteSpace(FeatureId));
        _location = RouteQuery.GetValue<FieldPoint>(query, "location");

        if (RouteQuery.GetBool(query, "isEdit", false))
        {
            IsNew = false;
        }
    }

    public Task OnNavigatedToAsync() => LoadDraft();

    [RelayCommand]
    private async Task LoadDraft()
    {
        await ExecuteAsync(async () =>
        {
            Attributes.Clear();
            PageTitle = IsNew ? "Create Record" : "Edit Record";
            Title = PageTitle;

            if (!IsNew && !string.IsNullOrWhiteSpace(FeatureId))
            {
                _existingFeature = await _featureService.GetFeatureAsync(LayerId, FeatureId);
            }

            var source = _existingFeature?.Attributes ?? CreateDefaultAttributes();
            foreach (var attribute in source.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
            {
                Attributes.Add(new EditableAttributeItem
                {
                    Key = attribute.Key,
                    ValueText = RecordDetailViewModel.FormatValue(attribute.Value)
                });
            }

            GeometrySummary = FormatGeometry(_existingFeature?.Geometry ?? _location);
        });
    }

    [RelayCommand]
    private void AddAttribute()
    {
        var index = Attributes.Count + 1;
        Attributes.Add(new EditableAttributeItem
        {
            Key = $"field_{index}",
            ValueText = string.Empty
        });
    }

    [RelayCommand]
    private void RemoveAttribute(EditableAttributeItem item)
    {
        Attributes.Remove(item);
    }

    [RelayCommand]
    private async Task SaveRecord()
    {
        await ExecuteAsync(async () =>
        {
            var feature = _existingFeature ?? new Feature
            {
                Id = string.IsNullOrWhiteSpace(FeatureId) ? Guid.NewGuid().ToString("N") : FeatureId,
                LayerId = LayerId,
                Geometry = _location
            };

            feature.LayerId = LayerId;
            feature.Attributes = Attributes
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .GroupBy(attribute => attribute.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => ParseAttributeValue(group.Last().ValueText),
                    StringComparer.OrdinalIgnoreCase);

            if (IsNew)
            {
                await _featureService.CreateFeatureAsync(LayerId, feature);
            }
            else
            {
                await _featureService.UpdateFeatureAsync(LayerId, feature);
            }

            FeatureId = feature.Id;
            IsNew = false;
            await NavigationService.NavigateToAsync(
                "record-detail",
                new Dictionary<string, object>
                {
                    ["layerId"] = LayerId,
                    ["featureId"] = FeatureId
                });
        });
    }

    [RelayCommand]
    private Task Cancel() => NavigationService.GoBackAsync();

    private static Dictionary<string, object?> CreateDefaultAttributes() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = string.Empty,
            ["status"] = "new",
            ["notes"] = string.Empty
        };

    private static object? ParseAttributeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (long.TryParse(value, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, out var number))
        {
            return number;
        }

        return value;
    }

    private static string FormatGeometry(Geometry? geometry)
    {
        return geometry switch
        {
            FieldPoint point => $"Point {point.Latitude:F6}, {point.Longitude:F6}",
            null => "No geometry",
            _ => geometry.Type
        };
    }
}

public sealed partial class AuthenticationViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Not signed in";

    [ObservableProperty]
    private bool isAuthenticated;

    public AuthenticationViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "Authentication";
        RefreshState();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        RefreshState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ValidateConnection()
    {
        await ExecuteAsync(async () =>
        {
            var valid = await _authService.ValidateConnectionAsync(ServerUrl, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey);
            StatusMessage = valid ? "Connection validated" : "Connection failed";
        });
    }

    [RelayCommand]
    private async Task SignIn()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _authService.AuthenticateAsync(ServerUrl, ApiKey);
            StatusMessage = result.IsSuccess ? $"Signed in as {result.UserName}" : result.ErrorMessage ?? "Sign-in failed";
            RefreshState();
        });
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.LogoutAsync();
        RefreshState();
    }

    private void RefreshState()
    {
        ServerUrl = _authService.ServerUrl ?? ServerUrl;
        ApiKey = _authService.ApiKey ?? ApiKey;
        IsAuthenticated = _authService.IsAuthenticated;
        StatusMessage = IsAuthenticated
            ? $"Signed in as {_authService.CurrentUserName ?? _authService.CurrentUserId ?? "current user"}"
            : "Not signed in";
    }
}

public sealed partial class DiagnosticsViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly DiagnosticService _diagnosticService;

    [ObservableProperty]
    private string summary = "Diagnostics not loaded";

    [ObservableProperty]
    private string exportPath = string.Empty;

    public DiagnosticsViewModel(INavigationService navigationService, DiagnosticService diagnosticService)
        : base(navigationService)
    {
        _diagnosticService = diagnosticService;
        Title = "Diagnostics";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync() => LoadDiagnostics();

    [RelayCommand]
    private async Task LoadDiagnostics()
    {
        await ExecuteAsync(UpdateSummaryAsync);
    }

    [RelayCommand]
    private async Task ExportDiagnostics()
    {
        await ExecuteAsync(async () =>
        {
            ExportPath = await _diagnosticService.ExportDiagnosticsAsync();
        });
    }

    [RelayCommand]
    private async Task CompactDatabase()
    {
        await ExecuteAsync(async () =>
        {
            var compacted = await _diagnosticService.CompactDatabaseAsync();
            Summary = compacted ? "Database compacted" : "Database compaction was not needed";
            await UpdateSummaryAsync();
        });
    }

    private async Task UpdateSummaryAsync()
    {
        var report = await _diagnosticService.GenerateDiagnosticReportAsync();
        Summary =
            $"App {report.AppVersion}\n" +
            $"Platform {report.System.Platform}\n" +
            $"Online {report.Connectivity.IsConnected}\n" +
            $"Remote sync configured {report.Sync.IsRemoteSyncConfigured}\n" +
            $"Pending changes {report.Sync.PendingChanges}\n" +
            $"Conflicts {report.Sync.ConflictCount}\n" +
            $"Database {report.Database.DatabaseSize}\n" +
            $"Offline cache {report.OfflineCache.PackageSizeDisplay}";
    }
}

public sealed partial class LayerSettingsViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;

    [ObservableProperty]
    private int layerId = 1;

    [ObservableProperty]
    private string layerName = "Layer 1";

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private int featureCount;

    [ObservableProperty]
    private int pendingCount;

    public LayerSettingsViewModel(INavigationService navigationService, IFeatureService featureService)
        : base(navigationService)
    {
        _featureService = featureService;
        Title = "Layer Settings";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId);
        LayerName = $"Layer {LayerId}";
    }

    public Task OnNavigatedToAsync() => LoadLayerState();

    [RelayCommand]
    private async Task LoadLayerState()
    {
        await ExecuteAsync(async () =>
        {
            var features = (await _featureService.GetFeaturesAsync(LayerId)).ToList();
            FeatureCount = features.Count;
            PendingCount = features.Count(feature => feature.IsPendingSync);
        });
    }
}

public sealed partial class ConflictResolutionViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private string conflictId = string.Empty;

    [ObservableProperty]
    private ConflictInfo? conflict;

    [ObservableProperty]
    private string localVersion = string.Empty;

    [ObservableProperty]
    private string serverVersion = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public ConflictResolutionViewModel(INavigationService navigationService, ISyncService syncService)
        : base(navigationService)
    {
        _syncService = syncService;
        Title = "Conflict Resolution";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ConflictId = RouteQuery.GetString(query, "conflictId", ConflictId);
    }

    public Task OnNavigatedToAsync() => LoadConflict();

    [RelayCommand]
    private async Task LoadConflict()
    {
        await ExecuteAsync(async () =>
        {
            Conflict = (await _syncService.GetConflictsAsync())
                .FirstOrDefault(conflict => conflict.Id == ConflictId);

            LocalVersion = Conflict?.RedactedLocalVersion ?? Conflict?.LocalVersion?.ToString() ?? string.Empty;
            ServerVersion = Conflict?.RedactedServerVersion ?? Conflict?.ServerVersion?.ToString() ?? string.Empty;
            StatusMessage = Conflict == null ? "Conflict not found" : Conflict.ConflictDescription;
        });
    }

    [RelayCommand]
    private Task AcceptLocal() => Resolve(ConflictResolution.AcceptLocal);

    [RelayCommand]
    private Task AcceptServer() => Resolve(ConflictResolution.AcceptServer);

    [RelayCommand]
    private async Task Defer()
    {
        if (string.IsNullOrWhiteSpace(ConflictId))
        {
            return;
        }

        var success = await _syncService.DeferConflictAsync(ConflictId);
        StatusMessage = success ? "Conflict deferred for manual review" : "Conflict defer failed";
        if (success)
        {
            await NavigationService.GoBackAsync();
        }
    }

    private async Task Resolve(ConflictResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(ConflictId))
        {
            return;
        }

        var success = await _syncService.ResolveConflictAsync(ConflictId, resolution);
        StatusMessage = success ? "Conflict resolved" : "Conflict resolution failed";
        if (success)
        {
            await NavigationService.GoBackAsync();
        }
    }
}

public sealed class SyncHistoryRow
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int ChangesPulled { get; init; }
    public int ChangesPushed { get; init; }
    public int ConflictsDetected { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string Summary => $"{Status}: pulled {ChangesPulled}, pushed {ChangesPushed}, conflicts {ConflictsDetected}";
}

public sealed partial class SyncHistoryViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly DatabaseService _databaseService;

    public ObservableCollection<SyncHistoryRow> Sessions { get; } = [];

    public SyncHistoryViewModel(INavigationService navigationService, DatabaseService databaseService)
        : base(navigationService)
    {
        _databaseService = databaseService;
        Title = "Sync History";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync() => LoadHistory();

    [RelayCommand]
    private async Task LoadHistory()
    {
        await ExecuteAsync(async () =>
        {
            var storage = await _databaseService.GetStorageServiceAsync();
            var sessions = await storage.GetSyncSessionsAsync();
            Sessions.Clear();

            foreach (var session in sessions)
            {
                Sessions.Add(MapSession(session));
            }
        });
    }

    private static SyncHistoryRow MapSession(StorageSyncSession session) =>
        new()
        {
            Id = session.Id,
            Status = session.Status.ToString(),
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            ChangesPulled = session.ChangesPulled,
            ChangesPushed = session.ChangesPushed,
            ConflictsDetected = session.ConflictsDetected,
            ErrorMessage = session.ErrorMessage ?? string.Empty
        };
}

public sealed partial class ServerConfigViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    public ServerConfigViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "Server Configuration";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        ServerUrl = _authService.ServerUrl ?? ServerUrl;
        ApiKey = _authService.ApiKey ?? ApiKey;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Save()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _authService.AuthenticateAsync(ServerUrl, ApiKey);
            ValidationMessage = result.IsSuccess ? "Server configuration saved" : result.ErrorMessage ?? "Server configuration failed";
        });
    }

    [RelayCommand]
    private async Task Validate()
    {
        await ExecuteAsync(async () =>
        {
            ValidationMessage = await _authService.ValidateConnectionAsync(ServerUrl, ApiKey)
                ? "Server reachable"
                : "Server unreachable";
        });
    }
}

public sealed partial class UserProfileViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string userName = "Not signed in";

    [ObservableProperty]
    private string serverUrl = string.Empty;

    public UserProfileViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "User Profile";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        UserName = _authService.CurrentUserName ?? _authService.CurrentUserId ?? "Not signed in";
        ServerUrl = _authService.ServerUrl ?? string.Empty;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.LogoutAsync();
        await OnNavigatedToAsync();
    }
}

public sealed partial class AboutViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly MobileBuildConfiguration _buildConfiguration;

    [ObservableProperty]
    private string summary = string.Empty;

    public AboutViewModel(INavigationService navigationService, MobileBuildConfiguration buildConfiguration)
        : base(navigationService)
    {
        _buildConfiguration = buildConfiguration;
        Title = "About";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        Summary =
            $"Honua Field Collection\n" +
            $"Environment: {_buildConfiguration.Metadata.BuildEnvironment}\n" +
            $"Build: {_buildConfiguration.Metadata.VersionDisplay}\n" +
            $"Commit: {_buildConfiguration.Metadata.CommitSha}";
        return Task.CompletedTask;
    }
}

internal static class RouteQuery
{
    public static string GetString(IDictionary<string, object> query, string key, string fallback)
    {
        return query.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;
    }

    public static int GetInt(IDictionary<string, object> query, string key, int fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            int integer => integer,
            long integer => checked((int)integer),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static bool GetBool(IDictionary<string, object> query, string key, bool fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static T? GetValue<T>(IDictionary<string, object> query, string key) where T : class
    {
        return query.TryGetValue(key, out var value) ? value as T : null;
    }
}
