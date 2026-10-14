using System.Diagnostics;

public sealed class AdaptiveWorkerLimiter : IDisposable
{
    private readonly SemaphoreSlim _change = new(0);
    private readonly object _sync = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly int _maximum;
    private readonly int _minimum;
    private int _limit;
    private int _active;
    private long _lastSample;
    private TimeSpan _lastCpu;
    private bool _disposed;

    public AdaptiveWorkerLimiter(int maximum, int minimum = 1)
    {
        if (maximum < 1 || minimum < 1 || minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum;
        _minimum = minimum;
        _limit = Math.Min(maximum, Math.Max(minimum, Environment.ProcessorCount / 2));
        _lastSample = Stopwatch.GetTimestamp();
        _lastCpu = _process.TotalProcessorTime;
    }

    public int CurrentLimit { get { lock (_sync) return _limit; } }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                SampleUnderLock();
                if (_active < _limit)
                {
                    _active++;
                    return new Lease(this);
                }
            }
            // A short bounded wait also allows recovery if all busy workers have exited.
            await _change.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private void SampleUnderLock()
    {
        var now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastSample) / (double)Stopwatch.Frequency;
        if (elapsed < 2) return;
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        double utilization = (cpu - _lastCpu).TotalSeconds / (elapsed * Environment.ProcessorCount);
        _lastCpu = cpu;
        _lastSample = now;

        var gcInfo = GC.GetGCMemoryInfo();
        bool memoryPressure = gcInfo.HighMemoryLoadThresholdBytes > 0 &&
            gcInfo.MemoryLoadBytes > gcInfo.HighMemoryLoadThresholdBytes * 0.85;
        bool heavyCpu = utilization > 0.88;
        bool lowCpu = utilization < 0.65;
        int previous = _limit;
        if (memoryPressure || heavyCpu)
            _limit = Math.Max(_minimum, _limit - 1);
        else if (lowCpu && _limit < _maximum)
            _limit++;
        if (_limit != previous)
            Console.WriteLine($"Adaptive workers: {_limit}/{_maximum}; process CPU {utilization:P0}; high memory pressure: {memoryPressure}");
    }

    private void Release()
    {
        lock (_sync) _active--;
        if (!_disposed) _change.Release();
    }

    public void Dispose()
    {
        _disposed = true;
        // Keep the semaphore alive until all in-flight workers have released.
    }

    private sealed class Lease : IDisposable
    {
        private AdaptiveWorkerLimiter? _owner;
        public Lease(AdaptiveWorkerLimiter owner) => _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
