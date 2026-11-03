using System;
using Avalonia;
using Avalonia.Media;

namespace RawConverter.Gui
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                // the same font on every platform, so the fixed WinForms-style layout looks alike everywhere;
                .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
                .LogToTrace();
        }
    }
}
