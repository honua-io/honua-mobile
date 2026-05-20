# SettingsPage - XAML source

File: `apps/Honua.Mobile.FieldCollection/Views/SettingsPage.xaml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:Honua.Mobile.FieldCollection.ViewModels"
             x:Class="Honua.Mobile.FieldCollection.Views.SettingsPage"
             Title="{Binding Title}"
             x:DataType="vm:SettingsViewModel">

    <RefreshView IsRefreshing="{Binding IsRefreshing}" Command="{Binding RefreshCommand}">
        <ScrollView>
            <VerticalStackLayout Spacing="20" Padding="20">

                <!-- Account -->
                <Frame Style="{StaticResource CardFrameStyle}">
                    <VerticalStackLayout Spacing="15">
                        <Label Text="Account" Style="{StaticResource SectionHeaderStyle}" />
                        <Grid RowDefinitions="Auto,Auto,Auto" ColumnDefinitions="Auto,*,Auto"
                              RowSpacing="10" ColumnSpacing="10">
                            <Label Grid.Row="0" Grid.Column="0" Text="👤" FontSize="16" />
                            <Label Grid.Row="0" Grid.Column="1" Text="{Binding UserName}" FontSize="16" VerticalOptions="Center" />
                            <Button Grid.Row="0" Grid.Column="2" Text="Profile"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Command="{Binding ViewUserProfileCommand}"
                                    IsVisible="{Binding IsAuthenticated}" />
                            <Label Grid.Row="1" Grid.Column="0" Text="🌐" FontSize="16" />
                            <Label Grid.Row="1" Grid.Column="1" Text="{Binding ServerUrl}"
                                   FontSize="14" TextColor="{StaticResource Gray600}" VerticalOptions="Center" />
                            <Button Grid.Row="1" Grid.Column="2" Text="Configure"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Command="{Binding ConfigureServerCommand}" />
                            <Label Grid.Row="2" Grid.Column="0" Text="🔐" FontSize="16" />
                            <Label Grid.Row="2" Grid.Column="1"
                                   Text="{Binding IsAuthenticated, Converter={StaticResource BoolToStringConverter}, ConverterParameter='Authenticated|Not signed in'}"
                                   FontSize="14"
                                   TextColor="{Binding IsAuthenticated, Converter={StaticResource BoolToColorConverter}, ConverterParameter='Green|Red'}"
                                   VerticalOptions="Center" />
                            <Button Grid.Row="2" Grid.Column="2" Text="Sign Out"
                                    Style="{StaticResource DangerButtonStyle}"
                                    Command="{Binding SignOutCommand}"
                                    IsVisible="{Binding IsAuthenticated}" />
                        </Grid>
                    </VerticalStackLayout>
                </Frame>

                <!-- Preferences (toggles, exception reporting entry, sliders, Save) -->
                <!-- Device Information (8-row grid of platform metadata) -->
                <!-- Developer Options (visible when EnableDeveloperMode) -->
                <!-- Developer Mode switch -->
                <!-- About -->
                <!-- Danger Zone (DangerLight background, Reset App) -->

            </VerticalStackLayout>
        </ScrollView>
    </RefreshView>

    <ActivityIndicator IsRunning="{Binding IsBusy}" IsVisible="{Binding IsBusy}"
                      Color="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center" />
</ContentPage>
```

> The Account card is shown in full. The remaining cards (Preferences, Device Information, Developer Options, Developer Mode toggle, About, Danger Zone) follow the same `CardFrameStyle` + `SectionHeaderStyle` pattern; see `apps/Honua.Mobile.FieldCollection/Views/SettingsPage.xaml` for the complete source.
