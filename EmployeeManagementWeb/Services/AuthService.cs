using EmployeeManagementWeb.Models;
using Microsoft.JSInterop;

namespace EmployeeManagementWeb.Services;

public class AuthService
{
    const string SessionKey = "ems.session";
    readonly DataStore store;
    readonly IJSRuntime js;

    public User? Current { get; private set; }

    public bool IsLoggedIn => Current != null;
    public bool IsAdmin => Current?.Role is "Owner" or "Admin";
    public bool IsOwner => Current?.Role == "Owner";
    public int? EmployeeId => Current?.EmployeeId;

    public AuthService(DataStore store, IJSRuntime js)
    {
        this.store = store;
        this.js = js;
    }

    /// <summary>Loads data and restores the login if this browser tab already has a session.</summary>
    public async Task RestoreAsync()
    {
        await store.InitAsync();
        if (Current != null) return;

        var s = await js.InvokeAsync<string?>("ems.sget", SessionKey);
        if (int.TryParse(s, out var id))
            Current = store.Data.Users.FirstOrDefault(u => u.Id == id);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        await store.InitAsync();
        var name = (username ?? "").Trim();
        var user = store.Data.Users.FirstOrDefault(u =>
            string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));

        if (user == null || PasswordHasher.Hash(password ?? "", user.Salt) != user.PasswordHash)
            return false;

        Current = user;
        await js.InvokeVoidAsync("ems.sset", SessionKey, user.Id.ToString());
        return true;
    }

    public async Task LogoutAsync()
    {
        Current = null;
        await js.InvokeVoidAsync("ems.sremove", SessionKey);
    }
}
