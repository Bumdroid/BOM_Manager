using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;
using BOMManager.Core;
using BOMManager.Modules.SpringDesigner.Models;

namespace BOMManager.Modules.SpringDesigner.Services
{
    public enum AutoCadStatus
    {
        NotRunning,   // acad.exe 없음 (Red)
        Initializing, // acad.exe 실행 중이나 COM 준비 중 (Yellow)
        Ready         // COM 연결 완료 및 명령 수신 준비됨 (Green)
    }

    /// <summary>
    /// AutoCAD 연동 및 2D/3D CAD 드로잉/아이콘 생성 서비스
    /// </summary>
    public static class SpringCadService
    {
        /// <summary>
        /// 스프링 UI 아이콘 비트맵 생성 (size x size)
        /// </summary>
        public static BitmapSource CreateSpringIconBitmap(int size = 24)
        {
            int s = Math.Max(16, size);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var pen = new System.Windows.Media.Pen(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)), 2.0);
                dc.DrawLine(pen, new System.Windows.Point(2, s / 2.0), new System.Windows.Point(s - 2, s / 2.0));
            }
            var bmp = new RenderTargetBitmap(s, s, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// 스프링 2D 미리보기 렌더링용 비트맵 아이콘 생성
        /// </summary>
        public static BitmapSource CreateSpringBitmapPreview(double wireDia, double outerDia, double freeLen, int coils, bool grinding, string material)
        {
            int width = 360;
            int height = 180;
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42)), // Slate 900
                    null,
                    new System.Windows.Rect(0, 0, width, height));

                var gridPen = new System.Windows.Media.Pen(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), 0.5);
                for (int x = 20; x < width; x += 30) dc.DrawLine(gridPen, new System.Windows.Point(x, 0), new System.Windows.Point(x, height));
                for (int y = 20; y < height; y += 30) dc.DrawLine(gridPen, new System.Windows.Point(0, y), new System.Windows.Point(width, y));

                var centerPen = new System.Windows.Media.Pen(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)), 1.0) // Red
                {
                    DashStyle = new System.Windows.Media.DashStyle(new double[] { 10, 4, 2, 4 }, 0)
                };
                dc.DrawLine(centerPen, new System.Windows.Point(20, height / 2.0), new System.Windows.Point(width - 20, height / 2.0));

                var springPen = new System.Windows.Media.Pen(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)), 2.5); // Sky 400

                double startX = 40.0;
                double endX = width - 40.0;
                double span = endX - startX;
                double centerY = height / 2.0;
                double amp = Math.Min(45.0, (outerDia / Math.Max(outerDia, 1.0)) * 40.0);

                int effectiveCoils = Math.Max(3, Math.Min(coils, 15));
                int segs = effectiveCoils * 2;
                double dx = span / segs;

                var pathGeo = new System.Windows.Media.StreamGeometry();
                using (var ctx = pathGeo.Open())
                {
                    ctx.BeginFigure(new System.Windows.Point(startX, centerY), false, false);
                    for (int i = 0; i < segs; i++)
                    {
                        double targetX = startX + (i + 1) * dx;
                        double targetY = (i % 2 == 0) ? (centerY - amp) : (centerY + amp);
                        if (i == segs - 1) targetY = centerY;
                        ctx.LineTo(new System.Windows.Point(targetX, targetY), true, false);
                    }
                }
                pathGeo.Freeze();
                dc.DrawGeometry(null, springPen, pathGeo);

                if (grinding)
                {
                    var grindPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60)), 2.0); // Orange
                    dc.DrawLine(grindPen, new System.Windows.Point(startX - 2, centerY - amp - 5), new System.Windows.Point(startX - 2, centerY + amp + 5));
                    dc.DrawLine(grindPen, new System.Windows.Point(endX + 2, centerY - amp - 5), new System.Windows.Point(endX + 2, centerY + amp + 5));
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// AutoCAD (acad.exe) 프로세스 및 COM 연결 준비 상태 확인
        /// </summary>
        public static AutoCadStatus CheckAutoCadStatus()
        {
            try
            {
                Process[] cadProcesses = Process.GetProcessesByName("acad");
                if (cadProcesses == null || cadProcesses.Length == 0)
                {
                    return AutoCadStatus.NotRunning;
                }

                // COM 연결 시도
                try
                {
                    object? acadApp = Marshal.GetActiveObject("AutoCAD.Application");
                    if (acadApp != null)
                    {
                        return AutoCadStatus.Ready;
                    }
                }
                catch
                {
                    // 로딩 중
                }

                return AutoCadStatus.Initializing;
            }
            catch
            {
                return AutoCadStatus.NotRunning;
            }
        }

        /// <summary>
        /// AutoCAD (acad.exe) 프로세스 실행 여부 확인 (하위 호환)
        /// </summary>
        public static bool IsAutoCadRunning()
        {
            return CheckAutoCadStatus() != AutoCadStatus.NotRunning;
        }

        /// <summary>
        /// 도면 생성 전 최신 스프링 설계 파라미터 JSON 파일들을 %AppData%\Common_Draw에 안전하게 동기화 저장
        /// </summary>
        public static void SaveAllParameters(SpringDesignParameters param)
        {
            if (param == null) return;
            try
            {
                string dir = SpringConfigService.GetConfigDirectoryPath();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 1. spring_spec.json
                var sbSpring = new StringBuilder();
                sbSpring.AppendLine("{");
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"WireDiameter\": {0},", param.WireDiameter));
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"OuterDiameter\": {0},", param.OuterDiameter));
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"FreeLength\": {0},", param.FreeLength));
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"P2h\": {0},", param.P2h));
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"TotalCoils\": {0},", param.TotalCoils));
                sbSpring.AppendLine($"  \"Material\": \"{param.Material}\",");
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"IsGrindingEnds\": {0},", param.IsGrindingEnds ? "true" : "false"));
                sbSpring.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"IsStandardized\": {0},", param.IsStandardized ? "true" : "false"));
                sbSpring.AppendLine($"  \"MatchedDrawingNo\": \"{SpringConfigService.EscapeJson(param.MatchedDrawingNo)}\",");
                sbSpring.AppendLine($"  \"MatchedPartNumber\": \"{SpringConfigService.EscapeJson(param.MatchedPartNumber)}\"");
                sbSpring.AppendLine("}");
                File.WriteAllText(Path.Combine(dir, "spring_spec.json"), sbSpring.ToString(), new UTF8Encoding(true));

                // 2. pkg_gap_spec.json
                var sbPkg = new StringBuilder();
                sbPkg.AppendLine("{");
                sbPkg.AppendLine($"  \"PKG\": \"{SpringConfigService.EscapeJson(param.PKG)}\",");
                sbPkg.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"SPR_num\": {0},", param.SPR_num));
                sbPkg.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"LID_gap\": {0}", param.LID_gap));
                sbPkg.AppendLine("}");
                File.WriteAllText(Path.Combine(dir, "pkg_gap_spec.json"), sbPkg.ToString(), new UTF8Encoding(true));

                // 3. elastomer_spec.json
                var sbElastomer = new StringBuilder();
                sbElastomer.AppendLine("{");
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_min\": {0},", param.EF_min));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_nor\": {0},", param.EF_nor));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_max\": {0},", param.EF_max));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_min\": {0},", param.ET_min));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_nor\": {0},", param.ET_nor));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_max\": {0},", param.ET_max));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_min\": {0},", param.PT_min));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_nor\": {0},", param.PT_nor));
                sbElastomer.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_max\": {0}", param.PT_max));
                sbElastomer.AppendLine("}");
                File.WriteAllText(Path.Combine(dir, "elastomer_spec.json"), sbElastomer.ToString(), new UTF8Encoding(true));

                // 4. template_option.json
                var sbTmpl = new StringBuilder();
                sbTmpl.AppendLine("{");
                sbTmpl.AppendLine($"  \"SelectedTemplate\": \"{param.SelectedTemplate}\"");
                sbTmpl.AppendLine("}");
                File.WriteAllText(Path.Combine(dir, "template_option.json"), sbTmpl.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        /// <summary>
        /// AutoCAD 플러그인 DLL 파일 경로 탐색 및 필요 시 AppData로 자동 복사 배포
        /// </summary>
        public static string GetOrDeployPluginDllPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string bundleDir = Path.Combine(appData, "Autodesk", "ApplicationPlugins", "Common_Draw.bundle");
            string bundleContents = Path.Combine(bundleDir, "Contents");
            string installedDll = Path.Combine(bundleContents, "Common_Draw.dll");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, "addin", "Common_Draw.dll"),
                @"C:\ISC_DT_Automation\addin\Common_Draw.dll",
                Path.Combine(baseDir, "Common_Draw.dll"),
                @"C:\ISC_DT_Automation\Common_Draw.dll",
                @"c:\Temp\BOM_Manager\addin\Common_Draw.dll",
                @"c:\Temp\common_draw\bin\Release\net8.0-windows\Common_Draw.dll"
            };

            string sourceDll = string.Empty;
            foreach (var cand in candidatePaths)
            {
                if (File.Exists(cand))
                {
                    sourceDll = cand;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(sourceDll))
            {
                try
                {
                    if (!Directory.Exists(bundleContents)) Directory.CreateDirectory(bundleContents);
                    File.Copy(sourceDll, installedDll, true);

                    string sourcePkg = Path.Combine(Path.GetDirectoryName(sourceDll) ?? "", "PackageContents.xml");
                    if (File.Exists(sourcePkg))
                    {
                        File.Copy(sourcePkg, Path.Combine(bundleDir, "PackageContents.xml"), true);
                    }
                }
                catch { }

                return installedDll;
            }

            if (File.Exists(installedDll)) return installedDll;
            return string.Empty;
        }

        /// <summary>
        /// AutoCAD 실행 여부 확인, 플러그인 동적 NETLOAD 및 도면 자동 생성 명령 전달
        /// </summary>
        public static bool DrawElement(SpringDesignParameters param, out string message)
        {
            message = string.Empty;
            try
            {
                var status = CheckAutoCadStatus();
                if (status == AutoCadStatus.NotRunning)
                {
                    message = "AutoCAD 프로그램이 실행되어 있지 않습니다.\n\n" +
                              "AutoCAD를 먼저 실행하신 후 [도면 해주세요]를 클릭하시면 도면이 자동 생성됩니다.";
                    return false;
                }
                else if (status == AutoCadStatus.Initializing)
                {
                    message = "AutoCAD 프로그램이 현재 로딩(초기화) 중입니다.\n\n" +
                              "AutoCAD 실행이 완료되면 다시 [도면 해주세요]를 클릭해 주세요.";
                    return false;
                }

                // 2. 파라미터 JSON 설정 사전 동기화 저장
                SaveAllParameters(param);

                // 3. 플러그인 DLL 경로 확보 및 배포
                string pluginDllPath = GetOrDeployPluginDllPath();

                // 4. 스크립트(.scr) 백업 생성
                string configDir = SpringConfigService.GetConfigDirectoryPath();
                string scrPath = Path.Combine(configDir, "auto_draw.scr");
                try
                {
                    string safePath = (!string.IsNullOrEmpty(pluginDllPath) ? pluginDllPath : @"C:\ISC_DT_Automation\addin\Common_Draw.dll").Replace("\\", "/");
                    var sbScr = new StringBuilder();
                    sbScr.AppendLine("FILEDIA 0");
                    sbScr.AppendLine($"_.NETLOAD \"{safePath}\"");
                    sbScr.AppendLine("FILEDIA 1");
                    sbScr.AppendLine("AUTODRAW_SPRING");
                    sbScr.AppendLine("");
                    File.WriteAllText(scrPath, sbScr.ToString(), Encoding.ASCII);
                }
                catch { }

                // 5. COM RPC 거부 방지를 위한 OLE Message Filter 등록
                OleMessageFilter.Register();

                try
                {
                    // 6. AutoCAD COM Interop 인스턴스 획득 시도
                    object? acadApp = null;
                    try
                    {
                        acadApp = Marshal.GetActiveObject("AutoCAD.Application");
                    }
                    catch
                    {
                        string[] progIds = { "AutoCAD.Application.25", "AutoCAD.Application.24", "AutoCAD.Application.23", "AutoCAD.Application.22", "AutoCAD.Application" };
                        foreach (var progId in progIds)
                        {
                            try
                            {
                                Type? acadType = Type.GetTypeFromProgID(progId);
                                if (acadType != null)
                                {
                                    acadApp = Activator.CreateInstance(acadType);
                                    if (acadApp != null) break;
                                }
                            }
                            catch { }
                        }
                    }

                    if (acadApp == null)
                    {
                        message = "실행 중인 AutoCAD 인스턴스에 연결할 수 없습니다.\nAutoCAD가 관리자 권한으로 실행 중이거나 초기 로딩 중인지 확인해 주세요.";
                        return false;
                    }

                    // 7. AutoCAD 윈도우 활성화 및 포커스 전환
                    try
                    {
                        acadApp.GetType().InvokeMember("Visible", System.Reflection.BindingFlags.SetProperty, null, acadApp, new object[] { true });
                    }
                    catch { }

                    try
                    {
                        Process[] cadProcesses = Process.GetProcessesByName("acad");
                        if (cadProcesses.Length > 0)
                        {
                            IntPtr hWnd = cadProcesses[0].MainWindowHandle;
                            if (hWnd != IntPtr.Zero)
                            {
                                NativeMethods.SetForegroundWindow(hWnd);
                                if (NativeMethods.IsIconic(hWnd))
                                {
                                    NativeMethods.ShowWindow(hWnd, 9); // SW_RESTORE
                                }
                            }
                        }
                    }
                    catch { }

                    Thread.Sleep(100);

                    // 8. AutoCAD 활성 문서 확인 또는 신규 문서 생성
                    dynamic app = acadApp;
                    dynamic? doc = null;
                    try
                    {
                        doc = app.ActiveDocument;
                    }
                    catch { }

                    if (doc == null)
                    {
                        try
                        {
                            dynamic docs = app.Documents;
                            doc = docs.Add("");
                        }
                        catch (Exception docEx)
                        {
                            message = $"AutoCAD 도면 문서를 활성화할 수 없습니다: {docEx.Message}";
                            return false;
                        }
                    }

                    if (doc == null)
                    {
                        message = "AutoCAD 도면 문서를 활성화할 수 없습니다.";
                        return false;
                    }

                    // 9. LISP 기반 동적 NETLOAD + AUTODRAW_SPRING 일괄 실행
                    string dllSafePath = (!string.IsNullOrEmpty(pluginDllPath) ? pluginDllPath : @"C:\ISC_DT_Automation\addin\Common_Draw.dll").Replace("\\", "/");
                    string lispCommand = $"\\x1B\\x1B(progn (setvar \"SECURELOAD\" 0) (setvar \"FILEDIA\" 0) (command \"_.netload\" \"{dllSafePath}\") (setvar \"FILEDIA\" 1) (if c:AUTODRAW_SPRING (c:AUTODRAW_SPRING) (command \"AUTODRAW_SPRING\")) (princ))\n";

                    bool commandSent = false;
                    string lastError = "";

                    for (int retry = 0; retry < 5; retry++)
                    {
                        try
                        {
                            doc.SendCommand(lispCommand);
                            commandSent = true;
                            break;
                        }
                        catch (Exception cmdEx)
                        {
                            lastError = cmdEx.Message;
                            Thread.Sleep(200);
                        }
                    }

                    // 2차 시도: LISP 실패 시 Script(.scr) 실행
                    if (!commandSent && File.Exists(scrPath))
                    {
                        try
                        {
                            string safeScr = scrPath.Replace("\\", "/");
                            doc.SendCommand($"\x1B\x1B_.SCRIPT \"{safeScr}\"\n");
                            commandSent = true;
                        }
                        catch (Exception scrEx)
                        {
                            lastError = scrEx.Message;
                        }
                    }

                    // 3차 시도: PostCommand 비동기 폴백
                    if (!commandSent)
                    {
                        try
                        {
                            doc.PostCommand(lispCommand);
                            commandSent = true;
                        }
                        catch (Exception postEx)
                        {
                            lastError = postEx.Message;
                        }
                    }

                    if (commandSent)
                    {
                        message = "AutoCAD로 도면 생성 명령이 성공적으로 전송되었습니다.";
                        return true;
                    }
                    else
                    {
                        message = $"AutoCAD 명령 전송 중 오류가 발생했습니다: {lastError}\n\nAutoCAD 명령창에 다른 명령어나 대화상자가 열려있는지 확인해 주세요.";
                        return false;
                    }
                }
                finally
                {
                    OleMessageFilter.Revoke();
                }
            }
            catch (Exception ex)
            {
                message = $"AutoCAD 연동 중 오류가 발생했습니다: {ex.Message}";
                return false;
            }
        }
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
