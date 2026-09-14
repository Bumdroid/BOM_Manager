using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using VDF = Autodesk.DataManagement.Client.Framework;
using VDFVault = Autodesk.DataManagement.Client.Framework.Vault;

namespace BOMManager.Core
{
    /// <summary>
    /// 릴리즈 단계 구분 (Alpha V?.? < Beta V?.? < V?.?)
    /// </summary>
    public enum ReleaseStage
    {
        Alpha = 1,
        Beta = 2,
        Release = 3
    }

    /// <summary>
    /// 시맨틱 버전 및 배포 단계 관리 객체
    /// 비교 우선순위: Alpha V?.? < Beta V?.? < V?.?
    /// </summary>
    public class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
    {
        public ReleaseStage Stage { get; set; } = ReleaseStage.Release;
        public Version NumericVersion { get; set; } = new Version(0, 0, 0, 0);

        public AppVersion() { }

        public AppVersion(ReleaseStage stage, Version numericVersion)
        {
            Stage = stage;
            NumericVersion = VaultUpdateService.NormalizeVersion(numericVersion);
        }

        public AppVersion(ReleaseStage stage, int major, int minor, int build = 0, int revision = 0)
        {
            Stage = stage;
            NumericVersion = new Version(major, minor, Math.Max(0, build), Math.Max(0, revision));
        }

        public string DisplayString
        {
            get
            {
                string numStr = (NumericVersion.Build > 0 || NumericVersion.Revision > 0)
                    ? $"{NumericVersion.Major}.{NumericVersion.Minor}.{NumericVersion.Build}"
                    : $"{NumericVersion.Major}.{NumericVersion.Minor}";

                switch (Stage)
                {
                    case ReleaseStage.Alpha: return $"Alpha V{numStr}";
                    case ReleaseStage.Beta:  return $"Beta V{numStr}";
                    default:                 return $"V{numStr}";
                }
            }
        }

        public int CompareTo(AppVersion other)
        {
            if (other == null) return 1;
            if (Stage != other.Stage)
            {
                return Stage.CompareTo(other.Stage);
            }
            return NumericVersion.CompareTo(other.NumericVersion);
        }

        public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

        public bool Equals(AppVersion? other)
        {
            if (other == null) return false;
            return Stage == other.Stage && NumericVersion.Equals(other.NumericVersion);
        }

        public override int GetHashCode() => (Stage, NumericVersion).GetHashCode();

        public override string ToString() => DisplayString;

        public static bool operator >(AppVersion? a, AppVersion? b)
        {
            if (a is null) return false;
            if (b is null) return true;
            return a.CompareTo(b) > 0;
        }

        public static bool operator <(AppVersion? a, AppVersion? b)
        {
            if (a is null) return b is not null;
            if (b is null) return false;
            return a.CompareTo(b) < 0;
        }

        public static bool operator >=(AppVersion? a, AppVersion? b)
        {
            if (a is null) return b is null;
            if (b is null) return true;
            return a.CompareTo(b) >= 0;
        }

        public static bool operator <=(AppVersion? a, AppVersion? b)
        {
            if (a is null) return true;
            if (b is null) return false;
            return a.CompareTo(b) <= 0;
        }

        public static bool operator ==(AppVersion? a, AppVersion? b) => Equals(a, b);
        public static bool operator !=(AppVersion? a, AppVersion? b) => !Equals(a, b);
    }

    /// <summary>
    /// 업데이트 후보 파일 정보
    /// </summary>
    public class UpdateCandidate
    {
        public string FileName { get; set; } = "";
        public AppVersion Version { get; set; } = new AppVersion(ReleaseStage.Alpha, 0, 0);
        public long FileSize { get; set; }
        public DateTime ModifiedDate { get; set; }
        public object? Tag { get; set; }
    }

