using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BOMManager.Core;
using BOMManager.Models;
using BOMManager.UI.Dialogs;
using BOMManager.Utils;
using Microsoft.Win32;

namespace BOMManager.UI
{
    public enum BomProcessStep
    {
        Step1Assy,       // ① Assy. 정리
        Summary,         // Summary
        Step2DrawingNo,  // ② 도번 입력
        Step3Other       // ③ 기타(재질 등..)
    }

    public partial class MainWindow : Window
    {
        private readonly ISolidWorksService _swService;
        private readonly bool _mockMode;
        private readonly Action? _onReturnToLauncher;
        private bool _forceClose = false;
        private readonly List<BOMItem> _allItems = new();
        private readonly ObservableCollection<BOMItem> _displayedItems = new();
        private AssemblyInfo _currentAssyInfo = new();
        private readonly DispatcherTimer _autoTimer;
        private string _filterText = string.Empty;
        private BomProcessStep _currentStep = BomProcessStep.Summary;

        public MainWindow(ISolidWorksService swService, bool mockMode = false, Action? onReturnToLauncher = null)
        {
            InitializeComponent();

            _swService = swService ?? throw new ArgumentNullException(nameof(swService));
            _mockMode = mockMode;
            _onReturnToLauncher = onReturnToLauncher;

            dgBom.ItemsSource = _displayedItems;

            LoadAppLogo();

            _autoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _autoTimer.Tick += AutoTimer_Tick;

            treeGraphView.ItemSelected += (s, item) =>
            {
                txtStatusBar.Text = $"선택된 파트: {item.PartName} (Q'TY: {item.Qty}, 도번: {item.DrawingNo})";
            };

            treeGraphView.ItemReparented += TreeGraphView_ItemReparented;
            treeGraphView.CreateSubAssyRequested += TreeGraphView_CreateSubAssyRequested;
            treeGraphView.ApplyToFileRequested += TreeGraphView_ApplyToFileRequested;
            treeGraphView.DevTempRequested += TreeGraphView_DevTempRequested;

            SetProcessStep(BomProcessStep.Summary);

            // 창이 0.05초 만에 즉시 표시되도록 초기 로딩을 백그라운드로 지연 실행
            Loaded += MainWindow_Loaded;

            Closing += MainWindow_Closing;
        }

        public void ForceClose()
        {
            _forceClose = true;
            try
            {
                Close();
            }
            catch { }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_forceClose) return;

