using System.Windows;
using PdfSharp.Fonts;
using PDFMaster.Services;

namespace PDFMaster;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new WindowsFontResolver();

        base.OnStartup(e);
    }
}
