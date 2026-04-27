namespace Honua.Mobile.FieldCollection.Views;

public abstract class PlaceholderPage : ContentPage
{
    protected PlaceholderPage(string title)
    {
        Title = title;
        Content = new Grid
        {
            Padding = 24,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };
    }
}

public sealed class RecordDetailPage : PlaceholderPage
{
    public RecordDetailPage() : base("Record Detail")
    {
    }
}

public sealed class RecordEditPage : PlaceholderPage
{
    public RecordEditPage() : base("Record Edit")
    {
    }
}

public sealed class AuthenticationPage : PlaceholderPage
{
    public AuthenticationPage() : base("Authentication")
    {
    }
}

public sealed class DiagnosticsPage : PlaceholderPage
{
    public DiagnosticsPage() : base("Diagnostics")
    {
    }
}

public sealed class LayerSettingsPage : PlaceholderPage
{
    public LayerSettingsPage() : base("Layer Settings")
    {
    }
}

public sealed class FeatureDetailPage : PlaceholderPage
{
    public FeatureDetailPage() : base("Feature Detail")
    {
    }
}

public sealed class ConflictResolutionPage : PlaceholderPage
{
    public ConflictResolutionPage() : base("Conflict Resolution")
    {
    }
}

public sealed class SyncHistoryPage : PlaceholderPage
{
    public SyncHistoryPage() : base("Sync History")
    {
    }
}

public sealed class ServerConfigPage : PlaceholderPage
{
    public ServerConfigPage() : base("Server Configuration")
    {
    }
}

public sealed class UserProfilePage : PlaceholderPage
{
    public UserProfilePage() : base("User Profile")
    {
    }
}

public sealed class AboutPage : PlaceholderPage
{
    public AboutPage() : base("About")
    {
    }
}
