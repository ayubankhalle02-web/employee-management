using System.Globalization;
using EmployeeManagementWeb.Models;
using Microsoft.JSInterop;

namespace EmployeeManagementWeb.Services;

public record Result(bool Ok, string Message)
{
    public static Result Success(string message = "") => new(true, message);
    public static Result Fail(string message) => new(false, message);
}

/// <summary>What the attendance records say about one employee in one month.</summary>
public record AttendanceSummary(int LateMinutes, int AbsentDays, decimal OvertimeHours);

/// <summary>Peso amounts derived from the attendance summary.</summary>
public record SalaryAmounts(decimal DailyRate, decimal Overtime, decimal LateDeduction, decimal AbsenceDeduction);

public record DashboardStats(int ActiveEmployees, int TodayAttendance, int PendingRequests, string? MyStatus);

/// <summary>
/// All business rules live here, and every method checks the caller's role,
/// so a page can never bypass permissions by accident.
/// </summary>
public class EmsService
{
    // ---- Payroll rules (change these to match your store's policy) ----
    public static readonly TimeSpan ShiftStart = new(9, 0, 0);    // late after 9:00 AM
    public static readonly TimeSpan ShiftEnd = new(18, 0, 0);     // overtime after 6:00 PM
    public const int WorkDaysPerMonth = 26;                       // daily rate = basic / 26
    public const decimal HoursPerDay = 8m;
    public const decimal OvertimeMultiplier = 1.25m;

    public static readonly string[] Roles = { "Owner", "Admin", "Employee" };
    public static readonly string[] RequestTypes = { "Certificate", "Leave Request", "Schedule Request", "Other" };

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly DataStore store;
    readonly AuthService auth;
    readonly IJSRuntime js;

    public EmsService(DataStore store, AuthService auth, IJSRuntime js)
    {
        this.store = store;
        this.auth = auth;
        this.js = js;
    }

    Db Data => store.Data;

    // ------------------------------------------------------------ helpers
    public async Task<DateTime> NowAsync()
    {
        var s = await js.InvokeAsync<string>("ems.now");
        return DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", Inv);
    }

