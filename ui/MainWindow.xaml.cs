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
    public enum BomViewMode
    {
        Summary,
        DrawingNo
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
        private BomViewMode _currentViewMode = BomViewMode.Summary;

        public MainWindow(ISolidWorksService swService, bool mockMode = false)
        {
            InitializeComponent();

            _swService = swService ?? throw new ArgumentNullException(nameof(swService));
            _mockMode = mockMode;

            dgBom.ItemsSource = _displayedItems;

            LoadAppLogo();

            _autoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2500)
            };
            _autoTimer.Tick += AutoTimer_Tick;
            _autoTimer.Start();

            SetViewMode(BomViewMode.Summary);
            CheckSwConnectionAndLoad(initial: true);
        }

        private void LoadAppLogo()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logoPath = Path.Combine(baseDir, "resources", "logo.png");
                if (!File.Exists(logoPath))
                {
                    logoPath = Path.Combine(baseDir, "addin", "mainicon_32.png");
                }

                if (File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    imgLogo.Source = bmp;
                    Icon = bmp;
                }
            }
            catch { }
        }

        private void CheckSwConnectionAndLoad(bool initial = false, bool silent = false)
        {
            _currentAssyInfo = _swService.GetActiveAssemblyInfo();

            if (!_currentAssyInfo.IsConnected)
            {
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
                txtStatusBadge.Text = "🔴 SolidWorks 미연결";
                txtStatusBar.Text = _currentAssyInfo.ErrorMessage ?? "SolidWorks 2021이 실행되어 있지 않습니다.";

                if (!initial && !silent && !_mockMode)
                {
                    MessageBox.Show(
                        _currentAssyInfo.ErrorMessage ?? "SolidWorks 2021 연결에 실패했습니다.\n\nSolidWorks가 켜져 있는지 확인해 주세요.",
                        "연결 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (!string.IsNullOrEmpty(_currentAssyInfo.ErrorMessage))
            {
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                txtStatusBadge.Text = $"🟡 SolidWorks 대기 중 ({_currentAssyInfo.Title})";
                txtStatusBar.Text = _currentAssyInfo.ErrorMessage;

                if (!initial && !silent)
                {
                    MessageBox.Show(_currentAssyInfo.ErrorMessage, "문서 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
            borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            txtStatusBadge.Text = $"🟢 {_currentAssyInfo.Title}";

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
                itm.PropertyChanged += Item_PropertyChanged;
                _allItems.Add(itm);
            }

            UpdateDisplayedItems();
            UpdateStatistics();
            txtStatusBar.Text = $"'{_currentAssyInfo.Title}'에서 {items.Count}개 파트 정보를 성공적으로 불러왔습니다.";
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateStatistics();
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
            if (info.IsConnected && string.IsNullOrEmpty(info.ErrorMessage))
            {
                if (info.Title != _currentAssyInfo.Title || _allItems.Count == 0)
                {
                    CheckSwConnectionAndLoad(initial: true, silent: true);
                }
            }
            else if (!info.IsConnected)
            {
                borderStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
                borderStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                txtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
                txtStatusBadge.Text = "🔴 SolidWorks 미연결";
            }
        }

        #region Event Handlers

        private void ChkTopLevel_Changed(object sender, RoutedEventArgs e)
        {
            CheckSwConnectionAndLoad(initial: false, silent: false);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterText = txtSearch.Text;
            UpdateDisplayedItems();
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

        private void BtnModeSummary_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(BomViewMode.Summary);
        }

        private void BtnModeDrawing_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(BomViewMode.DrawingNo);
        }

        public void SetViewMode(BomViewMode mode)
        {
            _currentViewMode = mode;

            if (mode == BomViewMode.Summary)
            {
                btnModeSummary.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                btnModeSummary.Foreground = Brushes.White;
                btnModeSummary.FontWeight = FontWeights.Bold;
                btnModeSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));

                btnModeDrawing.Background = Brushes.White;
                btnModeDrawing.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                btnModeDrawing.FontWeight = FontWeights.Normal;
                btnModeDrawing.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

                colDrawingNo.Visibility = Visibility.Collapsed;
                colMaterial.Visibility = Visibility.Visible;
                colRemark.Visibility = Visibility.Visible;
                colExplainer.Visibility = Visibility.Visible;

                // Summary 모드에서는 Rev. 수정 불가 (Read-Only)
                colRev.IsReadOnly = true;

                txtStatusBar.Text = "Summary 화면 모드: 부품명, 재질, 수량, Rev(읽기전용), 설명충, 비고가 표시됩니다.";
            }
            else
            {
                btnModeSummary.Background = Brushes.White;
                btnModeSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                btnModeSummary.FontWeight = FontWeights.Normal;
                btnModeSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

                btnModeDrawing.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                btnModeDrawing.Foreground = Brushes.White;
                btnModeDrawing.FontWeight = FontWeights.Bold;
                btnModeDrawing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));

                colMaterial.Visibility = Visibility.Collapsed;
                colRemark.Visibility = Visibility.Collapsed;
                colDrawingNo.Visibility = Visibility.Visible;
                colExplainer.Visibility = Visibility.Visible;

                // 도면번호 입력 모드에서는 Rev. 수정 가능 (Editable)
                colRev.IsReadOnly = false;

                txtStatusBar.Text = "도면번호 입력 모드: 부품명, 수량, 도면번호(Drawing No.), Rev.(편집가능), 설명충이 표시됩니다.";
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
            if (_allItems.Count == 0)
            {
                MessageBox.Show("적용할 BOM 항목이 없습니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var modified = _allItems.Where(i => i.IsModified).ToList();
            var targets = modified.Count > 0 ? modified : _allItems;

            var res = MessageBox.Show(
                $"총 {targets.Count}개 파트의 사용자 정의 속성(Custom Properties)에\n" +
                $"입력하신 정보(Name of Part, Drawing No., Material, Q'TY, Rev., 설명충, REMARK)를 SolidWorks 모델에 반영하시겠습니까?",
                "SolidWorks 속성 저장 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            txtStatusBar.Text = "SolidWorks 모델에 속성 저장 중...";
            var (successCnt, failCnt, errors) = _swService.ApplyPropertiesToSolidWorks(targets);

            UpdateStatistics();

            string resultMsg = $"성공: {successCnt}개 파트 반영 완료";
            if (failCnt > 0)
            {
                resultMsg += $"\n실패: {failCnt}개\n\n에러 세부내용:\n" + string.Join("\n", errors.Take(5));
                MessageBox.Show(resultMsg, "속성 저장 결과", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"{successCnt}개 파트의 속성이 SolidWorks에 성공적으로 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            txtStatusBar.Text = $"SolidWorks 속성 저장 완료 (성공: {successCnt}, 실패: {failCnt})";
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
                Clipboard.SetText($"{item.ItemNo}\t{item.PartName}\t{item.DrawingNo}\t{item.Material}\t{item.Qty}\t{item.Rev}\t{item.Explanation}\t{item.Remark}");
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
