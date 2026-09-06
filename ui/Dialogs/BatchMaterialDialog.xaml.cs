using System.Windows;

namespace BOMManager.UI.Dialogs
{
    public partial class BatchMaterialDialog : Window
    {
        public string SelectedMaterial { get; private set; } = string.Empty;

        public BatchMaterialDialog(int count = 1)
        {
            InitializeComponent();
            lblInfo.Text = $"선택한 {count}개 파트에 적용할 재질을 선택하거나 입력하세요:";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedMaterial = comboMaterial.Text.Trim();
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
