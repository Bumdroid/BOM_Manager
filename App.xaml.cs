using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BOMManager.Core;
using BOMManager.UI;

namespace BOMManager
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = "Global\\BOM_Manager_SingleInstance_Mutex_94B838E1";
        private static readonly string LogFile = @"c:\Temp\BOM_Manager\addin_debug.log";
        private ISolidWorksService? _activeService;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] {message}\r\n");
            }
            catch { }
        }

        public App()
        {
            // 3단 전역 예외 처리 및 크래시 방지 복구 메커니즘
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                var assemblyName = new AssemblyName(args.Name).Name;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. Look in base directory
                string path = Path.Combine(baseDir, $"{assemblyName}.dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);

                // 2. Look in lib subdirectory
                path = Path.Combine(baseDir, "lib", $"{assemblyName}.dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);

                // 3. Look in SolidWorks installation directory
                string swDir = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS";
                path = Path.Combine(swDir, $"{assemblyName}.dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Log($"AssemblyResolve error for {args.Name}: {ex}");
            }
            return null;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log($"[Dispatcher Unhandled Exception 가로채기 및 복구] {e.Exception}");
            MessageBox.Show(
                $"BOM Manager 실행 중 예외가 발생했으나 프로그램을 안전하게 유지합니다:\n\n{e.Exception.Message}\n\n상세 정보:\n{e.Exception.StackTrace}",
                "BOM Manager 예외 알림",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            
            // UI 스레드 비정상 종료 방지 및 복구
            e.Handled = true;

            // 메모리 정리
            TriggerGarbageCollection();
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log($"[TaskScheduler UnobservedTaskException 가로채기] {e.Exception}");
            // 비동기 작업 예외로 인한 프로세스 다운 방지
            e.SetObserved();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log($"[AppDomain Unhandled Exception] {e.ExceptionObject}");
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"BOM Manager 런타임 오류:\n\n{ex.Message}\n\n상세 정보:\n{ex.StackTrace}",
                    "BOM Manager 런타임 알림",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log("BOM Manager WPF OnStartup 시작 (.exe Standalone)");

            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                Log("이미 실행 중인 BOM Manager 인스턴스가 감지되었습니다. 기존 창을 활성화합니다.");
                ActivateExistingWindow();
                Shutdown();
                return;
            }

            try
            {
                bool isMock = e.Args.Any(a => a.Equals("--mock", StringComparison.OrdinalIgnoreCase) ||
                                              a.Equals("-m", StringComparison.OrdinalIgnoreCase));

                if (isMock)
                {
                    Log("가상 목업 모드(MockSwConnector)로 실행");
                    _activeService = new MockSwConnector();
                }
                else
                {
                    Log("SolidWorks 실제 연동 모드(SwConnector)로 실행");
                    _activeService = new SwConnector();
                }

                var mainWindow = new MainWindow(_activeService, isMock);
                mainWindow.Show();
                Log("MainWindow 표시 완료");
            }
            catch (Exception ex)
            {
                Log($"OnStartup 초기화 예외: {ex}");
                MessageBox.Show(
                    $"BOM Manager 시작 중 오류가 발생했습니다:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "BOM Manager 초기화 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var procs = Process.GetProcessesByName(current.ProcessName);
                foreach (var p in procs)
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
            }
            catch { }
        }

        private static void TriggerGarbageCollection()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_activeService is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch { }

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                }
                catch { }
            }

            TriggerGarbageCollection();
            base.OnExit(e);
        }
    }
}
