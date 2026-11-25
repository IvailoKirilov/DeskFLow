using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DeskFlow.Pages;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class IndexModel(OfficeStore store, OfficeClock clock) : PageModel
{
    [TempData] public string? Message { get; set; }
    [TempData] public bool IsError { get; set; }
    public Employee Employee { get; private set; } = null!;
    public DateOnly Today { get; private set; }
    public DateOnly SelectedDate { get; private set; }
    public IReadOnlyList<DateOnly> Dates { get; private set; } = [];
    public IReadOnlyList<Desk> Desks { get; private set; } = [];
    public Booking? TodayBooking { get; private set; }
    public Booking? SelectedBooking { get; private set; }
    public bool CanCheckIn { get; private set; }
    public bool CheckInStartsLater { get; private set; }
    public int? RefreshAfterMs { get; private set; }
    public static CultureInfo Bulgarian { get; } = CultureInfo.GetCultureInfo("bg-BG");

    public IActionResult OnGet(string? date)
    {
        if (CurrentEmployee() is not { } employee) return RedirectToPage("/Login");
        Employee = employee;
        var now = clock.Now;
        Today = DateOnly.FromDateTime(now.DateTime);
        Dates = clock.BookingDates(Today);
        SelectedDate = TryDate(date, out var selected) && Dates.Contains(selected) ? selected : Dates[0];
        Desks = store.Available(SelectedDate, employee.Id, out var existing);
        SelectedBooking = existing;
        var time = TimeOnly.FromDateTime(now.DateTime);
        var todayBooking = store.ForToday(employee.Id, Today);
        if (todayBooking?.State == "Confirmed" && time >= OfficeClock.ReleaseAt)
        {
            store.ReleaseDue();
            todayBooking = store.ForToday(employee.Id, Today);
        }
        TodayBooking = todayBooking;
        if (TodayBooking?.State == "Confirmed")
        {
            CanCheckIn = time >= OfficeClock.CheckInOpens && time < OfficeClock.ReleaseAt;
            CheckInStartsLater = time < OfficeClock.CheckInOpens;
            var next = CheckInStartsLater ? OfficeClock.CheckInOpens : OfficeClock.ReleaseAt;
            RefreshAfterMs = (int)Math.Max(1000, (next - time).TotalMilliseconds + 250);
        }
        return Page();
    }

    public IActionResult OnPostReserve(string? deskId, string? date)
    {
        if (CurrentEmployee() is not { } employee) return RedirectToPage("/Login");
        if (!TryDate(date, out var selected))
            return SetOutcome(new(false, "Изберете дата от следващите 5 работни дни."), null);
        return SetOutcome(store.Reserve(employee.Id, deskId ?? "", selected), date);
    }

    public IActionResult OnPostCheckIn(long bookingId, string? date)
    {
        if (CurrentEmployee() is not { } employee) return RedirectToPage("/Login");
        return SetOutcome(store.CheckIn(employee.Id, bookingId), date);
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Session.Clear();
        return RedirectToPage("/Login");
    }

    private Employee? CurrentEmployee() => HttpContext.Session.GetString(LoginModel.SessionKey) is { } id
        ? store.FindEmployee(id) : null;

    private IActionResult SetOutcome(Outcome outcome, string? date)
    {
        Message = outcome.Message;
        IsError = !outcome.Success;
        return RedirectToPage("/Index", new { date });
    }

    private static bool TryDate(string? date, out DateOnly value) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
