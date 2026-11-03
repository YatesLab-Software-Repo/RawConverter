using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using RawConverter.Common;
using RawConverter.DDADataProcess;
using RawConverter.DIADataProcess;
using RawConverter.MassSpec;

namespace RawConverter
{
    /// <summary>
    /// Precursor analysis of one spectrum exported from Xcalibur (the "Single Spectrum Analysis" windows).
    /// </summary>
    public static class SingleSpectrumAnalysis
    {
        /// <summary>
        /// DDA: find the precursor envelopes. The last two lines of the exported text must hold the
        /// instrument's precursor m/z and charge state.
        /// </summary>
        public static List<string> CorrectDdaPrecursor(string[] lines)
        {
            List<Ion> peaks = ParsePeaks(lines, @"\d+.\d+\t[ ]+\d+.\d+");
            double precMz = double.Parse(lines[lines.Length - 2].Trim(), CultureInfo.InvariantCulture);
            int precZ = int.Parse(lines[lines.Length - 1].Trim(), CultureInfo.InvariantCulture);

            List<Envelope> envelopes = new PrecursorCorrector().FindEnvelopes(peaks, precMz, precZ);
            return Describe(envelopes, double.MinValue);
        }

        /// <summary>
        /// DIA: predict the precursors in the spectrum.
        /// </summary>
        public static List<string> PredictDiaPrecursors(string[] lines)
        {
            List<Ion> peaks = ParsePeaks(lines, @"\d+.\d+\t[ ]*\d+.\d+");
            PrecursorPredictor predictor = new PrecursorPredictor(5, 1, 6, 0);
            return Describe(predictor.PredictPrecursors(peaks), predictor.Threshold);
        }

        private static List<Ion> ParsePeaks(string[] lines, string mzLinePattern)
        {
            List<Ion> peaks = new List<Ion>();
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, mzLinePattern);
                if (match.Success)
                {
                    MatchCollection values = Regex.Matches(match.Value, @"\d+.\d+");
                    peaks.Add(new Ion(double.Parse(values[0].Value, CultureInfo.InvariantCulture),
                        double.Parse(values[1].Value, CultureInfo.InvariantCulture)));
                }
            }
            return peaks;
        }

        private static List<string> Describe(List<Envelope> envelopes, double minScore)
        {
            List<string> output = new List<string>();
            int counter = 0;
            foreach (Envelope env in envelopes)
            {
                if (env.Score >= minScore)
                {
                    output.Add("Envelope " + (++counter) + ":");
                    output.Add("\tm/z = " + env.MonoisotPeak.MZ.ToString(CultureInfo.InvariantCulture) + ", z = " + env.Charge);
                    output.Add("\tscore = " + env.Score.ToString(CultureInfo.InvariantCulture));
                    output.Add("");
                }
            }
            return output;
        }
    }
}
