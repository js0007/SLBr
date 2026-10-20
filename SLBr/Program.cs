/*Copyright © SLT Softwares. All rights reserved.
Use of this source code is governed by a GNU license that can be found in the LICENSE file.*/

using SLBr.Controls;
using SLBr.Handlers;
using SLBr.WebView;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Windows;

namespace SLBr
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;
            string? FirstArgument = args.FirstOrDefault();
            if (FirstArgument != null && FirstArgument.StartsWith("--app="))
            {
                string AppsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SLBr", "Apps");
                string ID = FirstArgument[6..].Trim('"');
                string ManifestPath = Path.Combine(AppsFolder, $"{ID}.json");
                WebAppManifest? Manifest = WebAppHandler.LoadManifest(File.ReadAllText(ManifestPath));
                if (Manifest == null)
                    return Environment.ExitCode;

                new Application().Run(new WebAppWindow(Manifest));
            }
            else
            {
                App.Main();
                WebViewManager.ShutdownWebView2();
            }
            return Environment.ExitCode;
        }
    }
}
