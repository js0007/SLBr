/*Copyright © SLT Softwares. All rights reserved.
Use of this source code is governed by a GNU license that can be found in the LICENSE file.*/

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace SLBr.Controls
{
    public partial class WebAppWindow : Window
    {
        public static void SetTaskbarIcon(Window _Window, string IconPath)
        {
            if (File.Exists(IconPath))
            {
                var bitmap = new BitmapImage(new Uri(IconPath, UriKind.RelativeOrAbsolute));
                _Window.TaskbarItemInfo = new TaskbarItemInfo
                {
                    Overlay = bitmap
                };
            }
        }

        public WebView2 Browser;
        WebAppManifest Manifest;
        bool DarkMode;
        nint Handle;
        public WebAppWindow(WebAppManifest _Manifest)
        {
            Manifest = _Manifest;
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(Manifest?.ThemeColor))
            {
                DarkMode = DarkTheme(Utils.HexToColor(Manifest.ThemeColor));
                ApplyTheme(DarkMode);
            }

            Dispatcher.BeginInvoke(() =>
            {
                string AppsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SLBr", "Apps");
                string ID = Utils.SanitizeFileName(Manifest.StartUrl);
                string ImagePath = Path.Combine(AppsFolder, $"{ID}.ico");
                Icon = BitmapFrame.Create(new Uri(ImagePath, UriKind.RelativeOrAbsolute));
                SetTaskbarIcon(this, ImagePath);
            });

            Browser = new WebView2();
            WebContent.Visibility = Visibility.Collapsed;
            WebContent.Children.Add(Browser);

            int trueValue = 0x01;
            Handle = new WindowInteropHelper(this).EnsureHandle();
            DllUtils.DwmSetWindowAttribute(Handle, DwmWindowAttribute.DWMWA_MICA_EFFECT, ref trueValue, Marshal.SizeOf(typeof(int)));

            Loaded += async (s, e) =>
            {
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SLBr", "Default", "User Data");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await Browser.EnsureCoreWebView2Async(env);
                Browser.CoreWebView2.DocumentTitleChanged += (sender, args) =>
                {
                    Title = Browser.CoreWebView2.DocumentTitle + $" - {Manifest.Name}";
                };
                Browser.CoreWebView2.NewWindowRequested += (sender, args) =>
                {
                    args.Handled = true;
                    Browser.Source = new Uri(args.Uri);
                };
                WebContent.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(Manifest.StartUrl))
                    Browser.Source = new Uri(Manifest.StartUrl);
            };
        }

        public bool DarkTheme(Color BaseColor)
        {
            double a = 1 - (0.299 * BaseColor.R + 0.587 * BaseColor.G + 0.114 * BaseColor.B) / 255;
            return a >= 0.7;
        }

        public void ApplyTheme(bool Dark)
        {
            int DarkModeValue = Dark ? 0x01 : 0x00;
            DllUtils.DwmSetWindowAttribute(Handle, DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref DarkModeValue, Marshal.SizeOf(typeof(int)));
        }
    }
}
