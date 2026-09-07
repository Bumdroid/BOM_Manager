using System;
using System.Windows;
using System.Windows.Controls;

namespace BOMManager.UI.Dialogs
{
    public partial class CreateSubAssyDialog : Window
    {
        public string SubAssyName { get; private set; } = string.Empty;
        public string DrawingNo { get; private set; } = string.Empty;
        public string Explanation { get; private set; } = string.Empty;

        public CreateSubAssyDialog(string parentName = "Root Assy.", string? parentCategory = null, string defaultName = "")
        {
            InitializeComponent();
            txtParentInfo.Text = $"📍 생성 위치 (상위 어셈블리): {parentName}";

            cboSubAssyName.Items.Clear();

            string pCat = (parentCategory ?? "").ToUpperInvariant();
            string pName = (parentName ?? "").ToUpperInvariant();

            System.Collections.Generic.List<string> options;
            if (pCat.Contains("ELASTOMER") || pName.Contains("ELASTOMER"))
            {
                options = new System.Collections.Generic.List<string> { "FRAME ASSY", "BOTTOM COVER ASSY" };
            }
            else
            {
                options = new System.Collections.Generic.List<string> { "LID ASSY", "ELASTOMER ASSY", "BSS ASSY" };
            }

            foreach (var opt in options)
            {
                cboSubAssyName.Items.Add(new ComboBoxItem { Content = opt });
            }

            // Select defaultName if found, else first option
            int selectIdx = 0;
            if (!string.IsNullOrEmpty(defaultName))
            {
                for (int i = 0; i < cboSubAssyName.Items.Count; i++)
                {
                    if (cboSubAssyName.Items[i] is ComboBoxItem item &&
                        string.Equals(item.Content as string, defaultName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectIdx = i;
                        break;
                    }
                }
            }

            if (cboSubAssyName.Items.Count > 0)
            {
                cboSubAssyName.SelectedIndex = selectIdx;
            }
            cboSubAssyName.Focus();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string name = "";
            if (cboSubAssyName.SelectedItem is ComboBoxItem cbi)
            {
                name = cbi.Content as string ?? "";
            }
            else
            {
                name = (cboSubAssyName.Text ?? "").Trim();
            }

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Sub-Assy 이름을 선택해주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                cboSubAssyName.Focus();
                return;
            }

            SubAssyName = name;
            DrawingNo = "";
            Explanation = txtExplanation.Text.Trim();

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
