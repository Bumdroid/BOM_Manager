using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BOMManager.Core;

namespace BOMManager.UI.Dialogs
{
    public partial class UpdateProgressDialog : Window
    {
        private readonly UpdateCheckResult? _preloadedUpdateInfo;
        private readonly string _server;
        private readonly string _vault;
        private readonly string _username;
        private readonly string _password;

        public bool UpdateInitiated { get; private set; }

        public UpdateProgressDialog(
            string server,
            string vault,
            string username,
            string password)
        {
            InitializeComponent();
            _server = server;
            _vault = vault;
            _username = username;
            _password = password;

            LoadAppIcon();
        }

        public UpdateProgressDialog(
            UpdateCheckResult updateInfo,
            string server,
            string vault,
            string username,
            string password)
            : this(server, vault, username, password)
        {
            _preloadedUpdateInfo = updateInfo;
        }

        private void LoadAppIcon()
        {
            try
            {
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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RunUpdateCheckAndApplyAsync();
        }

        private async Task RunUpdateCheckAndApplyAsync()
        {
            try
            {
                // 1. 최신 배포 버전 확인
                UpdateCheckResult? updateInfo = _preloadedUpdateInfo;
                if (updateInfo == null)
                {
                    txtHeaderTitle.Text = "업데이트 확인";
                    txtHeaderSubtitle.Text = "업데이트를 확인 중입니다...";
                    txtMessage.Text = "Autodesk Vault 서버에서 최신 자동화 모듈 배포 버전을 확인하고 있습니다.";
                    pbProgress.IsIndeterminate = true;
                    txtStatus.Text = "서버 연결 및 최신 버전 메타데이터 조회 중...";

                    try
                    {
                        updateInfo = await Task.Run(() =>
                        {
                            return VaultUpdateService.CheckForUpdate(
                                _server,
                                _vault,
                                _username,
                                _password,
                                VaultUpdateService.DefaultUpdateFolderPath,
                                VaultUpdateService.CurrentAppVersion);
                        });
                    }
                    catch
                    {
                        // 조회 실패 시 사용자 방해 없이 창을 닫고 메인으로 진행
                        DialogResult = false;
                        Close();
                        return;
                    }
                }

                // 2. 업데이트가 없는 경우 -> 팝업 자동 종료 후 메인 런처로 전환
                if (updateInfo == null || !updateInfo.HasUpdate || updateInfo.Candidate == null)
                {
                    // 부드러운 전환을 위한 짧은 대기 (깜빡임 방지)
                    await Task.Delay(350);
                    DialogResult = false;
                    Close();
                    return;
                }

                // 3. 신규 버전이 있는 경우 -> 다운로드 및 적용 진행
                string remoteVer = updateInfo.RemoteVersion?.DisplayString ?? "최신버전";

                txtHeaderIcon.Text = "🚀";
                txtHeaderTitle.Text = "신규 버전 업데이트";
                txtHeaderSubtitle.Text = $"최신 버전({remoteVer})을 다운로드하고 있습니다...";
                txtMessage.Text = $"최신 기능 및 개선사항이 포함된 버전({remoteVer})을 적용합니다.";
                pbProgress.IsIndeterminate = false;
                pbProgress.Value = 10;
                txtStatus.Text = $"최신 패키지 다운로드 준비 중 ({updateInfo.RemoteFileName})...";

                string errorMsg = "";
                bool success = false;

                try
                {
                    success = await Task.Run(() =>
                    {
                        return VaultUpdateService.DownloadAndApplyUpdate(
                            _server,
                            _vault,
                            _username,
                            _password,
                            updateInfo.Candidate,
                            VaultUpdateService.DefaultUpdateFolderPath,
                            (percent, status) =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    pbProgress.Value = percent;
                                    txtStatus.Text = status;
                                });
                            },
                            out errorMsg);
                    });
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                    success = false;
                }

                if (success)
                {
                    // 4. 업데이트 성공 -> "프로그램이 최신버전 V~~~로 업데이트 되었습니다" 팝업 표시 및 [확인] 버튼 제공
                    txtHeaderIcon.Text = "✅";
                    borderHeader.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                    borderHeader.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                    borderIconContainer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5"));
                    txtHeaderTitle.Text = "업데이트 완료";
                    txtHeaderTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    txtHeaderSubtitle.Text = "최신 버전 설치 준비가 완료되었습니다.";
                    txtHeaderSubtitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#047857"));

                    panelProgress.Visibility = Visibility.Collapsed;
                    panelCompleted.Visibility = Visibility.Visible;
                    txtCompletedTitle.Text = $"프로그램이 최신버전 {remoteVer}로 업데이트 되었습니다.";
                    txtCompletedDesc.Text = "확인 버튼을 누르면 업데이트된 최신 버전으로 프로그램을 시작합니다.";

                    panelButtons.Visibility = Visibility.Visible;
                    btnConfirm.Focus();
                }
                else
                {
                    // 업데이트 실패 시 오류 안내 후 사용자가 확인할 수 있도록 함
                    txtHeaderIcon.Text = "⚠️";
                    txtHeaderTitle.Text = "업데이트 확인 안내";
                    txtHeaderSubtitle.Text = "업데이트 적용 중 문제가 발생했습니다.";

                    panelProgress.Visibility = Visibility.Collapsed;
                    panelCompleted.Visibility = Visibility.Visible;
                    txtCompletedTitle.Text = "최신 버전을 적용하지 못했습니다.";
                    txtCompletedDesc.Text = $"{errorMsg}\n\n확인 버튼을 누르면 기존 버전으로 계속 실행합니다.";

                    panelButtons.Visibility = Visibility.Visible;
                    btnConfirm.Focus();
                }
            }
            catch
            {
                DialogResult = false;
                Close();
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            UpdateInitiated = (panelCompleted.Visibility == Visibility.Visible && txtHeaderTitle.Text == "업데이트 완료");
            DialogResult = true;
            Close();
        }
    }
}
