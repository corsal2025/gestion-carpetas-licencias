using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Statistics;

/// <summary>One day of the statistics screen — the "ESCANEADAS Y SUBIDAS" sheet, rebuilt.</summary>
public sealed record DailyStatisticsRow(
    DateOnly Date,
    int? Scanned,
    int? Uploaded,
    IReadOnlyDictionary<Office, (int Scheduled, int Attended)> ByOffice)
{
    public int ScheduledTotal => ByOffice.Values.Sum(entry => entry.Scheduled);
    public int AttendedTotal => ByOffice.Values.Sum(entry => entry.Attended);

    /// <summary>Attendance rate for one office, null when nobody was scheduled there that day.</summary>
    public double? AttendanceRate(Office office)
        => ByOffice.TryGetValue(office, out var entry) && entry.Scheduled > 0
            ? (double)entry.Attended / entry.Scheduled
            : null;
}

public sealed record MonthlyStatistics(
    int Year,
    int Month,
    IReadOnlyList<DailyStatisticsRow> Days,
    IReadOnlyList<(FolderState? State, int Count)> FolderStates,
    IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisions)
{
    public int ScannedTotal => Days.Sum(day => day.Scanned ?? 0);
    public int UploadedTotal => Days.Sum(day => day.Uploaded ?? 0);
    public int ScheduledTotal => Days.Sum(day => day.ScheduledTotal);
    public int AttendedTotal => Days.Sum(day => day.AttendedTotal);

    public double? AttendanceRate => ScheduledTotal > 0 ? (double)AttendedTotal / ScheduledTotal : null;

    public int ScheduledFor(Office office) => Days.Sum(day => day.ByOffice.TryGetValue(office, out var e) ? e.Scheduled : 0);
    public int AttendedFor(Office office) => Days.Sum(day => day.ByOffice.TryGetValue(office, out var e) ? e.Attended : 0);
}

/// <summary>
/// Rebuilds the workbook's statistics sheet from the cases themselves: only "escaneadas" and
/// "subidas" stay manual counters, everything else is counted, so the numbers can no longer drift
/// away from the agenda they summarise.
/// </summary>
public sealed class StatisticsService(IFolderCaseRepository cases, IDailyCounterRepository counters)
{
    public MonthlyStatistics ForMonth(int year, int month)
    {
        var attendance = cases.DailyAttendance(year, month);
        var monthCounters = counters.ForMonth(year, month).ToDictionary(counter => counter.Date);

        var dates = attendance
            .Select(entry => entry.Date)
            .Concat(monthCounters.Keys)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        var days = new List<DailyStatisticsRow>(dates.Count);
        foreach (var date in dates)
        {
            var byOffice = attendance
                .Where(entry => entry.Date == date)
                .ToDictionary(entry => entry.Office, entry => (entry.Scheduled, entry.Attended));

            monthCounters.TryGetValue(date, out var counter);
            days.Add(new DailyStatisticsRow(date, counter?.Scanned, counter?.Uploaded, byOffice));
        }

        return new MonthlyStatistics(
            year,
            month,
            days,
            cases.FolderStateBreakdown(year, month, office: null),
            cases.FinalDecisionBreakdown(year, month, office: null));
    }
}
