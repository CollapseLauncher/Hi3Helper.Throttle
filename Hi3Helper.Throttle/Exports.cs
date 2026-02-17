#if CompileNative

using Microsoft.Win32.SafeHandles;
using System;
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
    public static unsafe int ThrottleServiceSetSharedThrottleBytes(int bytesPerSecond, int* burstBytes) =>
        ThrottleService.SetSharedThrottleBytes(bytesPerSecond, burstBytes == null ? null : *burstBytes);

    /// <summary>
    /// Adds the current read bytes and wait if the total exceeds the throttle limit from a given <see cref="ThrottleService"/> struct.
    /// </summary>
    /// <param name="service">A struct of the current throttle state.</param>
    /// <param name="readBytes">Amount of current read bytes to be added.</param>
    /// <param name="tokenHandle">Cancellation token to cancel the waiting throttle task.</param>
    /// <param name="asyncWaitHandle">Handle of the asynchronous event.</param>
    [UnmanagedCallersOnly(EntryPoint = "ThrottleServiceAddBytesOrWaitAsync", CallConvs = [typeof(CallConvCdecl)])]
    [SkipLocalsInit]
    public static unsafe int ThrottleServiceAddBytesOrWaitAsync(
        ThrottleService* service,
        long             readBytes,
        nint             tokenHandle,
        void**           asyncWaitHandle)
    {
        if (service == null)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        CancellationToken token = CancellationToken.None;
        if (tokenHandle != nint.Zero)
        {
            token.WaitHandle.SafeWaitHandle = new SafeWaitHandle(tokenHandle, ownsHandle: false);
        }

        nint waitHandle = CreateEvent(nint.Zero, 1, 0, nint.Zero);
        *asyncWaitHandle = (void*)waitHandle;

        ValueTask task = ThrottleService.AddBytesOrWaitAsyncCore((nint)service, readBytes, token);

        // Complete early
        if (task.IsCompleted)
        {
            _ = SetEvent(waitHandle);
            return 0; // S_OK
        }

        // If cancelled, return COR_E_OPERATIONCANCELED
        if (task.IsCanceled)
        {
            _ = SetEvent(waitHandle);
            return unchecked((int)0x8013153B);
        }

        // If faulted (which should not happen), return the exception's HRESULT
        if (task.IsFaulted)
        {
            Task taskTask = task.AsTask();
            Exception? exception = taskTask.Exception?.Flatten();
            exception = exception is AggregateException ? exception.InnerException : exception;

            _ = SetEvent(waitHandle);
            return exception?.HResult ?? 1; // HRESULT or S_FALSE
        }

        ValueTaskAwaiter taskAwaiter = task.GetAwaiter();
        taskAwaiter.OnCompleted(SetEventOnCompleted);
        return 0; // S_OK

        void SetEventOnCompleted()
        {
            _ = SetEvent(waitHandle);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    public static extern nint CreateEvent(nint lpEventAttributes, int bManualReset, int bInitialState, nint lpName);

    [DllImport("kernel32.dll", EntryPoint = "SetEvent", SetLastError = true)]
    public static extern int SetEvent(nint hEvent);
}

#endif