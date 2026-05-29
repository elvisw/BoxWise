namespace BoxWise.Client.Services;

public class AppState
{
    private readonly object _lock = new();

    public string? CurrentUserName { get; private set; }
    public string? CurrentUserEmail { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsPasswordManagedByEnv { get; private set; }
    public bool IsLoggedIn => CurrentUserName is not null;

    public int? ContinuousLocationId { get; private set; }
    public string? ContinuousLocationName { get; private set; }

    public event Action? StateChanged;

    public void SetUser(string userName, bool isAdmin, bool isPasswordManagedByEnv = false, string? email = null)
    {
        lock (_lock)
        {
            CurrentUserName = userName;
            IsAdmin = isAdmin;
            IsPasswordManagedByEnv = isPasswordManagedByEnv;
            CurrentUserEmail = email;
            StateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            CurrentUserName = null;
            CurrentUserEmail = null;
            IsAdmin = false;
            IsPasswordManagedByEnv = false;
            StateChanged?.Invoke();
        }
    }

    public void SetContinuousLocation(int locationId, string locationName)
    {
        lock (_lock)
        {
            ContinuousLocationId = locationId;
            ContinuousLocationName = locationName;
            StateChanged?.Invoke();
        }
    }

    public void ClearContinuousLocation()
    {
        lock (_lock)
        {
            ContinuousLocationId = null;
            ContinuousLocationName = null;
            StateChanged?.Invoke();
        }
    }
}
