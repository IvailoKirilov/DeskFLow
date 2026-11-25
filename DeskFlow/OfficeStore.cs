using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DeskFlow;

public sealed record Employee(string Id, string Name);
public sealed record Desk(string Id, string Number, string Zone)
{
    public string DisplayName => Number.StartsWith("DSK-", StringComparison.Ordinal)
        && Number.Length > 4 && Number.AsSpan(4).IndexOfAnyExceptInRange('0', '9') < 0
        ? $"Бюро {Number[4..]}" : Number;
}
public sealed record Booking(long Id, Desk Desk, DateOnly Date, string State);
public sealed record Outcome(bool Success, string Message);

public sealed class OfficeStore(IWebHostEnvironment environment, IConfiguration configuration, OfficeClock clock)
{
    private readonly string databasePath = Path.GetFullPath(configuration["Office:DatabasePath"] ?? "Data/office.db",
        environment.ContentRootPath);
    private const string Active = "State IN ('Confirmed', 'Checked-in')";
    private const string DeskColumns = "d.DeskID, d.DeskNumber, d.Zone";
    private static readonly string[] Zones = ["Тиха зона", "Екипна зона", "Зона до прозореца"];

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var db = Open();
        using (var journal = Command(db, null, "PRAGMA journal_mode=WAL;")) journal.ExecuteScalar();
        using var transaction = db.BeginTransaction(deferred: false);
        using var schema = Command(db, transaction, """
            CREATE TABLE IF NOT EXISTS Employees(
                EmployeeID TEXT PRIMARY KEY, Name TEXT NOT NULL, Active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS Desks(
                DeskID TEXT PRIMARY KEY, DeskNumber TEXT NOT NULL UNIQUE, Zone TEXT NOT NULL,
                Active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS Bookings(
                BookingID INTEGER PRIMARY KEY, EmployeeID TEXT NOT NULL REFERENCES Employees(EmployeeID),
                DeskID TEXT NOT NULL REFERENCES Desks(DeskID), Date TEXT NOT NULL,
                State TEXT NOT NULL CHECK(State IN ('Confirmed', 'Checked-in', 'Auto-Canceled', 'Replaced')),
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ActiveDesk ON Bookings(DeskID, Date)
                WHERE State IN ('Confirmed', 'Checked-in');
            CREATE UNIQUE INDEX IF NOT EXISTS ActiveEmployee ON Bookings(EmployeeID, Date)
                WHERE State IN ('Confirmed', 'Checked-in');
            CREATE INDEX IF NOT EXISTS DailyBookings ON Bookings(Date, State);
            CREATE TABLE IF NOT EXISTS System_Logs(
                ReleaseDate TEXT PRIMARY KEY, Timestamp TEXT NOT NULL,
                RowsAffected INTEGER NOT NULL CHECK(RowsAffected >= 0), Message TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SeedMetadata(Id INTEGER PRIMARY KEY CHECK(Id = 1), StartDate TEXT NOT NULL);
            """);
        schema.ExecuteNonQuery();
        using var seed = Command(db, transaction, "INSERT OR IGNORE INTO SeedMetadata VALUES (1, $date)",
            ("$date", Iso(DateOnly.FromDateTime(clock.Now.DateTime))));
        if (seed.ExecuteNonQuery() == 1)
        {
            for (var n = 1; n <= 150; n++)
            {
                var name = n switch { 1 => "Иван Петров", 2 => "Мария", 3 => "Петър", _ => $"Служител {n:000}" };
                using var insert = Command(db, transaction, "INSERT INTO Employees VALUES ($id, $name, 1)",
                    ("$id", $"EMP-{n:00}"), ("$name", name));
                insert.ExecuteNonQuery();
            }

            for (var n = 1; n <= 60; n++)
            {
                var id = $"DSK-{n:00}";
                using var insert = Command(db, transaction, "INSERT INTO Desks VALUES ($id, $id, $zone, 1)",
                    ("$id", id), ("$zone", Zones[(n - 1) / 20]));
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public Employee? FindEmployee(string code)
    {
        using var db = Open();
        using var query = Command(db, null, "SELECT Name FROM Employees WHERE EmployeeID = $id AND Active = 1",
            ("$id", code));
        return query.ExecuteScalar() is string name ? new Employee(code, name) : null;
    }

    public IReadOnlyList<Desk> Available(DateOnly date, string employeeId, out Booking? current)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: true);
        current = ReadBooking(db, transaction, employeeId, date, true);
        using var query = Command(db, transaction, $"""
            SELECT {DeskColumns} FROM Desks d WHERE d.Active = 1 AND NOT EXISTS (
                SELECT 1 FROM Bookings b WHERE b.DeskID = d.DeskID AND b.Date = $date AND {Active}
            ) ORDER BY d.Zone COLLATE BINARY, d.DeskNumber
            """, ("$date", Iso(date)));
        using var reader = query.ExecuteReader();
        var desks = new List<Desk>();
        while (reader.Read()) desks.Add(ReadDesk(reader));
        return desks;
    }

    public Booking? ForToday(string employeeId, DateOnly today)
    {
        using var db = Open();
        return ReadBooking(db, null, employeeId, today, false);
    }

