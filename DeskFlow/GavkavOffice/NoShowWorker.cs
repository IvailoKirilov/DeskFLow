namespace GavkavOffice;

public sealed class NoShowWorker(OfficeStore store, OfficeClock clock, TimeProvider timer,
    ILogger<NoShowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var observed = clock.Now;
            TimeSpan delay;
            try
            {
                store.ReleaseDue();
                delay = clock.UntilNextRelease(observed) - (clock.Now - observed);
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Could not release no-show bookings. Retrying.");
                delay = TimeSpan.FromSeconds(5);
            }
            await Task.Delay(delay, timer, stoppingToken);
        }
    }
}
