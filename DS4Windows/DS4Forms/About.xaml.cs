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
using System.Runtime.InteropServices;
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
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            // The HWND can be exposed before WPF renders its first content.
            // Set its clear colour before SourceInitialized observers or ShowDialog
            // can reveal the platform's unthemed initial surface.
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(PaintNativeBackground);
            UpdateNativeBackground();
            base.OnSourceInitialized(e);
            FitToWorkArea();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == BackgroundProperty || e.Property == ForegroundProperty) UpdateNativeBackground();
        }

        private void UpdateNativeBackground()
        {
            // FromVisual is still null for an unshown HWND; do not create a
            // handle here just to update a resource during construction.
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            if (Background is not SolidColorBrush brush) return;
            if (HwndSource.FromHwnd(handle)?.CompositionTarget is HwndTarget target)
                target.BackgroundColor = brush.Color;
            int color = brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);
            // The DWM frame is painted separately from WPF's client surface.
            int dark = brush.Color.R * .2126 + brush.Color.G * .7152 + brush.Color.B * .0722 < 128 ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref color, sizeof(int));
            if (Foreground is SolidColorBrush foreground)
            {
                int text = foreground.Color.R | (foreground.Color.G << 8) | (foreground.Color.B << 16);
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            }
        }

        private IntPtr PaintNativeBackground(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Windows may erase the client area before the first WPF render.
            // Paint that actual native DC with the current theme, rather than
            // just changing the later composition clear colour.
            if (message == 0x0014 && wParam != IntPtr.Zero && Background is SolidColorBrush brush &&
                GetClientRect(hwnd, out NativeRect rect))
            {
                int color = brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);
                IntPtr nativeBrush = CreateSolidBrush(color);
                if (nativeBrush != IntPtr.Zero)
                {
                    try { handled = FillRect(wParam, ref rect, nativeBrush) != 0; }
                    finally { DeleteObject(nativeBrush); }
                    if (handled) return new IntPtr(1);
                }
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr dc, ref NativeRect rect, IntPtr brush);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int color);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
