# Template starter MainPage - XAML source

File: `templates/honua-fieldcollector/MainPage.xaml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage x:Class="HonuaFieldCollector.MainPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:honua="http://schemas.honua.com/mobile/2024"
             xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
             Title="YOUR_COMPANY_NAME Field Collection">

    <Grid RowDefinitions="Auto,*,Auto">

        <!-- GPS Status & Accuracy -->
        <honua:HonuaLocationIndicator Grid.Row="0"
                                     x:Name="LocationIndicator"
                                     ShowAccuracy="true"
                                     RequiredAccuracy="5.0"
                                     UpdateInterval="2000"
                                     BackgroundColor="{AppThemeBinding Light=#E3F2FD, Dark=#1565C0}"
                                     Padding="15,10"
                                     LocationUpdated="OnLocationUpdated" />

        <!-- Main Content -->
        <Grid Grid.Row="1">
            <TabView x:Name="MainTabs"
                    TabStripBackgroundColor="{AppThemeBinding Light=#F5F5F5, Dark=#424242}"
                    TabStripHeight="60">

                <TabViewItem Text="📝 Collect" TextColor="{StaticResource Primary}" TextColorSelected="{StaticResource Primary}">
                    <ScrollView>
                        <StackLayout Spacing="0" Padding="20">
                            <Frame BackgroundColor="{StaticResource Primary}" CornerRadius="10" Padding="20"
                                  Margin="0,0,0,20" HasShadow="True">
                                <StackLayout>
                                    <Label Text="🚀 Field Data Collection" FontSize="20" FontAttributes="Bold"
                                          TextColor="White" HorizontalOptions="Center" />
                                    <Label Text="Professional data collection that competes with Fulcrum &amp; Survey123"
                                          FontSize="14" TextColor="White" HorizontalOptions="Center" Margin="0,5,0,0" />
                                </StackLayout>
                            </Frame>

                            <honua:HonuaFeatureForm x:Name="DataForm" FormId="field-site-inspection"
                                                    AllowDrafts="true" ShowProgress="true" />

                            <Grid ColumnDefinitions="*,*" ColumnSpacing="10" Margin="0,20,0,0">
                                <Button Grid.Column="0" Text="📷 Quick Photo"
                                       Command="{Binding QuickPhotoCommand}"
                                       BackgroundColor="{StaticResource Secondary}" TextColor="White" CornerRadius="8" />
                                <Button Grid.Column="1" Text="🗺️ View Map" Clicked="OnViewMapClicked"
                                       BackgroundColor="Transparent" BorderColor="{StaticResource Primary}"
                                       BorderWidth="1" TextColor="{StaticResource Primary}" CornerRadius="8" />
                            </Grid>
                        </StackLayout>
                    </ScrollView>
                </TabViewItem>

                <TabViewItem Text="🗺️ Map" TextColor="{StaticResource Secondary}" TextColorSelected="{StaticResource Secondary}">
                    <honua:HonuaMapView x:Name="MapView" ShowToolbar="true" ShowOverlays="true"
                                       EnableSpatialQuery="false" ShowCollectedFeatures="true" />
                </TabViewItem>

                <TabViewItem Text="📊 Stats" TextColor="Green" TextColorSelected="Green">
                    <ScrollView>
                        <StackLayout Padding="20" Spacing="20">
                            <Frame BackgroundColor="Green" CornerRadius="10" Padding="20" HasShadow="True">
                                <StackLayout>
                                    <Label Text="📊 Collection Statistics" FontSize="18" FontAttributes="Bold"
                                          TextColor="White" HorizontalOptions="Center" />
                                    <Grid ColumnDefinitions="*,*" ColumnSpacing="20" Margin="0,10,0,0">
                                        <StackLayout Grid.Column="0">
                                            <Label x:Name="RecordsCountLabel" Text="0" FontSize="32"
                                                  FontAttributes="Bold" TextColor="White" HorizontalOptions="Center" />
                                            <Label Text="Records" FontSize="14" TextColor="White" HorizontalOptions="Center" />
                                        </StackLayout>
                                        <StackLayout Grid.Column="1">
                                            <Label x:Name="PhotosCountLabel" Text="0" FontSize="32"
                                                  FontAttributes="Bold" TextColor="White" HorizontalOptions="Center" />
                                            <Label Text="Photos" FontSize="14" TextColor="White" HorizontalOptions="Center" />
                                        </StackLayout>
                                    </Grid>
                                </StackLayout>
                            </Frame>

                            <Label Text="📈 Recent Activity" FontSize="18" FontAttributes="Bold" />

                            <CollectionView x:Name="RecentActivityList" HeightRequest="300">
                                <CollectionView.ItemTemplate>
                                    <DataTemplate>
                                        <Grid ColumnDefinitions="Auto,*,Auto" Padding="15" RowSpacing="5">
                                            <Label Grid.Column="0" Text="{Binding Icon}" FontSize="24" VerticalOptions="Center" />
                                            <StackLayout Grid.Column="1" Margin="15,0,0,0" VerticalOptions="Center">
                                                <Label Text="{Binding Title}" FontSize="16" FontAttributes="Bold" />
                                                <Label Text="{Binding Description}" FontSize="14"
                                                      TextColor="{StaticResource Gray600}" />
                                            </StackLayout>
                                            <Label Grid.Column="2" Text="{Binding Time}" FontSize="12"
                                                  TextColor="{StaticResource Gray500}" VerticalOptions="Center" />
                                        </Grid>
                                    </DataTemplate>
                                </CollectionView.ItemTemplate>
                            </CollectionView>
                        </StackLayout>
                    </ScrollView>
                </TabViewItem>
            </TabView>

            <!-- Loading overlay (hidden by default) -->
            <Grid x:Name="LoadingOverlay" BackgroundColor="Black" Opacity="0.7" IsVisible="False">
                <StackLayout VerticalOptions="Center" HorizontalOptions="Center">
                    <ActivityIndicator x:Name="LoadingIndicator" IsRunning="False"
                                       Color="{StaticResource Primary}" Scale="2" />
                    <Label x:Name="LoadingMessage" Text="Loading..." TextColor="White" FontSize="16"
                          HorizontalOptions="Center" Margin="0,20,0,0" />
                </StackLayout>
            </Grid>
        </Grid>

        <!-- Sync Status & Controls -->
        <honua:HonuaSyncStatus Grid.Row="2" x:Name="SyncStatus" ShowDetails="false" EnableManualSync="true"
                              BackgroundColor="{AppThemeBinding Light=#F5F5F5, Dark=#424242}"
                              Padding="15,10" />
    </Grid>

    <!-- Success Toast (hidden by default) -->
    <Grid x:Name="SuccessToast" BackgroundColor="Green" Opacity="0.9" Padding="20"
         IsVisible="False" VerticalOptions="Start" HorizontalOptions="Fill">
        <Label x:Name="SuccessMessage" Text="" TextColor="White" FontSize="16" FontAttributes="Bold"
              HorizontalOptions="Center" VerticalOptions="Center" />
    </Grid>
</ContentPage>
```
