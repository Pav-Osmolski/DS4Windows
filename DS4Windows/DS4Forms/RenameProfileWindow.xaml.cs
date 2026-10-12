/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System.Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for RenameProfileWindow.xaml
    /// </summary>
    public partial class RenameProfileWindow : Window
    {
        private RenameProfileViewModel renameProfileVM;
        private ProfileEntity profile;
        public RenameProfileViewModel RenameProfileVM
        {
            get => renameProfileVM;
        }

        public RenameProfileWindow()
        {
            InitializeComponent();

            renameProfileVM = new RenameProfileViewModel();
            mainDockPanel.DataContext = renameProfileVM;
        }

        /// <summary>
        /// Method used to carry over current name of profile and copy
        /// it to ViewModel
        /// </summary>
        /// <param name="profileName">name of current profile</param>
        public void ChangeProfileName(ProfileEntity entity)
        {
            profile = entity;
            renameProfileVM.ProfileName = entity.Name;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (profile == null) return;
            if (!profile.RenameProfile(renameProfileVM.ProfileName, out string error))
            {
                e.Handled = true;
                MessageBox.Show(this, error, "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
        }

        private void Window_Closed(object sender, System.EventArgs e)
        {
            mainDockPanel.DataContext = null;
        }
    }
}
