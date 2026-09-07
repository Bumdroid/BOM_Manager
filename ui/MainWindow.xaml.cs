using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private readonly List<BOMItem> _allItems = new();
        private readonly ObservableCollection<BOMItem> _displayedItems = new();
        private AssemblyInfo _currentAssyInfo = new();
        private readonly DispatcherTimer _autoTimer;
        private string _filterText = string.Empty;
        private BomProcessStep _currentStep = BomProcessStep.Summary;

        public MainWindow(ISolidWorksService swService, bool mockMode = false)
        {
            InitializeComponent();

            _swService = swService ?? throw new ArgumentNullException(nameof(swService));
            _mockMode = mockMode;

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

            SetProcessStep(BomProcessStep.Summary);

            // 창이 0.05초 만에 즉시 표시되도록 초기 로딩을 백그라운드로 지연 실행
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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
                // 1. Pepe BOM Logo
                string? logoPath = FindResourceFile("logo.png") ?? FindResourceFile("mainicon_32.png");
                if (logoPath != null && File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    imgLogo.Source = bmp;
                    Icon = bmp;
                }

                // 2. Green CAD Pepe Icon for 제작도 V0.0 (Dummy)
                string? dwgLogoPath = FindResourceFile("pepe_cad_icon_green.jpg") ?? FindResourceFile("pepe_cad_icon_cyan.jpg");
                if (dwgLogoPath != null && File.Exists(dwgLogoPath))
                {
                    var dwgBmp = new BitmapImage();
                    dwgBmp.BeginInit();
                    dwgBmp.UriSource = new Uri(dwgLogoPath, UriKind.Absolute);
                    dwgBmp.CacheOption = BitmapCacheOption.OnLoad;
                    dwgBmp.EndInit();

                    imgDwgLogo.Source = dwgBmp;
                }
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

            // Load BOM Items
            bool topLevel = chkTopLevel.IsChecked == true;
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

            _allItems.Clear();
            foreach (var itm in items)
            {
                itm.IsExpanded = false; // 기본 상태: 모든 서브어셈블리 트리 접힘
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

        private void BtnNavBomManager_Click(object sender, RoutedEventArgs e)
        {
            btnNavBomManager.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
            btnNavBomManager.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            btnNavBomManager.BorderThickness = new Thickness(2);

            btnNavDrawingMaker.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            btnNavDrawingMaker.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            btnNavDrawingMaker.BorderThickness = new Thickness(1);

            SetProcessStep(_currentStep);
        }

        private void BtnNavDrawingMaker_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "📐 [제작도 V0.0 (Dummy)] 모듈 안내:\n\n" +
                "AutoCAD 도면 자동 생성 및 가공/제작도 일괄 출력 기능은 현재 준비 중입니다.\n" +
                "추후 업데이트 시 해당 모듈에서 바로 도면 생성이 진행됩니다.",
                "제작도 V0.0 (Dummy)",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            txtStatusBar.Text = "제작도 V0.0 (Dummy) 모듈: AutoCAD 도면 자동화 준비 중";
        }

        private void ChkTopLevel_Changed(object sender, RoutedEventArgs e)
        {
            CheckSwConnectionAndLoad(initial: false, silent: false);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterText = txtSearch.Text;
            UpdateDisplayedItems();
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
                txtStatusBar.Text = $"SolidWorks 화면에서 '{item.PartName}' 부품이 불투명(🟢)하게 강조되었습니다.";
            }
        }

        private void BtnShowAll_Click(object sender, RoutedEventArgs e)
        {
            _swService.ShowAllOpaque(_allItems);
            txtStatusBar.Text = "SolidWorks 화면의 모든 부품을 불투명(🟢) 상태로 복원했습니다.";
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
            SetProcessStep(BomProcessStep.Step2DrawingNo);
        }

        private void BtnStep3Other_Click(object sender, RoutedEventArgs e)
        {
            SetProcessStep(BomProcessStep.Step3Other);
        }

        public void SetProcessStep(BomProcessStep step)
        {
            _currentStep = step;

            // Step 버튼 기본 스타일 초기화
            btnStep1Assy.Background = Brushes.White;
            btnStep1Assy.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            btnStep1Assy.FontWeight = FontWeights.Normal;
            btnStep1Assy.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

            btnSummary.Background = Brushes.White;
            btnSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            btnSummary.FontWeight = FontWeights.Normal;
            btnSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

            btnStep2DrawingNo.Background = Brushes.White;
            btnStep2DrawingNo.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            btnStep2DrawingNo.FontWeight = FontWeights.Normal;
            btnStep2DrawingNo.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

            btnStep3Other.Background = Brushes.White;
            btnStep3Other.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            btnStep3Other.FontWeight = FontWeights.Normal;
            btnStep3Other.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

            var activeBg = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            var activeBorder = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));

            switch (step)
            {
                case BomProcessStep.Step1Assy:
                    btnStep1Assy.Background = activeBg;
                    btnStep1Assy.Foreground = Brushes.White;
                    btnStep1Assy.FontWeight = FontWeights.Bold;
                    btnStep1Assy.BorderBrush = activeBorder;

                    borderTreeView.Visibility = Visibility.Visible;
                    borderDataGrid.Visibility = Visibility.Collapsed;

                    treeGraphView.LoadItems(_allItems, _swService, _currentAssyInfo.Title);

                    txtStatusBar.Text = "📌 [① Assy. 정리] BOM을 좌에서 우로 뻗어나가는 직관적인 수평 노드 트리 구조로 표시합니다.";
                    break;

                case BomProcessStep.Summary:
                    btnSummary.Background = activeBg;
                    btnSummary.Foreground = Brushes.White;
                    btnSummary.FontWeight = FontWeights.Bold;
                    btnSummary.BorderBrush = activeBorder;

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
                    btnStep2DrawingNo.Background = activeBg;
                    btnStep2DrawingNo.Foreground = Brushes.White;
                    btnStep2DrawingNo.FontWeight = FontWeights.Bold;
                    btnStep2DrawingNo.BorderBrush = activeBorder;

                    borderTreeView.Visibility = Visibility.Collapsed;
                    borderDataGrid.Visibility = Visibility.Visible;

                    colItemNo.Visibility = Visibility.Visible;
                    colIsolate.Visibility = Visibility.Collapsed;
                    colPartName.Visibility = Visibility.Visible;
                    colQty.Visibility = Visibility.Visible;
                    colMaterial.Visibility = Visibility.Collapsed;
                    colDrawingNo.Visibility = Visibility.Visible;
                    colRev.Visibility = Visibility.Visible;
                    colRev.IsReadOnly = false;
                    colExplainer.Visibility = Visibility.Visible;
                    colRemark.Visibility = Visibility.Collapsed;

                    txtStatusBar.Text = "📌 [② 도번 입력] 파트별 도면번호(Drawing No. OOO-PPPPPGBBBXXXX), Revision, 설명충을 입력합니다.";
                    break;

                case BomProcessStep.Step3Other:
                    btnStep3Other.Background = activeBg;
                    btnStep3Other.Foreground = Brushes.White;
                    btnStep3Other.FontWeight = FontWeights.Bold;
                    btnStep3Other.BorderBrush = activeBorder;

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

                    txtStatusBar.Text = "📌 [③ 기타(재질 등..)] 재질(Material), 도번, 설명충, 비고(REMARK)를 확인하고 편집합니다.";
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
                txtStatusBar.Text = $"SolidWorks 화면에 {selected.Count}개 부품이 불투명(🟢)하게 강조되었습니다.";
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
    }
}
