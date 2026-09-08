using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BOMManager.Modules.SpringDesigner.Models;

namespace BOMManager.Modules.SpringDesigner.Views
{
    public partial class SpringStandardizationWindow : Window
    {
        public StandardizationCandidateModel? SelectedCandidate { get; private set; }

        public SpringStandardizationWindow(List<StandardizationCandidateModel> candidates)
        {
            InitializeComponent();
            CandidatesItemsControl.ItemsSource = candidates;
        }

        private void BtnSelectCandidate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StandardizationCandidateModel candidate)
            {
                SelectedCandidate = candidate;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
