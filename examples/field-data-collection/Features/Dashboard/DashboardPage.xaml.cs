// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace HonuaFieldCollector.Features.Dashboard;

/// <summary>
/// Dashboard page providing overview and quick access to field collection features.
/// </summary>
public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Handles page appearing lifecycle event.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Refresh dashboard data when page appears
        if (BindingContext is DashboardViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles page disappearing lifecycle event.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Optionally pause real-time updates when page is not visible
        // This helps with battery optimization
    }
}