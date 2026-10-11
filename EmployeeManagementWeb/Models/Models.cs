namespace EmployeeManagementWeb.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = "Employee";   // Owner | Admin | Employee
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
    public string DateHired { get; set; } = "";      // yyyy-MM-dd
    public string Status { get; set; } = "Active";   // Active | Inactive

    public Employee Clone() => (Employee)MemberwiseClone();
}

public class AttendanceRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Date { get; set; } = "";           // yyyy-MM-dd
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public string Photo { get; set; } = "";          // small JPEG data URL
}

public class SalaryRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Period { get; set; } = "";         // yyyy-MM
    public decimal Basic { get; set; }
    public decimal Overtime { get; set; }
    public decimal Allowance { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal Net { get; set; }
    public int LateMinutes { get; set; }
    public int AbsentDays { get; set; }
    public decimal OvertimeHours { get; set; }
}

public class ServiceRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string RequestType { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = "Pending";  // Pending | Approved | Rejected
}

/// <summary>Everything the app stores. Serialized to the browser's localStorage as JSON.</summary>
public class Db
{
    public List<User> Users { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<AttendanceRecord> Attendance { get; set; } = new();
    public List<SalaryRecord> Salaries { get; set; } = new();
    public List<ServiceRequest> Requests { get; set; } = new();
}
