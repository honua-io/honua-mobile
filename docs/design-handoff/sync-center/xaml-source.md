# SyncCenterPage - XAML source

File: `apps/Honua.Mobile.FieldCollection/Views/SyncCenterPage.xaml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:diagnostics="clr-namespace:Honua.Mobile.FieldCollection.Services.Diagnostics;assembly=Honua.Mobile.FieldCollection.Core"
             xmlns:services="clr-namespace:Honua.Mobile.FieldCollection.Services;assembly=Honua.Mobile.FieldCollection.Core"
             xmlns:vm="clr-namespace:Honua.Mobile.FieldCollection.ViewModels"
             x:Class="Honua.Mobile.FieldCollection.Views.SyncCenterPage"
             Title="{Binding Title}"
             x:DataType="vm:SyncCenterViewModel">

    <RefreshView IsRefreshing="{Binding IsRefreshing}" Command="{Binding RefreshCommand}">
        <ScrollView>
            <VerticalStackLayout Spacing="20" Padding="20">

                <!-- Sync Status Card -->
                <Frame Style="{StaticResource CardFrameStyle}">
                    <Grid RowDefinitions="Auto,Auto,Auto" ColumnDefinitions="*,Auto" RowSpacing="10">
                        <Label Grid.Row="0" Grid.ColumnSpan="2" Text="Sync Status"
                               Style="{StaticResource SectionHeaderStyle}" />

                        <VerticalStackLayout Grid.Row="1" Grid.Column="0" Spacing="5">
                            <Label Text="{Binding SyncStatusMessage}" FontSize="16" FontAttributes="Bold" />
                            <StackLayout Orientation="Horizontal" Spacing="10">
                                <Label Text="🌐" FontSize="14" />
                                <Label Text="{Binding IsOnline, Converter={StaticResource BoolToStringConverter}, ConverterParameter='Online|Offline'}"
                                       FontSize="14"
                                       TextColor="{Binding IsOnline, Converter={StaticResource BoolToColorConverter}, ConverterParameter='Green|Red'}" />
                            </StackLayout>
                            <StackLayout Orientation="Horizontal" Spacing="10"
                                        IsVisible="{Binding PendingChangesCount, Converter={StaticResource IntToBoolConverter}}">
                                <Label Text="📝" FontSize="14" />
                                <Label Text="{Binding PendingChangesCount, StringFormat='{0} pending changes'}" FontSize="14" />
                            </StackLayout>
                            <StackLayout Orientation="Horizontal" Spacing="10"
                                        IsVisible="{Binding LastSyncTime, Converter={StaticResource IsNotNullConverter}}">
                                <Label Text="🕒" FontSize="14" />
                                <Label Text="{Binding LastSyncTime, StringFormat='Last sync: {0:MM/dd HH:mm}'}" FontSize="14" />
                            </StackLayout>
                        </VerticalStackLayout>

                        <VerticalStackLayout Grid.Row="1" Grid.Column="1" Spacing="5">
                            <Button Text="🔄" Style="{StaticResource BaseButtonStyle}"
                                    Command="{Binding StartFullSyncCommand}"
                                    IsEnabled="{Binding CanRunSyncOperations}" WidthRequest="60" />
                            <Button Text="⏹️" Style="{StaticResource DangerButtonStyle}"
                                    Command="{Binding CancelSyncCommand}"
                                    IsVisible="{Binding IsSyncing}" WidthRequest="60" />
                        </VerticalStackLayout>

                        <ProgressBar Grid.Row="2" Grid.ColumnSpan="2"
                                    Progress="{Binding SyncProgress}" IsVisible="{Binding IsSyncing}"
                                    ProgressColor="{StaticResource Primary}" />
                    </Grid>
                </Frame>

                <!-- Sync Operations -->
                <!-- Pull Only / Push Only buttons + captions -->

                <!-- Sync Statistics, Offline Diagnostics, Active Conflicts, Conflict Review,
                     Sync History, Offline banner. See file for full markup. -->

            </VerticalStackLayout>
        </ScrollView>
    </RefreshView>

    <ActivityIndicator IsRunning="{Binding IsBusy}" IsVisible="{Binding IsBusy}"
                      Color="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center" />
</ContentPage>
```

> The Sync Status card is reproduced in full above. The remaining sections (Sync Operations, Last Sync Statistics, Offline Diagnostics, Active Conflicts, Conflict Review, Recent Sync History, Offline banner) live in the same file; see `apps/Honua.Mobile.FieldCollection/Views/SyncCenterPage.xaml` for the complete markup. They share the same `CardFrameStyle` + `SectionHeaderStyle` pattern.
