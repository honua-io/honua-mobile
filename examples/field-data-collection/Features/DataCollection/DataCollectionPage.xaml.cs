// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace HonuaFieldCollector.Features.DataCollection;

[QueryProperty(nameof(FormId), "formId")]
[QueryProperty(nameof(FeatureId), "featureId")]
public partial class DataCollectionPage : ContentPage
{
    public string? FormId { get; set; }
    public string? FeatureId { get; set; }

    public DataCollectionPage(DataCollectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is DataCollectionViewModel vm)
            await vm.InitializeAsync(FormId, FeatureId);
    }
}
