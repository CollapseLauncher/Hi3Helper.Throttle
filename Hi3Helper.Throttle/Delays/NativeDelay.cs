using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
// ReSharper disable InconsistentNaming
// ReSharper disable ConvertConstructorToMemberInitializers

namespace Hi3Helper.Throttle.Delays;

#if WIN32
internal abstract class NativeDelay :
#else
internal class NativeDelay :
#endif
    IValueTaskSource,
    IDisposable
{
    protected ManualResetValueTaskSourceCore<object?> _core;

    protected NativeDelay()
    {
        _core = new ManualResetValueTaskSourceCore<object?>
        {
            RunContinuationsAsynchronously = true
        };
    }

    public static NativeDelay Create()
    {
#if WIN32
        return new Win32NativeDelay();
#else
        return new NativeDelay();
#endif
    }

#if WIN32
    public abstract ValueTask WaitAsync(int delayMs, CancellationToken token);
#else
    public virtual ValueTask WaitAsync(int delayMs, CancellationToken token)
    {
        _core.Reset();
        try
        {
            Task task = Task.Delay(delayMs, token);
            task.GetAwaiter().OnCompleted(() =>
            {
                if (task.IsCanceled)
                {
                    Cancel();
                    return;
                }

                Complete();
            });
        }
        catch (Exception e)
        {
            _core.SetException(e);
        }

        return new ValueTask(this, _core.Version);
    }
#endif

    public virtual void GetResult(short token)
        => _core.GetResult(token);

    public virtual ValueTaskSourceStatus GetStatus(short token)
        => _core.GetStatus(token);

    public virtual void OnCompleted(
        Action<object?>                 continuation,
        object?                         state,
        short                           token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);

    public void Dispose()
        => Cleanup();

    protected virtual void Complete() => _core.SetResult(null);
    protected virtual void Cancel() => _core.SetException(new OperationCanceledException());
    protected virtual void Cleanup() { }
}