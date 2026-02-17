using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReadStorm.Desktop.ViewModels;

public abstract class LazyFeatureViewModelBase : ViewModelBase
{
    private readonly Func<Task> _initialize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;

    protected LazyFeatureViewModelBase(Func<Task> initialize)
    {
        _initialize = initialize;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;
            await _initialize();
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
