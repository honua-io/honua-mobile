using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Services;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    protected readonly INavigationService NavigationService;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    public BaseViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    protected virtual async Task Refresh()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            await OnRefresh();
        }
        catch (Exception ex)
        {
            await ShowError("Refresh Failed", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    protected virtual Task OnRefresh()
    {
        return Task.CompletedTask;
    }

    [RelayCommand]
    protected Task GoBack()
    {
        return NavigationService.GoBackAsync();
    }

    protected Task ShowError(string title, string message)
    {
        return NavigationService.DisplayAlert(title, message, "OK");
    }

    protected Task ShowMessage(string title, string message)
    {
        return NavigationService.DisplayAlert(title, message, "OK");
    }

    protected Task<bool> ShowConfirmation(string title, string message, string accept = "Yes", string cancel = "No")
    {
        return NavigationService.DisplayAlert(title, message, accept, cancel);
    }

    protected async Task ExecuteAsync(Func<Task> operation, string? loadingMessage = null)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task<T?> ExecuteAsync<T>(Func<Task<T>> operation, string? loadingMessage = null)
    {
        if (IsBusy)
        {
            return default;
        }

        try
        {
            IsBusy = true;
            return await operation();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
            return default;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
