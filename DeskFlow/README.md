# DeskFlow

A Bulgarian-language desk booking application built with ASP.NET Core 10 Razor Pages and SQLite. The application is self-contained and uses local assets.

## Run locally

Install the .NET 10 SDK or use the SDK already on this computer. If using the ZIP, extract it to a location outside any existing project folder. Open the folder containing `DeskFlow.csproj` and run:

```powershell
dotnet run --urls http://localhost:5182
```

Open http://localhost:5182 and sign in with `EMP-01` (Иван Петров), `EMP-02` (Мария), or `EMP-03` (Петър). The first run creates `Data/office.db` with 150 sample employee IDs and 60 desks in three zones. The three PDFs included examples but did not contain a complete roster or desk inventory. Fresh installations start with no reservations. Keep the database file when restarting or upgrading.

## Booking rules

- The five upcoming weekdays are selectable. To exclude office holidays and other closures, add their ISO dates to `Office:ClosedDates` in `appsettings.json`.
- The listing shows active desks that are free for the selected date, sorted by zone and desk number. Concurrent requests are serialized by SQLite and the database also enforces uniqueness for both desk/date and employee/date.
- For the conflicting paths in FS-001, the explicit replacement flow applies to a different free desk. A replacement is atomic and the earlier reservation is retained as `Replaced`. Repeating a reservation for the same desk shows the duplicate-booking message.
- The BR-03 same-day ban takes precedence over the same-day rebooking sentence in FS-003.
- Check-in is available from 07:00 inclusive to 10:00 exclusive, Sofia time, for the employee's own confirmed booking that day.
- At 10:00 Sofia time the background service changes remaining confirmed reservations to `Auto-Canceled`. The update and daily `System_Logs` entry commit together. Zero releases are logged. Missed runs are caught up on restart. Keep the app running for on-time processing.

An employee code grants access as described in FS-000. Keep the demo on localhost or a trusted internal network. For an internal deployment, configure the actual host name in `AllowedHosts`, supply the office's closure dates, and load the actual employee and desk records.
