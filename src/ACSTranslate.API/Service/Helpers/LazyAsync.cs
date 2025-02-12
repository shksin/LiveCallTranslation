namespace ACSTranslate;

public class LazyAsync<T>(Func<Task<T>> _factory)
{
    private Task<T>? _task;
    private readonly SemaphoreSlim _lock = new(1);
    public async Task<T> GetAsync()
    {
        if (_task != null) return await _task;
        await _lock.WaitAsync();
        try
        {
            if (_task != null) return await _task;
            _task = _factory();
            return await _task;
        }
        finally
        {
            _lock.Release();
        }
    }
}
