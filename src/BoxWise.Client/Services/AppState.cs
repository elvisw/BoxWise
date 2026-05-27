namespace BoxWise.Client.Services;

public class AppState
{
    public string? CurrentUserName { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsPasswordManagedByEnv { get; private set; }
    public bool IsLoggedIn => CurrentUserName is not null;

    public int? ContinuousLocationId { get; private set; }
    public string? ContinuousLocationName { get; private set; }

    public event Action? StateChanged;

    public void SetUser(string userName, bool isAdmin, bool isPasswordManagedByEnv = false)
    {
        CurrentUserName = userName;
        IsAdmin = isAdmin;
        IsPasswordManagedByEnv = isPasswordManagedByEnv;
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        CurrentUserName = null;
        IsAdmin = false;
        IsPasswordManagedByEnv = false;
        StateChanged?.Invoke();
    }

    public void SetContinuousLocation(int locationId, string locationName)
    {
        ContinuousLocationId = locationId;
        ContinuousLocationName = locationName;
        StateChanged?.Invoke();
    }

    public void ClearContinuousLocation()
    {
        ContinuousLocationId = null;
        ContinuousLocationName = null;
        StateChanged?.Invoke();
    }
}
