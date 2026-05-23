using Honua.Mobile.FieldCollection.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.FieldCollection.Views;

public sealed class RecordDetailPage : WorkflowPage<RecordDetailViewModel>
{
    public RecordDetailPage()
        : this(PageServiceResolver.GetRequiredService<RecordDetailViewModel>())
    {
    }

    public RecordDetailPage(RecordDetailViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateRecordDetailContent();
    }
}

public sealed class FeatureDetailPage : WorkflowPage<FeatureDetailViewModel>
{
    public FeatureDetailPage()
        : this(PageServiceResolver.GetRequiredService<FeatureDetailViewModel>())
    {
    }

    public FeatureDetailPage(FeatureDetailViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateRecordDetailContent();
    }
}

public sealed class RecordEditPage : WorkflowPage<RecordEditViewModel>
{
    public RecordEditPage()
        : this(PageServiceResolver.GetRequiredService<RecordEditViewModel>())
    {
    }

    public RecordEditPage(RecordEditViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateRecordEditContent();
    }
}

public sealed class AuthenticationPage : WorkflowPage<AuthenticationViewModel>
{
    public AuthenticationPage()
        : this(PageServiceResolver.GetRequiredService<AuthenticationViewModel>())
    {
    }

    public AuthenticationPage(AuthenticationViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateAuthenticationContent();
    }
}

public sealed class DiagnosticsPage : WorkflowPage<DiagnosticsViewModel>
{
    public DiagnosticsPage()
        : this(PageServiceResolver.GetRequiredService<DiagnosticsViewModel>())
    {
    }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateDiagnosticsContent();
    }
}

public sealed class LayerSettingsPage : WorkflowPage<LayerSettingsViewModel>
{
    public LayerSettingsPage()
        : this(PageServiceResolver.GetRequiredService<LayerSettingsViewModel>())
    {
    }

    public LayerSettingsPage(LayerSettingsViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateLayerSettingsContent();
    }
}

public sealed class ConflictResolutionPage : WorkflowPage<ConflictResolutionViewModel>
{
    public ConflictResolutionPage()
        : this(PageServiceResolver.GetRequiredService<ConflictResolutionViewModel>())
    {
    }

    public ConflictResolutionPage(ConflictResolutionViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateConflictResolutionContent();
    }
}

public sealed class SyncHistoryPage : WorkflowPage<SyncHistoryViewModel>
{
    public SyncHistoryPage()
        : this(PageServiceResolver.GetRequiredService<SyncHistoryViewModel>())
    {
    }

    public SyncHistoryPage(SyncHistoryViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateSyncHistoryContent();
    }
}

public sealed class ServerConfigPage : WorkflowPage<ServerConfigViewModel>
{
    public ServerConfigPage()
        : this(PageServiceResolver.GetRequiredService<ServerConfigViewModel>())
    {
    }

    public ServerConfigPage(ServerConfigViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateServerConfigContent();
    }
}

public sealed class UserProfilePage : WorkflowPage<UserProfileViewModel>
{
    public UserProfilePage()
        : this(PageServiceResolver.GetRequiredService<UserProfileViewModel>())
    {
    }

    public UserProfilePage(UserProfileViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateUserProfileContent();
    }
}

public sealed class AboutPage : WorkflowPage<AboutViewModel>
{
    public AboutPage()
        : this(PageServiceResolver.GetRequiredService<AboutViewModel>())
    {
    }

    public AboutPage(AboutViewModel viewModel)
        : base(viewModel)
    {
        Content = WorkflowPageContent.CreateAboutContent();
    }
}

public abstract class WorkflowPage<TViewModel> : ContentPage, IQueryAttributable
    where TViewModel : BaseViewModel, IRouteAwareViewModel
{
    protected WorkflowPage(TViewModel viewModel)
    {
        ViewModel = viewModel;
        BindingContext = viewModel;
        SetBinding(TitleProperty, nameof(BaseViewModel.Title));
    }

    protected TViewModel ViewModel { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ViewModel.ApplyQueryAttributes(query);
        _ = ViewModel.OnNavigatedToAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.OnNavigatedToAsync();
    }
}

