using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIThemaView2.Services
{
    public class RateLimitService
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _delayMilliseconds;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly object _lock = new object();

        public RateLimitService(int maxConcurrentRequests = 3, int delayMilliseconds = 1000)
        {
            _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
            _delayMilliseconds = delayMilliseconds;
        }

        public async Task WaitAsync()
        {
            await _semaphore.WaitAsync();

            try
            {
                lock (_lock)
                {
                    var timeSinceLastRequest = DateTime.Now - _lastRequest;
                    if (timeSinceLastRequest.TotalMilliseconds < _delayMilliseconds)
                    {
                        var delay = _delayMilliseconds - (int)timeSinceLastRequest.TotalMilliseconds;
                        Thread.Sleep(delay);
                    }
                    _lastRequest = DateTime.Now;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
