using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using ABAdvTools;
using ABAdvTools.Revit;
using AB.WorksetManager.Addin.Commands;
using AB.WorksetManager.Addin.Infrastructure;
using Autodesk.Revit.UI;

namespace AB.WorksetManager.Addin
{
    /// <summary>
    /// AB Worksets Manager - create, rename, assign, view and delete worksets in workshared models.
    /// Startup only adds a button to the AB Adv Tools tab: nothing runs until the button is pressed,
    /// and the add-in watches no events, so it costs Revit nothing while idle.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        public const string ProductName = "AB Worksets Manager";
        public const string Tagline = "Create, rename, assign, view and delete worksets in workshared models.";
        public const string GitHubRepository = "AB.WorksetManager.Revit";
        private const string PanelName = "Worksets";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Log.Info("Starting " + ProductName + " " + Version + " in Revit " + application.ControlledApplication.VersionNumber + ".");

                try { RevitAdvTools.Initialize(application, Product); }
                catch (Exception ex) { Log.Warn("AB Adv Tools initialisation failed: " + ex.Message); }

                RibbonPanel panel = RevitAdvTools.GetToolPanel(application, PanelName);
                var button = new PushButtonData("ABWorksetManager_Open", "Worksets" + Environment.NewLine + "Manager",
                    typeof(App).Assembly.Location, typeof(ShowManagerCommand).FullName)
                {
                    ToolTip = "Create, rename, assign, view and delete worksets.",
                    LongDescription =
                        "Create worksets from a list, rename them, put grids, levels, links and families on the right workset, " +
                        "make workset 3D views, switch workset visibility in views and templates, and delete worksets " +
                        "after moving their elements. Every change is previewed, skips what other users have borrowed, and is one Undo.",
                    LargeImage = Icon("worksets_32.png"),
                    Image = Icon("worksets_16.png"),
                    AvailabilityClassName = typeof(ProjectAvailability).FullName
                };
                panel.AddItem(button);
            }
            catch (Exception ex)
            {
                // Never stop Revit loading because of an add-in.
                Log.Error(ProductName + " failed to start.", ex);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try { RevitAdvTools.Shutdown(application); }
            catch (Exception ex) { Log.Error("Shutdown failed.", ex); }
            return Result.Succeeded;
        }

        internal static string Version => AdvToolsProduct.VersionOf(typeof(App).Assembly);

        private static AdvToolsProduct Product
        {
            get
            {
                var product = new AdvToolsProduct("WorksetManager", ProductName, Tagline, AdvToolsHost.Revit, GitHubRepository, typeof(App).Assembly)
                {
                    Details = () => "Manages the worksets of workshared models: create from lists, rename, assign grids, levels, links " +
                                    "and families, workset 3D views, visibility in views and templates, and delete with move." +
                                    Environment.NewLine + "Settings: " + UserSettings.Folder
                };
                product.AddAction("Open the log folder", () => OpenFolder(Log.Folder));
                return product;
            }
        }

        internal static void OpenFolder(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder)) return;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn("Could not open " + folder + ": " + ex.Message); }
        }

        internal static BitmapSource Icon(string name)
        {
            try
            {
                using (Stream stream = typeof(App).Assembly.GetManifestResourceStream("AB.WorksetManager.Addin.Resources.Icons." + name))
                {
                    if (stream == null) return null;
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch { return null; }
        }
    }
}
