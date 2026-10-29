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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for DupBox.xaml
    /// </summary>
    public partial class DupBox : UserControl
    {
        private string oldfilename;
        public string OldFilename
        {
            get => oldfilename;
            set
            {
                oldfilename = value;
                sourceProfileName.Text = value;
                profileTxt.Clear();
                validationMessage.Visibility = Visibility.Collapsed;
            }
        }

        public event EventHandler Cancel;
        public delegate void SaveHandler(DupBox sender, string profilename);
        public event SaveHandler Save;

        public DupBox()
        {
            InitializeComponent();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string profile;
            try { profile = DS4Windows.ProfileName.Normalize(profileTxt.Text); }
            catch (ArgumentException)
            {
                ShowValidation(Properties.Resources.ResourceManager.GetString("ProfileNameInvalid"));
                return;
            }

            string directory = System.IO.Path.Combine(DS4Windows.Global.appdatapath, "Profiles");
            string destination = System.IO.Path.Combine(directory, profile + ".xml");
            if (System.IO.File.Exists(destination))
            {
                ShowValidation(Properties.Resources.ResourceManager.GetString("ProfileRenameCollision"));
                return;
            }
            try
            {
                // A duplicate must never replace the source or another existing profile.
                // The persistence helper also protects a collision after preflight.
                DS4Windows.ProfilePersistence.Save(destination,
                    System.IO.File.ReadAllText(System.IO.Path.Combine(directory, oldfilename + ".xml")),
                    overwrite: false);
            }
            catch (Exception exception) when (exception is System.IO.IOException ||
                exception is UnauthorizedAccessException)
            {
                ShowValidation(exception.Message);
                return;
            }
            Save?.Invoke(this, profile);
        }

        private void ShowValidation(string message)
        {
            validationMessage.Text = message;
            validationMessage.Visibility = Visibility.Visible;
            profileTxt.Focus();
            profileTxt.SelectAll();
        }

        private void ProfileTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (validationMessage != null)
                validationMessage.Visibility = Visibility.Collapsed;
        }

        private void DuplicatePrompt_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (IsVisible) { profileTxt.Focus(); profileTxt.SelectAll(); }
                }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void DuplicatePrompt_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (cancelBtn.IsKeyboardFocusWithin) Cancel?.Invoke(this, EventArgs.Empty);
                else SaveBtn_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Cancel?.Invoke(this, EventArgs.Empty);
        }
    }
}
