using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace SolidWorksMLAddin
{
    [Guid("B7A3E2D1-4C5F-6A7B-8C9D-0E1F2A3B4C5D")]
    [ComVisible(true)]
    [ProgId("SolidWorksMLAddin.SwAddin")]
    public class SwAddin : ISwAddin
    {
        private ISldWorks _swApp;
        private ICommandManager _cmdMgr;
        private int _addinId;
        private const int MainCmdGroupId = 1005;

        private static readonly string LogFile = @"c:\Temp\BOM_Manager\addin_debug.log";
        private static readonly string ExePath = @"c:\Temp\BOM_Manager\BOM_Manager.exe";

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, message));
            }
            catch { }
        }

        #region ISwAddin Implementation

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            Log("ConnectToSW 시작. cookie = " + cookie);
            try
            {
                _swApp = (ISldWorks)ThisSW;
                _addinId = cookie;

                // 1. 콜백 인터페이스 등록
                _swApp.SetAddinCallbackInfo2(0, this, _addinId);
                Log("SetAddinCallbackInfo2 등록 완료");

                // 2. 상단 메뉴바 (도구 메뉴) 아이템 추가
                AddMenuItems();

                // 3. CommandManager 툴바 & 상단 탭 등록
                _cmdMgr = _swApp.GetCommandManager(_addinId);
                if (_cmdMgr != null)
                {
                    SetupCommandManager();
                }

                Log("ConnectToSW 성공 완료");
                return true;
            }
            catch (Exception ex)
            {
                Log("ConnectToSW 오류: " + ex.ToString());
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            Log("DisconnectFromSW 시작");
            try
            {
                RemoveCommandManager();
                RemoveMenuItems();

                if (_cmdMgr != null)
                {
                    Marshal.ReleaseComObject(_cmdMgr);
                    _cmdMgr = null;
                }
                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }
                Log("DisconnectFromSW 성공 완료");
                return true;
            }
            catch (Exception ex)
            {
                Log("DisconnectFromSW 오류: " + ex.ToString());
                return false;
            }
        }

        #endregion

        #region UI & Commands Setup

        private void SetupCommandManager()
        {
            int cmdGroupErr = 0;
            object cmdGroupObj = _cmdMgr.CreateCommandGroup2(
                MainCmdGroupId,
                "BOM Manager V0.0",
                "SolidWorks Active Assembly BOM Automation",
                "BOM Manager 실행",
                -1,
                false,
                ref cmdGroupErr
            );

            if (cmdGroupObj == null)
            {
                Log("CreateCommandGroup2 실패. 에러코드: " + cmdGroupErr);
                return;
            }

            ICommandGroup cmdGroup = (ICommandGroup)cmdGroupObj;

            // 아이콘 리스트 설정 (resources 또는 addin 폴더)
            string icon20 = @"c:\Temp\BOM_Manager\addin\mainicon_20.png";
            string icon32 = @"c:\Temp\BOM_Manager\addin\mainicon_32.png";
            if (File.Exists(icon20) && File.Exists(icon32))
            {
                cmdGroup.SmallIconList = icon20;
                cmdGroup.LargeIconList = icon32;
                cmdGroup.SmallMainIcon = icon20;
                cmdGroup.LargeMainIcon = icon32;
            }

            // Command Item 추가
            int cmdIndex = cmdGroup.AddCommandItem2(
                "BOM Manager V0.0",
                -1,
                "활성 어셈블리 BOM 정보 관리자 실행",
                "BOM Manager 실행",
                0,
                "LaunchBOMManager",
                "EnableBOMManager",
                0,
                (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem)
            );

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = true;
            cmdGroup.Activate();

            // 상단 CommandTab 등록 (어셈블리, 파트, 도면)
            int[] docTypes = new int[] {
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocDRAWING
            };

            foreach (int docType in docTypes)
            {
                try
                {
                    // 중복 버튼 방지: 기존 탭이 있으면 먼저 제거
                    ICommandTab existingTab = _cmdMgr.GetCommandTab(docType, "BOM Manager");
                    if (existingTab != null)
                    {
                        try { _cmdMgr.RemoveCommandTab((CommandTab)existingTab); } catch { }
                    }

                    // 단일 탭 생성 및 1개의 버튼만 추가
                    ICommandTab cmdTab = _cmdMgr.AddCommandTab(docType, "BOM Manager");
                    if (cmdTab != null)
                    {
                        ICommandTabBox cmdBox = cmdTab.AddCommandTabBox();
                        int cmdId = cmdGroup.get_CommandID(cmdIndex);
                        int[] cmdIDs = new int[] { cmdId };
                        int[] textDisplayStyles = new int[] { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal };

                        cmdBox.AddCommands(cmdIDs, textDisplayStyles);
                        Log(string.Format("CommandTab 등록 성공 (docType={0}, cmdId={1})", docType, cmdId));
                    }
                }
                catch (Exception exTab)
                {
                    Log(string.Format("CommandTab 등록 예외 (docType={0}): {1}", docType, exTab.Message));
                }
            }
        }

        private void RemoveCommandManager()
        {
            if (_cmdMgr != null)
            {
                try
                {
                    int[] docTypes = new int[] { (int)swDocumentTypes_e.swDocASSEMBLY, (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocDRAWING };
                    foreach (int docType in docTypes)
                    {
                        ICommandTab cmdTab = _cmdMgr.GetCommandTab(docType, "BOM Manager");
                        if (cmdTab != null)
                        {
                            _cmdMgr.RemoveCommandTab((CommandTab)cmdTab);
                        }
                    }
                    _cmdMgr.RemoveCommandGroup2(MainCmdGroupId, true);
                }
                catch (Exception ex)
                {
                    Log("RemoveCommandManager 예외: " + ex.Message);
                }
            }
        }

        private void AddMenuItems()
        {
            // 상단 메뉴바의 "도구(&T)" 하위에 BOM Manager 메뉴 추가
            int[] docTypes = new int[] {
                (int)swDocumentTypes_e.swDocNONE,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocDRAWING
            };

            foreach (int docType in docTypes)
            {
                try
                {
                    _swApp.AddMenuItem5(
                        docType,
                        _addinId,
                        "BOM Manager V0.0@도구(&T)",
                        -1,
                        "LaunchBOMManager",
                        "EnableBOMManager",
                        "SolidWorks BOM Manager 실행",
                        @"c:\Temp\BOM_Manager\addin\mainicon_20.png"
                    );
                }
                catch { }
            }
        }

        private void RemoveMenuItems()
        {
            // 메뉴 아이템은 DisconnectFromSW 및 RemoveCommandGroup2 호출 시 SolidWorks에 의해 자동 정리됩니다.
        }

        #endregion

        #region Callbacks

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public void LaunchBOMManager()
        {
            Log("LaunchBOMManager 콜백 실행됨");
            try
            {
                Process[] existing = Process.GetProcessesByName("BOM_Manager");
                if (existing != null && existing.Length > 0)
                {
                    IntPtr hWnd = existing[0].MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, 9); // SW_RESTORE
                        SetForegroundWindow(hWnd);
                    }
                    _swApp.SendMsgToUser2(
                        "BOM Manager가 이미 실행 중입니다.\n열려 있는 창을 확인해 주세요.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk
                    );
                    return;
                }

                if (File.Exists(ExePath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ExePath,
                        WorkingDirectory = Path.GetDirectoryName(ExePath),
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Log("BOM_Manager.exe 실행 완료");
                }
                else
                {
                    _swApp.SendMsgToUser2(
                        "BOM_Manager.exe 파일을 찾을 수 없습니다:\n" + ExePath,
                        (int)swMessageBoxIcon_e.swMbWarning,
                        (int)swMessageBoxBtn_e.swMbOk
                    );
                }
            }
            catch (Exception ex)
            {
                Log("BOM Manager 실행 오류: " + ex.ToString());
                _swApp.SendMsgToUser2(
                    "BOM Manager 실행 중 오류가 발생했습니다:\n" + ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk
                );
            }
        }

        public int EnableBOMManager()
        {
            // 1 = 활성화, 0 = 비활성화
            return 1;
        }

        #endregion

        #region COM Registration Functions

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                string guidStr = t.GUID.ToString("B").ToUpper();
                string keyPath = @"SOFTWARE\SolidWorks\Addins\" + guidStr;

                using (RegistryKey rk = Registry.LocalMachine.CreateSubKey(keyPath))
                {
                    if (rk != null)
                    {
                        rk.SetValue(null, 1, RegistryValueKind.DWord);
                        rk.SetValue("Title", "BOM Manager V0.0", RegistryValueKind.String);
                        rk.SetValue("Description", "SolidWorks 2021 BOM Manager Automation Add-in", RegistryValueKind.String);
                        rk.SetValue("Default", 1, RegistryValueKind.DWord);
                    }
                }
                Log("COM Add-in 레지스트리 자동 등록 완료: " + keyPath);
            }
            catch (Exception ex)
            {
                Log("RegisterFunction 예외: " + ex.ToString());
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                string guidStr = t.GUID.ToString("B").ToUpper();
                string keyPath = @"SOFTWARE\SolidWorks\Addins\" + guidStr;
                Registry.LocalMachine.DeleteSubKeyTree(keyPath, false);
                Log("COM Add-in 레지스트리 자동 삭제 완료: " + keyPath);
            }
            catch (Exception ex)
            {
                Log("UnregisterFunction 예외: " + ex.ToString());
            }
        }

        #endregion
    }
}
