using Hi3Helper.Throttle.Delays;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable CommentTypo
// ReSharper disable ConvertConstructorToMemberInitializers
#pragma warning disable CA2012
#pragma warning disable CA1401
#pragma warning disable SYSLIB1054

namespace Hi3Helper.Throttle;

/// <summary>
/// Provides throttling functionality to limit the rate of data processing per second.
/// This service provides a token-based throttling to support multiple threads processing.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)] // Pack to 8 bytes to ensure aligning
public struct ThrottleServiceContext
{
    internal static long SharedBytesPerSecond;
    internal static long Capacity;

    private long _availableTokens;
    private long _lastTimestamp;

    /// <summary>
    /// Creates a new instance of throttling service.
    /// </summary>
    public ThrottleServiceContext()
    {
        _availableTokens = Capacity;
#if NETCOREAPP
        _lastTimestamp = Environment.TickCount64;
#else
        _lastTimestamp = Environment.TickCount;
#endif
    }

    /// <summary>
    /// Set how many bytes to be throttled per second. Set to 0 to disable throttling.
    /// </summary>
    /// <param name="bytesPerSecond">Amount of bytes per second target to be throttled.</param>
    /// <param name="burstBytes">[Optional] Amount of bytes burst allowed once throttle is over.</param>
#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    public static int SetSharedThrottleBytes(long bytesPerSecond, long? burstBytes = null)
    {
        if (bytesPerSecond < 0)
        {
            return unchecked((int)0x80131502);
        }

        Interlocked.Exchange(ref SharedBytesPerSecond, bytesPerSecond);
        Interlocked.Exchange(ref Capacity,             burstBytes ?? bytesPerSecond);
        return 0;
    }

    /// <summary>
    /// Adds the current read bytes and wait if the total exceeds the throttle limit.
    /// </summary>
    /// <param name="service">Throttle service context to be used.</param>
    /// <param name="readBytes">Amount of current read bytes to be added.</param>
    /// <param name="token">Cancellation token to cancel the waiting throttle task.</param>
    public static unsafe ValueTask AddBytesOrWaitAsync(
        ref ThrottleServiceContext service,
        long                       readBytes,
        CancellationToken          token = default)
#if NET5_0_OR_GREATER
        => AddBytesOrWaitAsyncCore((nint)Unsafe.AsPointer(ref service), readBytes, token);
#else
    {
        fixed (ThrottleServiceContext* ptr = &service)
        {
            return AddBytesOrWaitAsyncCore((nint)ptr, readBytes, token);
        }
    }
#endif

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    internal static async ValueTask AddBytesOrWaitAsyncCore(
        nint              servicePtr,
        long              readBytes,
        CancellationToken token = default)
    {
        if (readBytes <= 0 || SharedBytesPerSecond == 0 || servicePtr == 0)
            return;

    Backoff:
        token.ThrowIfCancellationRequested();
        if (!IsTokenSufficient(servicePtr,
                               readBytes,
                               out bool isBackoff,
                               out long tokens))
        {
            return;
        }

        if (isBackoff)
        {
            goto Backoff;
        }
    
        long deficit = readBytes - tokens;
        int  delayMs = (int)(deficit * 1000 / SharedBytesPerSecond);

        using (NativeDelay delay = NativeDelay.Create())
        {
            await delay.WaitAsync(delayMs, token);
        }
        goto Backoff;
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    private static bool IsTokenSufficient(nint         servicePtr,
                                          long         readBytes,
                                          out bool     isBackoff,
                                          out long     tokens)
    {
        ref ThrottleServiceContext service = ref GetServiceFromPointer(servicePtr);
        isBackoff = false;

#if NETCOREAPP
        long now = Environment.TickCount64;
#else
        long now = Environment.TickCount;
#endif

        long last      = Volatile.Read(ref service._lastTimestamp);
        long elapsedMs = now - last;

        if (elapsedMs > 0)
        {
            // Move time forward (no CAS needed)
            Interlocked.Exchange(ref service._lastTimestamp, now);

            long rate   = SharedBytesPerSecond;
            long refill = elapsedMs * rate / 1000;

            if (refill > 0)
            {
                long newTotal = Interlocked.Add(ref service._availableTokens, refill);

                // Clamp without CAS loop
                if (newTotal > Capacity)
                {
                    Interlocked.Exchange(ref service._availableTokens, Capacity);
                }
            }
        }

        tokens = Volatile.Read(ref service._availableTokens);

        if (tokens < readBytes) return true;
        if (Interlocked.CompareExchange(ref service._availableTokens,
                tokens - readBytes,
                tokens) == tokens)
        {
            return false;
        }

        isBackoff = true;
        return true;
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe ref ThrottleServiceContext GetServiceFromPointer(nint pointer)
        => ref *(ThrottleServiceContext*)pointer;
}