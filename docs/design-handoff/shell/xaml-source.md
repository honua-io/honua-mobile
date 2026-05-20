# AppShell - XAML source

File: `apps/Honua.Mobile.FieldCollection/AppShell.xaml`

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:views="clr-namespace:Honua.Mobile.FieldCollection.Views"
       x:Class="Honua.Mobile.FieldCollection.AppShell"
       Title="Honua Field Collection">

    <Shell.Resources>
        <ResourceDictionary>
            <Style x:Key="BaseStyle" TargetType="Element">
                <Setter Property="Shell.BackgroundColor" Value="{StaticResource Primary}" />
                <Setter Property="Shell.ForegroundColor" Value="{StaticResource White}" />
                <Setter Property="Shell.TitleColor" Value="{StaticResource White}" />
                <Setter Property="Shell.DisabledColor" Value="{StaticResource Gray200}" />
                <Setter Property="Shell.UnselectedColor" Value="{StaticResource Gray300}" />
                <Setter Property="Shell.TabBarBackgroundColor" Value="{StaticResource Primary}" />
                <Setter Property="Shell.TabBarForegroundColor" Value="{StaticResource White}"/>
                <Setter Property="Shell.TabBarUnselectedColor" Value="{StaticResource Gray300}"/>
                <Setter Property="Shell.TabBarTitleColor" Value="{StaticResource White}"/>
            </Style>
        </ResourceDictionary>
    </Shell.Resources>

    <TabBar>
        <ShellContent Title="Map" Icon="map_icon.png" Route="map"
                      ContentTemplate="{DataTemplate views:MapPage}" />

        <Tab Title="Records" Icon="list_icon.png">
            <ShellContent Title="Records List" Route="records"
                          ContentTemplate="{DataTemplate views:RecordsPage}" />
        </Tab>

        <ShellContent Title="Sync" Icon="sync_icon.png" Route="sync"
                      ContentTemplate="{DataTemplate views:SyncCenterPage}" />

        <ShellContent Title="Settings" Icon="settings_icon.png" Route="settings"
                      ContentTemplate="{DataTemplate views:SettingsPage}" />
    </TabBar>

    <Shell.ItemTemplate>
        <DataTemplate>
            <Grid HeightRequest="50">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="0.2*" />
                    <ColumnDefinition Width="0.8*" />
                </Grid.ColumnDefinitions>
                <Image Grid.Column="0" Source="{Binding Icon}" Margin="5" HeightRequest="45" />
                <Label Grid.Column="1" Text="{Binding Title}" FontAttributes="Bold"
                       FontSize="18" VerticalOptions="Center" />
            </Grid>
        </DataTemplate>
    </Shell.ItemTemplate>
</Shell>
```
