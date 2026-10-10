namespace Ludaryx.Infrastructure.Igdb;

public sealed class IgdbRequestLimiter(TimeProvider time) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private readonly SemaphoreSlim concurrency = new(8, 8);
    private readonly SemaphoreSlim spacing = new(1, 1);
    private long? lastStart;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            await spacing.WaitAsync(cancellationToken);
            try
            {
                if (lastStart is not null)
                {
                    var remaining = Interval - time.GetElapsedTime(lastStart.Value);
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, time, cancellationToken);
                    }
                }
                lastStart = time.GetTimestamp();
            }
            finally
            {
                spacing.Release();
            }
            return new Lease(concurrency);
        }
        catch
        {
            concurrency.Release();
            throw;
        }
    }

    public void Dispose()
    {
        concurrency.Dispose();
        spacing.Dispose();
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? held = semaphore;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
