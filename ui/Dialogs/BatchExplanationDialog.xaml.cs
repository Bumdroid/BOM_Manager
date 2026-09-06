using System.Windows;

namespace BOMManager.UI.Dialogs
{
    public partial class BatchExplanationDialog : Window
    {
        public string ExplanationText => txtExplanation.Text.Trim();

        public BatchExplanationDialog(int selectedCount)
        {
            InitializeComponent();
            lblInfo.Text = $"선택한 {selectedCount}개 파트에 적용할 설명충(Explanation)을 입력하세요:";
            txtExplanation.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
