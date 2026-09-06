using System.Windows;

namespace BOMManager.UI.Dialogs
{
    public partial class BatchRevDialog : Window
    {
        public string RevText => txtRev.Text.Trim();

        public BatchRevDialog(int selectedCount)
        {
            InitializeComponent();
            lblInfo.Text = $"선택한 {selectedCount}개 파트에 적용할 Revision(Rev.)을 입력하세요:";
            txtRev.Focus();
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
