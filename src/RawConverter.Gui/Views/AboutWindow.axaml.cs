using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RawConverter.Gui.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            string version = typeof(ConversionRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            VersionText.Text = "Version " + version?.Split('+')[0];
            BrukerText.Text = ConversionRunner.BrukerNotice;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
