using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BOMManager.Core
{
    /// <summary>
    /// Vault 패키지를 설치 폴더에 적용하는 C# 자가 업데이트.
    /// PowerShell/robocopy/대상 폴더 전체 재귀를 쓰지 않아 랜섬웨어 행위 진단(Ransom/MDP.Behavior) 오탐을 피합니다.
    /// Autodesk 참조가 없는 별도 타입이라 TEMP에서 실행되는 작업 프로세스도 로드할 수 있습니다.
    /// </summary>
    public static class AppUpdateApplier
    {
        public const string ApplyUpdateArgument = "--apply-update";
        public const string WorkerFileName = "DAP_ApplyUpdate.exe";
        public const string JobFileName = "update_job.txt";
        public const string BackupSuffix = ".dap-update.bak";

        public static bool TryGetJobPathFromArgs(string[] args, out string jobPath)
        {
            jobPath = "";
            if (args == null || args.Length == 0) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], ApplyUpdateArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (i + 1 >= args.Length) return false;
                jobPath = args[i + 1].Trim().Trim('"');
                return !string.IsNullOrWhiteSpace(jobPath);
            }
            return false;
        }

        public static string BuildJobText(int waitPid, string payloadDir, string targetDir, string exeName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WaitPid=" + waitPid);
            sb.AppendLine("PayloadDir=" + payloadDir);
            sb.AppendLine("TargetDir=" + targetDir);
            sb.AppendLine("ExeName=" + exeName);
            return sb.ToString();
        }

        public static string WriteJob(string jobPath, int waitPid, string payloadDir, string targetDir, string exeName)
        {
            string text = BuildJobText(waitPid, payloadDir, targetDir, exeName);
            File.WriteAllText(jobPath, text, new UTF8Encoding(true));
            return jobPath;
        }

        public static bool TryReadJob(string jobPath, out int waitPid, out string payloadDir, out string targetDir, out string exeName)
        {
            waitPid = 0;
            payloadDir = "";
            targetDir = "";
            exeName = "Design_Automation_Portal.exe";
            if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath)) return false;

            foreach (var raw in File.ReadAllLines(jobPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Equals("WaitPid", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(value, out waitPid);
                }
                else if (key.Equals("PayloadDir", StringComparison.OrdinalIgnoreCase))
                {
                    payloadDir = value;
                }
                else if (key.Equals("TargetDir", StringComparison.OrdinalIgnoreCase))
                {
                    targetDir = value;
                }
                else if (key.Equals("ExeName", StringComparison.OrdinalIgnoreCase))
                {
                    exeName = value;
                }
            }

            return !string.IsNullOrWhiteSpace(payloadDir) && !string.IsNullOrWhiteSpace(targetDir);
        }

        public static string ResolvePayloadRoot(string extractedDir, string exeName)
        {
            if (string.IsNullOrWhiteSpace(extractedDir) || !Directory.Exists(extractedDir))
            {
                return extractedDir;
            }

            string safeExe = string.IsNullOrWhiteSpace(exeName) ? "Design_Automation_Portal.exe" : exeName;
            string direct = Path.Combine(extractedDir, safeExe);
            if (File.Exists(direct)) return Path.GetFullPath(extractedDir);

            try
            {
                string? found = Directory.EnumerateFiles(extractedDir, safeExe, SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(found))
                {
                    string? dir = Path.GetDirectoryName(found);
                    if (!string.IsNullOrEmpty(dir)) return Path.GetFullPath(dir);
                }
            }
            catch { }

            return Path.GetFullPath(extractedDir);
        }

        /// <summary>
        /// 패키지(소스)에 있는 파일만 설치 폴더로 복사합니다. 대상 폴더 전체를 훑거나 일괄 읽기전용 해제하지 않습니다.
        /// </summary>
        public static int ApplyPayloadFiles(string payloadRoot, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(payloadRoot) || string.IsNullOrWhiteSpace(targetDir))
            {
                throw new ArgumentException("업데이트 원본/대상 경로가 비어 있습니다.");
            }
            if (!Directory.Exists(payloadRoot))
            {
                throw new DirectoryNotFoundException("업데이트 패키지 폴더를 찾을 수 없습니다: " + payloadRoot);
            }

            string srcRoot = Path.GetFullPath(payloadRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destRoot = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(destRoot))
            {
                Directory.CreateDirectory(destRoot);
            }

            int copied = 0;
            foreach (string src in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(src);
                if (name.Equals(JobFileName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(WorkerFileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = src.Substring(srcRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative)) continue;
                if (relative.IndexOf("..", StringComparison.Ordinal) >= 0) continue;

                string dest = Path.GetFullPath(Path.Combine(destRoot, relative));
                if (!IsPathInside(destRoot, dest)) continue;

                ReplaceOneFile(src, dest);
                copied++;
            }

            return copied;
        }

        public static bool LaunchWorker(string jobPath, string currentExePath)
        {
            if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath)) return false;
            if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath)) return false;

            string jobDir = Path.GetDirectoryName(jobPath) ?? Path.GetTempPath();
            string workerPath = Path.Combine(jobDir, WorkerFileName);
            File.Copy(currentExePath, workerPath, overwrite: true);

            var psi = new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = ApplyUpdateArgument + " \"" + jobPath + "\"",
                WorkingDirectory = jobDir,
                UseShellExecute = false
            };
            Process.Start(psi);
            return true;
        }

        /// <summary>
        /// TEMP 작업 프로세스가 호출합니다. 기존 PID 종료를 기다린 뒤 패키지 파일만 교체하고 앱을 다시 켭니다.
        /// </summary>
        public static void RunApplyUpdateJob(string jobPath)
        {
            try
            {
                Log("Apply-update job: " + jobPath);
                if (!TryReadJob(jobPath, out int waitPid, out string payloadDir, out string targetDir, out string exeName))
                {
                    Log("Invalid update job file.");
                    return;
                }

                WaitForProcessExit(waitPid, TimeSpan.FromSeconds(40));
                Thread.Sleep(400);

                string payloadRoot = ResolvePayloadRoot(payloadDir, exeName);
                Log("Payload root: " + payloadRoot);
                Log("Target dir: " + targetDir);

                int copied = ApplyPayloadFiles(payloadRoot, targetDir);
                Log("Copied files: " + copied);

                string exePath = Path.Combine(targetDir, exeName);
                if (File.Exists(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Log("Relaunched " + exePath);
                }
                else
                {
                    Log("Updated exe not found: " + exePath);
                }
            }
            catch (Exception ex)
            {
                Log("Apply-update failed: " + ex);
            }
        }

        public static bool TryRunFromArgs(string[] args)
        {
            if (!TryGetJobPathFromArgs(args, out string jobPath)) return false;
            RunApplyUpdateJob(jobPath);
            return true;
        }

        private static void WaitForProcessExit(int pid, TimeSpan timeout)
        {
            if (pid <= 0) return;
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.HasExited) return;
                }
                catch (ArgumentException)
                {
                    return;
                }
                catch
                {
                    return;
                }
                Thread.Sleep(200);
            }
        }

        private static void ReplaceOneFile(string src, string dest)
        {
            IOException? last = null;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    string? destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    ClearReadOnlyIfPresent(dest);
                    File.Copy(src, dest, overwrite: true);
                    TryDeleteBackup(dest);
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    try
                    {
                        MoveLockedDestination(dest);
                        ClearReadOnlyIfPresent(dest);
                        File.Copy(src, dest, overwrite: true);
                        TryDeleteBackup(dest);
                        return;
                    }
                    catch (IOException moveEx)
                    {
                        last = moveEx;
                        Thread.Sleep(200);
                    }
                }
            }

            throw last ?? new IOException("파일을 교체하지 못했습니다: " + dest);
        }

        private static void MoveLockedDestination(string dest)
        {
            if (!File.Exists(dest)) return;
            string bak = dest + BackupSuffix;
            ClearReadOnlyIfPresent(bak);
            if (File.Exists(bak)) File.Delete(bak);
            ClearReadOnlyIfPresent(dest);
            File.Move(dest, bak);
        }

        private static void TryDeleteBackup(string dest)
        {
            string bak = dest + BackupSuffix;
            try
            {
                if (File.Exists(bak))
                {
                    ClearReadOnlyIfPresent(bak);
                    File.Delete(bak);
                }
            }
            catch { }
        }

        private static void ClearReadOnlyIfPresent(string path)
        {
            if (!File.Exists(path)) return;
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
        }

        private static bool IsPathInside(string root, string candidate)
        {
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
        }

        private static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BOM_Manager_Update");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "apply_update.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
