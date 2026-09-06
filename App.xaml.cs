using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BOMManager.Core;
using BOMManager.UI;

namespace BOMManager
{
    public partial class App : Application
    {
        private static readonly string LogFile = @"c:\Temp\BOM_Manager\addin_debug.log";

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
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
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

                // 2. Look in addin subdirectory
                path = Path.Combine(baseDir, "addin", $"{assemblyName}.dll");
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
            Log($"Dispatcher Unhandled Exception: {e.Exception}");
            MessageBox.Show(
                $"BOM Manager 실행 중 예외가 발생했습니다:\n\n{e.Exception.Message}\n\n상세 정보:\n{e.Exception.StackTrace}",
                "BOM Manager 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log($"AppDomain Unhandled Exception: {e.ExceptionObject}");
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"BOM Manager 치명적 오류:\n\n{ex.Message}\n\n상세 정보:\n{ex.StackTrace}",
                    "BOM Manager 치명적 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log("BOM Manager WPF OnStartup 시작");

            try
            {
                bool isMock = e.Args.Any(a => a.Equals("--mock", StringComparison.OrdinalIgnoreCase) ||
                                              a.Equals("-m", StringComparison.OrdinalIgnoreCase));

                ISolidWorksService service;
                if (isMock)
                {
                    Log("가상 목업 모드(MockSwConnector)로 실행");
                    service = new MockSwConnector();
                }
                else
                {
                    Log("SolidWorks 실제 연동 모드(SwConnector)로 실행");
                    service = new SwConnector();
                }

                var mainWindow = new MainWindow(service, isMock);
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
    }
}
