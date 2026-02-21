using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

// ReSharper disable ConvertToExtensionBlock

namespace Test;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ThrottleServiceContext
{
    private long _availableTokens;
    private long _lastTimestamp;
}

public static partial class ThrottleHelper
{
    [LibraryImport("Hi3Helper.Throttle.dll", EntryPoint = "ThrottleServiceSetSharedThrottleBytes")]
    internal static partial int SetSharedThrottleBytes(long bytesPerSecond, in long burstBytes);

    [LibraryImport("Hi3Helper.Throttle.dll", EntryPoint = "ThrottleServiceAddBytesOrWaitAsync")]
    [return: MarshalAs(UnmanagedType.Error)]
    private static partial int ThisThrottleServiceAddBytesOrWaitAsync(
        in ThrottleServiceContext service,
        long                      readBytes,
        nint                      tokenHandle,
        out nint                  asyncWaitHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static partial int CloseHandle(nint hObject);

    public static ValueTask AddBytesOrWaitAsync(
        this ref ThrottleServiceContext service,
        long                            readBytes,
        CancellationToken               token = default)
    {
        nint tokenHandle = token.WaitHandle.SafeWaitHandle.DangerousGetHandle();
        int hr = ThisThrottleServiceAddBytesOrWaitAsync(in service,
                                                        readBytes,
                                                        tokenHandle,
                                                        out nint asyncWaitHandle);

        Marshal.ThrowExceptionForHR(hr);

        SafeWaitHandle safeHandle = new(asyncWaitHandle, false);
        WaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset)
        {
            SafeWaitHandle = safeHandle
        };

        AsyncValueTaskMethodBuilder valueTaskCs = new();
        ThreadPool.UnsafeRegisterWaitForSingleObject(waitHandle,
                                                     DisposeWaitHandleCallback,
                                                     null,
                                                     -1,
                                                     true);

        return valueTaskCs.Task;
        
        void DisposeWaitHandleCallback(object? state, bool isTimedOut)
        {
            safeHandle.Dispose();
            waitHandle.Dispose();

            if (asyncWaitHandle != nint.Zero)
            {
                _ = CloseHandle(asyncWaitHandle);
            }

            valueTaskCs.SetResult();
        }
    }
}

public class Program
{
    private static ThrottleServiceContext _throttleContext;

    static Program()
    {
        _throttleContext = default;
        ThrottleHelper.SetSharedThrottleBytes(2 << 20, 4 << 20);
    }

    public static async Task Main(params string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        new Thread(ReadChangeSettings)
        {
            IsBackground = true
        }.Start();

        using HttpClient client = new();
        await DummyRead(client, args[0]);
    }

    private static void ReadChangeSettings()
    {
        while (true)
        {
            if (Console.ReadLine() is not { } line ||
                !double.TryParse(line, null, out double result)) continue;

            result *= 1 << 20;
            long resultAbs      = (long)Math.Abs(result);
            long resultAbsBurst = resultAbs * 2;
            ThrottleHelper.SetSharedThrottleBytes(resultAbs, resultAbsBurst);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private static async Task DummyRead(HttpClient client, string url)
    {
        while (true)
        {
            await using Stream nullStream   = Stream.Null;
            await using Stream remoteStream = await client.GetFileStream(url);

            Console.WriteLine($"Download for: {url} will be started...");

            byte[]       buffer    = ArrayPool<byte>.Shared.Rent(32 << 10);
            Memory<byte> bufferMem = buffer;

            try
            {
                int read;
                while ((read = await remoteStream.ReadAsync(bufferMem)) > 0)
                {
                    double speed    = Ext.CalculateSpeed(read);
                    string speedStr = Ext.SummarizeSizeSimple(speed, 1);

                    Console.Write($"\r{read} bytes read | Downloading {speedStr}/s");

                    await _throttleContext.AddBytesOrWaitAsync(read);
                    nullStream.Write(buffer.AsSpan(0, read));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}

public static class Ext
{
    internal static async Task<Stream> GetFileStream(this HttpClient client, string fileUrl)
    {
        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, fileUrl), HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    }

    private const  double ScOneSecond = 1000;
    private static long   _scLastTick = Environment.TickCount64;
    private static long   _scLastReceivedBytes;
    private static double _scLastSpeed;

    internal static double CalculateSpeed(long receivedBytes) => CalculateSpeed(receivedBytes, ref _scLastSpeed, ref _scLastReceivedBytes, ref _scLastTick);

    internal static double CalculateSpeed(long receivedBytes, ref double lastSpeedToUse, ref long lastReceivedBytesToUse, ref long lastTickToUse)
    {
        long   currentTick           = Environment.TickCount64 - lastTickToUse + 1;
        long   totalReceivedInSecond = Interlocked.Add(ref lastReceivedBytesToUse, receivedBytes);
        double speed                 = totalReceivedInSecond * ScOneSecond / currentTick;

        if (!(currentTick > ScOneSecond))
        {
            return lastSpeedToUse;
        }

        lastSpeedToUse = speed;
        _              = Interlocked.Exchange(ref lastSpeedToUse,         speed);
        _              = Interlocked.Exchange(ref lastReceivedBytesToUse, 0);
        _              = Interlocked.Exchange(ref lastTickToUse,          Environment.TickCount64);
        return lastSpeedToUse;
    }

    private static readonly string[] SizeSuffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];
    public static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
    {
        int mag = (int)Math.Log(value, 1000);
        mag = Math.Clamp(mag, 0, SizeSuffixes.Length - 1);

        return $"{Math.Round(value / (1L << (mag * 10)), decimalPlaces)} {SizeSuffixes[mag]}";
    }
}