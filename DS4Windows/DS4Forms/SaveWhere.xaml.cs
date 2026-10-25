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
using System.IO;
using System.Windows;


namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for SaveWhere.xaml
    /// </summary>
    public partial class SaveWhere : Window
    {
        private bool multisaves;
        public bool ChoiceMade { get; set; }

        public SaveWhere(bool multisavespots)
        {
            if (DS4Windows.PortableLabContext.IsActive)
                throw new InvalidOperationException(global::DS4Windows.UiText.Get("Portable lab configuration location is fixed."));
            InitializeComponent();
            multisaves = multisavespots;
            dontDeleteCk.Visibility = Visibility.Collapsed;
            pickWhereTxt.Text += "\n" + global::DS4Windows.UiText.Get("Both locations are kept. You can change location later on the Profiles page.");
            if (!multisavespots)
            {
                multipleSavesDockP.Visibility = Visibility.Collapsed;
                pickWhereTxt.Text += Properties.Resources.OtherFileLocation;
            }

            if (!DS4Windows.SettingsStorage.CanWrite(DS4Windows.Global.exedirpath, out _))
            {
                progFolderPanel.IsEnabled = false;
            }
        }

        private void ProgFolderBtn_Click(object sender, RoutedEventArgs e) => Choose(DS4Windows.SettingsFolder.Program);

        private void AppdataBtn_Click(object sender, RoutedEventArgs e) => Choose(DS4Windows.SettingsFolder.AppData);

        private void Choose(DS4Windows.SettingsFolder folder)
        {
            try
            {
                var storage = DS4Windows.SettingsStorage.Current;
                string root = storage.Root(folder);
                if (!DS4Windows.SettingsStorage.CanWrite(root, out string reason)) throw new IOException(reason);
                bool existing = storage.HasSettings(folder);
                DS4Windows.Global.SaveWhere(root);
                DS4Windows.Global.firstRun = !existing;
                if (!existing && !DS4Windows.Global.SaveDefault(Path.Combine(root, "Profiles.xml"))) throw new IOException(root);
                storage.Remember(folder);
                ChoiceMade = true;
                Close();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Xml.XmlException)
            {
                MessageBox.Show(error.Message, "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
