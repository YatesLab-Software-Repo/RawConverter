using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using RawConverter.Bruker;
using RawConverter.Common;
using RawConverter.Converter;
using RawConverter.MassSpec;

namespace RawConverter
{
    /// <summary>
    /// Output file formats. mzML is supported as an input format only.
    /// </summary>
    [Flags]
    public enum OutputFormat
    {
        None = 0,
        MS1 = 1,
        MS2 = 2,
        MS3 = 4,
        MGF = 8,
        MzXML = 16,
        MzParquet = 32,

        /// <summary>
        /// Pack a timsTOF .d folder into name.d.tar (only for .d folder input).
        /// </summary>
        DTar = 64,
    }

    public class ConversionOptions
    {
        public OutputFormat OutputFormats { get; set; }

        /// <summary>
        /// Folder for output files; null or empty writes each output next to its input file.
        /// </summary>
        public string OutputFolder { get; set; }

        public ExperimentType ExperimentType { get; set; } = ExperimentType.DDA;

        /// <summary>
        /// DDA: select the monoisotopic m/z of precursors using the surrounding MS1 scans.
        /// </summary>
        public bool CorrectPrecursorMz { get; set; }
        public bool CorrectPrecursorZ { get; set; }

        /// <summary>
        /// DIA: predict precursors for each MS2 scan.
        /// </summary>
        public bool PredictPrecursors { get; set; }

        public bool IsCentroided { get; set; } = true;
        public int MzDecimalPlace { get; set; } = 4;
        public int IntensityDecimalPlace { get; set; } = 1;

        /// <summary>
        /// Charge states assigned to MGF spectra that have no charge.
        /// </summary>
        public HashSet<int> DDADataChargeStates { get; set; } = new HashSet<int> { 2, 3 };

        /// <summary>
        /// Charge states assigned to MS2 precursors when the RAW file reports none; null keeps the default (2+, 3+).
        /// </summary>
        public int[] Ms2PrecursorCharges { get; set; }

        public bool ExtractPrecursorByMz { get; set; }

        /// <summary>
        /// Use the precursor m/z from the scan filter rather than Thermo's monoisotopic m/z.
        /// </summary>
        public bool BypassThermoAlgorithm { get; set; } = true;

        public bool ShowPeakChargeStates { get; set; } = true;
        public bool ShowPeakResolution { get; set; } = true;
        public bool ExportChargeState { get; set; }

        /// <summary>
        /// Number of files converted at the same time.
        /// </summary>
        public int MaxParallelFiles { get; set; } = 1;

        internal string[] OutputFileTypes()
        {
            List<string> types = new List<string>();
            if (OutputFormats.HasFlag(OutputFormat.MS1)) types.Add("ms1");
            if (OutputFormats.HasFlag(OutputFormat.MS2)) types.Add("ms2");
            if (OutputFormats.HasFlag(OutputFormat.MS3)) types.Add("ms3");
            if (OutputFormats.HasFlag(OutputFormat.MGF)) types.Add("mgf");
            if (OutputFormats.HasFlag(OutputFormat.MzXML)) types.Add("mzXML");
            if (OutputFormats.HasFlag(OutputFormat.MzParquet)) types.Add("mzparquet");
            return types.ToArray();
        }
    }

    public class ConversionProgress
    {
        public string InputFile { get; set; }
        public int FileIndex { get; set; }
        public int FileCount { get; set; }
        public int Percent { get; set; }
    }

    public class FileConversionResult
    {
        public string InputFile { get; set; }
        public bool Succeeded { get; set; }
        public bool Cancelled { get; set; }
        public Exception Error { get; set; }
    }

    /// <summary>
    /// Converts RAW, mzXML, mzML, MS2 and MGF files; shared by the command-line and GUI front ends.
    /// </summary>
    public static class ConversionRunner
    {
        public static readonly string[] SupportedInputExtensions = { ".raw", ".mzxml", ".mzml", ".ms2", ".mgf" };

        /// <summary>
        /// The notice the Bruker TDF SDK license (section 4.4) requires in the About box.
        /// </summary>
        public const string BrukerNotice =
            "Included software components: Copyright © 2022 by Bruker Daltonics GmbH & Co. KG. All rights reserved.";

        /// <summary>
        /// The RawConverter version (from Directory.Build.props), without the source revision suffix ("1.2.0+abcdef").
        /// </summary>
        public static readonly string Version = (typeof(ConversionRunner).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ConversionRunner).Assembly.GetName().Version?.ToString())?.Split('+')[0];

        /// <summary>
        /// Whether the path is a Bruker timsTOF .d folder (it holds analysis.tdf and analysis.tdf_bin).
        /// </summary>
        public static bool IsTimsTofInput(string path)
        {
            return TimsTofConverter.IsTimsTofDirectory(path);
        }

