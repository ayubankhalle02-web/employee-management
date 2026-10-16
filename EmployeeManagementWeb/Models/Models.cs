using System.Text.Json.Serialization;

namespace EmployeeManagementWeb.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = "Employee";
    public int? EmployeeId { get; set; }
}

public class Employee
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal BasicSalary { get; set; }
    public string DateHired { get; set; } = "";
    public string Status { get; set; } = "Active";

    public Employee Clone() => (Employee)MemberwiseClone();
}

public class AttendanceRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Date { get; set; } = "";
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public string Photo { get; set; } = "";
}

public class SalaryRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Period { get; set; } = "";
    public string Kind { get; set; } = "Monthly";

    public decimal Basic { get; set; }
    public decimal Overtime { get; set; }
    public decimal Allowance { get; set; }
    public decimal HolidayPay { get; set; }
    public decimal NightDiffPay { get; set; }
    public decimal LeavePay { get; set; }

    public decimal LateDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal Sss { get; set; }
    public decimal PhilHealth { get; set; }
    public decimal PagIbig { get; set; }
    public decimal Tax { get; set; }
    public decimal OtherDeductions { get; set; }

    public decimal Net { get; set; }

    public int LateMinutes { get; set; }
    public int AbsentDays { get; set; }
    public decimal OvertimeHours { get; set; }
    public int RegHolidayDays { get; set; }
    public int SpecialHolidayDays { get; set; }
    public decimal NightHours { get; set; }
    public int LeaveDays { get; set; }

    [JsonIgnore]
    public decimal Gross => Basic + Overtime + Allowance + HolidayPay + NightDiffPay + LeavePay - LateDeduction - AbsenceDeduction;

    [JsonIgnore]
    public string PeriodLabel => Kind == "13th Month" && Period.Length >= 4 ? Period.Substring(0, 4) + " - 13th month pay" : Period;
}

public class ServiceRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string RequestType { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = "Pending";
}

public class Db
{
    public List<User> Users { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<AttendanceRecord> Attendance { get; set; } = new();
    public List<SalaryRecord> Salaries { get; set; } = new();
    public List<ServiceRequest> Requests { get; set; } = new();
}
