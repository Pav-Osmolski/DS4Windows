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

using DS4WinWPF.DS4Forms.ViewModels;
using System.Windows;

namespace DS4WinWPF.DS4Forms;

public partial class ChangelogWindow : Window
{
    private readonly ChangelogViewModel changelogVM = new();

    public ChangelogWindow()
    {
        InitializeComponent();
        DataContext = changelogVM;
        Loaded += async (_, _) => await changelogVM.DisplayChangelog();
    }

    private async void RetryBtn_Click(object sender, RoutedEventArgs e) => await changelogVM.DisplayChangelog();

    private void ReleasesLink_Click(object sender, RoutedEventArgs e) =>
        DS4Windows.Util.StartProcessHelper("https://github.com/Pav-Osmolski/DS4Windows/releases");

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DataContext = null;
        Close();
    }
}
