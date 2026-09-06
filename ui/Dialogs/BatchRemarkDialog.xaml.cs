using System.Windows;

namespace BOMManager.UI.Dialogs
{
    public partial class BatchRemarkDialog : Window
    {
        public string RemarkText { get; private set; } = string.Empty;

        public BatchRemarkDialog(int count = 1)
        {
            InitializeComponent();
            lblInfo.Text = $"선택한 {count}개 파트에 적용할 비고(REMARK)를 입력하세요:";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            RemarkText = txtRemark.Text.Trim();
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
