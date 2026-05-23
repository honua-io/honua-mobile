using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.ViewModels;
using Honua.Sdk.Field.Forms;
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
        SetBinding(TitleProperty, new Binding(nameof(BaseViewModel.Title)));
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
                key.SetBinding(Label.TextProperty, new Binding(nameof(AttributeDisplayItem.Key)));

                var value = new Label { LineBreakMode = LineBreakMode.WordWrap };
                value.SetBinding(Label.TextProperty, new Binding(nameof(AttributeDisplayItem.Value)));

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
        attributes.SetBinding(ItemsView.ItemsSourceProperty, new Binding("Attributes"));
        var attachments = AttachmentList("OpenAttachmentCommand");
        attachments.SetBinding(ItemsView.ItemsSourceProperty, new Binding("Attachments"));

        return PageScroll(
            BoundHeader("Feature.DisplayTitle", "Record"),
            BoundLabel("Feature.Id", "Record ID"),
            BoundLabel("GeometrySummary", "Geometry"),
            SectionTitle("Attributes"),
            attributes,
            SectionTitle("Attachments"),
            attachments,
            ButtonRow(
                CommandButton("Edit", "EditRecordCommand"),
                CommandButton("Delete", "DeleteRecordCommand")));
    }

    public static View CreateRecordEditContent()
    {
        var repeatSections = RepeatSectionList();
        repeatSections.SetBinding(ItemsView.ItemsSourceProperty, new Binding("RepeatSections"));

        var fields = FormFieldList();
        fields.SetBinding(ItemsView.ItemsSourceProperty, new Binding("FormFields"));

        var validation = BoundMultilineLabel("ValidationSummary");
        validation.TextColor = Colors.DarkRed;
        validation.SetBinding(VisualElement.IsVisibleProperty, new Binding("HasValidationSummary"));

        var attachments = AttachmentList("RemoveAttachmentCommand");
        attachments.SetBinding(ItemsView.ItemsSourceProperty, new Binding("Attachments"));

        return PageScroll(
            BoundHeader("PageTitle", "Record"),
            BoundLabel("GeometrySummary", "Geometry"),
            validation,
            repeatSections,
            SectionTitle("Fields"),
            fields,
            SectionTitle("Attachments"),
            attachments,
            ButtonRow(
                CommandButton("Add field", "AddAttributeCommand"),
                CommandButton("Add file", "AddFileAttachmentCommand"),
                CommandButton("Add photo", "AddPhotoAttachmentCommand"),
                CommandButton("Save", "SaveRecordCommand"),
                CommandButton("Cancel", "CancelCommand")));
    }

    private static CollectionView FormFieldList()
    {
        return new CollectionView
        {
            EmptyView = new Label { Text = "No fields" },
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { FontAttributes = FontAttributes.Bold };
                label.SetBinding(Label.TextProperty, new Binding(nameof(EditableFormFieldItem.Label)));

                var help = new Label { FontSize = 12, TextColor = Colors.Gray };
                help.SetBinding(Label.TextProperty, new Binding(nameof(EditableFormFieldItem.HelpText)));

                var singleLine = new Entry { Placeholder = "Value" };
                singleLine.SetBinding(Entry.TextProperty, new Binding(nameof(EditableFormFieldItem.TextValue), mode: BindingMode.TwoWay));
                singleLine.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsSingleLineText)));
                singleLine.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var numeric = new Entry { Placeholder = "Number", Keyboard = Keyboard.Numeric };
                numeric.SetBinding(Entry.TextProperty, new Binding(nameof(EditableFormFieldItem.TextValue), mode: BindingMode.TwoWay));
                numeric.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsNumeric)));
                numeric.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var multiline = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 96 };
                multiline.SetBinding(Editor.TextProperty, new Binding(nameof(EditableFormFieldItem.TextValue), mode: BindingMode.TwoWay));
                multiline.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsMultilineText)));
                multiline.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var date = new DatePicker();
                date.SetBinding(DatePicker.DateProperty, new Binding(nameof(EditableFormFieldItem.DateValue), mode: BindingMode.TwoWay));
                date.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsDate)));
                date.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var dateTime = new HorizontalStackLayout { Spacing = 8 };
                dateTime.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsDateTime)));
                dateTime.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));
                var datePart = new DatePicker();
                datePart.SetBinding(DatePicker.DateProperty, new Binding(nameof(EditableFormFieldItem.DateValue), mode: BindingMode.TwoWay));
                var timePart = new TimePicker();
                timePart.SetBinding(TimePicker.TimeProperty, new Binding(nameof(EditableFormFieldItem.TimeValue), mode: BindingMode.TwoWay));
                dateTime.Children.Add(datePart);
                dateTime.Children.Add(timePart);

                var yesNo = new Switch();
                yesNo.SetBinding(Switch.IsToggledProperty, new Binding(nameof(EditableFormFieldItem.BoolValue), mode: BindingMode.TwoWay));
                yesNo.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsYesNo)));
                yesNo.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var choice = new Picker { ItemDisplayBinding = new Binding(nameof(FieldChoice.Label)) };
                choice.SetBinding(Picker.ItemsSourceProperty, new Binding("Definition.Choices"));
                choice.SetBinding(Picker.SelectedItemProperty, new Binding(nameof(EditableFormFieldItem.SelectedChoice), mode: BindingMode.TwoWay));
                choice.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsSingleChoice)));
                choice.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var choices = new VerticalStackLayout { Spacing = 6 };
                choices.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsMultipleChoice)));
                choices.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));
                choices.SetBinding(BindableLayout.ItemsSourceProperty, new Binding(nameof(EditableFormFieldItem.Choices)));
                BindableLayout.SetItemTemplate(choices, new DataTemplate(() =>
                {
                    var checkbox = new CheckBox();
                    checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(EditableChoiceItem.IsSelected), mode: BindingMode.TwoWay));

                    var choiceLabel = new Label { VerticalOptions = LayoutOptions.Center };
                    choiceLabel.SetBinding(Label.TextProperty, new Binding(nameof(EditableChoiceItem.Label)));

                    return new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { checkbox, choiceLabel }
                    };
                }));

                var summary = new Label { FontSize = 12, TextColor = Colors.Gray };
                summary.SetBinding(Label.TextProperty, new Binding(nameof(EditableFormFieldItem.ValueSummary)));
                summary.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.HasPrimaryAction)));

                var action = new Button();
                action.SetBinding(Button.TextProperty, new Binding(nameof(EditableFormFieldItem.PrimaryActionLabel)));
                action.SetBinding(Button.CommandProperty, new Binding(nameof(EditableFormFieldItem.PrimaryActionCommand)));
                action.SetBinding(Button.CommandParameterProperty, new Binding("."));
                action.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.HasPrimaryAction)));
                action.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableFormFieldItem.IsEditable)));

                var unsupported = new Label { Text = "Unsupported field type", TextColor = Colors.DarkRed };
                unsupported.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsUnsupported)));

                var error = new Label { FontSize = 12, TextColor = Colors.DarkRed };
                error.SetBinding(Label.TextProperty, new Binding(nameof(EditableFormFieldItem.ValidationError)));
                error.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.HasValidationError)));

                var row = new VerticalStackLayout
                {
                    Padding = new Thickness(0, 8),
                    Spacing = 6,
                    Children =
                    {
                        label,
                        help,
                        singleLine,
                        numeric,
                        multiline,
                        date,
                        dateTime,
                        yesNo,
                        choice,
                        choices,
                        summary,
                        action,
                        unsupported,
                        error
                    }
                };
                row.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditableFormFieldItem.IsVisible)));
                return row;
            })
        };
    }

    private static CollectionView RepeatSectionList()
    {
        return new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
                label.SetBinding(Label.TextProperty, new Binding(nameof(EditableRepeatSectionItem.Label)));

                var count = new Label { FontSize = 12, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };
                count.SetBinding(Label.TextProperty, new Binding(nameof(EditableRepeatSectionItem.EntryCount), stringFormat: "{0} entries"));

                var add = new Button { Text = "Add" };
                add.SetBinding(Button.CommandProperty, new Binding(nameof(EditableRepeatSectionItem.AddCommand)));
                add.SetBinding(Button.CommandParameterProperty, new Binding("."));

                var remove = new Button { Text = "Remove" };
                remove.SetBinding(Button.CommandProperty, new Binding(nameof(EditableRepeatSectionItem.RemoveCommand)));
                remove.SetBinding(Button.CommandParameterProperty, new Binding("."));
                remove.SetBinding(VisualElement.IsEnabledProperty, new Binding(nameof(EditableRepeatSectionItem.CanRemove)));

                var grid = new Grid
                {
                    Padding = new Thickness(0, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 8
                };
                Grid.SetColumn(label, 0);
                Grid.SetColumn(count, 1);
                Grid.SetColumn(add, 2);
                Grid.SetColumn(remove, 3);
                grid.Children.Add(label);
                grid.Children.Add(count);
                grid.Children.Add(add);
                grid.Children.Add(remove);
                return grid;
            })
        };
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
        var health = BoundLabel("HealthStatus", "Health");
        var summary = BoundMultilineLabel("Summary");
        var actions = BoundMultilineLabel("SupportActions");
        var exportPath = BoundLabel("ExportPath", "Export");
        var reportStatus = BoundLabel("ReportStatus", "Report");

        return PageScroll(
            SectionTitle("Diagnostics"),
            health,
            summary,
            SectionTitle("Support actions"),
            actions,
            exportPath,
            reportStatus,
            ButtonRow(
                CommandButton("Refresh", "LoadDiagnosticsCommand"),
                CommandButton("Export", "ExportDiagnosticsCommand"),
                CommandButton("Copy", "CopyDiagnosticsCommand"),
                CommandButton("Report", "ReportDiagnosticsCommand"),
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
                status.SetBinding(Label.TextProperty, new Binding(nameof(SyncHistoryRow.Summary)));

                var started = new Label { FontSize = 12 };
                started.SetBinding(Label.TextProperty, new Binding(nameof(SyncHistoryRow.StartTime), stringFormat: "Started {0:u}"));

                var error = new Label { FontSize = 12, TextColor = Colors.DarkRed };
                error.SetBinding(Label.TextProperty, new Binding(nameof(SyncHistoryRow.ErrorMessage)));

                return new VerticalStackLayout
                {
                    Padding = new Thickness(0, 8),
                    Children = { status, started, error }
                };
            })
        };
        sessions.SetBinding(ItemsView.ItemsSourceProperty, new Binding("Sessions"));

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
        label.SetBinding(Label.TextProperty, new Binding(path));
        return label;
    }

    private static CollectionView AttachmentList(string commandPath)
    {
        return new CollectionView
        {
            EmptyView = new Label { Text = "No attachments" },
            ItemTemplate = new DataTemplate(() =>
            {
                var fileName = new Label { FontAttributes = FontAttributes.Bold };
                fileName.SetBinding(Label.TextProperty, new Binding(nameof(AttachmentInfo.FileName)));

                var details = new Label { FontSize = 12, TextColor = Colors.Gray };
                details.SetBinding(
                    Label.TextProperty,
                    new Binding(nameof(AttachmentInfo.SyncStatus), stringFormat: "{0}"));

                var action = new Button { Text = commandPath.StartsWith("Open", StringComparison.Ordinal) ? "Open" : "Remove" };
                action.SetBinding(
                    Button.CommandProperty,
                    new Binding(
                        $"BindingContext.{commandPath}",
                        source: new RelativeBindingSource(
                            RelativeBindingSourceMode.FindAncestor,
                            typeof(ContentPage))));
                action.SetBinding(Button.CommandParameterProperty, new Binding("."));

                var info = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children = { fileName, details }
                };

                var grid = new Grid
                {
                    Padding = new Thickness(0, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };
                Grid.SetColumn(info, 0);
                Grid.SetColumn(action, 1);
                grid.Children.Add(info);
                grid.Children.Add(action);
                return grid;
            })
        };
    }

    private static Entry BoundEntry(string path, string placeholder)
    {
        var entry = new Entry { Placeholder = placeholder };
        entry.SetBinding(Entry.TextProperty, new Binding(path));
        return entry;
    }

    private static View Toggle(string path, string label)
    {
        var toggle = new Switch();
        toggle.SetBinding(Switch.IsToggledProperty, new Binding(path));

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
        button.SetBinding(Button.CommandProperty, new Binding(commandPath));
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
