using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Throttle.Delays;

#if NET5_0_OR_GREATER
internal unsafe class Win32NativeDelay :
#else
internal class Win32NativeDelay :
#endif
    NativeDelay
{
    private nint _timer;
    private nint _tpWait;
    private GCHandle _selfHandle;

    private CancellationTokenRegistration _ctr;

#if NET5_0_OR_GREATER
    private static readonly delegate* unmanaged[Stdcall]<nint, nint, nint, uint, void> WaitCallback = &Callback;
#else
    private static readonly PInvoke.PTP_WAIT_CALLBACK WaitCallback = Callback;
#endif

    public override ValueTask WaitAsync(int delayMs, CancellationToken token)
    {
        _core.Reset();

        _selfHandle = GCHandle.Alloc(this);
        _timer = PInvoke.CreateWaitableTimerEx(
            0,
            0,
            PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            PInvoke.TIMER_ALL_ACCESS);

        if (_timer == 0)
            throw new Win32Exception();

        long dueTime = -(delayMs * 10000);

        if (PInvoke.SetWaitableTimer(
            _timer,
            ref dueTime,
            0,
            0,
            0,
            false) == 0)
            throw new Win32Exception();

        _tpWait = PInvoke.CreateThreadpoolWait(
            WaitCallback,
            GCHandle.ToIntPtr(_selfHandle),
            0);

        if (_tpWait == 0)
            throw new Win32Exception();

        PInvoke.SetThreadpoolWait(_tpWait, _timer, 0);

        if (token.CanBeCanceled)
        {
            _ctr = token.Register(static s =>
            {
                ((Win32NativeDelay)s!).Cancel();
            }, this);
        }

        return new ValueTask(this, _core.Version);
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
#endif
    private static void Callback(
        nint instance,
        nint context,
        nint wait,
        uint waitResult)
    {
        GCHandle handle = GCHandle.FromIntPtr(context);
        Win32NativeDelay self = (Win32NativeDelay)handle.Target!;
        self.Complete();
    }

    protected override void Complete()
    {
        Cleanup();
        base.Complete();
    }

    protected override void Cancel()
    {
        _ = PInvoke.CancelWaitableTimer(_timer);
        Cleanup();
        base.Cancel();
    }

    protected override void Cleanup()
    {
        _ctr.Dispose();

        if (_tpWait != 0)
        {
            PInvoke.CloseThreadpoolWait(_tpWait);
            _tpWait = 0;
        }

        if (_timer != 0)
        {
            _ = PInvoke.CloseHandle(_timer);
            _timer = 0;
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }
}
