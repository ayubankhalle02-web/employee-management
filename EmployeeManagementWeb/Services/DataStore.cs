using System.Globalization;
using System.Text.Json;
using EmployeeManagementWeb.Models;
using Microsoft.JSInterop;

namespace EmployeeManagementWeb.Services;

/// <summary>Holds the in-memory database and persists it to localStorage.</summary>
public class DataStore
{
    // bumped to v2 when payroll fields and the sample employees were added (older saved data is ignored)
    const string Key = "ems.data.v2";
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

    // No., name, position, phone, address, monthly basic salary, date hired
    static readonly (string No, string Name, string Position, string Phone, string Address, decimal Salary, string Hired)[] People =
    {
        ("EMP001", "Juan Dela Cruz",     "Operations Manager",          "0917-555-0101", "Quezon City",       55000m, "2021-03-15"),
        ("EMP002", "Maria Santos",       "HR Officer",                  "0917-555-0102", "Pasig City",        32000m, "2022-06-01"),
        ("EMP003", "Carlo Mendoza",      "Senior Accountant",           "0917-555-0103", "Makati City",       42000m, "2021-09-20"),
        ("EMP004", "Angelica Reyes",     "Sales Supervisor",            "0917-555-0104", "Mandaluyong City",  30000m, "2022-11-07"),
        ("EMP005", "Paolo Villanueva",   "Software Developer",          "0917-555-0105", "Taguig City",       48000m, "2023-02-13"),
        ("EMP006", "Kristine Bautista",  "Marketing Specialist",        "0917-555-0106", "Marikina City",     28000m, "2023-05-22"),
        ("EMP007", "Miguel Torres",      "Warehouse Supervisor",        "0917-555-0107", "Caloocan City",     26000m, "2022-08-01"),
        ("EMP008", "Jasmine Aquino",     "Customer Service Rep",        "0917-555-0108", "Paranaque City",    20000m, "2024-01-15"),
        ("EMP009", "Rafael Navarro",     "IT Support Specialist",       "0917-555-0109", "Las Pinas City",    25000m, "2024-04-08"),
        ("EMP010", "Bianca Castillo",    "Administrative Assistant",    "0917-555-0110", "Manila",            19000m, "2026-03-02"),
        ("EMP011", "Daniel Ramos",       "Cashier",                     "0917-555-0111", "San Juan City",     18500m, "2025-07-14"),
        ("EMP012", "Hazel Fernandez",    "Inventory Clerk",             "0917-555-0112", "Valenzuela City",   17500m, "2026-05-04"),
    };

    void Seed()
    {
        Data = new Db();

        int id = 1;
        foreach (var p in People)
        {
            Data.Employees.Add(new Employee
            {
                Id = id++, EmployeeNo = p.No, FullName = p.Name, Position = p.Position, Phone = p.Phone,
                Address = p.Address, BasicSalary = p.Salary, DateHired = p.Hired, Status = "Active"
            });
        }

        Data.Users.Add(MakeUser(1, "owner", "owner123", "Owner", null));
        Data.Users.Add(MakeUser(2, "employee", "employee123", "Employee", 2));   // Maria Santos

        SeedPayrollHistory();
    }

    /// <summary>January - September 2026 payroll for every employee, so the salary page and 13th month have real data.</summary>
    void SeedPayrollHistory()
    {
        int salaryId = 1;
        foreach (var e in Data.Employees)
        {
            var hired = DateTime.ParseExact(e.DateHired, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int m = 1; m <= 9; m++)
            {
                var monthStart = new DateTime(2026, m, 1);
                if (monthStart <= hired) continue;                    // first full month after hiring

                var rnd = new Random(e.Id * 100 + m);                 // fixed seed = same sample data every time
                var input = new PayrollInput(
                    LateMinutes: rnd.Next(0, 3) == 0 ? rnd.Next(5, 50) : 0,
                    AbsentDays: rnd.Next(0, 9) == 0 ? 1 : 0,
                    OvertimeHours: rnd.Next(0, 3) == 0 ? rnd.Next(2, 9) : 0,
                    Allowance: 1500m,
                    RegHolidayDays: rnd.Next(0, 6) == 0 ? 1 : 0,
                    SpecialHolidayDays: 0,
                    NightHours: 0,
                    LeaveDays: 0,
                    OtherDeductions: e.Id % 4 == 0 ? 1000m : 0m);    // e.g. a company loan repayment

                Data.Salaries.Add(PayrollCalculator.BuildRecord(salaryId++, e, $"2026-{m:00}", input));
            }
        }
    }
}
