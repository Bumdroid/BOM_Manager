using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BOMManager.Core;

namespace BOMManager.UI.Dialogs
{
    public partial class VaultLoginDialog : Window
    {
        private readonly bool _isMock;
        private readonly bool _isSwitchAccount;
        private readonly bool _forceUncheckAutoLogin;
        private bool _isProcessing;

        public VaultLoginDialog(bool isMock = false, bool isSwitchAccount = false, bool forceUncheckAutoLogin = false)
        {
            InitializeComponent();
            _isMock = isMock;
            _isSwitchAccount = isSwitchAccount;
            _forceUncheckAutoLogin = forceUncheckAutoLogin;
            LoadVaultIcon();
        }

        private void LoadVaultIcon()
        {
            try
            {
                // 1. Taskbar & Titlebar Icon (DT Pepe Icon)
                string? appIconPath = FindResourceFile("app_dt_icon.png") ?? FindResourceFile("app.ico") ?? FindResourceFile("logo.png");
                if (appIconPath != null && File.Exists(appIconPath))
                {
                    var iconBmp = new BitmapImage();
                    iconBmp.BeginInit();
                    iconBmp.UriSource = new Uri(appIconPath, UriKind.Absolute);
                    iconBmp.CacheOption = BitmapCacheOption.OnLoad;
                    iconBmp.EndInit();
                    Icon = iconBmp;
                }

                // 2. In-App Vault Logo
                string? iconPath = FindResourceFile("Vault_Icon.png") ?? FindResourceFile("vault_icon.png");
                if (iconPath != null && File.Exists(iconPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    imgVaultIcon.Source = bmp;
                }
            }
            catch { }
        }

        private static string? FindResourceFile(string filename)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "resources", filename),
                Path.Combine(baseDir, filename),
                Path.Combine(Directory.GetCurrentDirectory(), "resources", filename),
                Path.Combine(Directory.GetCurrentDirectory(), filename),
                Path.Combine(baseDir, "..", "..", "..", "resources", filename),
                Path.Combine(baseDir, "..", "..", "resources", filename),
                Path.Combine(baseDir, "..", "resources", filename)
            };
            foreach (var p in candidates)
            {
                try
                {
                    if (File.Exists(p)) return Path.GetFullPath(p);
                }
                catch { }
            }
            return null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var config = VaultConfigManager.Load();
            if (_forceUncheckAutoLogin)
            {
                chkAutoLogin.IsChecked = false;
            }
            else
            {
                chkAutoLogin.IsChecked = config.AutoLogin;
            }

            if (!string.IsNullOrEmpty(config.LastUsername))
            {
                txtUsername.Text = config.LastUsername;
                txtUsername.SelectAll();
            }
            txtUsername.Focus();

            // 계정 전환/설정 변경 모드가 아니고, 자동 로그인 체크 상태 및 기존 사용자명이 있는 경우 즉시 자동 로그인 시도
            if (!_isSwitchAccount && !_forceUncheckAutoLogin && chkAutoLogin.IsChecked == true && !string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isProcessing)
                    {
                        ExecuteLogin();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void TxtUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isProcessing)
            {
                e.Handled = true;
                ExecuteLogin();
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            ExecuteLogin();
        }

        private async void ExecuteLogin()
        {
            if (_isProcessing) return;

            string username = txtUsername.Text?.Trim() ?? "";
            string server = txtServer.Text?.Trim() ?? VaultConfigManager.DefaultServer;
            string vault = txtVaultName.Text?.Trim() ?? VaultConfigManager.DefaultVaultName;
            string password = txtPassword.Text ?? VaultConfigManager.DefaultPassword;
            bool autoLogin = chkAutoLogin.IsChecked == true;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowUserError();
                return;
            }

            SetLoadingState(true);

            bool loginSuccess = false;
            string errorMsg = "";

            try
            {
                loginSuccess = await Task.Run(() =>
                {
                    try
                    {
                        return VaultService.TestLogIn(server, vault, username, password, out errorMsg);
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.Message;
                        return false;
                    }
                });

                // 개발/목업 모드(--mock)에서 네트워크 오류로 실패 시 허용 옵션 지원
                if (!loginSuccess && _isMock)
                {
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        loginSuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                loginSuccess = false;
            }
            finally
            {
                SetLoadingState(false);
            }

            if (loginSuccess)
            {
                VaultConfigManager.SaveLastUsername(username, autoLogin);
                VaultService.SetLoggedInUser(username, server, vault);
                DialogResult = true;
                Close();
            }
            else
            {
                ShowUserError();
            }
        }

        private void ShowUserError()
        {
            MessageBox.Show(
                this,
                "올바른 유저이름 (ex: 이두규)을 입력하세요.",
                "Vault 로그인 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            txtUsername.Focus();
            txtUsername.SelectAll();
        }

        private void SetLoadingState(bool isLoading)
        {
            _isProcessing = isLoading;
            pnlProgress.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            btnLogin.IsEnabled = !isLoading;
            btnCancel.IsEnabled = !isLoading;
            txtUsername.IsEnabled = !isLoading;
            chkAutoLogin.IsEnabled = !isLoading;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
