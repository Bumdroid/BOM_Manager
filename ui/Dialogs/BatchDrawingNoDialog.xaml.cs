using System.Windows;

namespace BOMManager.UI.Dialogs
{
    public partial class BatchDrawingNoDialog : Window
    {
        public string DrawingNoText => txtDrawingNo.Text.Trim();

        public BatchDrawingNoDialog(int selectedCount)
        {
            InitializeComponent();
            lblInfo.Text = $"선택한 {selectedCount}개 파트에 적용할 도면번호(Drawing No.)를 입력하세요:";
            txtDrawingNo.Focus();
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
