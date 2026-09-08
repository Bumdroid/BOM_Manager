using System;
using System.IO;
using System.Linq;
using VDF = Autodesk.DataManagement.Client.Framework;
using VDFVault = Autodesk.DataManagement.Client.Framework.Vault;

namespace BOMManager.Core
{
    public class VaultService
    {
        public static string CurrentUsername { get; private set; } = "";
        public static string CurrentServer { get; private set; } = VaultConfigManager.DefaultServer;
        public static string CurrentVault { get; private set; } = VaultConfigManager.DefaultVaultName;
        public static bool IsLoggedIn { get; private set; } = false;

        public static void SetLoggedInUser(string username, string server, string vault)
        {
            CurrentUsername = username;
            CurrentServer = server;
            CurrentVault = vault;
            IsLoggedIn = true;
        }

        /// <summary>
        /// Vault 서버에 입력된 정보로 로그인을 시도하여 접속 가능 여부를 확인합니다.
        /// </summary>
        public static bool TestLogIn(string server, string vault, string username, string password, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    errorMessage = "유저 이름을 입력하세요.";
                    return false;
                }

                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!results.Success)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 로그인 실패. {detail}".Trim();
                    return false;
                }

                try
                {
                    if (results.Connection != null)
                    {
                        VDFVault.Library.ConnectionManager.LogOut(results.Connection);
                    }
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Vault 서버에 실시간으로 접속하여 지정된 폴더에서 최신 CSV 공용화 DB 파일을 라이브로 다운로드합니다.
        /// </summary>
        public static bool DownloadLatestCsvFromVault(
            string server,
            string vault,
            string username,
            string password,
            string targetFolderPath,
            string localCacheDir,
            out string downloadedFilePath,
            out string errorMessage)
        {
            downloadedFilePath = "";
            errorMessage = "";

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                // VDF Vault ConnectionManager를 통한 Vault 실시간 로그인
                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!results.Success)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 라이브 접속 실패. {detail}".Trim();
                    return false;
                }

                VDFVault.Currency.Connections.Connection connection = results.Connection;

                // 1. WebServiceManager DocumentService를 이용해 Vault 폴더 및 파일 정보 획득
                Autodesk.Connectivity.WebServices.Folder? folder = null;
                try
                {
                    folder = connection.WebServiceManager.DocumentService.GetFolderByPath(targetFolderPath);
                }
                catch
                {
                    string fallbackFolder = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
                    try
                    {
                        folder = connection.WebServiceManager.DocumentService.GetFolderByPath(fallbackFolder);
                    }
                    catch { }
                }

                if (folder == null)
                {
                    errorMessage = $"Vault 지정 폴더 경로({targetFolderPath})를 찾을 수 없습니다.";
                    return false;
                }

                // 2. 폴더 내 최신 파일 목록 가져오기
                Autodesk.Connectivity.WebServices.File[] files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                if (files == null || files.Length == 0)
                {
                    errorMessage = "Vault 지정 폴더에 등록된 파일이 없습니다.";
                    return false;
                }

                // CSV 파일 중 가장 최근 수정된 파일 선택
                var targetWsFile = files
                    .Where(f => f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.ModDate)
                    .FirstOrDefault();

                if (targetWsFile == null)
                {
                    errorMessage = "Vault 폴더에서 CSV 파일(.csv)을 찾지 못했습니다.";
                    return false;
                }

                if (!Directory.Exists(localCacheDir))
                {
                    Directory.CreateDirectory(localCacheDir);
                }

                // 3. WebServices File을 VDF FileIteration으로 전환 후 VDF AcquireFiles API로 다운로드
                var fileIteration = new VDFVault.Currency.Entities.FileIteration(connection, targetWsFile);

                var downloadSettings = new VDFVault.Settings.AcquireFilesSettings(connection);
                downloadSettings.AddFileToAcquire(
                    fileIteration,
                    VDFVault.Settings.AcquireFilesSettings.AcquisitionOption.Download,
                    new VDF.Currency.FolderPathAbsolute(localCacheDir));

                var acquireResults = connection.FileManager.AcquireFiles(downloadSettings);

                string finalFilePath = ResolveDownloadedFilePath(acquireResults, localCacheDir, targetWsFile.Name);
                if (!string.IsNullOrEmpty(finalFilePath) && File.Exists(finalFilePath))
                {
                    downloadedFilePath = finalFilePath;
                    return true;
                }

                errorMessage = "Vault CSV 파일 다운로드 실패.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Vault 서버의 공용화 DB 지정 폴더에서 가장 최신의 DWG 파일(.dwg)을 라이브로 다운로드합니다.
        /// </summary>
        public static bool DownloadLatestDwgFromVault(
            string server,
            string vault,
            string username,
            string password,
            string targetFolderPath,
            string localCacheDir,
            out string downloadedFilePath,
            out string errorMessage)
        {
            downloadedFilePath = "";
            errorMessage = "";

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!results.Success)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 접속 실패. {detail}".Trim();
                    return false;
                }

                VDFVault.Currency.Connections.Connection connection = results.Connection;

                Autodesk.Connectivity.WebServices.Folder? folder = null;
                try
                {
                    folder = connection.WebServiceManager.DocumentService.GetFolderByPath(targetFolderPath);
                }
                catch
                {
                    string fallbackFolder = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
                    try
                    {
                        folder = connection.WebServiceManager.DocumentService.GetFolderByPath(fallbackFolder);
                    }
                    catch { }
                }

                if (folder == null)
                {
                    errorMessage = $"Vault 지정 폴더({targetFolderPath})를 찾을 수 없습니다.";
                    return false;
                }

                Autodesk.Connectivity.WebServices.File[] files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                if (files == null || files.Length == 0)
                {
                    errorMessage = "Vault 폴더에 파일이 없습니다.";
                    return false;
                }

                var targetWsFile = files
                    .Where(f => f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.ModDate)
                    .FirstOrDefault();

                if (targetWsFile == null)
                {
                    errorMessage = "Vault 폴더에서 DWG 파일(.dwg)을 찾지 못했습니다.";
                    return false;
                }

                if (!Directory.Exists(localCacheDir))
                {
                    Directory.CreateDirectory(localCacheDir);
                }

                var fileIteration = new VDFVault.Currency.Entities.FileIteration(connection, targetWsFile);
                var downloadSettings = new VDFVault.Settings.AcquireFilesSettings(connection);
                downloadSettings.AddFileToAcquire(
                    fileIteration,
                    VDFVault.Settings.AcquireFilesSettings.AcquisitionOption.Download,
                    new VDF.Currency.FolderPathAbsolute(localCacheDir));

                var acquireResults = connection.FileManager.AcquireFiles(downloadSettings);

                string finalFilePath = ResolveDownloadedFilePath(acquireResults, localCacheDir, targetWsFile.Name);
                if (!string.IsNullOrEmpty(finalFilePath) && File.Exists(finalFilePath))
                {
                    downloadedFilePath = finalFilePath;
                    return true;
                }

                errorMessage = "Vault DWG 파일 다운로드 실패.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Vault 서버의 지정된 템플릿 폴더 ($/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/00_기본) 에서 지정한 DWG 템플릿 파일(.dwg)을 라이브로 다운로드합니다.
        /// </summary>
        public static bool DownloadTemplateFromVault(
            string server,
            string vault,
            string username,
            string password,
            string templateFileName,
            string localCacheDir,
            out string downloadedFilePath,
            out string errorMessage)
        {
            downloadedFilePath = "";
            errorMessage = "";

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!results.Success)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 접속 실패. {detail}".Trim();
                    return false;
                }

                VDFVault.Currency.Connections.Connection connection = results.Connection;
                string templateVaultFolderPath = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/00_기본";

                Autodesk.Connectivity.WebServices.Folder? folder = null;
                try
                {
                    folder = connection.WebServiceManager.DocumentService.GetFolderByPath(templateVaultFolderPath);
                }
                catch { }

                if (folder == null)
                {
                    errorMessage = $"Vault 템플릿 폴더({templateVaultFolderPath})를 찾을 수 없습니다.";
                    return false;
                }

                Autodesk.Connectivity.WebServices.File[] files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                if (files == null || files.Length == 0)
                {
                    errorMessage = "Vault 템플릿 폴더에 파일이 없습니다.";
                    return false;
                }

                var targetWsFile = files
                    .Where(f => f.Name.Equals(templateFileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.ModDate)
                    .FirstOrDefault();

                if (targetWsFile == null)
                {
                    targetWsFile = files
                        .Where(f => f.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.ModDate)
                        .FirstOrDefault();
                }

                if (targetWsFile == null)
                {
                    errorMessage = $"Vault 템플릿 폴더에서 {templateFileName} 파일(.dwg)을 찾지 못했습니다.";
                    return false;
                }

                if (!Directory.Exists(localCacheDir))
                {
                    Directory.CreateDirectory(localCacheDir);
                }

                var fileIteration = new VDFVault.Currency.Entities.FileIteration(connection, targetWsFile);
                var downloadSettings = new VDFVault.Settings.AcquireFilesSettings(connection);
                downloadSettings.AddFileToAcquire(
                    fileIteration,
                    VDFVault.Settings.AcquireFilesSettings.AcquisitionOption.Download,
                    new VDF.Currency.FolderPathAbsolute(localCacheDir));

                var acquireResults = connection.FileManager.AcquireFiles(downloadSettings);

                string finalFilePath = ResolveDownloadedFilePath(acquireResults, localCacheDir, targetWsFile.Name);
                if (!string.IsNullOrEmpty(finalFilePath) && File.Exists(finalFilePath))
                {
                    downloadedFilePath = finalFilePath;
                    return true;
                }

                errorMessage = "Vault 템플릿 파일 다운로드 실패.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Vault 서버의 지정된 폴더에서 특정 파일명(예: Schematic_Spring.dwg)을 실시간으로 다운로드합니다.
        /// </summary>
        public static bool DownloadSpecificFileFromVault(
            string server,
            string vault,
            string username,
            string password,
            string targetFolderPath,
            string fileName,
            string localCacheDir,
            out string downloadedFilePath,
            out string errorMessage)
        {
            downloadedFilePath = "";
            errorMessage = "";

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.ReadOnly,
                    null);

                if (!results.Success)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 접속 실패. {detail}".Trim();
                    return false;
                }

                VDFVault.Currency.Connections.Connection connection = results.Connection;

                Autodesk.Connectivity.WebServices.Folder? folder = null;
                try
                {
                    folder = connection.WebServiceManager.DocumentService.GetFolderByPath(targetFolderPath);
                }
                catch
                {
                    string fallbackFolder = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
                    try
                    {
                        folder = connection.WebServiceManager.DocumentService.GetFolderByPath(fallbackFolder);
                    }
                    catch { }
                }

                if (folder == null)
                {
                    errorMessage = $"Vault 지정 폴더({targetFolderPath})를 찾을 수 없습니다.";
                    return false;
                }

                Autodesk.Connectivity.WebServices.File[] files = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                if (files == null || files.Length == 0)
                {
                    errorMessage = "Vault 폴더에 파일이 없습니다.";
                    return false;
                }

                var targetWsFile = files
                    .Where(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.ModDate)
                    .FirstOrDefault();

                if (targetWsFile == null)
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    targetWsFile = files
                        .Where(f => f.Name.IndexOf(nameNoExt, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(f => f.ModDate)
                        .FirstOrDefault();
                }

                if (targetWsFile == null)
                {
                    errorMessage = $"Vault 폴더에서 {fileName} 파일을 찾지 못했습니다.";
                    return false;
                }

                if (!Directory.Exists(localCacheDir))
                {
                    Directory.CreateDirectory(localCacheDir);
                }

                var fileIteration = new VDFVault.Currency.Entities.FileIteration(connection, targetWsFile);
                var downloadSettings = new VDFVault.Settings.AcquireFilesSettings(connection);
                downloadSettings.AddFileToAcquire(
                    fileIteration,
                    VDFVault.Settings.AcquireFilesSettings.AcquisitionOption.Download,
                    new VDF.Currency.FolderPathAbsolute(localCacheDir));

                var acquireResults = connection.FileManager.AcquireFiles(downloadSettings);

                string finalFilePath = ResolveDownloadedFilePath(acquireResults, localCacheDir, targetWsFile.Name);
                if (!string.IsNullOrEmpty(finalFilePath) && File.Exists(finalFilePath))
                {
                    try { File.SetLastWriteTimeUtc(finalFilePath, targetWsFile.ModDate); } catch { }
                    downloadedFilePath = finalFilePath;
                    return true;
                }

                errorMessage = "Vault 파일 다운로드 실패.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string ResolveDownloadedFilePath(
            VDFVault.Results.AcquireFilesResults acquireResults,
            string localCacheDir,
            string fileName)
        {
            if (acquireResults != null && acquireResults.FileResults != null)
            {
                foreach (var fileResult in acquireResults.FileResults)
                {
                    if (fileResult != null && fileResult.LocalPath != null)
                    {
                        string path = fileResult.LocalPath.ToString();
                        if (File.Exists(path))
                        {
                            string rootPath = Path.Combine(localCacheDir, Path.GetFileName(path));
                            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Copy(path, rootPath, true); } catch { }
                                return rootPath;
                            }
                            return path;
                        }
                    }
                }
            }

            string directPath = Path.Combine(localCacheDir, fileName);
            if (File.Exists(directPath)) return directPath;

            try
            {
                if (Directory.Exists(localCacheDir))
                {
                    var matchedFiles = Directory.GetFiles(localCacheDir, fileName, SearchOption.AllDirectories);
                    if (matchedFiles.Length > 0)
                    {
                        string found = matchedFiles[0];
                        if (!string.Equals(found, directPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Copy(found, directPath, true); } catch { }
                            return directPath;
                        }
                        return found;
                    }

                    var allFiles = Directory.GetFiles(localCacheDir, "*.*", SearchOption.AllDirectories);
                    var matched = allFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (matched != null)
                    {
                        if (!string.Equals(matched, directPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Copy(matched, directPath, true); } catch { }
                            return directPath;
                        }
                        return matched;
                    }
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// 버그신고 / 기능제안 피드백 텍스트 내용을 로컬 작업 폴더 및 임시 파일 생성 없이 Vault 지정 폴더로 직접 메모리 스트림 업로드합니다.
        /// </summary>
        public static bool UploadFeedbackTextToVault(
            string server,
            string vault,
            string username,
            string password,
            string txtFileName,
            string contentText,
            out string uploadedVaultFileName,
            out string errorMessage)
        {
            byte[] fileBytes = System.Text.Encoding.UTF8.GetBytes(contentText ?? "");
            return UploadFeedbackBytesToVault(server, vault, username, password, txtFileName, fileBytes, out uploadedVaultFileName, out errorMessage);
        }

        /// <summary>
        /// 버그신고 / 기능제안 피드백 텍스트 파일을 로컬 작업 폴더 생성 없이 Vault 지정 폴더로 라이브 업로드합니다.
        /// </summary>
        public static bool UploadFeedbackFileToVault(
            string server,
            string vault,
            string username,
            string password,
            string localFilePath,
            out string uploadedVaultFileName,
            out string errorMessage)
        {
            uploadedVaultFileName = Path.GetFileName(localFilePath);
            errorMessage = "";
            try
            {
                if (!File.Exists(localFilePath))
                {
                    errorMessage = "업로드할 로컬 파일을 찾을 수 없습니다.";
                    return false;
                }
                byte[] fileBytes = File.ReadAllBytes(localFilePath);
                string fileName = Path.GetFileName(localFilePath);
                return UploadFeedbackBytesToVault(server, vault, username, password, fileName, fileBytes, out uploadedVaultFileName, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 바이트 배열 데이터를 로컬 작업 폴더 및 임시 파일 없이 Vault 지정 폴더로 VDF 메모리 스트림 직접 업로드합니다.
        /// </summary>
        public static bool UploadFeedbackBytesToVault(
            string server,
            string vault,
            string username,
            string password,
            string fileName,
            byte[] fileBytes,
            out string uploadedVaultFileName,
            out string errorMessage)
        {
            uploadedVaultFileName = fileName;
            errorMessage = "";

            try
            {
                if (string.IsNullOrWhiteSpace(server) || server.Contains("???"))
                {
                    errorMessage = "Vault 서버 IP 정보가 유효하지 않습니다.";
                    return false;
                }

                if (fileBytes == null || fileBytes.Length == 0)
                {
                    errorMessage = "업로드할 데이터 내용이 비어있습니다.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    username = !string.IsNullOrWhiteSpace(CurrentUsername) ? CurrentUsername : "이두규";
                }

                // 1. Vault 로그인 시도: 업로드는 파일 쓰기(AddFile) 작업이므로 Standard 인증 사용
                VDFVault.Results.LogInResult results = VDFVault.Library.ConnectionManager.LogIn(
                    server,
                    vault,
                    username,
                    password ?? "",
                    VDFVault.Currency.Connections.AuthenticationFlags.Standard,
                    null);

                if (!results.Success || results.Connection == null)
                {
                    string detail = "";
                    try { if (results.Exception != null) detail = results.Exception.Message; } catch { }
                    errorMessage = $"Vault 업로드 로그인 실패 (User: {username}). {detail}".Trim();
                    return false;
                }

                VDFVault.Currency.Connections.Connection connection = results.Connection;
                try
                {
                    string targetFolderPath = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/50_VoC";

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
                        errorMessage = $"Vault 지정 폴더 경로({targetFolderPath})를 찾을 수 없습니다.";
                        return false;
                    }

                    // 1. Vault 폴더 내 중복 파일명 체크 및 (1), (2) 번호 부여
                    Autodesk.Connectivity.WebServices.File[]? existingFiles = null;
                    try
                    {
                        existingFiles = connection.WebServiceManager.DocumentService.GetLatestFilesByFolderId(folder.Id, true);
                    }
                    catch { }

                    string baseName = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".txt";

                    var existingNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (existingFiles != null)
                    {
                        foreach (var f in existingFiles)
                        {
                            if (f != null && !string.IsNullOrEmpty(f.Name))
                            {
                                existingNames.Add(f.Name);
                            }
                        }
                    }

                    string targetVaultFileName = fileName;
                    int counter = 1;
                    while (existingNames.Contains(targetVaultFileName))
                    {
                        targetVaultFileName = $"{baseName}({counter}){ext}";
                        counter++;
                    }

                    var folderEntity = new VDFVault.Currency.Entities.Folder(connection, folder);

                    // 2. Vault VDF Stream AddFile 메모리 직접 업로드 (로컬 파일 및 작업 폴더 사용 안 함)
                    bool uploadSuccess = false;
                    int retryCount = 0;
                    while (!uploadSuccess && retryCount < 10)
                    {
                        try
                        {
                            using (MemoryStream ms = new MemoryStream(fileBytes))
                            {
                                VDFVault.Currency.Entities.FileIteration fileIter = connection.FileManager.AddFile(
                                    folderEntity,
                                    targetVaultFileName,
                                    "Automated Feedback / Bug Report Upload",
                                    DateTime.Now,
                                    null,
                                    null,
                                    Autodesk.Connectivity.WebServices.FileClassification.None,
                                    false,
                                    ms);

                                uploadedVaultFileName = fileIter != null && !string.IsNullOrEmpty(fileIter.EntityName)
                                    ? fileIter.EntityName
                                    : targetVaultFileName;
                            }

                            uploadSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            if (retryCount >= 10)
                            {
                                errorMessage = ex.Message;
                                return false;
                            }

                            targetVaultFileName = $"{baseName}({counter}){ext}";
                            counter++;
                        }
                    }

                    return true;
                }
                finally
                {
                    try
                    {
                        if (connection != null)
                        {
                            VDFVault.Library.ConnectionManager.LogOut(connection);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
