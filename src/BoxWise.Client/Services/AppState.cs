namespace BoxWise.Client.Services;

public class AppState
{
    public string? CurrentUserName { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsLoggedIn => CurrentUserName is not null;

    public event Action? StateChanged;

    public void SetUser(string userName, bool isAdmin)
    {
        CurrentUserName = userName;
        IsAdmin = isAdmin;
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        CurrentUserName = null;
        IsAdmin = false;
        StateChanged?.Invoke();
    }
}
