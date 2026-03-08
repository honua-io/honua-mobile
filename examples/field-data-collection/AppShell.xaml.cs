// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using HonuaFieldCollector.Features.Dashboard;
using HonuaFieldCollector.Features.DataCollection;
using HonuaFieldCollector.Features.Mapping;
using HonuaFieldCollector.Features.Settings;
using HonuaFieldCollector.Features.Sync;

namespace HonuaFieldCollector;

/// <summary>
/// Application shell providing navigation and layout structure.
/// Implements the Shell navigation pattern for intuitive mobile UX.
/// </summary>
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for programmatic navigation
        RegisterRoutes();

        // Set up navigation events
        this.Navigating += OnNavigating;
        this.Navigated += OnNavigated;
    }

    /// <summary>
    /// Registers routes for Shell navigation.
    /// </summary>
    private static void RegisterRoutes()
    {
        // Core screen routes (scaffolded)
        Routing.RegisterRoute("dashboard", typeof(DashboardPage));
        Routing.RegisterRoute("collect", typeof(DataCollectionPage));
        Routing.RegisterRoute("map", typeof(MapPage));
        Routing.RegisterRoute("sync", typeof(SyncStatusPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));

        // TODO: Phase 1 — scaffold remaining feature screens
        // Routing.RegisterRoute("arview", typeof(ARViewPage));
        // Routing.RegisterRoute("sensors", typeof(SensorPage));
        // Routing.RegisterRoute("projects", typeof(ProjectsPage));
        // Routing.RegisterRoute("forms", typeof(FormsPage));
        // Routing.RegisterRoute("photos", typeof(PhotoGalleryPage));
        // Routing.RegisterRoute("reports", typeof(ReportsPage));
        // Routing.RegisterRoute("login", typeof(LoginPage));
        // Routing.RegisterRoute("onboarding", typeof(OnboardingPage));
        // Routing.RegisterRoute("camera", typeof(CameraPage));
        // Routing.RegisterRoute("formbuilder", typeof(FormBuilderPage));
        // Routing.RegisterRoute("sensorconfig", typeof(SensorConfigPage));
        // Routing.RegisterRoute("project/detail", typeof(ProjectDetailPage));
        // Routing.RegisterRoute("form/detail", typeof(FormDetailPage));
        // Routing.RegisterRoute("feature/edit", typeof(FeatureEditPage));
        // Routing.RegisterRoute("photo/detail", typeof(PhotoDetailPage));
        // Routing.RegisterRoute("sensor/detail", typeof(SensorDetailPage));
    }

    /// <summary>
    /// Handles navigation events for security and state management.
    /// </summary>
    private void OnNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        // Check authentication for protected routes
        var protectedRoutes = new[]
        {
            "collect", "map", "arview", "sensors", "projects", "forms",
            "sync", "photos", "reports", "settings"
        };

        var targetRoute = e.Target.Location.OriginalString;
        if (protectedRoutes.Any(route => targetRoute.Contains(route)))
        {
            var authService = App.GetOptionalService<IAuthenticationService>();
            if (authService != null && !authService.IsAuthenticated)
            {
                e.Cancel();
                Shell.Current.GoToAsync("//login");
                return;
            }
        }

        // Save navigation state for crash recovery
        Preferences.Set("LastRoute", targetRoute);
    }

    /// <summary>
    /// Handles completed navigation for analytics and state updates.
    /// </summary>
    private void OnNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var currentRoute = e.Current.Location.OriginalString;

        // Update user activity tracking
        var analyticsService = App.GetOptionalService<IAnalyticsService>();
        analyticsService?.TrackNavigation(currentRoute);

        // Update app state
        Preferences.Set("CurrentRoute", currentRoute);
        Preferences.Set("LastNavigationTime", DateTime.UtcNow.ToString("O"));
    }

    /// <summary>
    /// Shows the user profile flyout menu.
    /// </summary>
    public async Task ShowUserProfileAsync()
    {
        await Shell.Current.GoToAsync("//settings");
    }

    /// <summary>
    /// Navigates to the data collection page with optional project ID.
    /// </summary>
    public async Task StartDataCollectionAsync(string? projectId = null)
    {
        var route = projectId != null ? $"//collect?projectId={projectId}" : "//collect";
        await Shell.Current.GoToAsync(route);
    }

    /// <summary>
    /// Opens the camera for photo capture.
    /// </summary>
    public async Task OpenCameraAsync(string? context = null)
    {
        var route = context != null ? $"camera?context={context}" : "camera";
        await Shell.Current.GoToAsync(route);
    }

    /// <summary>
    /// Shows the AR view for utility visualization.
    /// </summary>
    public async Task ShowARViewAsync(string? utilityId = null)
    {
        var route = utilityId != null ? $"//arview?utilityId={utilityId}" : "//arview";
        await Shell.Current.GoToAsync(route);
    }

    /// <summary>
    /// Opens sensor configuration for IoT integration.
    /// </summary>
    public async Task ConfigureSensorsAsync(string? sensorType = null)
    {
        var route = sensorType != null ? $"sensorconfig?type={sensorType}" : "sensorconfig";
        await Shell.Current.GoToAsync(route);
    }

    /// <summary>
    /// Logs out the user and returns to the login screen.
    /// </summary>
    public async Task LogoutAsync()
    {
        var authService = App.GetOptionalService<IAuthenticationService>();
        if (authService != null)
        {
            await authService.LogoutAsync();
        }

        // Clear navigation stack and go to login
        await Shell.Current.GoToAsync("//login");
    }

    /// <summary>
    /// Handles hardware back button press on Android.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        var currentRoute = Shell.Current.CurrentState.Location.OriginalString;

        // Handle back button for specific pages
        if (currentRoute.Contains("dashboard"))
        {
            // Show exit confirmation on dashboard
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var result = await DisplayAlert(
                    "Exit App",
                    "Do you want to exit Honua Field Collector?",
                    "Exit",
                    "Cancel");

                if (result)
                {
                    Application.Current?.Quit();
                }
            });

            return true; // Prevent default back behavior
        }

        return base.OnBackButtonPressed();
    }
}