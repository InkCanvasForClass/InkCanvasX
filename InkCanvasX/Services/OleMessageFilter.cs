using System;
using System.Runtime.InteropServices;

namespace SkiaAnnotate.Services;

/// <summary>
/// Office COM call-retry filter for RPC_E_CALL_REJECTED.
/// This prevents crashes when PowerPoint is temporarily busy.
/// </summary>
internal static class OleMessageFilter
{
    // IOleMessageFilter interface (COM)
    [ComImport]
    [Guid("00000016-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }

    private sealed class MessageFilterImpl : IOleMessageFilter
    {
        // SERVERCALL_ISHANDLED = 0
        public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => 0;

        // When the server is busy, tell COM to retry shortly.
        // dwRejectType: SERVERCALL_RETRYLATER = 2
        public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            if (dwRejectType == 2)
            {
                // Retry after 50ms
                return 50;
            }

            // Cancel the call
            return -1;
        }

        // PENDINGMSG_WAITDEFPROCESS = 2
        public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => 2;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);

    private static IOleMessageFilter? _oldFilter;

    public static void Register()
    {
        try
        {
            var newFilter = new MessageFilterImpl();
            CoRegisterMessageFilter(newFilter, out _oldFilter);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"注册 OleMessageFilter 失败：{ex.Message}");
        }
    }

    public static void Revoke()
    {
        try
        {
            CoRegisterMessageFilter(_oldFilter, out _);
            _oldFilter = null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"撤销 OleMessageFilter 失败：{ex.Message}");
        }
    }
}

