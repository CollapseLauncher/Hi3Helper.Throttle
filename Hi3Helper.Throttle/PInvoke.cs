using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming
#pragma warning disable SYSLIB1054

namespace Hi3Helper.Throttle;

internal static class PInvoke
{
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    public static extern int CloseHandle(nint hObject);

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    public static extern nint CreateWaitableTimerEx(
        nint lpTimerAttributes,
        nint lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", EntryPoint = "SetWaitableTimer", SetLastError = true)]
    public static extern int SetWaitableTimer(
        nint     hTimer,
        ref long pDueTime,
        int      lPeriod,
        nint     pfnCompletionRoutine,
        nint     lpArgToCompletionRoutine,
        bool     fResume);

    [DllImport("kernel32.dll", EntryPoint = "CancelWaitableTimer", SetLastError = true)]
    public static extern int CancelWaitableTimer(nint hTimer);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    public static extern nint CreateEvent(
        nint lpEventAttributes,
        int  bManualReset,
        int  bInitialState,
        nint lpName);

    [DllImport("kernel32.dll", EntryPoint = "SetEvent", SetLastError = true)]
    public static extern int SetEvent(nint hEvent);

    [DllImport("kernel32.dll", EntryPoint = "CreateThreadpoolWait", SetLastError = true)]
#if NET5_0_OR_GREATER
    public static extern unsafe nint CreateThreadpoolWait(
        delegate* unmanaged[Stdcall]<nint, nint, nint, uint, void> callback,
        nint                                                       context,
        nint                                                       callbackEnviron);
#else
    public static extern nint CreateThreadpoolWait(
        PTP_WAIT_CALLBACK callback,
        nint              context,
        nint              callbackEnviron);
    
    public delegate void PTP_WAIT_CALLBACK(
        nint instance,
        nint context,
        nint wait,
        uint waitResult);
#endif

    [DllImport("kernel32.dll", EntryPoint = "SetThreadpoolWait")]
    public static extern void SetThreadpoolWait(
        nint pWait,
        nint h,
        nint pftTimeout);

    [DllImport("kernel32.dll", EntryPoint = "CloseThreadpoolWait")]
    public static extern void CloseThreadpoolWait(nint pWait);

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    public const uint TIMER_ALL_ACCESS                      = 0x1F0003;
}
