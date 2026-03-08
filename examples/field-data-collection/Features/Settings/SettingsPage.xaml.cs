// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace HonuaFieldCollector.Features.Settings;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SettingsViewModel vm)
            vm.RefreshCommand.Execute(null);
    }
}
