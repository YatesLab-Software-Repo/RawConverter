using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RawConverter.Gui.Views
{
    /// <summary>
    /// Precursor analysis of one spectrum pasted from Xcalibur; for DDA, the text must end with the
    /// instrument's precursor m/z and charge state on two lines.
    /// </summary>
    public partial class SingleSpectrumWindow : Window
    {
        public enum Mode
        {
            Dda,
            Dia,
        }

        private readonly Mode _mode;

        public SingleSpectrumWindow() : this(Mode.Dda)
        {
        }

        public SingleSpectrumWindow(Mode mode)
        {
            InitializeComponent();
            _mode = mode;
            Title = mode == Mode.Dda ? "DDA Single Spectrum Precursor Corrector" : "DIA Single Spectrum Precursor Predictor";
        }

        private void OnPredict(object sender, RoutedEventArgs e)
        {
            string[] lines = (SpectrumText.Text ?? "").Replace("\r\n", "\n").Split('\n');
            List<string> output;
            try
            {
                output = _mode == Mode.Dda
                    ? SingleSpectrumAnalysis.CorrectDdaPrecursor(lines)
                    : SingleSpectrumAnalysis.PredictDiaPrecursors(lines);
            }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentException)
            {
                output = new List<string> { "Could not read the spectrum: " + ex.Message };
            }
            OutputList.ItemsSource = output;
        }
    }
}