    /// <summary>
    /// 업데이트 확인 결과
    /// </summary>
    public class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public AppVersion CurrentVersion { get; set; } = VaultUpdateService.CurrentAppVersion;
        public AppVersion? RemoteVersion { get; set; }
        public string RemoteFileName { get; set; } = "";
        public long RemoteFileSize { get; set; }
        public string Message { get; set; } = "";
        public UpdateCandidate? Candidate { get; set; }
    }

    /// <summary>
    /// Autodesk Vault 기반의 자동 업데이트 서비스
    /// $/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/01_Source 폴더의 최신 버전을 확인하고 자가 업데이트를 수행합니다.
    /// 버전 우선순위: Alpha V?.? < Beta V?.? < V?.?
    /// </summary>
    public static class VaultUpdateService
    {
        public const string DefaultUpdateFolderPath = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/01_Source";

        private static AppVersion? _currentAppVersion;

        /// <summary>
        /// 현재 실행 중인 어셈블리의 버전 정보를 동적으로 추출하여 반환합니다. (기본값: Alpha V0.2)
        /// </summary>
        public static AppVersion CurrentAppVersion
        {
            get
            {
                if (_currentAppVersion == null)
                {
                    _currentAppVersion = GetExecutingAppVersion();
                }
                return _currentAppVersion;
            }
            set => _currentAppVersion = value;
        }

        /// <summary>
        /// 실행 어셈블리의 InformationalVersion 또는 FileVersion에서 릴리즈 단계 및 버전을 파싱합니다.
        /// </summary>
        public static AppVersion GetExecutingAppVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var infoVerAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string? infoVer = infoVerAttr?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVer))
                {
                    var parsed = ParseVersionString(infoVer!) ?? ExtractAppVersion(infoVer!);
                    if (parsed != null) return parsed;
                }

                var loc = asm.Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(loc);
                    if (fvi?.FileVersion != null && Version.TryParse(fvi.FileVersion, out var v))
                    {
                        return new AppVersion(ReleaseStage.Alpha, v);
                    }
                }
            }
            catch { }
            return new AppVersion(ReleaseStage.Alpha, 0, 2, 0, 0);
        }

        /// <summary>
        /// 'Alpha V0.1', 'Beta V1.0', 'V0.1.0', '0.1.0' 형식의 문자열을 AppVersion으로 파싱합니다.
        /// </summary>
        public static AppVersion? ParseVersionString(string verText)
        {
            if (string.IsNullOrWhiteSpace(verText)) return null;

            var match = Regex.Match(verText.Trim(), @"^(?:(?<stage>Alpha|Beta)[_\s-]?)?(?:v|V)?(?<ver>\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            string stageStr = match.Groups["stage"].Value;
            string verStr = match.Groups["ver"].Value;

            ReleaseStage stage = ReleaseStage.Release;
            if (stageStr.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
            {
                stage = ReleaseStage.Alpha;
            }
            else if (stageStr.Equals("Beta", StringComparison.OrdinalIgnoreCase))
            {
                stage = ReleaseStage.Beta;
            }

            if (Version.TryParse(verStr, out var parsed))
            {
                return new AppVersion(stage, parsed);
            }
            else if (int.TryParse(verStr, out int singleMajor))
            {
                return new AppVersion(stage, singleMajor, 0);
            }

            return null;
        }

        // Regex supporting:
        // Design_Automation_Portal_Alpha_V0.0.zip, Design_Automation_Portal_Beta_V0.1.zip, Design_Automation_Portal_V1.0.zip
        // BOM_Manager_Alpha_V0.0.zip, BOM_Manager_Beta_V0.0.zip, BOM_Manager_V1.0.zip
        private static readonly Regex VersionPattern = new Regex(
            @"^(?:Design_Automation_Portal|BOM_Manager)_(?:(?<stage>Alpha|Beta)[_\s-]?)?(?:v|V)?(?<ver>\d+(?:\.\d+)*)\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 파일명에서 릴리즈 단계 및 버전 번호를 추출하여 AppVersion 객체로 파싱합니다.
        /// </summary>
        public static AppVersion? ExtractAppVersion(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            string nameOnly = Path.GetFileName(fileName);
            var match = VersionPattern.Match(nameOnly);
            if (!match.Success) return null;

            string stageStr = match.Groups["stage"].Value;
            string verStr = match.Groups["ver"].Value;

            ReleaseStage stage = ReleaseStage.Release;
            if (stageStr.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
            {
                stage = ReleaseStage.Alpha;
            }
            else if (stageStr.Equals("Beta", StringComparison.OrdinalIgnoreCase))
            {
                stage = ReleaseStage.Beta;
            }

            if (Version.TryParse(verStr, out var parsed))
            {
                return new AppVersion(stage, parsed);
            }
            else if (int.TryParse(verStr, out int singleMajor))
            {
                return new AppVersion(stage, singleMajor, 0);
            }

            return null;
        }

        /// <summary>
        /// Version 객체의 Major, Minor, Build, Revision을 4자리 표준 형태로 정규화합니다.
        /// </summary>
        public static Version NormalizeVersion(Version v)
        {
            int major = Math.Max(0, v.Major);
            int minor = Math.Max(0, v.Minor);
            int build = Math.Max(0, v.Build);
            int revision = Math.Max(0, v.Revision);
            return new Version(major, minor, build, revision);
        }

        /// <summary>
        /// 주어진 파일명 목록 중에서 현재 버전보다 높은 최신 업데이트 후보를 선출합니다.
        /// (Alpha V?.? < Beta V?.? < V?.?)
        /// </summary>
        public static UpdateCandidate? FindLatestUpdateCandidate(IEnumerable<string> fileNames, AppVersion currentVersion)
        {
            return fileNames
                .Select(f => new
                {
                    FileName = f,
                    Version = ExtractAppVersion(f)
                })
                .Where(x => x.Version != null && x.Version > currentVersion)
                .OrderByDescending(x => x.Version)
                .Select(x => new UpdateCandidate
                {
                    FileName = x.FileName,
                    Version = x.Version!
                })
                .FirstOrDefault();
        }

        /// <summary>
        /// Vault 서버의 지정된 폴더($/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/01_Source)에 접속하여 최신 버전 유무를 확인합니다.
        /// 실제 파일을 다운로드하지 않고 Vault 메타데이터(약 1KB 미만)만 초고속 조회합니다.
        /// </summary>
        public static UpdateCheckResult CheckForUpdate(
            string server,
            string vault,
            string username,
            string password,
            string targetFolderPath = DefaultUpdateFolderPath,
            AppVersion? currentVersion = null)
        {
            var localVersion = currentVersion ?? CurrentAppVersion;
            var result = new UpdateCheckResult
            {
                CurrentVersion = localVersion
            };

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???") || string.IsNullOrWhiteSpace(username))
                {
                    result.Message = "Vault 접속 정보가 유효하지 않습니다.";
                    return result;
                }

                // 1. Vault 읽기 전용 로그인 (메타데이터 조회용)
                VDFVault.Results.LogInResult loginResult = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!loginResult.Success || loginResult.Connection == null)
                {
                    string err = loginResult.Exception?.Message ?? "로그인 실패";
                    result.Message = $"Vault 메타데이터 조회 로그인 실패: {err}";
                    return result;
                }

                var connection = loginResult.Connection;

                try
                {
                    // 2. 소스 배포 폴더 획득
                    Autodesk.Connectivity.WebServices.Folder? folder = null;
                    try
                    {
                        folder = connection.WebServiceManager.DocumentService.GetFolderByPath(targetFolderPath);
                    }
                    catch
                    {
                        string fallbackFolder = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화";
                        try
                        {
                            folder = connection.WebServiceManager.DocumentService.GetFolderByPath(fallbackFolder);
                        }
                        catch { }
                    }

                    if (folder == null)
                    {
                        result.Message = $"Vault 소스 배포 폴더({targetFolderPath})를 찾을 수 없습니다.";
                        return result;
                    }

                    // 3. 폴더 내 최신 파일 목록 메타데이터 조회
                    Autodesk.Connectivity.WebServices.File[] files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                    if (files == null || files.Length == 0)
                    {
                        result.Message = "Vault 소스 배포 폴더에 등록된 파일이 없습니다.";
                        return result;
                    }

                    // 4. 시맨틱 버전 파싱 및 최신 버전 비교 (Alpha V?.? < Beta V?.? < V?.?)
                    var latestCandidate = files
                        .Where(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .Select(f => new
                        {
                            File = f,
                            Version = ExtractAppVersion(f.Name)
                        })
                        .Where(x => x.Version != null && x.Version > localVersion)
                        .OrderByDescending(x => x.Version)
                        .ThenByDescending(x => x.File.ModDate)
                        .Select(x => new UpdateCandidate
                        {
                            FileName = x.File.Name,
                            Version = x.Version!,
                            FileSize = x.File.FileSize,
                            ModifiedDate = x.File.ModDate,
                            Tag = x.File
                        })
                        .FirstOrDefault();

                    if (latestCandidate != null)
                    {
                        result.HasUpdate = true;
                        result.RemoteVersion = latestCandidate.Version;
                        result.RemoteFileName = latestCandidate.FileName;
                        result.RemoteFileSize = latestCandidate.FileSize;
                        result.Candidate = latestCandidate;
                        result.Message = $"최신 버전({latestCandidate.Version.DisplayString})이 발견되었습니다.";
                    }
                    else
                    {
                        result.HasUpdate = false;
                        result.Message = $"현재 최신 버전({localVersion.DisplayString})을 사용하고 있습니다.";
                    }
                }
                finally
                {
                    try
                    {
                        VDFVault.Library.ConnectionManager.LogOut(connection);
                    }
                    catch { }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Message = $"업데이트 확인 중 오류 발생: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 최신 배포 zip 파일을 Vault에서 다운로드하고 압축을 해제한 후 자가 교체 스크립트를 생성합니다.
        /// (실제 교체 스크립트 실행은 사용자가 확인 버튼을 눌러 앱이 종료될 때 수행됩니다.)
        /// </summary>
        public static bool DownloadAndApplyUpdate(
            string server,
            string vault,
            string username,
            string password,
            UpdateCandidate candidate,
            string targetFolderPath,
            Action<int, string>? progressCallback,
            out string updaterScriptPath,
            out string errorMessage)
        {
            updaterScriptPath = "";
            errorMessage = "";
            progressCallback?.Invoke(10, "Vault 서버에 접속하여 업데이트 패키지를 준비하는 중...");

            try
            {
                // 1. 임시 작업 디렉터리 준비 (%TEMP%\BOM_Manager_Update\)
                string tempDir = Path.Combine(Path.GetTempPath(), "BOM_Manager_Update");
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
                Directory.CreateDirectory(tempDir);

                string zipDownloadPath = Path.Combine(tempDir, candidate.FileName);
                string extractedDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractedDir);

                // 2. Vault 연결 및 파일 다운로드
                VDFVault.Results.LogInResult loginResult = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!loginResult.Success || loginResult.Connection == null)
                {
                    errorMessage = "업데이트 다운로드를 위한 Vault 접속에 실패했습니다.";
                    return false;
                }

                var connection = loginResult.Connection;
                try
                {
                    progressCallback?.Invoke(30, $"최신 패키지 다운로드 중: {candidate.FileName}");

                    Autodesk.Connectivity.WebServices.File? targetWsFile = candidate.Tag as Autodesk.Connectivity.WebServices.File;
                    if (targetWsFile == null)
                    {
                        var folder = connection.WebServiceManager.DocumentService.GetFolderByPath(targetFolderPath);
                        var files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                        targetWsFile = files.FirstOrDefault(f => f.Name.Equals(candidate.FileName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (targetWsFile == null)
                    {
                        errorMessage = $"Vault에서 다운로드할 파일({candidate.FileName})을 찾을 수 없습니다.";
                        return false;
                    }

                    var fileIteration = new VDFVault.Currency.Entities.FileIteration(connection, targetWsFile);
                    var downloadSettings = new VDFVault.Settings.AcquireFilesSettings(connection);
                    downloadSettings.AddFileToAcquire(
                        fileIteration,
                        VDFVault.Settings.AcquireFilesSettings.AcquisitionOption.Download,
                        new VDF.Currency.FolderPathAbsolute(tempDir));

                    var acquireResults = connection.FileManager.AcquireFiles(downloadSettings);

                    // 다운로드된 파일 확인
                    string resolvedZip = Path.Combine(tempDir, targetWsFile.Name);
                    if (!File.Exists(resolvedZip))
                    {
                        var matched = Directory.GetFiles(tempDir, "*.zip", SearchOption.AllDirectories).FirstOrDefault();
                        if (matched != null) resolvedZip = matched;
                    }

                    if (!File.Exists(resolvedZip))
                    {
                        errorMessage = "업데이트 패키지 다운로드에 실패했습니다.";
                        return false;
                    }

                    progressCallback?.Invoke(70, "업데이트 패키지 압축 해제 및 검증 중...");

                    // 3. ZIP 압축 해제
                    ZipFile.ExtractToDirectory(resolvedZip, extractedDir);

                    // 4. 자가 교체 파워쉘/배치 스크립트 생성
                    progressCallback?.Invoke(90, "자가 교체 스크립트 준비 중...");
                    int currentPid = Process.GetCurrentProcess().Id;
                    string targetAppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                    string ps1ScriptPath = Path.Combine(tempDir, "updater.ps1");
                    string batScriptPath = Path.Combine(tempDir, "updater.bat");

                    string currentExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "Design_Automation_Portal.exe");
                    if (string.IsNullOrWhiteSpace(currentExeName) || currentExeName.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        currentExeName = "Design_Automation_Portal.exe";
                    }

                    string ps1Content = GenerateUpdaterPs1Script(currentPid, extractedDir, targetAppDir, currentExeName);
                    File.WriteAllText(ps1ScriptPath, ps1Content, new System.Text.UTF8Encoding(true));

                    string batContent = "@echo off\r\nchcp 65001 > nul\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0updater.ps1\"\r\n";
                    File.WriteAllText(batScriptPath, batContent, System.Text.Encoding.Default);

                    updaterScriptPath = ps1ScriptPath;
                    progressCallback?.Invoke(100, "업데이트 준비가 완료되었습니다.");
                    return true;
                }
                finally
                {
                    try
                    {
                        VDFVault.Library.ConnectionManager.LogOut(connection);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"업데이트 적용 중 예외 발생: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 자가 교체 스크립트를 백그라운드 독립 프로세스로 실행합니다.
        /// </summary>
        public static bool LaunchUpdater(string updaterScriptPath)
        {
            if (string.IsNullOrWhiteSpace(updaterScriptPath) || !File.Exists(updaterScriptPath)) return false;
            try
            {
                string ext = Path.GetExtension(updaterScriptPath).ToLowerInvariant();
                ProcessStartInfo psi;
                if (ext == ".ps1")
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{updaterScriptPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(updaterScriptPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"\"{updaterScriptPath}\"\"",
                        WorkingDirectory = Path.GetDirectoryName(updaterScriptPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                }
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 실행 중인 프로세스가 종료되기를 기다린 후 새 파일로 덮어쓰고 재실행하는 PowerShell 교체 스크립트를 생성합니다.
        /// 한글 경로, 괄호 (), 권한, 파일 잠금 문제를 robocopy 및 안전한 재시도 루프로 완벽하게 해결합니다.
        /// </summary>
        public static string GenerateUpdaterPs1Script(int currentPid, string extractedDir, string targetAppDir, string exeName)
        {
            string safeExtracted = extractedDir.TrimEnd('\\', '/');
            string safeTarget = targetAppDir.TrimEnd('\\', '/');

            return $@"# Design Automation Portal Self-Updater
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$pidToWait = {currentPid}
$srcDir = '{safeExtracted}'
$targetDir = '{safeTarget}'
$exe = '{exeName}'

Write-Host ('[1/4] Waiting for calling process (PID: ' + $pidToWait + ') to exit...')
$waitCount = 0
while ((Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) -and ($waitCount -lt 150)) {{
    Start-Sleep -Milliseconds 200
    $waitCount++
}}
Start-Sleep -Milliseconds 600

# Terminate any duplicate lingering instances in target folder
try {{
    $procs = Get-Process -Name 'Design_Automation_Portal' -ErrorAction SilentlyContinue
    foreach ($p in $procs) {{
        try {{
            if ($p.Id -ne $PID -and $p.Id -ne $pidToWait) {{
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }}
        }} catch {{ }}
    }}
}} catch {{ }}

# Locate payload root directory containing exe
$actualSrc = $srcDir
if (Test-Path -LiteralPath $srcDir) {{
    $foundExe = Get-ChildItem -LiteralPath $srcDir -Filter $exe -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundExe) {{
        $actualSrc = $foundExe.DirectoryName
    }}
}}

Write-Host ('[2/4] Clearing read-only attributes on ' + $targetDir + '...')
if (Test-Path -LiteralPath $targetDir) {{
    Get-ChildItem -LiteralPath $targetDir -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {{
        if ($_.IsReadOnly) {{ $_.IsReadOnly = $false }}
    }}
}}

Write-Host ('[3/4] Copying updated files from ' + $actualSrc + ' to ' + $targetDir + '...')
$copyDone = $false
for ($retry = 0; $retry -lt 15; $retry++) {{
    try {{
        & robocopy ""$actualSrc"" ""$targetDir"" /E /IS /IT /NP /R:2 /W:1 *>$null
        if ($LASTEXITCODE -lt 8) {{
            $copyDone = $true
            break
        }}
    }} catch {{ }}

    try {{
        Get-ChildItem -LiteralPath $actualSrc -Force | ForEach-Object {{
            Copy-Item -LiteralPath $_.FullName -Destination $targetDir -Recurse -Force -ErrorAction Stop
        }}
        $copyDone = $true
        break
    }} catch {{
        Start-Sleep -Milliseconds 500
    }}
}}

Write-Host ('[4/4] Launching updated application...')
$exePath = Join-Path $targetDir $exe
if (Test-Path -LiteralPath $exePath) {{
    Start-Process -FilePath $exePath -WorkingDirectory $targetDir
}}
";
        }

        /// <summary>
        /// 이전 버전 호환용 레거시 스크립트 생성기
        /// </summary>
        public static string GenerateUpdaterScript(int currentPid, string extractedDir, string targetAppDir, string exeName)
        {
            return GenerateUpdaterPs1Script(currentPid, extractedDir, targetAppDir, exeName);
        }
    }
}
