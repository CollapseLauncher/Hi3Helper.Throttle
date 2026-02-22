#if CompileNative

using System;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable CommentTypo
#pragma warning disable CA1401
#pragma warning disable SYSLIB1054
#pragma warning disable CA2012

namespace Hi3Helper.Throttle;

public static class Exports
{
    /// <summary>
    /// Set how many bytes to be throttled per second. Set to 0 to disable throttling.
    /// </summary>
    /// <param name="bytesPerSecond">Amount of bytes per second target to be throttled.</param>
    /// <param name="burstBytes">[Optional] Amount of bytes burst allowed once throttle is over.</param>
    [UnmanagedCallersOnly(EntryPoint = "ThrottleServiceSetSharedThrottleBytes", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int ThrottleServiceSetSharedThrottleBytes(long bytesPerSecond, long* burstBytes) =>
        ThrottleServiceContext.SetSharedThrottleBytes(bytesPerSecond, burstBytes == null ? null : *burstBytes);

    /// <summary>
    /// Gets current shared throttle bytes per second and burst bytes.
    /// </summary>
    /// <param name="bytesPerSecond">Amount of bytes per second target being throttled.</param>
    /// <param name="burstBytes">Amount of bytes burst allowed once throttle is over.</param>
    [UnmanagedCallersOnly(EntryPoint = "ThrottleServiceGetSharedThrottleBytes", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe void ThrottleServiceGetSharedThrottleBytes(long* bytesPerSecond, long* burstBytes)
    {
        if (bytesPerSecond != null)
        {
            *bytesPerSecond = ThrottleServiceContext.SharedBytesPerSecond;
        }

        if (burstBytes != null)
        {
            *burstBytes = ThrottleServiceContext.Capacity;
        }
    }

    /// <summary>
    /// Adds the current read bytes and wait if the total exceeds the throttle limit from a given <see cref="ThrottleServiceContext"/> struct.
    /// </summary>
    /// <param name="context">A struct of the current throttle state.</param>
    /// <param name="readBytes">Amount of current read bytes to be added.</param>
    /// <param name="cancelTokenHandle">Cancellation token to cancel the waiting throttle task.</param>
    /// <param name="asyncWaitHandle">Handle of the asynchronous event.</param>
    [UnmanagedCallersOnly(EntryPoint = "ThrottleServiceAddBytesOrWaitAsync", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int ThrottleServiceAddBytesOrWaitAsync(
        ThrottleServiceContext* context,
        long                    readBytes,
        nint                    cancelTokenHandle,
        nint*                   asyncWaitHandle)
    {
        if (context == null) return unchecked((int)0x80004003);

        CancellationTokenSource cts = new();
        if (cancelTokenHandle != nint.Zero)
        {
            SafeWaitHandle cancelTokenSafeWaitHandle = new(cancelTokenHandle, false);
            EventWaitHandle cancelTokenWaitHandle = new(false, EventResetMode.ManualReset)
            {
                SafeWaitHandle = cancelTokenSafeWaitHandle
            };

            ThreadPool.RegisterWaitForSingleObject(cancelTokenWaitHandle,
                OnWaitSingleCompleted,
                cts,
                -1,
                true);
        }

        nint completionEvent = PInvoke.CreateEvent(nint.Zero, 1, 0, nint.Zero);
        *asyncWaitHandle = completionEvent;
        RunAsync((nint)context, readBytes, completionEvent, cts.Token);
        return 0;
    }

    private static void OnWaitSingleCompleted(object? state, bool isTimedOut)
    {
        ((CancellationTokenSource)state!).Cancel();
    }

    private static async void RunAsync(
        nint              context,
        long              readBytes,
        nint              completionEvent,
        CancellationToken token)
    {
        try
        {
            await ThrottleServiceContext
                .AddBytesOrWaitAsyncCore(context, readBytes, token)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
        finally
        {
            try
            {
                _ = PInvoke.SetEvent(completionEvent);
            }
            catch
            {
                // ignored
            }
        }
    }
}

#endif