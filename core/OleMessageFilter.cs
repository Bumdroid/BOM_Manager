using System;
using System.Runtime.InteropServices;

namespace BOMManager.Core
{
    /// <summary>
    /// COM RPC 호출 시 피호출자 거부(RPC_E_CALL_REJECTED / 0x80010001, SERVERCALL_RETRYLATER)를 
    /// 자동으로 대기 및 재시도 처리해주는 OLE Message Filter
    /// </summary>
    public class OleMessageFilter : IMessageFilter
    {
        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IMessageFilter? newFilter, out IMessageFilter? oldFilter);

        [ThreadStatic]
        private static IMessageFilter? _previousFilter;

        /// <summary>
        /// 현재 스레드에 OleMessageFilter를 등록합니다.
        /// </summary>
        public static void Register()
        {
            try
            {
                IMessageFilter newFilter = new OleMessageFilter();
                CoRegisterMessageFilter(newFilter, out _previousFilter);
            }
            catch { }
        }

        /// <summary>
        /// 현재 스레드의 OleMessageFilter 등록을 해제하고 이전 필터로 복원합니다.
        /// </summary>
        public static void Revoke()
        {
            try
            {
                CoRegisterMessageFilter(_previousFilter, out _);
                _previousFilter = null;
            }
            catch { }
        }

        int IMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        {
            // SERVERCALL_ISHANDLED = 0
            return 0;
        }

        int IMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            // dwRejectType: 2 = SERVERCALL_RETRYLATER, 1 = SERVERCALL_REJECTED
            // 15초 이내인 경우 150ms 대기 후 OLE가 자동 재시도하도록 150 리턴
            if (dwTickCount < 15000)
            {
                return 150; // Milliseconds to wait before retry
            }
            // 15초 초과 시 호출 취소 (-1)
            return -1;
        }

        int IMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        {
            // PENDINGMSG_WAITDEFPROCESS = 2
            return 2;
        }
    }

    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }
}
