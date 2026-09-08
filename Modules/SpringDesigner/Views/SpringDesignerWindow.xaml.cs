using System;
using System.Windows;
using System.Windows.Input;

namespace BOMManager.Modules.SpringDesigner.Views
{
    /// <summary>
    /// 스프링 설계기 단독 팝업 윈도우
    /// </summary>
    public partial class SpringDesignerWindow : Window
    {
        public SpringDesignerWindow()
        {
            InitializeComponent();

            // 작업 표시줄 및 크기 조정 영역 보정
            double workAreaHeight = SystemParameters.WorkArea.Height;
            double desiredHeight = 900;
            Height = Math.Min(desiredHeight, Math.Max(560, workAreaHeight * 0.94));

            DesignControl.RequestClose = () =>
            {
                Close();
            };

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }
    }
}
