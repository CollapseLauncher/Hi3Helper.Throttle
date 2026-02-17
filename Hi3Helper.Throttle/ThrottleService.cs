using System;
using System.Diagnostics;
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
[StructLayout(LayoutKind.Sequential)]
public struct ThrottleService
{
    internal static long SharedBytesPerSecond;
    internal static long Capacity;

    private long _availableTokens;
    private long _lastTimestamp;

    /// <summary>
    /// Creates a new instance of throttling service.
    /// </summary>
    public ThrottleService()
    {
        _availableTokens = Capacity;
        _lastTimestamp   = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Set how many bytes to be throttled per second. Set to 0 to disable throttling.
    /// </summary>
    /// <param name="bytesPerSecond">Amount of bytes per second target to be throttled.</param>
    /// <param name="burstBytes">[Optional] Amount of bytes burst allowed once throttle is over.</param>
    public static int SetSharedThrottleBytes(int bytesPerSecond, int? burstBytes = null)
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
        ref ThrottleService service,
        long                readBytes,
        CancellationToken   token = default)
#if NET5_0_OR_GREATER
        => AddBytesOrWaitAsyncCore((nint)Unsafe.AsPointer(ref service), readBytes, token);
#else
    {
        fixed (ThrottleService* ptr = &service)
        {
            return AddBytesOrWaitAsyncCore((nint)ptr, readBytes, token);
        }
    }
#endif

    internal static async ValueTask AddBytesOrWaitAsyncCore(
        nint              servicePtr,
        long              readBytes,
        CancellationToken token = default)
    {
        if (readBytes <= 0 || SharedBytesPerSecond == 0 || servicePtr == 0)
            return;

        SpinWait spin = default;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            long now = Stopwatch.GetTimestamp();
            ReadLastTokens(servicePtr, out long last, out long tokens);

            long elapsedTicks = now - last;

            if (elapsedTicks > 0)
            {
                if (!TryExchangeLastTimeTokenOrContinue(servicePtr,
                                                        ref spin,
                                                        elapsedTicks,
                                                        ref now,
                                                        ref last,
                                                        ref tokens))
                {
                    continue;
                }
            }

            if (tokens >= readBytes)
            {
                if (IsTokenEqual(servicePtr,
                                 ref spin,
                                 readBytes,
                                 tokens))
                {
                    return;
                }

                continue;
            }

            long deficit    = readBytes - tokens;
            long delayTicks = deficit * Stopwatch.Frequency / SharedBytesPerSecond;

            int delayMs = (int)(delayTicks * 1000 / Stopwatch.Frequency);
            if (delayMs <= 0)
                delayMs = 1;

            await Task.Delay(delayMs, token);
            spin.Reset();
        }
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadLastTokens(nint serviceP, out long lastTimestamp, out long tokens)
    {
        ref ThrottleService service = ref GetServiceFromPointer(serviceP);

        lastTimestamp = Volatile.Read(ref service._lastTimestamp);
        tokens        = Volatile.Read(ref service._availableTokens);
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryExchangeLastTimeTokenOrContinue(nint         serviceP,
                                                           ref SpinWait spin,
                                                           long         elapsedTicks,
                                                           ref long     now,
                                                           ref long     last,
                                                           ref long     tokens)
    {
        ref ThrottleService service = ref GetServiceFromPointer(serviceP);

        long refill    = elapsedTicks * SharedBytesPerSecond / Stopwatch.Frequency;
        long newTokens = Math.Min(Capacity, tokens + refill);

        if (Interlocked.CompareExchange(ref service._lastTimestamp, now, last) == last)
        {
            Interlocked.Exchange(ref service._availableTokens, newTokens);
            tokens = newTokens;
            return true;
        }

        spin.SpinOnce();
        return false;
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTokenEqual(nint         serviceP,
                                     ref SpinWait spin,
                                     long         readBytes,
                                     long         tokens)
    {
        ref ThrottleService service = ref GetServiceFromPointer(serviceP);

        if (Interlocked.CompareExchange(ref service._availableTokens,
                                        tokens - readBytes,
                                        tokens) == tokens)
        {
            return true;
        }

        spin.SpinOnce();
        return false;
    }

#if NET5_0_OR_GREATER
    [SkipLocalsInit]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe ref ThrottleService GetServiceFromPointer(nint pointer)
#if NET5_0_OR_GREATER
        => ref Unsafe.AsRef<ThrottleService>((void*)pointer);
#else
        => ref *(ThrottleService*)pointer;
#endif
}