/**
 * Program:     RawXtract
 * Authors:     Diogo Borges Lima, Paulo Costa Carvalho and Lin He
 * Update:      05/06/2014
 * Update by:   Lin He
 * Description: Mass Spectrum Class
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RawConverter.MassSpec
{
    [Serializable]
    public class MassSpectrum
    {
        public Activation ActivationMethod { get; set; }
        public InstrumentType InstrumentType { get; set; }
        public List<Tuple<double, int>> Precursors { get; set; }
        public double PrecMzFromFilter { get; set; }
        public List<Ion> Peaks { get; set; }
        public ExperimentType ExperimentType { get; set; }
        public int MsLevel { get; set; }
        public int ScanNumber { get; set; }
        public double RetentionTime { get; set; }
        public int PrecursorScanNumber { get; set; }
        public double IonInjectionTime { get; set; }
        public string Filter { get; set; }
        public double ProbabilityScore { get; set; }
        public int NoCandidatePeptides { get; set; }
        public double TotalSecondaryScore { get; set; }
        public string SpectrumId { get; set; }
        public string PeptideSequence { get; set; }
        public double TemperatureFTAnalyzer { get; set; }
        public double PrecursorIntensity { get; set; }
        public bool PrecursorRefined { get; set; }
        public bool isMonoIsotopicPeakVal { get; set; }
        public double LowMz { get; set; }
        public double HighMz { get; set; }
        public double BasePeakMz { get; set; }
        public double BasePeakIntensity { get; set; }
        public double TotIonCurrent { get; set; }
        public double IsolationWindowSize { get; set; }

        /// <summary>
        /// Isolation window bounds in m/z, when the input gives them (mzParquet output).
        /// </summary>
        public double? IsolationLowerMz { get; set; }
        public double? IsolationUpperMz { get; set; }

        /// <summary>
        /// Precursor ion mobility (1/K0) of timsTOF MS2 spectra.
        /// </summary>
        public double? IonMobility { get; set; }

        /// <summary>
        /// Empty Constructor for serializing class
        /// </summary>
        public MassSpectrum() { }

        /// <summary>
        /// Constructor for a general mass spectrum.
        /// </summary>
        /// <param name="scanNumber"></param>
        /// <param name="spectrumId"></param>
        /// <param name="retentionTime"></param>
        /// <param name="peaks"></param>
        /// <param name="ionInjectionTime"></param>
        /// <param name="instrumentType"></param>
        /// <param name="filter"></param>
        public MassSpectrum
            (
            int scanNumber,
            string spectrumId,
            double retentionTime,
            List<Ion> peaks,
            double ionInjectionTime,
            InstrumentType instrumentType,
            string filter,
            double temperatureFTAnalyzer,
            bool isMonoIsotopicPeak
            )
        {
            ScanNumber = scanNumber;
            SpectrumId = spectrumId;
            Peaks = peaks;
            RetentionTime = retentionTime;
            IonInjectionTime = Math.Round(ionInjectionTime,3);
            InstrumentType = instrumentType;
            Filter = filter;
            TemperatureFTAnalyzer = temperatureFTAnalyzer;
            PrecursorRefined = false;
            isMonoIsotopicPeakVal = isMonoIsotopicPeak;
        }

    }
}
