// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace HonuaFieldCollector.Features.Sync;

public partial class SyncStatusPage : ContentPage
{
    public SyncStatusPage(SyncStatusViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SyncStatusViewModel vm)
            vm.RefreshCommand.Execute(null);
    }
}
