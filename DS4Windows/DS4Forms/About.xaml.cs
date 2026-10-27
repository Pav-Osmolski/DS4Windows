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

using DS4Windows;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms
{
    public partial class About : Window
    {
        public About(bool showHotkeys = false)
        {
            InitializeComponent();
            string version = string.IsNullOrWhiteSpace(Global.exeDisplayVersion)
                ? typeof(About).Assembly.GetName().Version?.ToString(3)
                : Global.exeDisplayVersion;
            versionText.Text = global::DS4Windows.UiText.Format("Version {0}", version);
            licenseText.Text = LoadLicenseText();
            if (showHotkeys) aboutTabs.SelectedIndex = 1;
            SourceInitialized += (_, _) => FitToWorkArea();
        }

        internal static string LoadLicenseText()
        {
            using Stream stream = typeof(About).Assembly.GetManifestResourceStream("DS4Windows.License.txt")
                ?? throw new InvalidOperationException(global::DS4Windows.UiText.Get("The bundled GNU GPL licence is missing."));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void AboutTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != sender || sender is not TabControl tabs) return;
            // Each page's scroll container is the first keyboard stop. WPF can
            // focus that container without bringing a link at the end into view.
            // Reset synchronously, before layout/render; never correct a visible frame.
            if (tabs.SelectedContent is ScrollViewer scroll)
            {
                scroll.ScrollToTop();
                scroll.UpdateLayout();
            }
            else if (tabs.SelectedIndex == 3) licenseText?.ScrollToHome();
        }

        private void FitToWorkArea()
        {
            Window target = Owner ?? this;
            var working = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(target).Handle).WorkingArea;
            Matrix fromDevice = PresentationSource.FromVisual(target)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point origin = fromDevice.Transform(new Point(working.Left, working.Top));
            Vector size = fromDevice.Transform(new Vector(working.Width, working.Height));
            Size bounds = GetWindowSize(size.X, size.Y);
            MinWidth = Math.Min(620, bounds.Width);
            MinHeight = Math.Min(480, bounds.Height);
            Width = bounds.Width;
            Height = bounds.Height;
            Left = origin.X + (size.X - Width) / 2;
            Top = origin.Y + (size.Y - Height) / 2;
        }

        internal static Size GetWindowSize(double workWidth, double workHeight) => new(
            Math.Min(960, Math.Max(1, workWidth - 24)),
            Math.Min(780, Math.Max(1, workHeight - 24)));

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                Util.StartProcessHelper(uri.AbsoluteUri);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