    public Outcome Reserve(string employeeId, string deskId, DateOnly date)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: false);
        var now = clock.Now;
        if (!clock.BookingDates(DateOnly.FromDateTime(now.DateTime)).Contains(date))
            return new(false, "Изберете дата от следващите 5 работни дни.");

        using var desk = Command(db, transaction, "SELECT 1 FROM Desks WHERE DeskID = $desk AND Active = 1",
            ("$desk", deskId));
        if (desk.ExecuteScalar() is null) return new(false, "Това бюро не е налично.");

        var previous = ReadBooking(db, transaction, employeeId, date, true);
        if (previous?.Desk.Id == deskId)
            return new(false, "Вече имате активна резервация за този ден!");

        const string occupiedMessage = "Това бюро току-що беше резервирано от друг колега.";
        using var occupied = Command(db, transaction, $"""
            SELECT 1 FROM Bookings WHERE DeskID = $desk AND Date = $date AND {Active}
            """, ("$desk", deskId), ("$date", Iso(date)));
        if (occupied.ExecuteScalar() is not null) return new(false, occupiedMessage);

        var timestamp = Timestamp(now);
        if (previous is not null)
        {
            using var release = Command(db, transaction, """
                UPDATE Bookings SET State = 'Replaced', UpdatedAt = $now WHERE BookingID = $id
                """, ("$now", timestamp), ("$id", previous.Id));
            release.ExecuteNonQuery();
        }

        using var insert = Command(db, transaction, """
            INSERT INTO Bookings(EmployeeID, DeskID, Date, State, CreatedAt, UpdatedAt)
            VALUES($employee, $desk, $date, 'Confirmed', $now, $now)
            """, ("$employee", employeeId), ("$desk", deskId), ("$date", Iso(date)), ("$now", timestamp));
        try
        {
            insert.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            return new(false, occupiedMessage);
        }

        return previous is null
            ? new(true, "Бюрото беше резервирано успешно!")
            : new(true, $"Бюрото беше резервирано успешно! Предишната резервация за {previous.Desk.Number} беше отменена.");
    }

    public Outcome CheckIn(string employeeId, long bookingId)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: false);
        var now = clock.Now;
        var time = TimeOnly.FromDateTime(now.DateTime);
        if (time >= OfficeClock.ReleaseAt)
            return new(false, "Чекирането е невъзможно. Времето за чекиране изтече.");
        if (time < OfficeClock.CheckInOpens)
            return new(false, "Чекирането започва в 07:00 ч.");

        using var update = Command(db, transaction, """
            UPDATE Bookings SET State = 'Checked-in', UpdatedAt = $now
            WHERE BookingID = $id AND EmployeeID = $employee AND Date = $date AND State = 'Confirmed'
            """, ("$now", Timestamp(now)), ("$id", bookingId), ("$employee", employeeId),
            ("$date", Iso(DateOnly.FromDateTime(now.DateTime))));
        if (update.ExecuteNonQuery() != 1)
            return new(false, "Няма резервация, която можете да чекирате.");
        transaction.Commit();
        return new(true, "Успешно чекиране. Приятен работен ден!");
    }

    public void ReleaseDue()
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: false);
        var now = clock.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var through = TimeOnly.FromDateTime(now.DateTime) >= OfficeClock.ReleaseAt ? today : today.AddDays(-1);
        using var marker = Command(db, transaction, """
            SELECT COALESCE((SELECT MAX(ReleaseDate) FROM System_Logs), (SELECT StartDate FROM SeedMetadata))
            """);
        var start = DateOnly.ParseExact((string)marker.ExecuteScalar()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var exists = Command(db, transaction, "SELECT 1 FROM System_Logs WHERE ReleaseDate = $date",
            ("$date", Iso(start)));
        if (exists.ExecuteScalar() is not null) start = start.AddDays(1);

        for (var date = start; date <= through; date = date.AddDays(1))
        {
            using var update = Command(db, transaction, """
                UPDATE Bookings SET State = 'Auto-Canceled', UpdatedAt = $now
                WHERE Date = $date AND State = 'Confirmed'
                """, ("$now", Timestamp(now)), ("$date", Iso(date)));
            var count = update.ExecuteNonQuery();
            using var log = Command(db, transaction, """
                INSERT INTO System_Logs VALUES($date, $now, $count, $message)
                """, ("$date", Iso(date)), ("$now", Timestamp(now)), ("$count", count),
                ("$message", $"Executed NoShowRelease. Rows affected: {count}"));
            log.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static Booking? ReadBooking(SqliteConnection db, SqliteTransaction? transaction,
        string employeeId, DateOnly date, bool activeOnly)
    {
        using var query = Command(db, transaction, $"""
            SELECT {DeskColumns}, b.BookingID, b.State FROM Bookings b
            JOIN Desks d ON d.DeskID = b.DeskID
            WHERE b.EmployeeID = $employee AND b.Date = $date
            AND {(activeOnly ? Active : "State <> 'Replaced'")}
            ORDER BY b.BookingID DESC LIMIT 1
            """, ("$employee", employeeId), ("$date", Iso(date)));
        using var reader = query.ExecuteReader();
        return reader.Read() ? new Booking(reader.GetInt64(3), ReadDesk(reader), date, reader.GetString(4)) : null;
    }

    private static Desk ReadDesk(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Timestamp(DateTimeOffset date) => date.ToString("O", CultureInfo.InvariantCulture);

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, DefaultTimeout = 10
        }.ToString());
        db.Open();
        return db;
    }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
