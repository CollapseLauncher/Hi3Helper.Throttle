#if WIN32
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming
#pragma warning disable SYSLIB1054

namespace Hi3Helper.Throttle;

internal static class PInvoke
{
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    internal static extern int CloseHandle(nint hObject);

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    internal static extern nint CreateWaitableTimerEx(
        nint lpTimerAttributes,
        nint lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", EntryPoint = "SetWaitableTimer", SetLastError = true)]
    internal static extern int SetWaitableTimer(
        nint     hTimer,
        ref long pDueTime,
        int      lPeriod,
        nint     pfnCompletionRoutine,
        nint     lpArgToCompletionRoutine,
        bool     fResume);

    [DllImport("kernel32.dll", EntryPoint = "CancelWaitableTimer", SetLastError = true)]
    internal static extern int CancelWaitableTimer(nint hTimer);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    internal static extern nint CreateEvent(
        nint lpEventAttributes,
        int  bManualReset,
        int  bInitialState,
        nint lpName);

    [DllImport("kernel32.dll", EntryPoint = "SetEvent", SetLastError = true)]
    internal static extern int SetEvent(nint hEvent);

    [DllImport("kernel32.dll", EntryPoint = "CreateThreadpoolWait", SetLastError = true)]
#if NET5_0_OR_GREATER
    internal static unsafe extern nint CreateThreadpoolWait(
        delegate* unmanaged[Stdcall]<nint, nint, nint, uint, void> callback,
        nint                                                       context,
        nint                                                       callbackEnviron);
#else
    internal static extern nint CreateThreadpoolWait(
        PTP_WAIT_CALLBACK callback,
        nint              context,
        nint              callbackEnviron);
    
    internal delegate void PTP_WAIT_CALLBACK(
        nint instance,
        nint context,
        nint wait,
        uint waitResult);
#endif

    [DllImport("kernel32.dll", EntryPoint = "SetThreadpoolWait")]
    internal static extern void SetThreadpoolWait(
        nint pWait,
        nint h,
        nint pftTimeout);

    [DllImport("kernel32.dll", EntryPoint = "CloseThreadpoolWait")]
    internal static extern void CloseThreadpoolWait(nint pWait);

    internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    internal const uint TIMER_ALL_ACCESS                      = 0x1F0003;
}
#endif