internal static class PageServiceResolver
{
    public static T GetRequiredService<T>() where T : notnull
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services == null)
        {
            throw new InvalidOperationException("MAUI services are not available for route page activation.");
        }

        return services.GetRequiredService<T>();
    }
}

internal static class WorkflowPageContent
{
    public static View CreateRecordDetailContent()
    {
        var attributes = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var key = new Label { FontAttributes = FontAttributes.Bold };
                key.SetBinding(Label.TextProperty, nameof(AttributeDisplayItem.Key));

                var value = new Label { LineBreakMode = LineBreakMode.WordWrap };
                value.SetBinding(Label.TextProperty, nameof(AttributeDisplayItem.Value));

                var grid = new Grid
                {
                    Padding = new Thickness(0, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(120) },
                        new ColumnDefinition { Width = GridLength.Star }
                    }
                };

                Grid.SetColumn(key, 0);
                Grid.SetColumn(value, 1);
                grid.Children.Add(key);
                grid.Children.Add(value);
                return grid;
            })
        };
        attributes.SetBinding(ItemsView.ItemsSourceProperty, "Attributes");

        return PageScroll(
            BoundHeader("Feature.DisplayTitle", "Record"),
            BoundLabel("Feature.Id", "Record ID"),
            BoundLabel("GeometrySummary", "Geometry"),
            SectionTitle("Attributes"),
            attributes,
            ButtonRow(
                CommandButton("Edit", "EditRecordCommand"),
                CommandButton("Delete", "DeleteRecordCommand")));
    }

    public static View CreateRecordEditContent()
    {
        var attributes = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var key = new Entry { Placeholder = "Field" };
                key.SetBinding(Entry.TextProperty, nameof(EditableAttributeItem.Key));

                var value = new Entry { Placeholder = "Value" };
                value.SetBinding(Entry.TextProperty, nameof(EditableAttributeItem.ValueText));

                var grid = new Grid
                {
                    Padding = new Thickness(0, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(130) },
                        new ColumnDefinition { Width = GridLength.Star }
                    }
                };

                Grid.SetColumn(key, 0);
                Grid.SetColumn(value, 1);
                grid.Children.Add(key);
                grid.Children.Add(value);
                return grid;
            })
        };
        attributes.SetBinding(ItemsView.ItemsSourceProperty, "Attributes");

        return PageScroll(
            BoundHeader("PageTitle", "Record"),
            BoundLabel("GeometrySummary", "Geometry"),
            SectionTitle("Attributes"),
            attributes,
            ButtonRow(
                CommandButton("Add field", "AddAttributeCommand"),
                CommandButton("Save", "SaveRecordCommand"),
                CommandButton("Cancel", "CancelCommand")));
    }

    public static View CreateAuthenticationContent()
    {
        var server = BoundEntry("ServerUrl", "Server URL");
        var apiKey = BoundEntry("ApiKey", "API key");
        apiKey.IsPassword = true;

        return PageScroll(
            BoundHeader("StatusMessage", "Authentication"),
            server,
            apiKey,
            ButtonRow(
                CommandButton("Validate", "ValidateConnectionCommand"),
                CommandButton("Sign in", "SignInCommand"),
                CommandButton("Logout", "LogoutCommand")));
    }

    public static View CreateDiagnosticsContent()
    {
        var summary = BoundMultilineLabel("Summary");
        var exportPath = BoundLabel("ExportPath", "Export");

        return PageScroll(
            SectionTitle("Diagnostics"),
            summary,
            exportPath,
            ButtonRow(
                CommandButton("Refresh", "LoadDiagnosticsCommand"),
                CommandButton("Export", "ExportDiagnosticsCommand"),
                CommandButton("Compact", "CompactDatabaseCommand")));
    }

    public static View CreateLayerSettingsContent()
    {
        return PageScroll(
            BoundHeader("LayerName", "Layer"),
            BoundLabel("LayerId", "Layer ID"),
            BoundLabel("FeatureCount", "Features"),
            BoundLabel("PendingCount", "Pending sync"),
            Toggle("IsVisible", "Visible"),
            CommandButton("Refresh", "LoadLayerStateCommand"));
    }

    public static View CreateConflictResolutionContent()
    {
        return PageScroll(
            BoundHeader("Conflict.FeatureId", "Conflict"),
            BoundLabel("StatusMessage", "Status"),
            SectionTitle("Local"),
            BoundMultilineLabel("LocalVersion"),
            SectionTitle("Server"),
            BoundMultilineLabel("ServerVersion"),
            ButtonRow(
                CommandButton("Accept local", "AcceptLocalCommand"),
                CommandButton("Accept server", "AcceptServerCommand"),
                CommandButton("Defer", "DeferCommand")));
    }

    public static View CreateSyncHistoryContent()
    {
        var sessions = new CollectionView
        {
            EmptyView = new Label { Text = "No sync sessions recorded" },
            ItemTemplate = new DataTemplate(() =>
            {
                var status = new Label { FontAttributes = FontAttributes.Bold };
                status.SetBinding(Label.TextProperty, nameof(SyncHistoryRow.Summary));

                var started = new Label { FontSize = 12 };
                started.SetBinding(Label.TextProperty, new Binding(nameof(SyncHistoryRow.StartTime), stringFormat: "Started {0:u}"));

                var error = new Label { FontSize = 12, TextColor = Colors.DarkRed };
                error.SetBinding(Label.TextProperty, nameof(SyncHistoryRow.ErrorMessage));

                return new VerticalStackLayout
                {
                    Padding = new Thickness(0, 8),
                    Children = { status, started, error }
                };
            })
        };
        sessions.SetBinding(ItemsView.ItemsSourceProperty, "Sessions");

        return PageScroll(
            SectionTitle("Sync History"),
            sessions,
            CommandButton("Refresh", "LoadHistoryCommand"));
    }

    public static View CreateServerConfigContent()
    {
        var server = BoundEntry("ServerUrl", "Server URL");
        var apiKey = BoundEntry("ApiKey", "API key");
        apiKey.IsPassword = true;

        return PageScroll(
            BoundHeader("ValidationMessage", "Server Configuration"),
            server,
            apiKey,
            ButtonRow(
                CommandButton("Validate", "ValidateCommand"),
                CommandButton("Save", "SaveCommand")));
    }

    public static View CreateUserProfileContent()
    {
        return PageScroll(
            BoundHeader("UserName", "User Profile"),
            BoundLabel("ServerUrl", "Server"),
            CommandButton("Logout", "LogoutCommand"));
    }

    public static View CreateAboutContent()
    {
        return PageScroll(
            SectionTitle("About"),
            BoundMultilineLabel("Summary"));
    }

    private static ScrollView PageScroll(params View[] children)
    {
        var stack = new VerticalStackLayout
        {
            Padding = 20,
            Spacing = 14
        };

        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return new ScrollView
        {
            Content = stack
        };
    }

    private static Label SectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold
        };
    }

    private static Label BoundHeader(string path, string fallback)
    {
        var label = SectionTitle(fallback);
        label.SetBinding(
            Label.TextProperty,
            new Binding(path)
            {
                FallbackValue = fallback,
                TargetNullValue = fallback
            });
        return label;
    }

    private static Label BoundLabel(string path, string caption)
    {
        var label = new Label { LineBreakMode = LineBreakMode.WordWrap };
        label.SetBinding(Label.TextProperty, new Binding(path, stringFormat: $"{caption}: {{0}}"));
        return label;
    }

    private static Label BoundMultilineLabel(string path)
    {
        var label = new Label { LineBreakMode = LineBreakMode.WordWrap };
        label.SetBinding(Label.TextProperty, path);
        return label;
    }

    private static Entry BoundEntry(string path, string placeholder)
    {
        var entry = new Entry { Placeholder = placeholder };
        entry.SetBinding(Entry.TextProperty, path);
        return entry;
    }

    private static View Toggle(string path, string label)
    {
        var toggle = new Switch();
        toggle.SetBinding(Switch.IsToggledProperty, path);

        return new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                toggle,
                new Label { Text = label, VerticalOptions = LayoutOptions.Center }
            }
        };
    }

    private static Button CommandButton(string text, string commandPath)
    {
        var button = new Button { Text = text };
        button.SetBinding(Button.CommandProperty, commandPath);
        return button;
    }

    private static HorizontalStackLayout ButtonRow(params View[] children)
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 10
        };

        foreach (var child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }
}
