using System.Text.Json;
using EmployeeManagementWeb.Models;
using Microsoft.JSInterop;

namespace EmployeeManagementWeb.Services;

/// <summary>Holds the in-memory database and persists it to localStorage.</summary>
public class DataStore
{
    const string Key = "ems.data.v1";
    readonly IJSRuntime js;
    bool loaded;

    public Db Data { get; private set; } = new();

    public DataStore(IJSRuntime js) => this.js = js;

    public async Task InitAsync()
    {
        if (loaded) return;

        var json = await js.InvokeAsync<string?>("ems.load", Key);
        if (!string.IsNullOrEmpty(json))
        {
            try { Data = JsonSerializer.Deserialize<Db>(json) ?? new Db(); }
            catch { Data = new Db(); }
        }

        if (Data.Users.Count == 0) Seed();
        loaded = true;
        await SaveAsync();
    }

    /// <summary>Returns false if the browser refused the write (storage full).</summary>
    public async Task<bool> SaveAsync()
        => await js.InvokeAsync<bool>("ems.save", Key, JsonSerializer.Serialize(Data));

    public async Task ResetAsync()
    {
        Seed();
        loaded = true;
        await SaveAsync();
    }

    static User MakeUser(int id, string username, string password, string role, int? employeeId)
    {
        var salt = PasswordHasher.NewSalt();
        return new User
        {
            Id = id, Username = username, Salt = salt,
            PasswordHash = PasswordHasher.Hash(password, salt),
            Role = role, EmployeeId = employeeId
        };
    }

    void Seed()
    {
        Data = new Db();

        Data.Employees.Add(new Employee
        {
            Id = 1, EmployeeNo = "EMP001", FullName = "Juan Dela Cruz", Position = "Manager",
            Phone = "09123456789", Address = "Sample Address", BasicSalary = 25000m,
            DateHired = "2026-01-05", Status = "Active"
        });
        Data.Employees.Add(new Employee
        {
            Id = 2, EmployeeNo = "EMP002", FullName = "Maria Santos", Position = "Employee",
            Phone = "09987654321", Address = "Sample Address", BasicSalary = 18000m,
            DateHired = "2026-02-10", Status = "Active"
        });

        Data.Users.Add(MakeUser(1, "owner", "owner123", "Owner", null));
        Data.Users.Add(MakeUser(2, "employee", "employee123", "Employee", 2));
    }
}
