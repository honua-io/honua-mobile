namespace Honua.Mobile.FieldCollection.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route);
    Task NavigateToAsync(string route, IDictionary<string, object> parameters);
    Task GoBackAsync();
    Task PopToRootAsync();
    Task DisplayAlert(string title, string message, string cancel);
    Task<bool> DisplayAlert(string title, string message, string accept, string cancel);
    Task<string> DisplayActionSheet(string title, string cancel, string destruction, params string[] buttons);
    Task<string> DisplayPromptAsync(
        string title,
        string message,
        string accept = "OK",
        string cancel = "Cancel",
        string placeholder = "",
        int maxLength = -1,
        string initialValue = "");
}
