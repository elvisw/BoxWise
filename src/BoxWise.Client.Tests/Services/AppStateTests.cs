using BoxWise.Client.Services;

namespace BoxWise.Client.Tests.Services;

public class AppStateTests
{
    [Fact]
    public void SetUser_SetsProperties()
    {
        var state = new AppState();

        state.SetUser("elvis", true, false);

        Assert.Equal("elvis", state.CurrentUserName);
        Assert.True(state.IsAdmin);
        Assert.False(state.IsPasswordManagedByEnv);
    }

    [Fact]
    public void SetUser_IsLoggedIn_ReturnsTrue()
    {
        var state = new AppState();

        state.SetUser("elvis", false);

        Assert.True(state.IsLoggedIn);
    }

    [Fact]
    public void SetUser_FiresStateChanged()
    {
        var state = new AppState();
        var fireCount = 0;
        state.StateChanged += () => fireCount++;

        state.SetUser("elvis", false);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SetUser_PasswordManagedByEnv_DefaultsToFalse()
    {
        var state = new AppState();

        state.SetUser("elvis", true);

        Assert.False(state.IsPasswordManagedByEnv);
    }

    [Fact]
    public void Clear_ResetsAllProperties()
    {
        var state = new AppState();
        state.SetUser("elvis", true, true);

        state.Clear();

        Assert.Null(state.CurrentUserName);
        Assert.False(state.IsAdmin);
        Assert.False(state.IsPasswordManagedByEnv);
    }

    [Fact]
    public void Clear_IsLoggedIn_ReturnsFalse()
    {
        var state = new AppState();
        state.SetUser("elvis", false);

        state.Clear();

        Assert.False(state.IsLoggedIn);
    }

    [Fact]
    public void Clear_FiresStateChanged()
    {
        var state = new AppState();
        state.SetUser("elvis", false);
        var fireCount = 0;
        state.StateChanged += () => fireCount++;

        state.Clear();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SetContinuousLocation_SetsAndFiresEvent()
    {
        var state = new AppState();
        var fireCount = 0;
        state.StateChanged += () => fireCount++;

        state.SetContinuousLocation(5, "车库");

        Assert.Equal(5, state.ContinuousLocationId);
        Assert.Equal("车库", state.ContinuousLocationName);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void ClearContinuousLocation_ResetsAndFiresEvent()
    {
        var state = new AppState();
        state.SetContinuousLocation(5, "车库");
        var fireCount = 0;
        state.StateChanged += () => fireCount++;

        state.ClearContinuousLocation();

        Assert.Null(state.ContinuousLocationId);
        Assert.Null(state.ContinuousLocationName);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void InitialState_IsLoggedIn_ReturnsFalse()
    {
        var state = new AppState();

        Assert.False(state.IsLoggedIn);
        Assert.Null(state.CurrentUserName);
        Assert.False(state.IsAdmin);
    }

    [Fact]
    public void StateChanged_NoSubscribers_DoesNotThrow()
    {
        var state = new AppState();

        state.SetUser("elvis", false);
        state.Clear();
        state.SetContinuousLocation(1, "test");
        state.ClearContinuousLocation();

        Assert.True(true);
    }
}
