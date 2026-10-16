using System.Globalization;
using System.Text;
using System.Text.Json;
using EmployeeManagementWeb.Models;
using Microsoft.JSInterop;

namespace EmployeeManagementWeb.Services;

public class DataStore
{
    const string Key = "ems.data.v4";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly IJSRuntime js;
    bool loaded;

    public Db Data { get; private set; } = new();

    public DataStore(IJSRuntime js) => this.js = js;

    async Task<DateTime> NowAsync()
        => DateTime.ParseExact(await js.InvokeAsync<string>("ems.now"), "yyyy-MM-dd'T'HH:mm:ss", Inv);

    public async Task InitAsync()
    {
        if (loaded) return;

        var json = await js.InvokeAsync<string?>("ems.load", Key);
        if (!string.IsNullOrEmpty(json))
        {
            try { Data = JsonSerializer.Deserialize<Db>(json) ?? new Db(); }
            catch { Data = new Db(); }
        }

        if (Data.Users.Count == 0) Seed(await NowAsync());
        loaded = true;
        await SaveAsync();
    }

    public async Task<bool> SaveAsync()
        => await js.InvokeAsync<bool>("ems.save", Key, JsonSerializer.Serialize(Data));

    public async Task ResetAsync()
    {
        Seed(await NowAsync());
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

    static readonly string[] AvatarColors = { "#5b5bf0", "#8a5cf6", "#f0509b", "#ff8a3d", "#22b573", "#2f9bff" };

    void Seed(DateTime now)
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
        Data.Users.Add(MakeUser(2, "employee", "employee123", "Employee", 2));

        var windowStart = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
        SeedAttendance(now, windowStart);
        SeedPayrollHistory(now, windowStart);
    }

    static DateTime ParseDate(string s)
        => DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

    static string PlaceholderPhoto(Employee e)
    {
        var parts = e.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        var color = AvatarColors[e.Id % AvatarColors.Length];
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect width='64' height='64' fill='" + color +
                  "'/><text x='32' y='41' font-size='24' font-family='Arial' fill='white' text-anchor='middle'>" + initials + "</text></svg>";
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    const int NightShiftEmployeeId = 9;

    void SeedAttendance(DateTime now, DateTime windowStart)
    {
        int recordId = 1;
        foreach (var e in Data.Employees)
        {
            var hired = ParseDate(e.DateHired);
            var photo = PlaceholderPhoto(e);

            for (var d = windowStart; d <= now.Date; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Sunday || d < hired) continue;

                var rnd = new Random(e.Id * 100003 + d.Year * 400 + d.DayOfYear);
                bool isToday = d == now.Date;

                if (e.Id == NightShiftEmployeeId)
                {
                    if (isToday || rnd.Next(0, 20) == 0) continue;
                    var nightIn = d + new TimeSpan(22, rnd.Next(30, 60), 0).Add(TimeSpan.FromMinutes(rnd.Next(0, 16)));
                    var nightOut = d.AddDays(1) + new TimeSpan(6, rnd.Next(0, 31), 0);
                    Data.Attendance.Add(new AttendanceRecord
                    {
                        Id = recordId++, EmployeeId = e.Id, Date = EmsService.DateKey(d),
                        TimeIn = nightIn, TimeOut = (nightOut > now || (d == now.Date.AddDays(-1) && (now - nightIn).TotalHours <= 20)) ? null : nightOut, Photo = photo
                    });
                    continue;
                }

                if (isToday)
                {
                    if (now.Hour < 9 || e.Id == 2 || rnd.Next(0, 4) == 0) continue;
                }
                else if (rnd.Next(0, 20) == 0)
                {
                    continue;
                }

                bool late = !isToday && rnd.Next(0, 10) < 3;
                var timeIn = d + (late ? new TimeSpan(9, rnd.Next(1, 46), 0) : new TimeSpan(8, rnd.Next(30, 60), 0));

                DateTime? timeOut = null;
                if (!isToday)
                {
                    bool overtime = rnd.Next(0, 10) < 2;
                    timeOut = d + (overtime ? new TimeSpan(18, rnd.Next(15, 120), 0) : new TimeSpan(17, rnd.Next(30, 60), 0));
                }

                Data.Attendance.Add(new AttendanceRecord
                {
                    Id = recordId++, EmployeeId = e.Id, Date = EmsService.DateKey(d),
                    TimeIn = timeIn, TimeOut = timeOut, Photo = photo
                });
            }
        }
    }

    void SeedPayrollHistory(DateTime now, DateTime windowStart)
    {
        int salaryId = 1;
        foreach (var e in Data.Employees)
        {
            var hired = ParseDate(e.DateHired);
            for (int m = 1; m < now.Month; m++)
            {
                var monthStart = new DateTime(now.Year, m, 1);
                if (monthStart <= hired) continue;

                var period = $"{now.Year}-{m:00}";
                var loan = e.Id % 4 == 0 ? 1000m : 0m;

                PayrollInput input;
                if (monthStart >= windowStart)
                {
                    var s = PayrollCalculator.Summarize(Data, e, period, now);
                    input = new PayrollInput(s.LateMinutes, s.AbsentDays, s.OvertimeHours, 1500m, 0, 0, s.NightHours, 0, loan);
                }
                else
                {
                    var rnd = new Random(e.Id * 100 + m);
                    input = new PayrollInput(
                        LateMinutes: rnd.Next(0, 3) == 0 ? rnd.Next(5, 50) : 0,
                        AbsentDays: rnd.Next(0, 9) == 0 ? 1 : 0,
                        OvertimeHours: rnd.Next(0, 3) == 0 ? rnd.Next(2, 9) : 0,
                        Allowance: 1500m,
                        RegHolidayDays: rnd.Next(0, 6) == 0 ? 1 : 0,
                        SpecialHolidayDays: 0,
                        NightHours: e.Id == NightShiftEmployeeId ? 7m * rnd.Next(22, 27) : 0m,
                        LeaveDays: 0,
                        OtherDeductions: loan);
                }

                Data.Salaries.Add(PayrollCalculator.BuildRecord(salaryId++, e, period, input));
            }
        }
    }
}