    public static string DateKey(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

    static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;

    static Result Denied() => Result.Fail("You do not have permission to do that.");

    public Employee? GetEmployee(int id) => Data.Employees.FirstOrDefault(e => e.Id == id);
    public string EmployeeName(int id) => GetEmployee(id)?.FullName ?? "(deleted)";
    public string EmployeeNoOf(int id) => GetEmployee(id)?.EmployeeNo ?? "-";
    public string OwnName() => auth.EmployeeId is int id ? EmployeeName(id) : (auth.Current?.Username ?? "");

    // ---------------------------------------------------------- dashboard
    public async Task<DashboardStats> GetDashboardAsync()
    {
        var today = DateKey(await NowAsync());

        if (auth.IsAdmin)
        {
            return new DashboardStats(
                Data.Employees.Count(e => e.Status == "Active"),
                Data.Attendance.Count(a => a.Date == today),
                Data.Requests.Count(r => r.Status == "Pending"),
                null);
        }

        var id = auth.EmployeeId;
        var rec = Data.Attendance.FirstOrDefault(a => a.EmployeeId == id && a.Date == today);
        var mine = rec == null ? "Not punched in" : rec.TimeOut != null ? "Punched out" : "Punched in";
        return new DashboardStats(0, 0, Data.Requests.Count(r => r.EmployeeId == id && r.Status == "Pending"), mine);
    }

    // ---------------------------------------------------------- employees
    public List<Employee> ListEmployees() =>
        auth.IsAdmin
            ? Data.Employees.OrderByDescending(e => e.Id).ToList()
            : Data.Employees.Where(e => e.Id == auth.EmployeeId).ToList();

    /// <summary>Adds (Id == 0) or updates an employee. Admin/Owner only.</summary>
    public async Task<Result> SaveEmployeeAsync(Employee input)
    {
        if (!auth.IsAdmin) return Denied();

        var no = (input.EmployeeNo ?? "").Trim();
        var name = (input.FullName ?? "").Trim();
        if (no.Length == 0 || name.Length == 0) return Result.Fail("Employee No. and Full Name are required.");
        if (input.BasicSalary < 0) return Result.Fail("Basic salary cannot be negative.");
        if (!DateTime.TryParseExact(input.DateHired, "yyyy-MM-dd", Inv, DateTimeStyles.None, out _))
            return Result.Fail("Please enter a valid Date Hired.");
        if (Data.Employees.Any(e => e.Id != input.Id && string.Equals(e.EmployeeNo, no, StringComparison.OrdinalIgnoreCase)))
            return Result.Fail($"Employee No. {no} already exists.");

        Employee target;
        if (input.Id == 0)
        {
            target = new Employee { Id = NextId(Data.Employees.Select(e => e.Id)), Status = "Active" };
            Data.Employees.Add(target);
        }
        else
        {
            var existing = GetEmployee(input.Id);
            if (existing == null) return Result.Fail("Employee not found.");
            target = existing;
        }

        target.EmployeeNo = no;
        target.FullName = name;
        target.Position = (input.Position ?? "").Trim();
        target.Phone = (input.Phone ?? "").Trim();
        target.Address = (input.Address ?? "").Trim();
        target.BasicSalary = Math.Round(input.BasicSalary, 2);
        target.DateHired = input.DateHired;

        await store.SaveAsync();
        return Result.Success(input.Id == 0 ? "Employee added." : "Employee updated.");
    }

    public async Task<Result> SetEmployeeStatusAsync(int id, string status)
    {
        if (!auth.IsAdmin) return Denied();
        var e = GetEmployee(id);
        if (e == null) return Result.Fail("Employee not found.");
        e.Status = status == "Inactive" ? "Inactive" : "Active";
        await store.SaveAsync();
        return Result.Success($"{e.FullName} is now {e.Status}.");
    }

    /// <summary>Permanently deletes an employee and everything linked to them. Owner only.</summary>
    public async Task<Result> DeleteEmployeeAsync(int id)
    {
        if (!auth.IsOwner) return Result.Fail("Only an Owner can delete employees. Use Deactivate instead.");
        var e = GetEmployee(id);
        if (e == null) return Result.Fail("Employee not found.");

        Data.Attendance.RemoveAll(a => a.EmployeeId == id);
        Data.Salaries.RemoveAll(s => s.EmployeeId == id);
        Data.Requests.RemoveAll(r => r.EmployeeId == id);
        Data.Users.RemoveAll(u => u.EmployeeId == id);
        Data.Employees.Remove(e);

        await store.SaveAsync();
        return Result.Success($"{e.FullName} and all linked records were deleted.");
    }

    /// <summary>Employees may update only their own phone and address.</summary>
    public async Task<Result> UpdateOwnContactAsync(string phone, string address)
    {
        if (auth.EmployeeId is not int id || GetEmployee(id) is not Employee e)
            return Result.Fail("Your account is not linked to an employee record.");
        e.Phone = (phone ?? "").Trim();
        e.Address = (address ?? "").Trim();
        await store.SaveAsync();
        return Result.Success("Contact details saved.");
    }

    // --------------------------------------------------------- attendance
    public List<AttendanceRecord> ListAttendance() =>
        Data.Attendance
            .Where(a => auth.IsAdmin || a.EmployeeId == auth.EmployeeId)
            .OrderByDescending(a => a.TimeIn ?? DateTime.MinValue)
            .ToList();

    /// <summary>Admins punch for the chosen employee; everyone else can only punch for themselves.</summary>
    Employee? ResolveTarget(string? empNo, out string error)
    {
        error = "";
        Employee? emp;

        if (auth.IsAdmin)
        {
            var wanted = (empNo ?? "").Trim();
            emp = Data.Employees.FirstOrDefault(e => string.Equals(e.EmployeeNo, wanted, StringComparison.OrdinalIgnoreCase));
            if (emp == null) error = "Please choose an employee.";
        }
        else
        {
            emp = auth.EmployeeId is int id ? GetEmployee(id) : null;
            if (emp == null) error = "Your account is not linked to an employee record.";
        }

        if (emp != null && emp.Status != "Active")
        {
            error = "This employee is inactive.";
            return null;
        }
        return emp;
    }

    public async Task<Result> PunchInAsync(string? empNo, string photo)
    {
        var emp = ResolveTarget(empNo, out var error);
        if (emp == null) return Result.Fail(error);

        var now = await NowAsync();
        var key = DateKey(now);
        if (Data.Attendance.Any(a => a.EmployeeId == emp.Id && a.Date == key))
            return Result.Fail($"{emp.FullName} already has an attendance record today.");

        var rec = new AttendanceRecord
        {
            Id = NextId(Data.Attendance.Select(a => a.Id)),
            EmployeeId = emp.Id,
            Date = key,
            TimeIn = now,
            Photo = photo ?? ""
        };
        Data.Attendance.Add(rec);

        if (!await store.SaveAsync())
        {
            Data.Attendance.Remove(rec);
            return Result.Fail("Browser storage is full. Delete old data or use a smaller photo.");
        }
        return Result.Success($"{emp.FullName} punched IN at {now.ToString("hh:mm:ss tt", Inv)}.");
    }

    public async Task<Result> PunchOutAsync(string? empNo)
    {
        var emp = ResolveTarget(empNo, out var error);
        if (emp == null) return Result.Fail(error);

        var now = await NowAsync();
        var key = DateKey(now);
        var rec = Data.Attendance.FirstOrDefault(a => a.EmployeeId == emp.Id && a.Date == key);

        if (rec == null) return Result.Fail($"{emp.FullName} has not punched in today.");
        if (rec.TimeOut != null) return Result.Fail($"{emp.FullName} already punched out today.");

        rec.TimeOut = now;
        await store.SaveAsync();
        return Result.Success($"{emp.FullName} punched OUT at {now.ToString("hh:mm:ss tt", Inv)}.");
    }

    // ------------------------------------------------------------- salary
    public List<SalaryRecord> ListSalary() =>
        Data.Salaries
            .Where(s => auth.IsAdmin || s.EmployeeId == auth.EmployeeId)
            .OrderByDescending(s => s.Period).ThenByDescending(s => s.Id)
            .ToList();

    /// <summary>Counts late minutes, absent days and overtime hours from attendance for a month.</summary>
    public AttendanceSummary Summarize(int employeeId, string period, DateTime today)
    {
        var emp = GetEmployee(employeeId);
        if (emp == null || !DateTime.TryParseExact(period + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var first))
            return new AttendanceSummary(0, 0, 0);

        var last = first.AddMonths(1).AddDays(-1);
        var recs = Data.Attendance.Where(a => a.EmployeeId == employeeId && a.Date.StartsWith(period)).ToList();

        int lateMinutes = 0;
        double otHours = 0;
        foreach (var a in recs)
        {
            if (a.TimeIn is DateTime tin && tin.TimeOfDay > ShiftStart)
                lateMinutes += (int)(tin.TimeOfDay - ShiftStart).TotalMinutes;
            if (a.TimeOut is DateTime tout && tout.TimeOfDay > ShiftEnd)
                otHours += (tout.TimeOfDay - ShiftEnd).TotalHours;
        }

        // Absent = a Mon-Sat day (after the hire date, before today) with no attendance record.
        var startDay = first;
        if (DateTime.TryParseExact(emp.DateHired, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var hired) && hired > first)
            startDay = hired;
        var endDay = last < today.Date ? last : today.Date.AddDays(-1);

        var present = recs.Select(a => a.Date).ToHashSet();
        int absent = 0;
        for (var d = startDay; d <= endDay; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!present.Contains(DateKey(d))) absent++;
        }

        return new AttendanceSummary(lateMinutes, absent, Math.Round((decimal)otHours, 2));
    }

