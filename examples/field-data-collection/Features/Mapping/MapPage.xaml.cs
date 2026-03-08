// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace HonuaFieldCollector.Features.Mapping;

public partial class MapPage : ContentPage
{
    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MapViewModel vm)
            vm.OnAppearingCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is MapViewModel vm)
            vm.OnDisappearingCommand.Execute(null);
    }
}
