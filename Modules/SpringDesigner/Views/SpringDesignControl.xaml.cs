using System;
using System.Windows;
using System.Windows.Controls;
using BOMManager.Core;
using BOMManager.Modules.SpringDesigner.Models;
using BOMManager.Modules.SpringDesigner.Services;
using BOMManager.Modules.SpringDesigner.ViewModels;


namespace BOMManager.Modules.SpringDesigner.Views
{
    public partial class SpringDesignControl : System.Windows.Controls.UserControl
    {
        public SpringDesignerViewModel ViewModel { get; }
        public Action? RequestClose { get; set; }

        public SpringDesignControl()
        {
            InitializeComponent();
            ViewModel = new SpringDesignerViewModel();
            DataContext = ViewModel;
        }

        private void BtnDraw_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedElementBinding();

            if (ViewModel.FreeLength <= 0 || ViewModel.P2h <= 0)
            {
                System.Windows.MessageBox.Show(
                    "자유장 (Hs) 및 취부, 장착장 (P2h) 입력값을 모두 올바르게 입력해 주세요.",
                    "입력값 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (ViewModel.FreeLength <= ViewModel.P2h)
            {
                System.Windows.MessageBox.Show(
                    "자유장 (Hs)은 취부, 장착장 (P2h)보다 커야 합니다.",
                    "입력값 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // [⚡ 신작 해줘] 또는 [⚡ 해줘 공용화]를 실행하지 않은 상태에서 도면 생성 클릭 시 현 사양 평가 NG 항목 확인
            if (!ViewModel.HasExecutedAction)
            {
                if (ViewModel.HasNgEvaluation)
                {
                    var result = System.Windows.MessageBox.Show(
                        "현재 사양 평가에 NG 항목이 있습니다.\n\n도면 생성을 계속 진행하시겠습니까?",
                        "사양 평가 NG 항목 확인",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
            }

            // 파라미터 최신 상태 동기화
            ViewModel.Parameters.IsStandardized = ViewModel.IsStandardized;
            ViewModel.Parameters.MatchedDrawingNo = ViewModel.MatchedDrawingNo;
            ViewModel.Parameters.MatchedPartNumber = ViewModel.MatchedPartNumber;

            // 2D CAD 도면 자동 생성 명령 AutoCAD로 전송 (신작 및 공용화 모두 지원)
            bool success = SpringCadService.DrawElement(ViewModel.Parameters, out string message);

            if (!success)
            {
                System.Windows.MessageBox.Show(message, "도면 생성 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // 도면 생성 성공 시 창을 닫지 않고 상태를 유지합니다.
        }

        private void CommitFocusedElementBinding()
        {
            try
            {
                var focused = System.Windows.Input.FocusManager.GetFocusedElement(this) as UIElement;
                if (focused is System.Windows.Controls.TextBox tb)
                {
                    var binding = tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                    binding?.UpdateSource();
                }
            }
            catch { }
        }

        private void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedElementBinding();
            ViewModel.ExecuteOptimization();
        }

        private void BtnStandardization_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedElementBinding();
            ViewModel.ExecuteStandardizationSearch();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ExecuteUndo();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            RequestClose?.Invoke();
        }

        private void BtnSaveVault_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedElementBinding();
            bool success = ViewModel.SaveVaultConfig(out string message);
            if (success)
            {
                System.Windows.MessageBox.Show(message, "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveElastomerSpec_Click(object sender, RoutedEventArgs e)
        {
            bool success = ViewModel.SaveElastomerSpecConfig(out string message);
            if (success)
            {
                System.Windows.MessageBox.Show(message, "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSavePkgGap_Click(object sender, RoutedEventArgs e)
        {
            bool success = ViewModel.SavePkgGapSpecConfig(out string message);
            if (success)
            {
                System.Windows.MessageBox.Show(message, "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveTemplateOption_Click(object sender, RoutedEventArgs e)
        {
            bool success = ViewModel.SaveTemplateOptionConfig(out string message);
            if (success)
            {
                System.Windows.MessageBox.Show(message, "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveSpringSpec_Click(object sender, RoutedEventArgs e)
        {
            bool success = ViewModel.SaveSpringSpecConfig(out string message);
            if (success)
            {
                System.Windows.MessageBox.Show(message, "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFeedback_Click(object sender, RoutedEventArgs e)
        {
            string defaultAuthor = !string.IsNullOrEmpty(VaultService.CurrentUsername)
                ? VaultService.CurrentUsername
                : (!string.IsNullOrEmpty(ViewModel?.VaultUser) && !ViewModel.VaultUser.Equals("User", StringComparison.OrdinalIgnoreCase)
                    ? ViewModel.VaultUser
                    : (VaultConfigManager.Load().LastUsername ?? "이두규"));
            SpringFeedbackWindow window = new SpringFeedbackWindow(ViewModel, defaultAuthor);
            window.Owner = System.Windows.Window.GetWindow(this);
            window.ShowDialog();
        }
    }
}