        public static IReadOnlyList<FileConversionResult> ConvertFiles(IReadOnlyList<string> inputFiles, ConversionOptions options,
            IProgress<ConversionProgress> progress = null, CancellationToken cancellationToken = default)
        {
            FileConversionResult[] results = new FileConversionResult[inputFiles.Count];
            ParallelOptions po = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelFiles),
            };

            try
            {
                Parallel.For(0, inputFiles.Count, po, idx =>
                {
                    results[idx] = cancellationToken.IsCancellationRequested
                        ? new FileConversionResult { InputFile = inputFiles[idx], Cancelled = true }
                        : ConvertFile(inputFiles[idx], idx, inputFiles.Count, options, progress, cancellationToken);
                });
            }
            catch (AggregateException e)
            {
                // ConvertFile catches its own exceptions, so this is unexpected;
                throw e.Flatten().InnerExceptions.Count == 1 ? e.Flatten().InnerException : e;
            }

            return results;
        }

        public static FileConversionResult ConvertFile(string inFile, int fileIdx, int fileCount, ConversionOptions options,
            IProgress<ConversionProgress> progress = null, CancellationToken cancellationToken = default)
        {
            FileConversionResult result = new FileConversionResult { InputFile = inFile };
            TaskProgress taskProgress = new TaskProgress { CancellationToken = cancellationToken };
            if (progress != null)
            {
                taskProgress.ProgressChanged += percent => progress.Report(new ConversionProgress
                {
                    InputFile = inFile, FileIndex = fileIdx, FileCount = fileCount, Percent = percent
                });
            }

            // the output formats depend on the invariant number format ('.' decimal separator);
            CultureInfo previousCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = OutputCulture;
            try
            {
                Log.Info(" Starting to convert file " + (fileIdx + 1) + " / " + fileCount + "...");
                Log.Info(" Parsing file: " + inFile + " . . . ");
                ConvertFile(inFile, options, taskProgress);
                result.Cancelled = taskProgress.Aborted;
                result.Succeeded = !result.Cancelled;
            }
            catch (Exception e)
            {
                result.Error = e;
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }
            return result;
        }

        private static void ConvertFile(string inFile, ConversionOptions options, TaskProgress progress)
        {
            if (options.OutputFormats == OutputFormat.None)
            {
                throw new ArgumentException("No output format selected.");
            }

            if (Directory.Exists(inFile))
            {
                ConvertTimsTof(inFile, options, progress);
                return;
            }
            if (!File.Exists(inFile))
            {
                throw new FileNotFoundException("Input file not found: " + inFile, inFile);
            }

            if (options.OutputFormats.HasFlag(OutputFormat.DTar))
            {
                if ((options.OutputFormats & ~OutputFormat.DTar) == OutputFormat.None)
                {
                    throw new ArgumentException("Packing into .d.tar only applies to timsTOF .d folders: " + inFile);
                }
                Log.Warning("Packing into .d.tar only applies to timsTOF .d folders; skipped for " + inFile);
            }
            string[] outFileTypes = options.OutputFileTypes();

            if (TimsTofArchive.IsArchive(inFile))
            {
                ConvertTimsTofArchive(inFile, options, progress);
                return;
            }

            string outFolder = string.IsNullOrEmpty(options.OutputFolder)
                ? Path.GetDirectoryName(Path.GetFullPath(inFile))
                : options.OutputFolder;
            Directory.CreateDirectory(outFolder);

            string extension = Path.GetExtension(inFile).ToLowerInvariant();
            switch (extension)
            {
                case ".raw":
                    {
                        RawFileConverter rc = new RawFileConverter(inFile, outFolder, outFileTypes, options.ExperimentType, options.ExportChargeState);
                        try
                        {
                            rc.SetOptions(options.IsCentroided, options.MzDecimalPlace, options.IntensityDecimalPlace, options.ExtractPrecursorByMz,
                                options.BypassThermoAlgorithm, options.CorrectPrecursorMz, options.CorrectPrecursorZ, options.PredictPrecursors,
                                options.DDADataChargeStates, options.Ms2PrecursorCharges, options.ShowPeakChargeStates, options.ShowPeakResolution,
                                options.ExportChargeState);
                            rc.Convert(progress);
                        }
                        finally
                        {
                            rc.Close();
                        }
                        break;
                    }
                case ".mzxml":
                    {
                        MzXMLConverter mc = new MzXMLConverter(inFile, outFolder, outFileTypes, options.ExperimentType);
                        try
                        {
                            mc.SetOptions(options.MzDecimalPlace, options.IntensityDecimalPlace);
                            mc.Convert(progress);
                        }
                        finally
                        {
                            mc.Close();
                        }
                        break;
                    }
                case ".mzml":
                    {
                        MzMLConverter mc = new MzMLConverter(inFile, outFolder, outFileTypes);
                        try
                        {
                            mc.SetOptions(options.MzDecimalPlace, options.IntensityDecimalPlace, options.CorrectPrecursorMz);
                            mc.Convert(progress);
                        }
                        finally
                        {
                            mc.Close();
                        }
                        break;
                    }
                case ".ms2":
                    {
                        const OutputFormat ms2Outputs = OutputFormat.MGF | OutputFormat.MzParquet;
                        if ((options.OutputFormats & ms2Outputs) == 0)
                        {
                            throw new ArgumentException("MS2 input can only be converted to MGF and mzParquet.");
                        }
                        MS2Converter mc = new MS2Converter(inFile, outFolder, outFileTypes, options.CorrectPrecursorMz);
                        try
                        {
                            mc.SetOptions(options.MzDecimalPlace, options.IntensityDecimalPlace);
                            mc.Convert(progress);
                        }
                        finally
                        {
                            mc.Close();
                        }
                        break;
                    }
                case ".mgf":
                    {
                        if (!options.OutputFormats.HasFlag(OutputFormat.MS2))
                        {
                            throw new ArgumentException("MGF input can only be converted to MS2.");
                        }
                        MgfConverter mc = new MgfConverter(inFile, outFolder, "ms2");
                        mc.SetOptions(options.MzDecimalPlace, options.IntensityDecimalPlace, options.DDADataChargeStates);
                        mc.Convert(progress);
                        break;
                    }
                default:
                    throw new ArgumentException("Unsupported input file type: " + inFile);
            }
            Log.Info(" Conversion finished: " + inFile);
        }

        /// <summary>
        /// A .d folder in a tar archive: extract the .d next to the archive and write the outputs next to it.
        /// </summary>
        private static void ConvertTimsTofArchive(string archive, ConversionOptions options, TaskProgress progress)
        {
            CheckTimsTofOutputFormats(archive, options);
            const int extractionShare = 20;
            string analysisDir = TimsTofArchive.Extract(archive, progress, extractionShare);
            if (analysisDir == null)
            {
                return;
            }
            string outFolder = string.IsNullOrEmpty(options.OutputFolder)
                ? Path.GetDirectoryName(analysisDir)
                : options.OutputFolder;
            ConvertTimsTof(analysisDir, options, progress, outFolder, extractionShare);
        }

        // MS1 output from timsTOF input is disabled for now: its peak order for equal m/z does not reproduce
        // the TimsTOF_extractor script, whose order depends on the numpy version;
        private static void CheckTimsTofOutputFormats(string input, ConversionOptions options)
        {
            const OutputFormat supported = OutputFormat.MS2 | OutputFormat.MzXML | OutputFormat.MzParquet;
            if ((options.OutputFormats & supported) == 0)
            {
                throw new ArgumentException("timsTOF input can only be converted to MS2, mzXML and mzParquet.");
            }
            if ((options.OutputFormats & ~supported & ~OutputFormat.DTar) != 0)
            {
                Log.Warning("timsTOF input only produces MS2, mzXML and mzParquet files; other formats are skipped for " + input);
            }
        }

        /// <summary>
        /// A .d folder: pack it into name.d.tar if asked (first, so the archive does not pick up the outputs, which
        /// are written inside the .d by default), then convert it if any conversion output is asked for.
        /// </summary>
        private static void ConvertTimsTof(string analysisDir, ConversionOptions options, TaskProgress progress)
        {
            bool pack = options.OutputFormats.HasFlag(OutputFormat.DTar);
            bool convert = (options.OutputFormats & ~OutputFormat.DTar) != OutputFormat.None;
            if (convert)
            {
                CheckTimsTofOutputFormats(analysisDir, options);
            }
            if (!TimsTofConverter.IsTimsTofDirectory(analysisDir))
            {
                throw new ArgumentException("Not a timsTOF .d folder (it needs analysis.tdf and analysis.tdf_bin): " + analysisDir);
            }

            int packEnd = 0;
            if (pack)
            {
                packEnd = convert ? 30 : 100;
                if (TimsTofArchive.Create(analysisDir, options.OutputFolder, progress, packEnd) == null)
                {
                    return;
                }
                if (!convert)
                {
                    Log.Info(" Packing finished: " + analysisDir);
                    return;
                }
            }
            ConvertTimsTof(analysisDir, options, progress, options.OutputFolder, packEnd);
        }

        /// <param name="outFolder">null writes inside the .d folder, as the TimsTOF_extractor script did</param>
        private static void ConvertTimsTof(string analysisDir, ConversionOptions options, TaskProgress progress, string outFolder, int progressStart)
        {
            if (!TimsTofConverter.IsTimsTofDirectory(analysisDir))
            {
                throw new ArgumentException("Not a timsTOF .d folder (it needs analysis.tdf and analysis.tdf_bin): " + analysisDir);
            }
            if (!string.IsNullOrEmpty(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            new TimsTofConverter(analysisDir, outFolder, false, options.OutputFormats.HasFlag(OutputFormat.MS2),
                options.OutputFormats.HasFlag(OutputFormat.MzXML), options.OutputFormats.HasFlag(OutputFormat.MzParquet))
                .Convert(progress, progressStart);
            Log.Info(" Conversion finished: " + analysisDir);
        }

        private static readonly CultureInfo OutputCulture = CreateOutputCulture();

        private static CultureInfo CreateOutputCulture()
        {
            // en-US keeps the header dates in the format earlier versions wrote; fall back to the
            // invariant culture when ICU culture data is not available (invariant globalization mode);
            try
            {
                return CultureInfo.ReadOnly(new CultureInfo("en-US"));
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
