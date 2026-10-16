using System.Globalization;
using EmployeeManagementWeb.Models;

namespace EmployeeManagementWeb.Services;

/// <summary>Everything an admin can type in for one month of payroll.</summary>
public record PayrollInput(
    int LateMinutes, int AbsentDays, decimal OvertimeHours, decimal Allowance,
    int RegHolidayDays, int SpecialHolidayDays, decimal NightHours, int LeaveDays, decimal OtherDeductions);

public record PayrollResult(
    decimal DailyRate, decimal Overtime, decimal Allowance, decimal HolidayPay, decimal NightDiff, decimal LeavePay,
    decimal Late, decimal Absence, decimal Gross,
    decimal Sss, decimal PhilHealth, decimal PagIbig, decimal TaxableIncome, decimal Tax, decimal Other,
    decimal TotalDeductions, decimal Net);

/// <summary>
/// Philippine payroll rules (2026 rates). Rates are constants at the top so they are easy to update
/// when SSS / PhilHealth / Pag-IBIG / BIR publish new circulars.
/// </summary>
public static class PayrollCalculator
{
    // SSS: 15% of Monthly Salary Credit (5% employee, 10% employer). MSC runs 4,000 - 35,000 in steps of 500.
    public const decimal SssEmployeeRate = 0.05m;
    public const decimal SssMinMsc = 4000m;
    public const decimal SssMaxMsc = 35000m;
    // PhilHealth: 5% of basic (2.5% employee), income floor 10,000 and ceiling 100,000.
    public const decimal PhilHealthEmployeeRate = 0.025m;
    public const decimal PhilHealthFloor = 10000m;
    public const decimal PhilHealthCeiling = 100000m;
    // Pag-IBIG: 2% of monthly compensation, maximum fund salary 10,000 (so max 200).
    public const decimal PagIbigRate = 0.02m;
    public const decimal PagIbigMaxSalary = 10000m;
    // Premium pay rules
    public const decimal NightDiffRate = 0.10m;          // +10% of hourly rate
    public const decimal RegularHolidayExtra = 1.00m;    // worked regular holiday: +100% of daily rate on top of monthly pay
    public const decimal SpecialHolidayExtra = 0.30m;    // worked special non-working day: +30% of daily rate
    public const decimal ThirteenthMonthTaxFree = 90000m;

    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static decimal Sss(decimal basic)
    {
        var msc = Math.Floor((basic + 250m) / 500m) * 500m;
        msc = Math.Clamp(msc, SssMinMsc, SssMaxMsc);
        return Round(msc * SssEmployeeRate);
    }

    public static decimal PhilHealth(decimal basic)
        => Round(Math.Clamp(basic, PhilHealthFloor, PhilHealthCeiling) * PhilHealthEmployeeRate);

    public static decimal PagIbig(decimal basic)
        => basic <= 1500m ? Round(basic * 0.01m) : Round(Math.Min(basic, PagIbigMaxSalary) * PagIbigRate);

    /// <summary>TRAIN-law annual income tax on taxable compensation.</summary>
    public static decimal AnnualTax(decimal t)
    {
        if (t <= 250000m) return 0m;
        if (t <= 400000m) return (t - 250000m) * 0.15m;
        if (t <= 800000m) return 22500m + (t - 400000m) * 0.20m;
        if (t <= 2000000m) return 102500m + (t - 800000m) * 0.25m;
        if (t <= 8000000m) return 402500m + (t - 2000000m) * 0.30m;
        return 2202500m + (t - 8000000m) * 0.35m;
    }

    /// <summary>Monthly withholding tax: annualise the month's taxable pay, tax it, divide by 12.</summary>
    public static decimal MonthlyTax(decimal taxableMonthly)
        => Round(AnnualTax(Math.Max(0m, taxableMonthly) * 12m) / 12m);

    public static PayrollResult Compute(decimal basic, PayrollInput i)
    {
        var daily = Round(basic / EmsService.WorkDaysPerMonth);
        var hourly = daily / EmsService.HoursPerDay;

        var overtime = Round(i.OvertimeHours * hourly * EmsService.OvertimeMultiplier);
        var late = Round(i.LateMinutes * hourly / 60m);
        var absence = Round(i.AbsentDays * daily);
        var holiday = Round(i.RegHolidayDays * daily * RegularHolidayExtra + i.SpecialHolidayDays * daily * SpecialHolidayExtra);
        var night = Round(i.NightHours * hourly * NightDiffRate);
        var leave = Round(i.LeaveDays * daily);
        var allowance = Round(i.Allowance);

        var gross = basic + overtime + allowance + holiday + night + leave - late - absence;

        var sss = Sss(basic);
        var phil = PhilHealth(basic);
        var pag = PagIbig(basic);
        var taxable = gross - sss - phil - pag;
        var tax = MonthlyTax(taxable);
        var other = Round(i.OtherDeductions);

        var totalDeductions = sss + phil + pag + tax + other;
        return new PayrollResult(daily, overtime, allowance, holiday, night, leave, late, absence, gross,
            sss, phil, pag, taxable, tax, other, totalDeductions, gross - totalDeductions);
    }