    public static SalaryAmounts CalculateAmounts(decimal basic, int lateMinutes, int absentDays, decimal overtimeHours)
    {
        var daily = Math.Round(basic / WorkDaysPerMonth, 2);
        var hourly = daily / HoursPerDay;
        return new SalaryAmounts(
            daily,
            Math.Round(overtimeHours * hourly * OvertimeMultiplier, 2),
            Math.Round(lateMinutes * hourly / 60m, 2),
            Math.Round(absentDays * daily, 2));
    }

    /// <summary>Saves a payroll record. One record per employee per month (saving again replaces it).</summary>
    public async Task<Result> SaveSalaryAsync(SalaryRecord input)
    {
        if (!auth.IsAdmin) return Denied();

        var emp = GetEmployee(input.EmployeeId);
        if (emp == null) return Result.Fail("Please choose an employee.");
        if (!DateTime.TryParseExact((input.Period ?? "") + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out _))
            return Result.Fail("Please choose a valid month.");
        if (input.Allowance < 0 || input.LateMinutes < 0 || input.AbsentDays < 0 || input.OvertimeHours < 0)
            return Result.Fail("Values cannot be negative.");

        var amt = CalculateAmounts(emp.BasicSalary, input.LateMinutes, input.AbsentDays, input.OvertimeHours);
        var allowance = Math.Round(input.Allowance, 2);
        var net = emp.BasicSalary + amt.Overtime + allowance - amt.LateDeduction - amt.AbsenceDeduction;
        if (net < 0) return Result.Fail("Deductions are larger than the pay. Please check the values.");

        var rec = Data.Salaries.FirstOrDefault(s => s.EmployeeId == emp.Id && s.Period == input.Period);
        var isNew = rec == null;
        if (rec == null)
        {
            rec = new SalaryRecord { Id = NextId(Data.Salaries.Select(s => s.Id)), EmployeeId = emp.Id, Period = input.Period! };
            Data.Salaries.Add(rec);
        }

        rec.Basic = emp.BasicSalary;
        rec.Overtime = amt.Overtime;
        rec.Allowance = allowance;
        rec.LateDeduction = amt.LateDeduction;
        rec.AbsenceDeduction = amt.AbsenceDeduction;
        rec.Net = net;
        rec.LateMinutes = input.LateMinutes;
        rec.AbsentDays = input.AbsentDays;
        rec.OvertimeHours = input.OvertimeHours;

        await store.SaveAsync();
        return Result.Success($"{(isNew ? "Saved" : "Updated")} payroll for {emp.FullName} ({input.Period}). Net: ₱{net:N2}");
    }

