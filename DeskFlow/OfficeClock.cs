using System.Globalization;

namespace DeskFlow;

public sealed class OfficeClock(TimeProvider clock, IConfiguration configuration)
{
    public static readonly TimeOnly CheckInOpens = new(7, 0);
    public static readonly TimeOnly ReleaseAt = new(10, 0);

    private readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");
    private readonly HashSet<DateOnly> closedDates = configuration.GetSection("Office:ClosedDates")
        .Get<string[]>()?.Select(value => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture))
        .ToHashSet() ?? [];

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);

    public IReadOnlyList<DateOnly> BookingDates(DateOnly today)
    {
        var dates = new List<DateOnly>(5);
        for (var date = today.AddDays(1); dates.Count < 5; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !closedDates.Contains(date))
                dates.Add(date);
        }
        return dates;
    }

    public TimeSpan UntilNextRelease(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.DateTime);
        if (TimeOnly.FromDateTime(now.DateTime) >= ReleaseAt) date = date.AddDays(1);
        var release = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(ReleaseAt), zone);
        return release - now.UtcDateTime;
    }
}
