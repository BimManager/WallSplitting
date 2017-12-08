using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Configuration;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.IO;

using RevitAppServices = Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Exceptions;
using RevitCreation = Autodesk.Revit.Creation;

namespace TektaRevitPlugins
{
    class App : IExternalApplication
    {
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public Result OnStartup(UIControlledApplication application)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string panelName = "Tekta";

            // Produce a new ribbon panel
            RibbonPanel ribbonPanel =
                application.CreateRibbonPanel(Tab.AddIns, panelName);
            // Add a push button to the ribbon panel
            PushButton pushButton = ribbonPanel
                .AddItem(new PushButtonData
                ("Splitter", "Splitter", assemblyPath, "TektaRevitPlugins.SplitterCommand"))
                as PushButton;

            // Set the image depicted on the button
            pushButton.LargeImage = BmpImageSource("TektaRevitPlugins.Resources.axe_32x32.png");
            pushButton.Image = BmpImageSource("TektaRevitPlugins.Resources.axe_16x16.png");

            return Result.Succeeded;

        }

        #region Helper Methods
        System.Windows.Media.ImageSource BmpImageSource(string embeddedPath)
        {
            Stream stream = this.GetType().Assembly.GetManifestResourceStream(embeddedPath);
            var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder
                (stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            return decoder.Frames[0];
        }

        #endregion
    }
}
