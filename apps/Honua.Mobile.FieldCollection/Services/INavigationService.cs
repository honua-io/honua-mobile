namespace Honua.Mobile.FieldCollection.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route);
    Task NavigateToAsync(string route, IDictionary<string, object> parameters);
    Task NavigateToAsync<T>(IDictionary<string, object> parameters) where T : ContentPage;
    Task GoBackAsync();
    Task PopToRootAsync();
    Task DisplayAlert(string title, string message, string cancel);
    Task<bool> DisplayAlert(string title, string message, string accept, string cancel);
    Task<string> DisplayActionSheet(string title, string cancel, string destruction, params string[] buttons);
    Task<string> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string placeholder = "", int maxLength = -1, Keyboard? keyboard = null, string initialValue = "");
}

public class NavigationService : INavigationService
{
    public async Task NavigateToAsync(string route)
    {
        await Shell.Current.GoToAsync(route);
    }

    public async Task NavigateToAsync(string route, IDictionary<string, object> parameters)
    {
        await Shell.Current.GoToAsync(route, parameters);
    }

    public async Task NavigateToAsync<T>(IDictionary<string, object> parameters) where T : ContentPage
    {
        var route = typeof(T).Name.Replace("Page", "").ToLowerInvariant();
        await NavigateToAsync(route, parameters);
    }

    public async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    public async Task PopToRootAsync()
    {
        await Shell.Current.GoToAsync("//");
    }

    public async Task DisplayAlert(string title, string message, string cancel)
    {
        await Shell.Current.DisplayAlert(title, message, cancel);
    }

    public async Task<bool> DisplayAlert(string title, string message, string accept, string cancel)
    {
        return await Shell.Current.DisplayAlert(title, message, accept, cancel);
    }

    public async Task<string> DisplayActionSheet(string title, string cancel, string destruction, params string[] buttons)
    {
        return await Shell.Current.DisplayActionSheet(title, cancel, destruction, buttons);
    }

    public async Task<string> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string placeholder = "", int maxLength = -1, Keyboard? keyboard = null, string initialValue = "")
    {
        return await Shell.Current.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue);
    }
}