    public async Task<Result> DeleteSalaryAsync(int id)
    {
        if (!auth.IsAdmin) return Denied();
        Data.Salaries.RemoveAll(s => s.Id == id);
        await store.SaveAsync();
        return Result.Success("Payroll record deleted.");
    }

    // ----------------------------------------------------------- accounts
    public List<User> ListUsers() => auth.IsAdmin ? Data.Users.OrderBy(u => u.Id).ToList() : new List<User>();

    public async Task<Result> CreateAccountAsync(string username, string password, string role, int employeeId)
    {
        if (!auth.IsAdmin) return Denied();

        var name = (username ?? "").Trim();
        if (name.Length < 3) return Result.Fail("Username must be at least 3 characters.");
        if ((password ?? "").Length < 6) return Result.Fail("Password must be at least 6 characters.");
        if (!Roles.Contains(role)) return Result.Fail("Invalid role.");
        if (role == "Owner" && !auth.IsOwner) return Result.Fail("Only an Owner can create Owner accounts.");
        if (Data.Users.Any(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Fail("That username is already taken.");

        int? linked = null;
        if (role == "Employee")
        {
            var emp = GetEmployee(employeeId);
            if (emp == null) return Result.Fail("Please choose the employee this account belongs to.");
            if (Data.Users.Any(u => u.EmployeeId == emp.Id)) return Result.Fail($"{emp.FullName} already has an account.");
            linked = emp.Id;
        }

        var salt = PasswordHasher.NewSalt();
        Data.Users.Add(new User
        {
            Id = NextId(Data.Users.Select(u => u.Id)),
            Username = name,
            Salt = salt,
            PasswordHash = PasswordHasher.Hash(password!, salt),
            Role = role,
            EmployeeId = linked
        });
        await store.SaveAsync();
        return Result.Success("Account created.");
    }

    Result? CanManage(User? target)
    {
        if (!auth.IsAdmin) return Denied();
        if (target == null) return Result.Fail("Account not found.");
        if (target.Role == "Owner" && !auth.IsOwner) return Result.Fail("Only an Owner can change an Owner account.");
        return null;
    }

    public async Task<Result> ResetPasswordAsync(int userId, string newPassword)
    {
        var target = Data.Users.FirstOrDefault(u => u.Id == userId);
        var denied = CanManage(target);
        if (denied != null) return denied;
        if ((newPassword ?? "").Length < 6) return Result.Fail("Password must be at least 6 characters.");

        target!.Salt = PasswordHasher.NewSalt();
        target.PasswordHash = PasswordHasher.Hash(newPassword!, target.Salt);
        await store.SaveAsync();
        return Result.Success($"Password for {target.Username} was reset.");
    }

    public async Task<Result> DeleteUserAsync(int userId)
    {
        var target = Data.Users.FirstOrDefault(u => u.Id == userId);
        var denied = CanManage(target);
        if (denied != null) return denied;
        if (target!.Id == auth.Current?.Id) return Result.Fail("You cannot delete the account you are logged in with.");
        if (target.Role == "Owner" && Data.Users.Count(u => u.Role == "Owner") <= 1)
            return Result.Fail("You cannot delete the last Owner account.");

        Data.Users.Remove(target);
        await store.SaveAsync();
        return Result.Success("Account deleted.");
    }

    // ----------------------------------------------------------- requests
    public List<ServiceRequest> ListRequests() =>
        Data.Requests
            .Where(r => auth.IsAdmin || r.EmployeeId == auth.EmployeeId)
            .OrderBy(r => r.Status == "Pending" ? 0 : 1)
            .ThenByDescending(r => r.RequestDate)
            .ToList();

    public async Task<Result> SubmitRequestAsync(string type, string details)
    {
        if (auth.EmployeeId is not int id) return Result.Fail("Your account is not linked to an employee record.");
        if (!RequestTypes.Contains(type)) return Result.Fail("Please choose a request type.");

        var text = (details ?? "").Trim();
        if (text.Length == 0) return Result.Fail("Please describe your request.");
        if (text.Length > 500) return Result.Fail("Details are too long (500 characters max).");

        Data.Requests.Add(new ServiceRequest
        {
            Id = NextId(Data.Requests.Select(r => r.Id)),
            EmployeeId = id,
            RequestType = type,
            Details = text,
            RequestDate = await NowAsync(),
            Status = "Pending"
        });
        await store.SaveAsync();
        return Result.Success("Service request submitted.");
    }

    public async Task<Result> SetRequestStatusAsync(int id, string status)
    {
        if (!auth.IsAdmin) return Denied();
        if (status != "Approved" && status != "Rejected" && status != "Pending") return Result.Fail("Invalid status.");
        var r = Data.Requests.FirstOrDefault(x => x.Id == id);
        if (r == null) return Result.Fail("Request not found.");
        r.Status = status;
        await store.SaveAsync();
        return Result.Success($"Request marked {status}.");
    }
}