            if (_onReturnToLauncher != null)
            {
                e.Cancel = true;
                Hide();
                _onReturnToLauncher.Invoke();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVaultUserDisplay();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CheckSwConnectionAndLoad(initial: true, silent: true);
                _autoTimer.Start();
            }), DispatcherPriority.Background);
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

        private void LoadAppLogo()
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

                // 2. In-App Classic Pepe BOM Logo
                string? logoPath = FindResourceFile("logo.png") ?? FindResourceFile("mainicon_32.png");
                if (logoPath != null && File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    imgLogo.Source = bmp;
                }

                // 3. Vault Icon for Header Badge
                string? vaultIconPath = FindResourceFile("Vault_Icon.png") ?? FindResourceFile("vault_icon.png");
                if (vaultIconPath != null && File.Exists(vaultIconPath))
                {
                    var vaultBmp = new BitmapImage();
                    vaultBmp.BeginInit();
                    vaultBmp.UriSource = new Uri(vaultIconPath, UriKind.Absolute);
                    vaultBmp.CacheOption = BitmapCacheOption.OnLoad;
                    vaultBmp.EndInit();

                    imgHeaderVaultIcon.Source = vaultBmp;
                }
            }
            catch { }
        }

        private void UpdateVaultUserDisplay()
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
                txtHeaderVaultUser.Text = user;
            }
            catch { }
        }

        private void CheckSwConnectionAndLoad(bool initial = false, bool silent = false)
        {
            _currentAssyInfo = _swService.GetActiveAssemblyInfo();

            if (!_currentAssyInfo.IsConnected)
            {
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xEE, 0xEE));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
                dotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                dotStatus.Stroke = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
                txtStatusBadge.Text = "SolidWorks 2021 미연결";
                txtStatusBar.Text = _currentAssyInfo.ErrorMessage ?? "SolidWorks 2021이 실행 중이지 않거나 설치되어 있지 않습니다. PC에 설치된 SolidWorks 2021 버전을 확인해주세요.";

                if (!initial && !silent && !_mockMode)
                {
                    MessageBox.Show(
                        _currentAssyInfo.ErrorMessage ?? "SolidWorks 2021이 실행 중이지 않거나 설치되어 있지 않습니다.\n\nPC에 설치된 SolidWorks 2021 버전을 확인해 주세요.",
                        "SolidWorks 2021 연결 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (!string.IsNullOrEmpty(_currentAssyInfo.ErrorMessage))
            {
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D));
                dotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                dotStatus.Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                txtStatusBadge.Text = $"대기 중 ({_currentAssyInfo.Title})";
                txtStatusBar.Text = _currentAssyInfo.ErrorMessage;

                if (!initial && !silent)
                {
                    MessageBox.Show(_currentAssyInfo.ErrorMessage, "문서 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5));
            borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0xF3, 0xD0));
            dotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            dotStatus.Stroke = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
            txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            txtStatusBadge.Text = _currentAssyInfo.Title;

            // 0. 기존 사용자 입력값 상태 스냅샷 캡처 (리로드 시 값 보존용)
            var stateSnapshot = CaptureCurrentState();

            // Load BOM Items
            bool topLevel = false;
            var (items, err) = _swService.LoadBom(topLevelOnly: topLevel);

            if (err != null)
            {
                txtStatusBar.Text = $"오류: {err}";
                if (!initial && !silent)
                {
                    MessageBox.Show(err, "BOM 로드 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            // 1. 기존 입력값 지능형 병합 및 복원
            if (stateSnapshot == null || stateSnapshot.Count == 0)
            {
                treeGraphView.ResetInitialLoadState();
            }
            RestoreAndMergeState(items, stateSnapshot);

            // 1.5. 개발자용 임시저장 데이터가 있는 경우 우선 적용 (다음에 켤 때 자동 복원)
            string devTempPath = GetDevTempFilePath();
            if (File.Exists(devTempPath))
            {
                try
                {
                    var savedDevItems = LoadDevTempState(devTempPath);
                    if (savedDevItems != null && savedDevItems.Count > 0)
                    {
                        items = savedDevItems;
                    }
                }
                catch { }
            }

            _allItems.Clear();
            foreach (var itm in items)
            {
                itm.PropertyChanged += Item_PropertyChanged;
                _allItems.Add(itm);
            }

            // 상위 어셈블리의 AssyCategory에 맞춰 하위 파트 AvailableAssyCategories 초기 갱신
            for (int i = 0; i < _allItems.Count; i++)
            {
                var parent = _allItems[i];
                if (!string.IsNullOrEmpty(parent.AssyCategory))
                {
                    for (int j = i + 1; j < _allItems.Count; j++)
                    {
                        if (_allItems[j].Level <= parent.Level) break;
                        _allItems[j].UpdateAvailableAssyCategories(parent.AssyCategory);
                    }
                }
            }

            UpdateDisplayedItems();
            UpdateStatistics();

            if (_currentStep == BomProcessStep.Step1Assy)
            {
                treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);
            }

            txtStatusBar.Text = $"'{_currentAssyInfo.Title}'에서 {items.Count}개 파트 정보를 성공적으로 불러왔습니다.";
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BOMItem.AssyCategory) && sender is BOMItem item)
            {
                // 상위 서브어셈블리 또는 자식이 있는 항목인 경우에만 하위 선택 목록 갱신 및 트리 펼침 수행
                if (item.IsSubassembly)
                {
                    HandleParentAssyCategoryChanged(item);
                }
            }
            UpdateStatistics();
        }

        private void HandleParentAssyCategoryChanged(BOMItem parentItem)
        {
            if (string.IsNullOrEmpty(parentItem.AssyCategory)) return;

            int parentIdx = _allItems.IndexOf(parentItem);
            if (parentIdx >= 0)
            {
                int pLevel = parentItem.Level;
                bool hasChildren = false;

                for (int i = parentIdx + 1; i < _allItems.Count; i++)
                {
                    var child = _allItems[i];
                    if (child.Level <= pLevel) break;

                    hasChildren = true;
                    // 상위 AssyCategory(예: LID Assy)에 따라 직속/하위 파트 선택 목록 갱신
                    child.UpdateAvailableAssyCategories(parentItem.AssyCategory);
                }

                if (hasChildren && !parentItem.IsExpanded)
                {
                    // 상위 assy의 assy. 정보가 결정되면 그 assy 트리가 한단계 펼쳐짐
                    parentItem.IsExpanded = true;
                    UpdateDisplayedItems();
                }
            }
        }

        private bool IsElastomerItem(BOMItem item)
        {
            string assy = (item.AssyCategory ?? "").ToLowerInvariant();
            if (assy.Contains("elastomer")) return true;

            string mat = (item.Material ?? "").ToLowerInvariant();
            string name = (item.PartName ?? "").ToLowerInvariant();
            string rem = (item.Remark ?? "").ToLowerInvariant();
            string exp = (item.Explanation ?? "").ToLowerInvariant();

            string[] keywords = new[] { "elastomer", "rubber", "o-ring", "oring", "packing", "gasket", "우레탄", "urethane", "silicone", "실리콘", "고무", "epdm", "nbr", "fkm", "viton", "kapton" };
            return keywords.Any(k => mat.Contains(k) || name.Contains(k) || rem.Contains(k) || exp.Contains(k));
        }

        private void UpdateDisplayedItems()
        {
            _displayedItems.Clear();
            var hiddenAncestorLevels = new List<int>();

            string query = _filterText.Trim().ToLowerInvariant();

            for (int i = 0; i < _allItems.Count; i++)
            {
                var item = _allItems[i];
                int lvl = item.Level;
                bool isSub = item.IsSubassembly;

                // Remove ancestor levels >= current level
                hiddenAncestorLevels.RemoveAll(pl => pl >= lvl);

                bool isHiddenByTree = hiddenAncestorLevels.Count > 0;

                // Check if this subassembly has children
                bool hasChildren = (i + 1 < _allItems.Count) && (_allItems[i + 1].Level > lvl);

                if (!string.IsNullOrEmpty(query))
                {
                    bool match = item.PartName.ToLowerInvariant().Contains(query) ||
                                 item.DrawingNo.ToLowerInvariant().Contains(query) ||
                                 item.Material.ToLowerInvariant().Contains(query) ||
                                 item.AssyCategory.ToLowerInvariant().Contains(query) ||
                                 item.Rev.ToLowerInvariant().Contains(query) ||
                                 item.Explanation.ToLowerInvariant().Contains(query) ||
                                 item.Remark.ToLowerInvariant().Contains(query) ||
                                 item.FileName.ToLowerInvariant().Contains(query);

                    if (match)
                    {
                        _displayedItems.Add(item);
                    }
                }
                else
                {
                    if (!isHiddenByTree)
                    {
                        _displayedItems.Add(item);
                    }
                }

                if (isSub && hasChildren)
                {
                    if (!item.IsExpanded)
                    {
                        hiddenAncestorLevels.Add(lvl);
                    }
                }
            }
        }

        private void UpdateStatistics()
        {
            int totalCount = _allItems.Count;
            int totalQty = _allItems.Sum(i => i.Qty);
            int modifiedCount = _allItems.Count(i => i.IsModified);

            lblUniqueParts.Text = totalCount.ToString();
            lblTotalQty.Text = totalQty.ToString();
            lblModifiedCount.Text = modifiedCount.ToString();
        }

        private void AutoTimer_Tick(object? sender, EventArgs e)
        {
            if (_mockMode) return;

            // Don't auto-reload if user has uncommitted modifications
            if (_allItems.Any(i => i.IsModified)) return;

            var info = _swService.GetActiveAssemblyInfo();
            if (info.IsConnected)
            {
                if (string.IsNullOrEmpty(info.ErrorMessage))
                {
                    // 정상 어셈블리 열림
                    if (!_currentAssyInfo.IsConnected || info.Title != _currentAssyInfo.Title || _allItems.Count == 0)
                    {
                        CheckSwConnectionAndLoad(initial: true, silent: true);
                    }
                }
                else
                {
                    // SolidWorks 2021은 켜져 있으나 어셈블리 문서가 안 열려있거나 대기 상태
                    _currentAssyInfo = info;
                    borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                    borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D));
                    dotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                    dotStatus.Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                    txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                    txtStatusBadge.Text = $"대기 중 ({info.Title})";
                    txtStatusBar.Text = info.ErrorMessage;

                    if (_allItems.Count > 0)
                    {
                        _allItems.Clear();
                        _displayedItems.Clear();
                        UpdateStatistics();
                    }
                }
            }
            else
            {
                _currentAssyInfo = info;
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xEE, 0xEE));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
                dotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                dotStatus.Stroke = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
                txtStatusBadge.Text = "SolidWorks 2021 미연결";
                txtStatusBar.Text = info.ErrorMessage ?? "SolidWorks 2021이 실행 중이지 않거나 설치되어 있지 않습니다.";

                if (_allItems.Count > 0)
                {
                    _allItems.Clear();
                    _displayedItems.Clear();
                    UpdateStatistics();
                }
            }
        }

        #region Event Handlers

        private void BtnGoHome_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _onReturnToLauncher?.Invoke();
        }


        private void BtnOpenAssembly_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "SolidWorks 어셈블리 열기",
                Filter = "SolidWorks 어셈블리 (*.sldasm)|*.sldasm|SolidWorks 파트 (*.sldprt)|*.sldprt|모든 지원 CAD 파일 (*.sldasm;*.sldprt)|*.sldasm;*.sldprt",
                FilterIndex = 1
            };

            if (ofd.ShowDialog(this) == true)
            {
                txtStatusBar.Text = $"'{Path.GetFileName(ofd.FileName)}' 문서를 여는 중...";
                var (ok, msg) = _swService.OpenDocument(ofd.FileName);
                if (ok)
                {
                    txtStatusBar.Text = msg;
                    CheckSwConnectionAndLoad(initial: false, silent: false);
                }
                else
                {
                    MessageBox.Show(msg, "파일 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtStatusBar.Text = $"파일 열기 실패: {msg}";
                }
            }
        }

        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var itm in _allItems)
            {
                if (itm.IsSubassembly) itm.IsExpanded = true;
            }
            UpdateDisplayedItems();
        }

        private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var itm in _allItems)
            {
                if (itm.IsSubassembly) itm.IsExpanded = false;
            }
            UpdateDisplayedItems();
        }

        private void BtnTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BOMItem item)
            {
                item.IsExpanded = !item.IsExpanded;
                UpdateDisplayedItems();
            }
        }

        private void BtnIsolateRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BOMItem item)
            {
                dgBom.SelectedItem = item;
                _swService.SetComponentsTransparency(new[] { item }, _allItems, isolateMode: true);
                txtStatusBar.Text = $"SolidWorks 화면에서 '{item.PartName}' 부품이 불투명(👁️ 눈 뜸)하게 강조되었습니다.";
            }
        }

        private void BtnShowAll_Click(object sender, RoutedEventArgs e)
        {
            _swService.ShowAllOpaque(_allItems);
            txtStatusBar.Text = "SolidWorks 화면의 모든 부품을 불투명(👁️ 눈 뜸) 상태로 복원했습니다.";
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            CheckSwConnectionAndLoad(initial: false, silent: false);
        }

        private void DgBom_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.DataContext is BOMItem item)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    item.CheckModified();
                    UpdateStatistics();
                }), DispatcherPriority.Background);
            }
        }

        private void BtnStep1Assy_Click(object sender, RoutedEventArgs e)
        {
            SetProcessStep(BomProcessStep.Step1Assy);
        }

        private void BtnSummary_Click(object sender, RoutedEventArgs e)
        {
            SetProcessStep(BomProcessStep.Summary);
        }

        private void BtnStep2DrawingNo_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStep1Completed)
            {
                MessageBox.Show("⚠️ [① Assy. 정리] 단계를 먼저 완료(사본저장 다음단계로)해야 [② 도번, 재질 입력]으로 이동할 수 있습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SetProcessStep(BomProcessStep.Step2DrawingNo);
        }

        private void BtnStep3Other_Click(object sender, RoutedEventArgs e)
        {
            SetProcessStep(BomProcessStep.Step3Other);
        }

        private bool _isStep1Completed = false;

        public void SetProcessStep(BomProcessStep step)
        {
            if (step == BomProcessStep.Step2DrawingNo && !_isStep1Completed)
            {
                MessageBox.Show("⚠️ [① Assy. 정리] 단계를 먼저 완료(사본저장 다음단계로)해야 [② 도번, 재질 입력]으로 이동할 수 있습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _currentStep = step;
            treeGraphView.CurrentStep = step;

            var defaultTextBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            var defaultBorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            var activeTextBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
            var activeBorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            var greenBg = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            var greenBorder = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));

            // 1. Summary 버튼 (선택 시: 테두리 두꺼운 파란색, 채우기 흰색)
            if (step == BomProcessStep.Summary)
            {
                btnSummary.Background = Brushes.White;
                btnSummary.Foreground = activeTextBrush;
                btnSummary.FontWeight = FontWeights.Bold;
                btnSummary.BorderBrush = activeBorderBrush;
                btnSummary.BorderThickness = new Thickness(2.5);
            }
            else
            {
                btnSummary.Background = Brushes.White;
                btnSummary.Foreground = defaultTextBrush;
                btnSummary.FontWeight = FontWeights.Normal;
                btnSummary.BorderBrush = defaultBorderBrush;
                btnSummary.BorderThickness = new Thickness(1);
            }

            // 2. Step 1 (Assy. 정리) 버튼
            if (step == BomProcessStep.Step1Assy)
            {
                if (_isStep1Completed)
                {
                    // 1단계 완료 후 다시 1단계로 돌아온 경우: 채우기는 초록색, 테두리만 두꺼운 파란색
                    btnStep1Assy.Background = greenBg;
                    btnStep1Assy.Foreground = Brushes.White;
                    btnStep1Assy.FontWeight = FontWeights.Bold;
                    btnStep1Assy.BorderBrush = activeBorderBrush;
                    btnStep1Assy.BorderThickness = new Thickness(2.5);
                }
                else
                {
                    // 1단계 미완료 선택 상태: 채우기 흰색, 테두리 두꺼운 파란색
                    btnStep1Assy.Background = Brushes.White;
                    btnStep1Assy.Foreground = activeTextBrush;
                    btnStep1Assy.FontWeight = FontWeights.Bold;
                    btnStep1Assy.BorderBrush = activeBorderBrush;
                    btnStep1Assy.BorderThickness = new Thickness(2.5);
                }
            }
            else if (_isStep1Completed)
            {
                // 타 단계 이동 시 1단계 완료됨: 채우기 초록색, 테두리 일반 초록색
                btnStep1Assy.Background = greenBg;
                btnStep1Assy.Foreground = Brushes.White;
                btnStep1Assy.FontWeight = FontWeights.Bold;
                btnStep1Assy.BorderBrush = greenBorder;
                btnStep1Assy.BorderThickness = new Thickness(1);
            }
            else
            {
                btnStep1Assy.Background = Brushes.White;
                btnStep1Assy.Foreground = defaultTextBrush;
                btnStep1Assy.FontWeight = FontWeights.Normal;
                btnStep1Assy.BorderBrush = defaultBorderBrush;
                btnStep1Assy.BorderThickness = new Thickness(1);
            }

            // 3. Step 2 (도번, 재질 입력) 버튼 (선택 시: 테두리 두꺼운 파란색, 채우기 흰색)
            if (step == BomProcessStep.Step2DrawingNo)
            {
                btnStep2DrawingNo.Background = Brushes.White;
                btnStep2DrawingNo.Foreground = activeTextBrush;
                btnStep2DrawingNo.FontWeight = FontWeights.Bold;
                btnStep2DrawingNo.BorderBrush = activeBorderBrush;
                btnStep2DrawingNo.BorderThickness = new Thickness(2.5);
            }
            else
            {
                btnStep2DrawingNo.Background = Brushes.White;
                btnStep2DrawingNo.Foreground = defaultTextBrush;
                btnStep2DrawingNo.FontWeight = FontWeights.Normal;
                btnStep2DrawingNo.BorderBrush = defaultBorderBrush;
                btnStep2DrawingNo.BorderThickness = new Thickness(1);
            }

            // 4. Step 3 (무언가 썸팅) 버튼 (선택 시: 테두리 두꺼운 파란색, 채우기 흰색)
            if (step == BomProcessStep.Step3Other)
            {
                btnStep3Other.Background = Brushes.White;
                btnStep3Other.Foreground = activeTextBrush;
                btnStep3Other.FontWeight = FontWeights.Bold;
                btnStep3Other.BorderBrush = activeBorderBrush;
                btnStep3Other.BorderThickness = new Thickness(2.5);
            }
            else
            {
                btnStep3Other.Background = Brushes.White;
                btnStep3Other.Foreground = defaultTextBrush;
                btnStep3Other.FontWeight = FontWeights.Normal;
                btnStep3Other.BorderBrush = defaultBorderBrush;
                btnStep3Other.BorderThickness = new Thickness(1);
            }

            switch (step)
            {
                case BomProcessStep.Step1Assy:
                    borderTreeView.Visibility = Visibility.Visible;
                    borderDataGrid.Visibility = Visibility.Collapsed;

                    treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);

                    txtStatusBar.Text = "📌 [① Assy. 정리] BOM을 좌에서 우로 뻗어나가는 직관적인 수평 노드 트리 구조로 표시합니다.";
                    break;

                case BomProcessStep.Summary:
                    borderTreeView.Visibility = Visibility.Collapsed;
                    borderDataGrid.Visibility = Visibility.Visible;

                    colItemNo.Visibility = Visibility.Visible;
                    colIsolate.Visibility = Visibility.Collapsed;
                    colPartName.Visibility = Visibility.Visible;
                    colQty.Visibility = Visibility.Visible;
                    colMaterial.Visibility = Visibility.Visible;
                    colDrawingNo.Visibility = Visibility.Collapsed;
                    colRev.Visibility = Visibility.Visible;
                    colRev.IsReadOnly = true;
                    colExplainer.Visibility = Visibility.Visible;
                    colRemark.Visibility = Visibility.Visible;

                    txtStatusBar.Text = "📌 [Summary 모드] 전체 BOM 요약 화면 (부품명, 수량, Material(적용값), Rev(읽기전용), 설명충, 비고)을 확인합니다.";
                    break;

                case BomProcessStep.Step2DrawingNo:
                    borderTreeView.Visibility = Visibility.Visible;
                    borderDataGrid.Visibility = Visibility.Collapsed;

                    treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);

                    txtStatusBar.Text = "📌 [② 도번, 재질 입력] 어셈블리 및 파트 수평 노드 트리에서 도번 및 재질을 입력/관리합니다.";
                    break;

                case BomProcessStep.Step3Other:
                    borderTreeView.Visibility = Visibility.Collapsed;
                    borderDataGrid.Visibility = Visibility.Visible;

                    colItemNo.Visibility = Visibility.Visible;
                    colIsolate.Visibility = Visibility.Visible;
                    colPartName.Visibility = Visibility.Visible;
                    colQty.Visibility = Visibility.Visible;
                    colMaterial.Visibility = Visibility.Visible;
                    colDrawingNo.Visibility = Visibility.Visible;
                    colRev.Visibility = Visibility.Visible;
                    colRev.IsReadOnly = false;
                    colExplainer.Visibility = Visibility.Visible;
                    colRemark.Visibility = Visibility.Visible;

                    txtStatusBar.Text = "📌 [③ 무언가 썸팅(개발중)] 재질(Material), 도번, 설명충, 비고(REMARK)를 확인하고 편집합니다.";
                    break;
            }
        }

        private void BtnBatchDrawingNo_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("도면번호를 일괄 지정할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchDrawingNoDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var item in selected)
                {
                    item.DrawingNo = dlg.DrawingNoText;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 도면번호를 '{dlg.DrawingNoText}'(으)로 일괄 변경했습니다.";
            }
        }

        private void BtnBatchRev_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Revision(Rev.)을 일괄 지정할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchRevDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var item in selected)
                {
                    item.Rev = dlg.RevText;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 Rev.를 '{dlg.RevText}'(으)로 일괄 변경했습니다.";
            }
        }

        private void BtnBatchExplanation_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("설명충을 일괄 변경할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchExplanationDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var item in selected)
                {
                    item.Explanation = dlg.ExplanationText;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 설명충을 '{dlg.ExplanationText}'(으)로 일괄 변경했습니다.";
            }
        }

        private void BtnBatchAssyCategory_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Assy. 종류를 일괄 지정할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchAssyCategoryDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedAssyCategory))
            {
                foreach (var item in selected)
                {
                    item.AssyCategory = dlg.SelectedAssyCategory;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 Assy. 종류를 '{dlg.SelectedAssyCategory}'(으)로 일괄 변경했습니다.";
            }
        }

        private void BtnBatchMaterial_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("재질을 일괄 변경할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchMaterialDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedMaterial))
            {
                foreach (var item in selected)
                {
                    item.Material = dlg.SelectedMaterial;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 재질을 '{dlg.SelectedMaterial}'(으)로 일괄 변경했습니다.";
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0)
            {
                SetProcessStep(BomProcessStep.Summary);
                return;
            }

            var modified = _allItems.Where(i => i.IsModified).ToList();
            if (modified.Count > 0)
            {
                txtStatusBar.Text = $"{modified.Count}개 파트의 속성을 SolidWorks 모델에 저장 중...";
                var (successCnt, failCnt, errors) = _swService.ApplyPropertiesToSolidWorks(modified);
                UpdateStatistics();

                if (failCnt > 0)
                {
                    txtStatusBar.Text = $"⚠️ SolidWorks 저장 결과: {successCnt}개 성공, {failCnt}개 실패";
                }
                else
                {
                    txtStatusBar.Text = $"✅ {successCnt}개 파트의 모든 수정 사항이 SolidWorks에 저장/적용되었습니다.";
                }
            }
            else
            {
                txtStatusBar.Text = "ℹ️ 변경된 속성이 없습니다. Summary 요약 화면으로 이동합니다.";
            }

            // 누르면 Summary에서 수정된 내용으로 볼 수 있도록 Summary 모드로 즉시 전환
            SetProcessStep(BomProcessStep.Summary);
        }

        private void TreeGraphView_ItemReparented(object? sender, (BOMItem DraggedItem, BOMItem TargetParentItem) e)
        {
            var (draggedItem, targetParent) = e;
            if (draggedItem == null || targetParent == null) return;
            if (draggedItem == targetParent) return;

            // 1. Find dragged item and its contiguous children in _allItems
            int draggedIdx = _allItems.IndexOf(draggedItem);
            if (draggedIdx < 0) return;

            int draggedLevel = draggedItem.Level;
            var draggedBlock = new List<BOMItem> { draggedItem };

            int checkIdx = draggedIdx + 1;
            while (checkIdx < _allItems.Count && _allItems[checkIdx].Level > draggedLevel)
            {
                draggedBlock.Add(_allItems[checkIdx]);
                checkIdx++;
            }

            // Target Level
            bool isMasterRoot = targetParent.Level == 0 && targetParent.ItemNo == 0;
            int targetLevel = isMasterRoot ? 1 : targetParent.Level + 1;
            int levelDelta = targetLevel - draggedLevel;

            // Adjust levels for dragged item and all descendants
            foreach (var itm in draggedBlock)
            {
                itm.Level = Math.Max(1, itm.Level + levelDelta);
                itm.CheckModified();
            }

            // Update subassembly / category context if parent has category
            if (!string.IsNullOrEmpty(targetParent.AssyCategory))
            {
                foreach (var itm in draggedBlock)
                {
                    itm.UpdateAvailableAssyCategories(targetParent.AssyCategory);
                }
            }

            // 2. Remove draggedBlock from _allItems
            _allItems.RemoveRange(draggedIdx, draggedBlock.Count);

            // 3. Find target parent insertion point
            int insertIdx;
            if (isMasterRoot)
            {
                insertIdx = _allItems.Count;
            }
            else
            {
                int targetIdx = _allItems.IndexOf(targetParent);
                if (targetIdx < 0)
                {
                    insertIdx = _allItems.Count;
                }
                else
                {
                    // Insert after target parent and all its existing descendants
                    insertIdx = targetIdx + 1;
                    while (insertIdx < _allItems.Count && _allItems[insertIdx].Level > targetParent.Level)
                    {
                        insertIdx++;
                    }
                }
            }

            _allItems.InsertRange(insertIdx, draggedBlock);

            // 4. Renumber ItemNo (1..N)
            for (int i = 0; i < _allItems.Count; i++)
            {
                _allItems[i].ItemNo = i + 1;
            }

            // Expand target parent if it was collapsed
            targetParent.IsExpanded = true;

            // 5. Refresh UI
            UpdateDisplayedItems();
            UpdateStatistics();
            treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);

            txtStatusBar.Text = $"✅ '{draggedItem.PartName}'이(가) '{targetParent.PartName}' 하위로 이동되었습니다.";
        }

        private void TreeGraphView_CreateSubAssyRequested(object? sender, BOMItem? targetParent)
        {
            string parentName = targetParent != null ? targetParent.PartName : "Root Assy.";
            string? parentCat = targetParent?.AssyCategory;
            var dlg = new CreateSubAssyDialog(parentName, parentCat) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                int newLevel = (targetParent != null && targetParent.ItemNo > 0) ? targetParent.Level + 1 : 1;
                var newSub = new BOMItem(
                    itemNo: _allItems.Count + 1,
                    partName: dlg.SubAssyName,
                    qty: 1,
                    drawingNo: dlg.DrawingNo,
                    explanation: dlg.Explanation,
                    isSubassembly: true,
                    level: newLevel,
                    assyCategory: dlg.SubAssyName,
                    remark: "수동 생성된 Sub-Assy"
                )
                {
                    IsUserCreated = true,
                    IsExpanded = true
                };
                newSub.CheckModified();

                if (targetParent != null && targetParent.ItemNo > 0)
                {
                    int pIdx = _allItems.IndexOf(targetParent);
                    if (pIdx >= 0)
                    {
                        int ins = pIdx + 1;
                        while (ins < _allItems.Count && _allItems[ins].Level > targetParent.Level) ins++;
                        _allItems.Insert(ins, newSub);
                    }
                    else
                    {
                        _allItems.Add(newSub);
                    }
                    targetParent.IsExpanded = true;
                }
                else
                {
                    _allItems.Add(newSub);
                }

                // Re-index
                for (int i = 0; i < _allItems.Count; i++)
                {
                    _allItems[i].ItemNo = i + 1;
                }

                UpdateDisplayedItems();
                UpdateStatistics();
                treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);

                txtStatusBar.Text = $"➕ '{parentName}' 하위에 새 Sub-Assy '{dlg.SubAssyName}'이(가) 생성되었습니다.";
            }
        }

        private void ShowLoadingOverlay(string title = "사본 저장 및 서브어셈블리 구성 중...")
        {
            if (gridLoadingOverlay != null)
            {
                txtLoadingTitle.Text = title;
                gridLoadingOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HideLoadingOverlay()
        {
            if (gridLoadingOverlay != null)
            {
                gridLoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void TreeGraphView_ApplyToFileRequested(object? sender, EventArgs e)
        {
            if (_allItems.Count == 0)
            {
                MessageBox.Show("적용할 부품 데이터가 없습니다. 어셈블리를 먼저 로드해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1. 미승인 항목 검사 (Sub-Assy 승인 여부 및 파트 분류 여부)
            var unapprovedSubs = _allItems.Where(i => i.IsSubassembly && i.Level > 0 && !i.IsApproved).ToList();
            var unassignedParts = _allItems.Where(i => !i.IsSubassembly && string.IsNullOrWhiteSpace(i.AssyCategory)).ToList();

            if (unapprovedSubs.Count > 0 || unassignedParts.Count > 0)
            {
                string unapprovedInfo = "";
                if (unapprovedSubs.Count > 0)
                {
                    unapprovedInfo += $"\n• 미승인 Sub-Assy ({unapprovedSubs.Count}개): " + string.Join(", ", unapprovedSubs.Take(3).Select(s => s.PartName)) + (unapprovedSubs.Count > 3 ? "..." : "");
                }
                if (unassignedParts.Count > 0)
                {
                    unapprovedInfo += $"\n• 미분류 파트 ({unassignedParts.Count}개): " + string.Join(", ", unassignedParts.Take(3).Select(p => p.PartName)) + (unassignedParts.Count > 3 ? "..." : "");
                }

                MessageBox.Show($"⚠️ 승인되지 않은 어셈블리 또는 분류되지 않은 부품이 있습니다.{unapprovedInfo}\n\n모든 항목을 확인하고 [승인] 체크 후 다시 시도해주세요.", "승인 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtStatusBar.Text = "⚠️ 미승인 항목이 있어 정리된 파일 저장이 중단되었습니다. 모든 항목을 승인해주세요.";
                return;
            }

            var subAssies = _allItems.Where(i => i.IsSubassembly && i.ItemNo > 0).ToList();
            if (subAssies.Count == 0)
            {
                MessageBox.Show("구성된 Sub-Assy(서브어셈블리)가 없습니다.\n먼저 [➕ Sub-Assy 만들기] 버튼을 눌러 서브어셈블리를 추가하고 부품을 구성해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string targetDir = @"C:\Temp";
            if (!string.IsNullOrWhiteSpace(_currentAssyInfo.Path))
            {
                try
                {
                    string? d = Path.GetDirectoryName(_currentAssyInfo.Path);
                    if (!string.IsNullOrWhiteSpace(d)) targetDir = d;
                }
                catch { }
            }

            // 로딩 및 SolidWorks 잠금 오버레이 활성화
            ShowLoadingOverlay("사본 저장 및 서브어셈블리 구성 중...");
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // UI 렌더링을 위해 잠깐 양보
                await Task.Delay(60);

                // 2. SolidWorks 모델트리에 서브어셈블리 적용 (초고속 화면/트리 갱신 차단 + CommandInProgress 잠금 모드)
                var result = _swService.ApplySubAssembliesToFile(_allItems, targetDir);

                // 3. Auto_3D 계층 폴더 구조 사본 저장 (사전 인덱스 캐시 기반 O(1) 복사)
                var exportResult = _swService.ExportOrganizedAuto3DFiles(_allItems, targetDir);

                if (result.Success || exportResult.Success)
                {
                    string devTempPath = GetDevTempFilePath();
                    if (File.Exists(devTempPath))
                    {
                        try { SaveDevTempState(devTempPath, _allItems, _currentAssyInfo.Title); } catch { }
                    }

                    CheckSwConnectionAndLoad(silent: true);

                    _isStep1Completed = true;
                    SetProcessStep(BomProcessStep.Step2DrawingNo);

                    HideLoadingOverlay();
                    Mouse.OverrideCursor = null;

                    MessageBox.Show($"✅ 사본이 저장되었습니다.\n\n[저장 위치]\n{exportResult.TargetAuto3DDir}", "사본저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatusBar.Text = $"💾 사본이 저장되었습니다: {exportResult.TargetAuto3DDir}";
                }
                else
                {
                    HideLoadingOverlay();
                    Mouse.OverrideCursor = null;

                    string errMsg = string.Join("\n", result.Messages.Concat(exportResult.Messages));
                    MessageBox.Show($"정리된 파일 저장 실패:\n{errMsg}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtStatusBar.Text = $"❌ 정리된 파일 저장 실패: {errMsg}";
                }
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                Mouse.OverrideCursor = null;

                MessageBox.Show($"사본 저장 중 오류 발생:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatusBar.Text = $"❌ 사본 저장 오류: {ex.Message}";
            }
            finally
            {
                HideLoadingOverlay();
                Mouse.OverrideCursor = null;
            }
        }

        private void TreeGraphView_DevTempRequested(object? sender, EventArgs e)
        {
            string devTempPath = GetDevTempFilePath();

            if (File.Exists(devTempPath))
            {
                // 이미 임시 저장된 파일이 있는 상태에서 한 번 더 누르면 -> 초기화(삭제)
                try
                {
                    File.Delete(devTempPath);
                }
                catch { }

                MessageBox.Show("개발자용 임시저장 데이터가 초기화되었습니다.", "초기화됨", MessageBoxButton.OK, MessageBoxImage.Information);
                txtStatusBar.Text = "🗑️ 개발자용 임시저장 데이터가 초기화되었습니다.";
            }
            else
            {
                // 임시 저장된 파일이 없는 경우 -> 현재 UI의 Assy 정리 설정 상태를 임시 저장
                if (_allItems.Count == 0)
                {
                    MessageBox.Show("임시 저장할 부품 데이터가 없습니다. 어셈블리를 먼저 로드해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    SaveDevTempState(devTempPath, _allItems, _currentAssyInfo.Title);
                    MessageBox.Show("현재 Assy. 정리 설정이 임시 저장되었습니다.\n다음에 프로그램을 실행할 때 이 상태가 자동으로 불러와집니다.\n\n(한 번 더 누르면 초기화됩니다.)", "임시 저장됨", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatusBar.Text = $"💾 개발자용 임시 저장 완료 ({_allItems.Count}개 항목, 다음 실행 시 자동 복원)";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"임시 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnBatchRemark_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("비고를 일괄 변경할 파트 행을 먼저 선택해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BatchRemarkDialog(selected.Count) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var item in selected)
                {
                    item.Remark = dlg.RemarkText;
                    item.CheckModified();
                }
                UpdateStatistics();
                txtStatusBar.Text = $"{selected.Count}개 파트의 비고를 일괄 변경했습니다.";
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0)
            {
                MessageBox.Show("내보낼 BOM 데이터가 없습니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseTitle = Path.GetFileNameWithoutExtension(_currentAssyInfo.Title);
            if (string.IsNullOrEmpty(baseTitle)) baseTitle = "Assembly";

            var sfd = new SaveFileDialog
            {
                Title = "BOM Excel 저장",
                FileName = $"BOM_{baseTitle}.xlsx",
                Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx"
            };

            if (sfd.ShowDialog(this) == true)
            {
                bool ok = BomExporter.ExportToExcel(_allItems, sfd.FileName, _currentAssyInfo.Title);
                if (ok)
                {
                    txtStatusBar.Text = $"Excel 파일 저장 완료: {sfd.FileName}";
                    MessageBox.Show($"BOM 데이터가 Excel 파일로 성공적으로 저장되었습니다.\n\n경로: {sfd.FileName}", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Excel 파일 저장 중 오류가 발생했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0)
            {
                MessageBox.Show("내보낼 BOM 데이터가 없습니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseTitle = Path.GetFileNameWithoutExtension(_currentAssyInfo.Title);
            if (string.IsNullOrEmpty(baseTitle)) baseTitle = "Assembly";

            var sfd = new SaveFileDialog
            {
                Title = "BOM CSV 저장",
                FileName = $"BOM_{baseTitle}.csv",
                Filter = "CSV 파일 (쉼표로 분리) (*.csv)|*.csv"
            };

            if (sfd.ShowDialog(this) == true)
            {
                bool ok = BomExporter.ExportToCsv(_allItems, sfd.FileName);
                if (ok)
                {
                    txtStatusBar.Text = $"CSV 파일 저장 완료: {sfd.FileName}";
                    MessageBox.Show($"BOM 데이터가 CSV 파일로 성공적으로 저장되었습니다.\n\n경로: {sfd.FileName}", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("CSV 파일 저장 중 오류가 발생했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnApplySw_Click(object sender, RoutedEventArgs e)
        {
            BtnApplyAll_Click(sender, e);
        }

        #endregion

        #region State Preservation & Merge

        private class ItemStateSnapshot
        {
            public string PartName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string DrawingNo { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public string Rev { get; set; } = string.Empty;
            public string Explanation { get; set; } = string.Empty;
            public string Remark { get; set; } = string.Empty;
            public string AssyCategory { get; set; } = string.Empty;
            public bool IsCommonPart { get; set; }
            public bool IsModified { get; set; }
            public bool IsExpanded { get; set; }
            public bool IsSubassembly { get; set; }
        }

        private Dictionary<string, ItemStateSnapshot> CaptureCurrentState()
        {
            var stateMap = new Dictionary<string, ItemStateSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (_allItems == null || _allItems.Count == 0) return stateMap;

            foreach (var item in _allItems)
            {
                var snap = new ItemStateSnapshot
                {
                    PartName = item.PartName ?? string.Empty,
                    FilePath = item.FilePath ?? string.Empty,
                    FileName = item.FileName ?? string.Empty,
                    DrawingNo = item.DrawingNo ?? string.Empty,
                    Material = item.Material ?? string.Empty,
                    Rev = item.Rev ?? string.Empty,
                    Explanation = item.Explanation ?? string.Empty,
                    Remark = item.Remark ?? string.Empty,
                    AssyCategory = item.AssyCategory ?? string.Empty,
                    IsCommonPart = item.IsCommonPart,
                    IsModified = item.IsModified,
                    IsExpanded = item.IsExpanded,
                    IsSubassembly = item.IsSubassembly
                };

                // 1. 정규화된 전체 파일 경로 키
                string normPath = SafeNormalizePath(item.FilePath);
                if (!string.IsNullOrEmpty(normPath) && !stateMap.ContainsKey(normPath))
                {
                    stateMap[normPath] = snap;
                }

                // 2. 파일명 키 (FNAME:xxx)
                string fName = SafeGetFileName(item.FilePath);
                if (string.IsNullOrEmpty(fName)) fName = SafeGetFileName(item.FileName);
                if (!string.IsNullOrEmpty(fName))
                {
                    string fKey = "FNAME:" + fName.ToLowerInvariant();
                    if (!stateMap.ContainsKey(fKey))
                    {
                        stateMap[fKey] = snap;
                    }
                }

                // 3. 파트명 키 (NAME:xxx)
                if (!string.IsNullOrWhiteSpace(item.PartName))
                {
                    string pKey = "NAME:" + (item.PartName ?? "").Trim().ToLowerInvariant();
                    if (!stateMap.ContainsKey(pKey))
                    {
                        stateMap[pKey] = snap;
                    }
                }
            }
            return stateMap;
        }

        private void RestoreAndMergeState(List<BOMItem> newItems, Dictionary<string, ItemStateSnapshot>? stateMap)
        {
            if (newItems == null || newItems.Count == 0 || stateMap == null || stateMap.Count == 0) return;

            var usedSubSnapshots = new HashSet<ItemStateSnapshot>();

            foreach (var item in newItems)
            {
                ItemStateSnapshot? snap = null;

                // 1순위: 파일 전체 경로 매칭
                string normPath = SafeNormalizePath(item.FilePath);
                if (!string.IsNullOrEmpty(normPath) && stateMap.TryGetValue(normPath, out var s1))
                {
                    snap = s1;
                }

                // 2순위: 파일명 매칭
                if (snap == null)
                {
                    string fName = SafeGetFileName(item.FilePath);
                    if (string.IsNullOrEmpty(fName)) fName = SafeGetFileName(item.FileName);
                    if (!string.IsNullOrEmpty(fName) && stateMap.TryGetValue("FNAME:" + fName.ToLowerInvariant(), out var s2))
                    {
                        snap = s2;
                    }
                }

                // 3순위: 파트명 매칭
                if (snap == null && !string.IsNullOrWhiteSpace(item.PartName))
                {
                    if (stateMap.TryGetValue("NAME:" + (item.PartName ?? "").Trim().ToLowerInvariant(), out var s3))
                    {
                        snap = s3;
                    }
                }

                // 4순위: 새로 생성된 서브어셈블리가 '어셈블리1', 'Assembly1' 등의 기본 이름으로 로드된 경우
                if (snap == null && item.IsSubassembly)
                {
                    string pUpper = (item.PartName ?? "").Trim().ToUpperInvariant();
                    if (pUpper.StartsWith("어셈블리") || pUpper.StartsWith("ASSEMBLY") || pUpper.StartsWith("SUBASSY") || pUpper.Contains("^"))
                    {
                        var unassignedSub = stateMap.Values.FirstOrDefault(s => s.IsSubassembly && !string.IsNullOrWhiteSpace(s.PartName) &&
                            !s.PartName.StartsWith("어셈블리", StringComparison.OrdinalIgnoreCase) &&
                            !s.PartName.StartsWith("ASSEMBLY", StringComparison.OrdinalIgnoreCase) &&
                            !usedSubSnapshots.Contains(s));
                        if (unassignedSub != null)
                        {
                            snap = unassignedSub;
                            usedSubSnapshots.Add(unassignedSub);
                            item.PartName = unassignedSub.PartName;
                        }
                    }
                }

                if (snap != null)
                {
                    // 사용자가 입력/수정한 값 우선 복원
                    if (!string.IsNullOrWhiteSpace(snap.PartName) && item.IsSubassembly && (item.PartName?.StartsWith("어셈블리", StringComparison.OrdinalIgnoreCase) == true || item.PartName?.StartsWith("ASSEMBLY", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        item.PartName = snap.PartName;
                    }
                    if (!string.IsNullOrWhiteSpace(snap.DrawingNo)) item.DrawingNo = snap.DrawingNo;
                    if (!string.IsNullOrWhiteSpace(snap.Material)) item.Material = snap.Material;
                    if (!string.IsNullOrWhiteSpace(snap.Rev)) item.Rev = snap.Rev;
                    if (!string.IsNullOrWhiteSpace(snap.Explanation)) item.Explanation = snap.Explanation;
                    if (!string.IsNullOrWhiteSpace(snap.Remark)) item.Remark = snap.Remark;
                    if (!string.IsNullOrWhiteSpace(snap.AssyCategory)) item.AssyCategory = snap.AssyCategory;
                    item.IsCommonPart = snap.IsCommonPart;
                    item.IsExpanded = snap.IsExpanded;
                    if (snap.IsModified)
                    {
                        item.IsModified = true;
                    }
                }
            }
        }

        private static string SafeNormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path).Trim().ToLowerInvariant();
            }
            catch
            {
                return (path ?? "").Trim().Replace('/', '\\').ToLowerInvariant();
            }
        }

        private static string SafeGetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFileName(path) ?? string.Empty;
            }
            catch
            {
                if (path == null) return string.Empty;
                int lastSlash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                if (lastSlash >= 0 && lastSlash < path.Length - 1)
                {
                    return path.Substring(lastSlash + 1);
                }
                return path;
            }
        }

        #endregion

        #region Context Menu

        private void MenuToggleTree_Click(object sender, RoutedEventArgs e)
        {
            if (dgBom.SelectedItem is BOMItem item && item.IsSubassembly)
            {
                item.IsExpanded = !item.IsExpanded;
                UpdateDisplayedItems();
            }
        }

        private void MenuIsolate_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            if (selected.Count > 0)
            {
                _swService.SetComponentsTransparency(selected, _allItems, isolateMode: true);
                txtStatusBar.Text = $"SolidWorks 화면에 {selected.Count}개 부품이 불투명(👁️ 눈 뜸)하게 강조되었습니다.";
            }
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            if (dgBom.SelectedItem is BOMItem item)
            {
                Clipboard.SetText($"{item.ItemNo}\t{item.PartName}\t{item.Qty}\t{item.AssyCategory}\t{item.Material}\t{item.DrawingNo}\t{item.Rev}\t{item.Explanation}\t{item.Remark}");
            }
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (dgBom.SelectedItem is BOMItem item && !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
        }

        private void MenuReset_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgBom.SelectedItems.OfType<BOMItem>().ToList();
            foreach (var item in selected)
            {
                item.ResetToOriginal();
            }
            UpdateStatistics();
            txtStatusBar.Text = $"{selected.Count}개 파트를 원래 값으로 되돌렸습니다.";
        }

        #endregion

        #region Developer Temp State Helper Methods

        private static string GetDevTempFilePath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(appData, "BOM_Manager");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return Path.Combine(dir, "dev_temp_state.json");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev_temp_state.json");
            }
        }

        private static void SaveDevTempState(string filePath, IList<BOMItem> items, string? rootTitle)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"RootTitle\": \"{EscapeJson(rootTitle ?? "")}\",");
            sb.AppendLine($"  \"SavedTime\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",");
            sb.AppendLine("  \"Items\": [");

            for (int i = 0; i < items.Count; i++)
            {
                var itm = items[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"ItemNo\": {itm.ItemNo},");
                sb.AppendLine($"      \"PartName\": \"{EscapeJson(itm.PartName)}\",");
                sb.AppendLine($"      \"FilePath\": \"{EscapeJson(itm.FilePath)}\",");
                sb.AppendLine($"      \"FileName\": \"{EscapeJson(itm.FileName)}\",");
                sb.AppendLine($"      \"DrawingNo\": \"{EscapeJson(itm.DrawingNo)}\",");
                sb.AppendLine($"      \"Material\": \"{EscapeJson(itm.Material)}\",");
                sb.AppendLine($"      \"Qty\": {itm.Qty},");
                sb.AppendLine($"      \"Rev\": \"{EscapeJson(itm.Rev)}\",");
                sb.AppendLine($"      \"Explanation\": \"{EscapeJson(itm.Explanation)}\",");
                sb.AppendLine($"      \"Remark\": \"{EscapeJson(itm.Remark)}\",");
                sb.AppendLine($"      \"AssyCategory\": \"{EscapeJson(itm.AssyCategory)}\",");
                sb.AppendLine($"      \"IsSubassembly\": {(itm.IsSubassembly ? "true" : "false")},");
                sb.AppendLine($"      \"Level\": {itm.Level},");
                sb.AppendLine($"      \"IsApproved\": {(itm.IsApproved ? "true" : "false")},");
                sb.AppendLine($"      \"IsExpanded\": {(itm.IsExpanded ? "true" : "false")},");
                sb.AppendLine($"      \"IsCommonPart\": {(itm.IsCommonPart ? "true" : "false")},");
                sb.AppendLine($"      \"IsUserCreated\": {(itm.IsUserCreated ? "true" : "false")},");
                sb.AppendLine($"      \"Configuration\": \"{EscapeJson(itm.Configuration)}\"");
                sb.Append("    }");
                if (i < items.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static List<BOMItem> LoadDevTempState(string filePath)
        {
            var list = new List<BOMItem>();
            if (!File.Exists(filePath)) return list;

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            int itemsStart = json.IndexOf("\"Items\"", StringComparison.OrdinalIgnoreCase);
            if (itemsStart < 0) return list;

            int arrayStart = json.IndexOf('[', itemsStart);
            int arrayEnd = json.LastIndexOf(']');
            if (arrayStart < 0 || arrayEnd <= arrayStart) return list;

            string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

            int pos = 0;
            while (pos < arrayContent.Length)
            {
                int openBrace = arrayContent.IndexOf('{', pos);
                if (openBrace < 0) break;
                int closeBrace = arrayContent.IndexOf('}', openBrace);
                if (closeBrace < 0) break;

                string block = arrayContent.Substring(openBrace + 1, closeBrace - openBrace - 1);
                pos = closeBrace + 1;

                var dict = ParseJsonBlock(block);
                if (dict.Count == 0) continue;

                int itemNo = dict.TryGetValue("ItemNo", out var sItemNo) && int.TryParse(sItemNo, out int parsedNo) ? parsedNo : list.Count + 1;
                string partName = dict.TryGetValue("PartName", out var sPartName) ? UnescapeJson(sPartName) : "";
                string filePathVal = dict.TryGetValue("FilePath", out var sFilePath) ? UnescapeJson(sFilePath) : "";
                string fileName = dict.TryGetValue("FileName", out var sFileName) ? UnescapeJson(sFileName) : "";
                string drawingNo = dict.TryGetValue("DrawingNo", out var sDrawingNo) ? UnescapeJson(sDrawingNo) : "";
                string material = dict.TryGetValue("Material", out var sMaterial) ? UnescapeJson(sMaterial) : "";
                int qty = dict.TryGetValue("Qty", out var sQty) && int.TryParse(sQty, out int parsedQty) ? parsedQty : 1;
                string rev = dict.TryGetValue("Rev", out var sRev) ? UnescapeJson(sRev) : "";
                string explanation = dict.TryGetValue("Explanation", out var sExplanation) ? UnescapeJson(sExplanation) : "";
                string remark = dict.TryGetValue("Remark", out var sRemark) ? UnescapeJson(sRemark) : "";
                string assyCategory = dict.TryGetValue("AssyCategory", out var sAssyCat) ? UnescapeJson(sAssyCat) : "";
                bool isSub = dict.TryGetValue("IsSubassembly", out var sIsSub) && bool.TryParse(sIsSub, out bool parsedSub) && parsedSub;
                int level = dict.TryGetValue("Level", out var sLevel) && int.TryParse(sLevel, out int parsedLevel) ? parsedLevel : 0;
                bool isApproved = dict.TryGetValue("IsApproved", out var sIsApp) && bool.TryParse(sIsApp, out bool parsedApp) && parsedApp;
                bool isExpanded = dict.TryGetValue("IsExpanded", out var sIsExp) && bool.TryParse(sIsExp, out bool parsedExp) && parsedExp;
                bool isCommon = dict.TryGetValue("IsCommonPart", out var sIsCom) && bool.TryParse(sIsCom, out bool parsedCom) && parsedCom;
                bool isUserCreated = (dict.TryGetValue("IsUserCreated", out var sIsUc) && bool.TryParse(sIsUc, out bool parsedUc) && parsedUc) || (!string.IsNullOrEmpty(remark) && remark.Contains("수동 생성된"));
                string config = dict.TryGetValue("Configuration", out var sConfig) ? UnescapeJson(sConfig) : "Default";

                var item = new BOMItem(
                    itemNo: itemNo,
                    partName: partName,
                    material: material,
                    qty: qty,
                    remark: remark,
                    filePath: filePathVal,
                    isSubassembly: isSub,
                    level: level,
                    drawingNo: drawingNo,
                    explanation: explanation,
                    assyCategory: assyCategory
                )
                {
                    Rev = rev,
                    FileName = fileName,
                    Configuration = config,
                    IsCommonPart = isCommon,
                    IsApproved = isApproved,
                    IsExpanded = isExpanded,
                    IsUserCreated = isUserCreated
                };

                if (isApproved || !string.IsNullOrEmpty(assyCategory))
                {
                    item.CheckModified();
                }

                list.Add(item);
            }

            return SanitizeItemList(list);
        }

        private static List<BOMItem> SanitizeItemList(List<BOMItem> rawList)
        {
            if (rawList == null || rawList.Count == 0) return new List<BOMItem>();

            var result = new List<BOMItem>();
            var parentStack = new Stack<(BOMItem Item, int OriginalLevel, int NewLevel)>();

            for (int i = 0; i < rawList.Count; i++)
            {
                var cur = rawList[i];
                while (parentStack.Count > 0 && parentStack.Peek().OriginalLevel >= cur.Level)
                {
                    parentStack.Pop();
                }

                bool isDuplicateOfParent = false;
                if (cur.IsSubassembly && parentStack.Count > 0)
                {
                    var parent = parentStack.Peek().Item;
                    string cleanCur = SwConnector.CleanSingleName(cur.PartName);
                    string cleanParent = SwConnector.CleanSingleName(parent.PartName);
                    if (!string.IsNullOrEmpty(cleanCur) && string.Equals(cleanCur, cleanParent, StringComparison.OrdinalIgnoreCase))
                    {
                        isDuplicateOfParent = true;
                    }
                }

                if (isDuplicateOfParent)
                {
                    // 중복 서브어셈블리 래퍼 건너뜀 및 하위 자식들의 레벨 1단계 축소 보정
                    int duplicateLevel = cur.Level;
                    int nextIdx = i + 1;
                    while (nextIdx < rawList.Count && rawList[nextIdx].Level > duplicateLevel)
                    {
                        rawList[nextIdx].Level = Math.Max(parentStack.Peek().NewLevel + 1, rawList[nextIdx].Level - 1);
                        nextIdx++;
                    }
                    continue;
                }

                int targetLevel = cur.Level;
                if (parentStack.Count > 0)
                {
                    targetLevel = parentStack.Peek().NewLevel + 1;
                }
                else if (cur.Level > 0)
                {
                    targetLevel = cur.Level;
                }
                else
                {
                    targetLevel = 0;
                }

                cur.Level = targetLevel;
                result.Add(cur);

                if (cur.IsSubassembly)
                {
                    parentStack.Push((cur, cur.Level, targetLevel));
                }
            }

            for (int i = 0; i < result.Count; i++)
            {
                result[i].ItemNo = i + 1;
            }

            return result;
        }

        private static Dictionary<string, string> ParseJsonBlock(string block)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim().TrimEnd(',');
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    string key = trimmed.Substring(0, colonIdx).Trim().Trim('"');
                    string val = trimmed.Substring(colonIdx + 1).Trim();
                    if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                    {
                        val = val.Substring(1, val.Length - 2);
                    }
                    dict[key] = val;
                }
            }
            return dict;
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\r", "\\r")
                      .Replace("\n", "\\n")
                      .Replace("\t", "\\t");
        }

        private static string UnescapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\\"", "\"")
                      .Replace("\\r", "\r")
                      .Replace("\\n", "\n")
                      .Replace("\\t", "\t")
                      .Replace("\\\\", "\\");
        }

        #endregion
    }
}
