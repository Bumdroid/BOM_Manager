using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BOMManager.Core;
using BOMManager.UI.Dialogs;
using BOMManager.Modules.SpringDesigner.Services;
using BOMManager.Modules.SpringDesigner.Views;

namespace BOMManager.UI
{
    public partial class LauncherWindow : Window
    {
        private readonly ISolidWorksService _swService;
        private readonly bool _mockMode;
        private MainWindow? _bomMainWindow;
        private SpringDesignerWindow? _springDesignerWindow;
        private DispatcherTimer? _statusPollTimer;
        private bool _isClosing = false;

        public LauncherWindow(ISolidWorksService swService, bool mockMode = false)
        {
            InitializeComponent();

            _swService = swService ?? throw new ArgumentNullException(nameof(swService));
            _mockMode = mockMode;

            LoadAppLogos();
            UpdateVaultUserDisplay();

            _statusPollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _statusPollTimer.Tick += (s, e) => UpdateCadStatus();
            _statusPollTimer.Start();

            Loaded += LauncherWindow_Loaded;
            Closed += LauncherWindow_Closed;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            _statusPollTimer?.Stop();
            base.OnClosing(e);
        }

        private void LauncherWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVaultUserDisplay();
            UpdateCadStatus();
        }

        private void LauncherWindow_Closed(object? sender, EventArgs e)
        {
            _isClosing = true;
            try
            {
                _statusPollTimer?.Stop();
            }
            catch { }
            try
            {
                if (_bomMainWindow != null)
                {
                    _bomMainWindow.ForceClose();
                }
            }
            catch { }

            try
            {
                if (_swService is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch { }

            try
            {
                Application.Current?.Shutdown();
            }
            catch { }
        }

        public void UpdateVaultUserDisplay()
        {
            try
            {
                string user = VaultService.CurrentUsername;
                if (string.IsNullOrWhiteSpace(user))
                {
                    user = VaultConfigManager.Load().LastUsername;
                }
                if (string.IsNullOrWhiteSpace(user))
                {
                    user = "미로그인";
                }

                txtVaultUsername.Text = user;
                txtWelcomeUser.Text = user;
            }
            catch { }
        }

        private string? FindResourceFile(string relativeFileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchPaths = new[]
            {
                Path.Combine(baseDir, "resources", relativeFileName),
                Path.Combine(baseDir, relativeFileName),
                Path.Combine(baseDir, "..", "..", "resources", relativeFileName),
                Path.Combine(baseDir, "..", "..", "..", "resources", relativeFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "resources", relativeFileName),
                Path.Combine(Directory.GetCurrentDirectory(), relativeFileName)
            };

            foreach (var p in searchPaths)
            {
                try
                {
                    if (File.Exists(p)) return Path.GetFullPath(p);
                }
                catch { }
            }
            return null;
        }

        private void LoadAppLogos()
        {
            try
            {
                // 1. Windows Taskbar & Titlebar Icon (DT Pepe Icon)
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

                // 2. In-App Classic Pepe Logo for BOM App Tile
                string? logoPath = FindResourceFile("logo.png") ?? FindResourceFile("mainicon_32.png");
                if (logoPath != null && File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    imgBomAppLogo.Source = bmp;
                }

                // 3. Green CAD Pepe Icon for 제작도 V0.0
                string? dwgLogoPath = FindResourceFile("pepe_cad_icon_green.jpg") ?? FindResourceFile("pepe_cad_icon_cyan.jpg");
                if (dwgLogoPath != null && File.Exists(dwgLogoPath))
                {
                    var dwgBmp = new BitmapImage();
                    dwgBmp.BeginInit();
                    dwgBmp.UriSource = new Uri(dwgLogoPath, UriKind.Absolute);
                    dwgBmp.CacheOption = BitmapCacheOption.OnLoad;
                    dwgBmp.EndInit();

                    imgDwgAppLogo.Source = dwgBmp;
                }

                // 3. Duo-tone Transparent Spring Icon for 스프링설계 V1.0
                string? springLogoPath = FindResourceFile("spring_icon.png") ?? FindResourceFile("spring_icon.jpg") ?? FindResourceFile("spring_logo.png");
                if (springLogoPath != null && File.Exists(springLogoPath))
                {
                    var springBmp = new BitmapImage();
                    springBmp.BeginInit();
                    springBmp.UriSource = new Uri(springLogoPath, UriKind.Absolute);
                    springBmp.CacheOption = BitmapCacheOption.OnLoad;
                    springBmp.EndInit();

                    imgSpringAppLogo.Source = springBmp;
                }

                // 4. Vault Icon for Header Badge
                string? vaultIconPath = FindResourceFile("Vault_Icon.png") ?? FindResourceFile("vault_icon.png");
                if (vaultIconPath != null && File.Exists(vaultIconPath))
                {
                    var vaultBmp = new BitmapImage();
                    vaultBmp.BeginInit();
                    vaultBmp.UriSource = new Uri(vaultIconPath, UriKind.Absolute);
                    vaultBmp.CacheOption = BitmapCacheOption.OnLoad;
                    vaultBmp.EndInit();

                    imgVaultIcon.Source = vaultBmp;
                }
            }
            catch { }
        }

        #region App Launch Actions

        private void BtnLaunchBomManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bomMainWindow == null)
                {
                    _bomMainWindow = new MainWindow(_swService, _mockMode, onReturnToLauncher: ReturnFromBomManager);
                }

                Hide();
                _bomMainWindow.Show();
                _bomMainWindow.WindowState = WindowState.Maximized;
                _bomMainWindow.Activate();
                _bomMainWindow.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"BOM Manager 실행 중 오류가 발생했습니다:\n\n{ex.Message}",
                    "BOM Manager 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Show();
            }
        }

        private void ReturnFromBomManager()
        {
            if (_isClosing) return;

            Dispatcher.Invoke(() =>
            {
                if (_isClosing) return;
                try
                {
                    UpdateVaultUserDisplay();
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    Focus();
                }
                catch { }
            });
        }

        private void BtnLaunchDrawingMaker_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "📐 [제작도 V0.0] 모듈 안내:\n\n" +
                "AutoCAD 2025 기반 가공/제작도 도면 자동 생성 및 일괄 출력 기능은 현재 개발 중입니다.\n\n" +
                "추후 정식 릴리즈 시 런처에서 원클릭으로 가공 도면 생성이 즉시 실행됩니다.",
                "제작도 V0.0 (AutoCAD 2025)",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public void UpdateCadStatus()
        {
            try
            {
                if (_isClosing) return;

                var status = SpringCadService.CheckAutoCadStatus();

                if (status == AutoCadStatus.Ready)
                {
                    // 1. 실행 가능 (Green #10B981) & 활성화 (#0284C7)
                    borderSpringStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                    borderSpringStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                    dotSpringStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    txtSpringStatus.Text = "실행 가능";
                    txtSpringStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#047857"));

                    btnLaunchSpringAction.IsEnabled = true;
                    btnLaunchSpringAction.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    txtSpringAction.Foreground = Brushes.White;
                    txtSpringActionArrow.Foreground = Brushes.White;
                    btnLaunchSpringAction.ToolTip = "스프링 설계 모듈 실행 (AutoCAD 연동 준비 완료)";
                }
                else if (status == AutoCadStatus.Initializing)
                {
                    // 2. AutoCAD 실행중 (Yellow #F59E0B) & 초기화 대기
                    borderSpringStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                    borderSpringStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                    dotSpringStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    txtSpringStatus.Text = "AutoCAD 실행중";
                    txtSpringStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B45309"));

                    btnLaunchSpringAction.IsEnabled = false;
                    btnLaunchSpringAction.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    txtSpringAction.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    txtSpringActionArrow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    btnLaunchSpringAction.ToolTip = "AutoCAD 프로그램이 로딩 중입니다. 잠시 후 '실행 가능'으로 자동 전환됩니다.";
                }
                else
                {
                    // 3. AutoCAD 실행 필요 (Red #EF4444) & 비활성화 회색 (#E2E8F0)
                    borderSpringStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                    borderSpringStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
                    dotSpringStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    txtSpringStatus.Text = "AutoCAD 실행 필요";
                    txtSpringStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));

                    btnLaunchSpringAction.IsEnabled = false;
                    btnLaunchSpringAction.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    txtSpringAction.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    txtSpringActionArrow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    btnLaunchSpringAction.ToolTip = "AutoCAD 프로그램이 실행되어 있지 않습니다. AutoCAD를 먼저 실행해 주세요.";
                }
            }
            catch { }
        }

        private void BtnLaunchSpringDesigner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var status = SpringCadService.CheckAutoCadStatus();
                if (status == AutoCadStatus.NotRunning)
                {
                    UpdateCadStatus();
                    MessageBox.Show(
                        this,
                        "AutoCAD 프로그램이 실행되어 있지 않습니다.\n\n" +
                        "스프링 설계 모듈을 구동하려면 먼저 AutoCAD를 실행해 주세요.",
                        "AutoCAD 실행 필요",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                else if (status == AutoCadStatus.Initializing)
                {
                    UpdateCadStatus();
                    MessageBox.Show(
                        this,
                        "AutoCAD 프로그램이 현재 로딩 중입니다.\n\n" +
                        "잠시 후 '실행 가능'으로 전환되면 버튼을 클릭해 주세요.",
                        "AutoCAD 실행중 (초기화 중)",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (_springDesignerWindow != null && _springDesignerWindow.IsVisible)
                {
                    if (_springDesignerWindow.WindowState == WindowState.Minimized)
                    {
                        _springDesignerWindow.WindowState = WindowState.Normal;
                    }
                    _springDesignerWindow.Activate();
                    _springDesignerWindow.Focus();
                    return;
                }

                _springDesignerWindow = new SpringDesignerWindow
                {
                    Owner = this
                };

                _springDesignerWindow.Closed += (s, ev) =>
                {
                    _springDesignerWindow = null;
                };

                _springDesignerWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"스프링 설계 모듈 구동 중 오류가 발생했습니다:\n\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Settings & Menu Actions

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (btnSettings.ContextMenu != null)
            {
                btnSettings.ContextMenu.PlacementTarget = btnSettings;
                btnSettings.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btnSettings.ContextMenu.IsOpen = true;
            }
        }

        private void MenuItemChangeLogin_Click(object sender, RoutedEventArgs e)
        {
            var loginDialog = new VaultLoginDialog(_mockMode, isSwitchAccount: true, forceUncheckAutoLogin: true)
            {
                Owner = this
            };
            bool? result = loginDialog.ShowDialog();
            if (result == true)
            {
                UpdateVaultUserDisplay();
                MessageBox.Show(
                    this,
                    $"Vault 계정이 '{VaultService.CurrentUsername}'(으)로 변경되었습니다.",
                    "Vault 로그인 정보 변경 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void MenuItemAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "🚀 CAD Automation Suite 2026\n\n" +
                "• BOM Manager V0.0 (SolidWorks 2021)\n" +
                "• 제작도 V0.0 (AutoCAD 2025)\n" +
                "• 스프링 설계 V1.0\n" +
                "• Autodesk Vault Professional 2021 Integration\n\n" +
                "Copyright © 2026 CAD Automation. All rights reserved.",
                "애플리케이션 정보",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
