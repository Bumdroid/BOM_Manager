using System;
using System.IO;
using System.Text;
using System.Windows;
using BOMManager.Core;
using BOMManager.Modules.SpringDesigner.ViewModels;
using BOMManager.Modules.SpringDesigner.Services;

namespace BOMManager.Modules.SpringDesigner.Views
{
    public partial class SpringFeedbackWindow : Window
    {
        private readonly SpringDesignerViewModel? _viewModel;

        public SpringFeedbackWindow(SpringDesignerViewModel? viewModel, string defaultAuthor = "이두규")
        {
            InitializeComponent();
            _viewModel = viewModel;

            // 캐시된 작성자가 있으면 캐시 우선(User 제외), 없으면 Vault 사용자명(defaultAuthor) 적용
            string cachedAuthor = LoadCachedAuthor();
            if (!string.IsNullOrWhiteSpace(cachedAuthor) && !cachedAuthor.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                TxtAuthor.Text = cachedAuthor;
            }
            else
            {
                string resolvedUser = !string.IsNullOrWhiteSpace(VaultService.CurrentUsername)
                    ? VaultService.CurrentUsername
                    : (!string.IsNullOrWhiteSpace(defaultAuthor) && !defaultAuthor.Equals("User", StringComparison.OrdinalIgnoreCase)
                        ? defaultAuthor
                        : (!string.IsNullOrWhiteSpace(VaultConfigManager.Load().LastUsername) ? VaultConfigManager.Load().LastUsername : "이두규"));
                TxtAuthor.Text = resolvedUser;
            }

            Closed += (s, e) =>
            {
                SaveCachedAuthor(TxtAuthor.Text.Trim());
            };
        }

        private static string GetCacheFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Common_Draw");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "feedback_cache.json");
        }

        private static string LoadCachedAuthor()
        {
            try
            {
                string cachePath = GetCacheFilePath();
                if (File.Exists(cachePath))
                {
                    string json = File.ReadAllText(cachePath, Encoding.UTF8);
                    // Lightweight author extract
                    int idx = json.IndexOf("\"Author\"", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        int colon = json.IndexOf(':', idx);
                        if (colon >= 0)
                        {
                            string sub = json.Substring(colon + 1);
                            int quote1 = sub.IndexOf('"');
                            if (quote1 >= 0)
                            {
                                int quote2 = sub.IndexOf('"', quote1 + 1);
                                if (quote2 > quote1)
                                {
                                    return sub.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static void SaveCachedAuthor(string author)
        {
            if (string.IsNullOrWhiteSpace(author)) return;
            try
            {
                string cachePath = GetCacheFilePath();
                string json = "{\r\n  \"Author\": \"" + author.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\r\n}";
                File.WriteAllText(cachePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            string author = TxtAuthor.Text.Trim();
            string type = RbBug.IsChecked == true ? "버그신고" : "제안";
            string content = TxtContent.Text;

            if (string.IsNullOrWhiteSpace(author))
            {
                MessageBox.Show("작성자를 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAuthor.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show("상세 내용을 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtContent.Focus();
                return;
            }

            SaveCachedAuthor(author);

            try
            {
                // 1. 유형_작성자_YYYYMMDDHHmmss.txt 파일명 규격
                string safeAuthor = string.Concat(author.Split(Path.GetInvalidFileNameChars()));
                string dateStr = DateTime.Now.ToString("yyyyMMddHHmmss");
                string baseFileName = $"{type}_{safeAuthor}_{dateStr}";
                string txtFileName = $"{baseFileName}.txt";

                // 2. Vault $/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/50_VoC 폴더로 직접 업로드
                bool vaultUploaded = false;
                string uploadedFileName = txtFileName;
                string vaultMsg = "";
                
                var vCfg = VaultConfigManager.Load();
                string server = !string.IsNullOrWhiteSpace(VaultService.CurrentServer) && !VaultService.CurrentServer.Contains("???")
                    ? VaultService.CurrentServer 
                    : (_viewModel?.VaultServer ?? VaultConfigManager.DefaultServer);
                string vault = !string.IsNullOrWhiteSpace(VaultService.CurrentVault)
                    ? VaultService.CurrentVault 
                    : (_viewModel?.VaultName ?? VaultConfigManager.DefaultVaultName);
                
                string user = !string.IsNullOrWhiteSpace(VaultService.CurrentUsername)
                    ? VaultService.CurrentUsername
                    : (!string.IsNullOrWhiteSpace(author) && !author.Equals("User", StringComparison.OrdinalIgnoreCase)
                        ? author
                        : (!string.IsNullOrWhiteSpace(_viewModel?.VaultUser) && !_viewModel.VaultUser.Equals("User", StringComparison.OrdinalIgnoreCase)
                            ? _viewModel.VaultUser
                            : (!string.IsNullOrWhiteSpace(vCfg.LastUsername) ? vCfg.LastUsername : "이두규")));

                string pass = !string.IsNullOrWhiteSpace(_viewModel?.VaultPassword) 
                    ? _viewModel.VaultPassword 
                    : (vCfg.Password ?? "");

                vaultUploaded = VaultService.UploadFeedbackTextToVault(
                    server,
                    vault,
                    user,
                    pass,
                    txtFileName,
                    content,
                    out uploadedFileName,
                    out vaultMsg);

                string statusMsg = vaultUploaded
                    ? "Vault 전용 폴더($/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/50_VoC)로 직접 전달되었습니다."
                    : $"Vault 전송 결과: {vaultMsg}";

                MessageBox.Show(
                    $"[{type}] 가 성공적으로 접수되었습니다.\n\n" +
                    $"• 등록 파일명: {uploadedFileName}\n" +
                    $"• 작성자: {author}\n" +
                    $"• 접수 일자: {DateTime.Now:yyyy-MM-dd}\n\n" +
                    $"• 상태: {statusMsg}",
                    "접수 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"접수 처리 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
