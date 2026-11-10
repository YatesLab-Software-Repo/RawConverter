using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RawConverter.Gui.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "Version " + ConversionRunner.Version;
            BrukerText.Text = ConversionRunner.BrukerNotice;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