    public static SalaryRecord BuildRecord(int id, Employee emp, string period, PayrollInput i)
    {
        var r = Compute(emp.BasicSalary, i);
        return new SalaryRecord
        {
            Id = id, EmployeeId = emp.Id, Period = period, Kind = "Monthly",
            Basic = emp.BasicSalary, Overtime = r.Overtime, Allowance = r.Allowance,
            HolidayPay = r.HolidayPay, NightDiffPay = r.NightDiff, LeavePay = r.LeavePay,
            LateDeduction = r.Late, AbsenceDeduction = r.Absence,
            Sss = r.Sss, PhilHealth = r.PhilHealth, PagIbig = r.PagIbig, Tax = r.Tax, OtherDeductions = r.Other,
            Net = r.Net,
            LateMinutes = i.LateMinutes, AbsentDays = i.AbsentDays, OvertimeHours = i.OvertimeHours,
            RegHolidayDays = i.RegHolidayDays, SpecialHolidayDays = i.SpecialHolidayDays,
            NightHours = i.NightHours, LeaveDays = i.LeaveDays
        };
    }

    /// <summary>Tax on a 13th month pay: only the part above 90,000 is taxable.</summary>
    public static decimal ThirteenthMonthTax(decimal amount, decimal monthlyBasic)
    {
        var excess = amount - ThirteenthMonthTaxFree;
        if (excess <= 0m) return 0m;
        var baseTaxable = (monthlyBasic - Sss(monthlyBasic) - PhilHealth(monthlyBasic) - PagIbig(monthlyBasic)) * 12m;
        return Round(AnnualTax(baseTaxable + excess) - AnnualTax(baseTaxable));
    }

    /// <summary>Hours between timeIn and timeOut that fall inside 10 PM - 6 AM (works across midnight and over several nights).</summary>
    public static decimal NightHoursOf(DateTime timeIn, DateTime timeOut)
    {
        if (timeOut <= timeIn) return 0m;
        double total = 0;
        for (var day = timeIn.Date.AddDays(-1); day <= timeOut.Date; day = day.AddDays(1))
        {
            var windowStart = day + EmsService.NightStart;                 // 10 PM of this day
            var windowEnd = day.AddDays(1) + EmsService.NightEnd;          // 6 AM of the next day
            var from = timeIn > windowStart ? timeIn : windowStart;
            var to = timeOut < windowEnd ? timeOut : windowEnd;
            if (to > from) total += (to - from).TotalHours;
        }
        return Math.Round((decimal)total, 2);
    }

    /// <summary>Late minutes, absent days, overtime hours and night-differential hours for one employee in one month, from the attendance records.</summary>
    public static AttendanceSummary Summarize(Db data, Employee emp, string period, DateTime today)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!DateTime.TryParseExact(period + "-01", "yyyy-MM-dd", inv, DateTimeStyles.None, out var first))
            return new AttendanceSummary(0, 0, 0, 0);

        var last = first.AddMonths(1).AddDays(-1);
        var recs = data.Attendance.Where(a => a.EmployeeId == emp.Id && a.Date.StartsWith(period)).ToList();

        int lateMinutes = 0;
        double otHours = 0;
        decimal nightHours = 0m;
        foreach (var a in recs)
        {
            if (a.TimeIn is not DateTime tin) continue;

            // Punching in at/after 6 PM = night shift: it is never "late" and has no day-shift overtime.
            bool dayShift = tin.TimeOfDay < EmsService.NightShiftFrom;

            if (dayShift && tin.TimeOfDay > EmsService.ShiftStart)
                lateMinutes += (int)(tin.TimeOfDay - EmsService.ShiftStart).TotalMinutes;

            if (a.TimeOut is DateTime tout)
            {
                if (dayShift && tout > tin.Date + EmsService.ShiftEnd)
                    otHours += (tout - (tin.Date + EmsService.ShiftEnd)).TotalHours;   // also counts work past midnight

                nightHours += NightHoursOf(tin, tout);
            }
        }

        // Absent = a Mon-Sat day (after the hire date, before today) with no attendance record.
        var startDay = first;
        if (DateTime.TryParseExact(emp.DateHired, "yyyy-MM-dd", inv, DateTimeStyles.None, out var hired) && hired > first)
            startDay = hired;
        var endDay = last < today.Date ? last : today.Date.AddDays(-1);

        var present = recs.Select(a => a.Date).ToHashSet();
        int absent = 0;
        for (var d = startDay; d <= endDay; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!present.Contains(EmsService.DateKey(d))) absent++;
        }

        return new AttendanceSummary(lateMinutes, absent, Math.Round((decimal)otHours, 2), Math.Round(nightHours, 2));
    }
}
