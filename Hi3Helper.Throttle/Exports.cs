#if CompileNative

using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
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
    /// Adds the current read bytes and wait if the total exceeds the throttle limit from a given <see cref="ThrottleServiceContext"/> struct.
    /// </summary>
    /// <param name="context">A struct of the current throttle state.</param>
    /// <param name="readBytes">Amount of current read bytes to be added.</param>
    /// <param name="tokenHandle">Cancellation token to cancel the waiting throttle task.</param>
    /// <param name="asyncWaitHandle">Handle of the asynchronous event.</param>
    [UnmanagedCallersOnly(EntryPoint = "ThrottleServiceAddBytesOrWaitAsync", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int ThrottleServiceAddBytesOrWaitAsync(
        ThrottleServiceContext* context,
        long                    readBytes,
        nint                    tokenHandle,
        void**                  asyncWaitHandle)
    {
        if (context == null)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        CancellationToken token           = CancellationToken.None;
        SafeWaitHandle?   tokenWaitHandle = null;
        if (tokenHandle != nint.Zero)
        {
            tokenWaitHandle                 = new SafeWaitHandle(tokenHandle, ownsHandle: false);
            token.WaitHandle.SafeWaitHandle = tokenWaitHandle;
        }

        nint waitHandle = PInvoke.CreateEvent(nint.Zero, 1, 0, nint.Zero);
        *asyncWaitHandle = (void*)waitHandle;

        RunAsync((nint)context, readBytes, waitHandle, tokenWaitHandle, token);
        return 0; // S_OK
    }

    private static async void RunAsync(nint context, long readBytes, nint waitHandle, SafeWaitHandle? tokenWaitHandle, CancellationToken token)
    {
        try
        {
            await ThrottleServiceContext.AddBytesOrWaitAsyncCore(context, readBytes, token);
        }
        catch
        {
            // ignore
        }
        finally
        {
            _ = PInvoke.SetEvent(waitHandle);
            tokenWaitHandle?.Dispose();
        }
    }
}

#endif