namespace BoxWise.Client.Services;

/// <summary>
/// 客户端全局应用状态。Blazor WASM 运行在 UI 线程上（单线程模型），
/// 所有状态变更通过事件回调 <see cref="StateChanged"/> 通知 UI 刷新，
/// 因此无需额外的线程同步保护。
/// </summary>
public class AppState
{
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
        CurrentUserName = userName;
        IsAdmin = isAdmin;
        IsPasswordManagedByEnv = isPasswordManagedByEnv;
        CurrentUserEmail = email;
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        CurrentUserName = null;
        CurrentUserEmail = null;
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
