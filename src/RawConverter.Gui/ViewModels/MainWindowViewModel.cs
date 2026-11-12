using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RawConverter.MassSpec;

namespace RawConverter.Gui.ViewModels
{
    /// <summary>
    /// Message boxes and confirmations, provided by the window.
    /// </summary>
    public interface IDialogService
    {
        Task ShowMessage(string text, string title);
        Task<bool> Confirm(string text, string title);
    }

    /// <summary>
    /// State and behaviour of the main window, ported from the WinForms RawConverterGUI.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        public const string Ms123Format = "MS1, MS2, and MS3";

        public static readonly string[] OutputFormatChoices = { Ms123Format, "MGF", "mzXML", "mzML", "mzParquet", "All" };

        private readonly IDialogService _dialogs;
        private CancellationTokenSource _cancellation;
        private string _currentInputFormat;

        public MainWindowViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;

            // initial state of the legacy window;
            IsDda = true;
            MassCorrectionEnabled = true;
            MassCorrectionChecked = false;
            PredictDiaEnabled = false;
            PredictDiaChecked = false;
            CentroidEnabled = false;
            CentroidChecked = true;
        }

        public ObservableCollection<string> InputFiles { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();
        public string[] OutputFormats => OutputFormatChoices;

        [ObservableProperty] private string _selectedInputFile;
        [ObservableProperty] private string _outputDirectory = "";
        [ObservableProperty] private string _threadCountText = "3";
        [ObservableProperty] private int _outputFormatIndex = -1;

        [ObservableProperty] private bool _isDda;
        [ObservableProperty] private bool _isDia;
        [ObservableProperty] private bool _massCorrectionChecked;
        [ObservableProperty] private bool _massCorrectionEnabled;
        [ObservableProperty] private bool _predictDiaChecked;
        [ObservableProperty] private bool _predictDiaEnabled;
        [ObservableProperty] private bool _centroidChecked;
        [ObservableProperty] private bool _centroidEnabled;

        [ObservableProperty] private bool _ms1Checked;
        [ObservableProperty] private bool _ms1Enabled;
        [ObservableProperty] private bool _ms2Checked;
        [ObservableProperty] private bool _ms2Enabled;
        [ObservableProperty] private bool _ms3Checked;
        [ObservableProperty] private bool _ms3Enabled;
        [ObservableProperty] private bool _mgfChecked;
        [ObservableProperty] private bool _mgfEnabled;
        [ObservableProperty] private bool _mzXmlChecked;
        [ObservableProperty] private bool _mzXmlEnabled;
        [ObservableProperty] private bool _mzMlChecked;
        [ObservableProperty] private bool _mzMlEnabled;
        [ObservableProperty] private bool _mzParquetChecked;
        [ObservableProperty] private bool _mzParquetEnabled;
        [ObservableProperty] private bool _logChecked;
        [ObservableProperty] private bool _logEnabled;

        /// <summary>
        /// Pack timsTOF .d folders into .d.tar; independent of the output format, and only for .d folder input.
        /// </summary>
        [ObservableProperty] private bool _dTarChecked;
        [ObservableProperty] private bool _dTarEnabled;

        // Options menu;
        [ObservableProperty] private int _mzDecimalPlace = 4;
        [ObservableProperty] private int _intensityDecimalPlace = 1;
        [ObservableProperty] private bool _extractPrecursorByMz;
        [ObservableProperty] private bool _showPeakChargeStates = true;
        [ObservableProperty] private bool _showPeakResolution = true;
        [ObservableProperty] private bool _exportChargeState;
        [ObservableProperty] private bool _bypassThermoAlgorithm = true;
        public SortedSet<int> ChargeStates { get; } = new SortedSet<int> { 2, 3 };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GoButtonText), nameof(IsIdle))]
        private bool _isRunning;
        [ObservableProperty] private int _progressPercent;
        [ObservableProperty] private string _fileProcessingLabel = "File 0 / 0:";

        public bool IsIdle => !IsRunning;
        public string GoButtonText => IsRunning ? "Stop!" : "Go!";

        /// <summary>
        /// The input format the output choices are checked against: the extension of the first input file,
        /// ".d" for a timsTOF folder, or ".tar" for a timsTOF archive.
        /// </summary>
        public string CurrentInputFormat
        {
            get { return _currentInputFormat; }
            private set
            {
                _currentInputFormat = value;
                OutputFormatIndex = -1;
                SetOutputChoices(false, false, false, false, false, false, false);
                SetOutputChecks(false, false, false, false, false, false, false);
                DTarEnabled = IsFormat(".d");
                DTarChecked = DTarChecked && DTarEnabled;
            }
        }

        public static string InputFormatOf(string path)
        {
            if (Directory.Exists(path))
            {
                return ".d";
            }
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name.EndsWith(".tar") || name.EndsWith(".tar.gz") || name.EndsWith(".tgz"))
            {
                return ".tar";
            }
            return Path.GetExtension(path);
        }

        private bool IsTimsTofInput => IsFormat(".d") || IsFormat(".tar");

        private bool IsFormat(params string[] formats)
        {
            return CurrentInputFormat != null && formats.Any(f => string.Equals(CurrentInputFormat, f, StringComparison.OrdinalIgnoreCase));
        }

        public async Task AddInputs(IEnumerable<string> paths)
        {
            List<string> added = paths.ToList();
            foreach (string path in added)
            {
                if (!InputFiles.Contains(path))
                {
                    InputFiles.Add(path);
                }
            }

            // show the default output directory;
            if (OutputDirectory == "" && added.Count > 0)
            {
                OutputDirectory = Path.GetDirectoryName(Path.GetFullPath(added[0]).TrimEnd(Path.DirectorySeparatorChar));
            }

            // check the selected files formats;
            if (InputFiles.Count > 0)
            {
                if (!string.Equals(CurrentInputFormat, InputFormatOf(InputFiles[0]), StringComparison.OrdinalIgnoreCase))
                {
                    CurrentInputFormat = InputFormatOf(InputFiles[0]);
                }
                if (InputFiles.Any(f => !string.Equals(CurrentInputFormat, InputFormatOf(f), StringComparison.OrdinalIgnoreCase)))
                {
                    await _dialogs.ShowMessage("Different file formats? Please do NOT!", "Warning");
                }
            }
        }

        public void RemoveSelectedInput()
        {
            if (SelectedInputFile == null)
            {
                return;
            }
            int idx = InputFiles.IndexOf(SelectedInputFile);
            InputFiles.RemoveAt(idx);

            // select the previous item, or the following one when the first was removed;
            if (InputFiles.Count > 0)
            {
                SelectedInputFile = InputFiles[Math.Max(0, Math.Min(idx - 1, InputFiles.Count - 1))];
                CurrentInputFormat = InputFormatOf(InputFiles[0]);
            }
            else
            {
                CurrentInputFormat = null;
            }
        }

        partial void OnIsDdaChanged(bool value)
        {
            if (value)
            {
                IsDia = false;
                MassCorrectionEnabled = true;
                MassCorrectionChecked = true;
                PredictDiaEnabled = false;
                PredictDiaChecked = false;
                CentroidEnabled = true;
                CentroidChecked = true;
            }
        }

        partial void OnIsDiaChanged(bool value)
        {
            if (value)
            {
                IsDda = false;
                MassCorrectionEnabled = false;
                MassCorrectionChecked = false;
                PredictDiaEnabled = true;
                PredictDiaChecked = false;
                CentroidEnabled = false;
                CentroidChecked = true;
            }
        }

        partial void OnOutputFormatIndexChanged(int value)
        {
            string warning = ApplyOutputFormat(value);
            if (warning != null)
            {
                _ = _dialogs.ShowMessage(warning, "Warning");
            }
        }

        /// <summary>
        /// Enable and check the output choices for the selected output format; returns a warning when the
        /// selection does not suit the input format.
        /// </summary>
        private string ApplyOutputFormat(int index)
        {
            const string same = "The input and the output formats cannot be the same!";
            const string timsTof = "timsTOF input can only be converted to MS2, mzXML and mzParquet!";
            switch (index)
            {
                case 0: // MS1, MS2, and MS3;
                    if (IsFormat(".MS1", ".MS2", ".MS3"))
                    {
                        DisableOutputs();
                        return same;
                    }
                    if (IsFormat(".MGF", ".mzXML"))
                    {
                        SetOutputChoices(false, true, true, false, false, false, false);
                        SetOutputChecks(false, true, false, false, false, false, false);
                    }
                    else if (IsTimsTofInput)
                    {
                        SetOutputChoices(false, true, false, false, false, false, true);
                        SetOutputChecks(false, true, false, false, false, false, false);
                    }
                    else
                    {
                        SetOutputChoices(true, true, true, false, false, false, true);
                        SetOutputChecks(false, true, false, false, false, false, false);
                    }
                    return null;
                case 1: // MGF;
                    if (IsFormat(".MGF"))
                    {
                        DisableOutputs();
                        return same;
                    }
                    if (IsTimsTofInput)
                    {
                        DisableOutputs();
                        return timsTof;
                    }
                    SetOutputChoices(false, false, false, false, false, false, !IsFormat(".MS1", ".MS2", ".MS3"));
                    SetOutputChecks(false, false, false, true, false, false, false);
                    return null;
                case 2: // mzXML;
                case 3: // mzML;
                    bool mzXml = index == 2;
                    if (CurrentInputFormat != null && !IsFormat(".RAW") && !(mzXml && IsTimsTofInput))
                    {
                        SetOutputChoices(false, false, false, false, false, false, false);
                        SetOutputChecks(false, false, false, false, mzXml, !mzXml, false);
                        return IsTimsTofInput ? timsTof : "The input can only be RAW files if " + (mzXml ? "mzXML" : "mzML") + " is selected";
                    }
                    SetOutputChoices(false, false, false, false, false, false, true);
                    SetOutputChecks(false, false, false, false, mzXml, !mzXml, false);
                    return null;
                case 4: // mzParquet;
                    if (IsFormat(".MGF"))
                    {
                        DisableOutputs();
                        return "MGF input cannot be converted to mzParquet!";
                    }
                    SetOutputChoices(false, false, false, false, false, false, true);
                    SetOutputChecks(false, false, false, false, false, false, false, true);
                    return null;
                case 5: // All;
                    if (CurrentInputFormat != null && !IsFormat(".RAW"))
                    {
                        DisableOutputs();
                        return IsTimsTofInput ? timsTof : "This option is only availabe for RAW data extraction!";
                    }
                    SetOutputChoices(true, true, true, true, true, true, true, true);
                    SetOutputChecks(false, true, false, false, false, false, false);
                    return null;
                default:
                    return null;
            }
        }

        private void DisableOutputs()
        {
            SetOutputChoices(false, false, false, false, false, false, false);
            SetOutputChecks(false, false, false, false, false, false, false);
        }

        private void SetOutputChoices(bool ms1, bool ms2, bool ms3, bool mgf, bool mzXml, bool mzMl, bool log, bool mzParquet = false)
        {
            MzParquetEnabled = mzParquet;
            Ms1Enabled = ms1;
            Ms2Enabled = ms2;
            Ms3Enabled = ms3;
            MgfEnabled = mgf;
            MzXmlEnabled = mzXml;
            MzMlEnabled = mzMl;
            LogEnabled = log;
        }

        private void SetOutputChecks(bool ms1, bool ms2, bool ms3, bool mgf, bool mzXml, bool mzMl, bool log, bool mzParquet = false)
        {
            MzParquetChecked = mzParquet;
            Ms1Checked = ms1;
            Ms2Checked = ms2;
            Ms3Checked = ms3;
            MgfChecked = mgf;
            MzXmlChecked = mzXml;
            MzMlChecked = mzMl;
            LogChecked = log;
        }

        public void ToggleChargeState(int charge)
        {
            if (!ChargeStates.Remove(charge))
            {
                ChargeStates.Add(charge);
            }
            OnPropertyChanged(nameof(ChargeStates));
        }

        /// <summary>
        /// Validate the settings the way the legacy Go! button did; returns null and the error message, or
        /// the conversion options.
        /// </summary>
        public (ConversionOptions Options, string Error) BuildOptions()
        {
            if (InputFiles.Count == 0)
            {
                return (null, "No input file! Please select input file(s).");
            }
            if (ThreadCountText.Trim() == "")
            {
                return (null, "Please Enter Number of Threads to Convert!");
            }
            if (!int.TryParse(ThreadCountText.Trim(), out int threads) || threads < 1)
            {
                return (null, "Threads count only in Numbers!");
            }
            if (!Ms1Checked && !Ms2Checked && !Ms3Checked && !MgfChecked && !MzXmlChecked && !MzMlChecked && !MzParquetChecked && !LogChecked
                && !DTarChecked)
            {
                return (null, "Please select an output format!");
            }
            if (MzMlChecked)
            {
                return (null, "mzML output is not supported yet. Please select another output format!");
            }
            if (OutputDirectory.Trim() == "")
            {
                return (null, "Please select a destination directory!");
            }

            // if no charge state is selected, use 2+ and 3+;
            if (ChargeStates.Count == 0)
            {
                ChargeStates.Add(2);
                ChargeStates.Add(3);
                OnPropertyChanged(nameof(ChargeStates));
            }

            OutputFormat formats = OutputFormat.None;
            if (Ms1Checked) formats |= OutputFormat.MS1;
            if (Ms2Checked) formats |= OutputFormat.MS2;
            if (Ms3Checked) formats |= OutputFormat.MS3;
            if (MgfChecked) formats |= OutputFormat.MGF;
            if (MzXmlChecked) formats |= OutputFormat.MzXML;
            if (MzParquetChecked) formats |= OutputFormat.MzParquet;
            if (DTarChecked && DTarEnabled) formats |= OutputFormat.DTar;
            if (formats == OutputFormat.None)
            {
                return (null, "Please select an output format!");
            }

            ConversionOptions options = new ConversionOptions
            {
                OutputFormats = formats,
                OutputFolder = OutputDirectory.Trim(),
                ExperimentType = IsDia ? ExperimentType.DIA : ExperimentType.DDA,
                CorrectPrecursorMz = MassCorrectionChecked,
                PredictPrecursors = PredictDiaChecked,
                IsCentroided = CentroidChecked,
                MzDecimalPlace = MzDecimalPlace,
                IntensityDecimalPlace = IntensityDecimalPlace,
                ExtractPrecursorByMz = ExtractPrecursorByMz,
                BypassThermoAlgorithm = BypassThermoAlgorithm,
                ShowPeakChargeStates = ShowPeakChargeStates,
                ShowPeakResolution = ShowPeakResolution,
                ExportChargeState = ExportChargeState,
                DDADataChargeStates = new HashSet<int>(ChargeStates),
                Ms2PrecursorCharges = ChargeStates.ToArray(),
                MaxParallelFiles = threads,
            };
            return (options, null);
        }

        /// <summary>
        /// The Go!/Stop! button.
        /// </summary>
        public async Task GoOrStop()
        {
            if (IsRunning)
            {
                if (await _dialogs.Confirm("Stop the converting?", "Warning"))
                {
                    _cancellation?.Cancel();
                }
                return;
            }

            ProgressPercent = 0;
            LogLines.Clear();
            (ConversionOptions options, string error) = BuildOptions();
            if (error != null)
            {
                await _dialogs.ShowMessage(error, "Information");
                return;
            }
            Directory.CreateDirectory(options.OutputFolder);

            List<string> files = InputFiles.ToList();
            int[] percents = new int[files.Count];
            FileProcessingLabel = "File 1 / " + files.Count + ":";
            UiProgress progress = new UiProgress(p =>
            {
                percents[p.FileIndex] = p.Percent;
                ProgressPercent = (int)percents.Average();
                int current = Math.Min(files.Count, percents.Count(x => x >= 100) + 1);
                FileProcessingLabel = "File " + current + " / " + files.Count + ":";
            });

            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            IReadOnlyList<FileConversionResult> results;
            try
            {
                CancellationToken token = _cancellation.Token;
                results = await Task.Run(() => ConversionRunner.ConvertFiles(files, options, progress, token));
            }
            finally
            {
                IsRunning = false;
            }

            if (results.Any(r => r.Cancelled))
            {
                LogLines.Clear();
                ProgressPercent = 0;
                FileProcessingLabel = "File 0 / 0:";
                return;
            }
            foreach (FileConversionResult result in results.Where(r => !r.Succeeded))
            {
                LogLines.Add(" ERROR converting " + result.InputFile + ": " + result.Error?.Message);
            }
            if (LogChecked)
            {
                WriteLogFile(options.OutputFolder);
            }
            bool succeeded = results.All(r => r.Succeeded);
            await _dialogs.ShowMessage(succeeded ? "Finished converting successfully!" : "Finished converting unsuccessfully!",
                succeeded ? "Information" : "Warning");
        }

        private void WriteLogFile(string folder)
        {
            string path = Path.Combine(folder, "RawConverter_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            File.WriteAllLines(path, LogLines);
            LogLines.Add(" Log written to " + path);
        }

        /// <summary>
        /// Called from the conversion threads; the window marshals it to the UI thread.
        /// </summary>
        public void AddLogLine(string line)
        {
            LogLines.Add(line);
        }

        /// <summary>
        /// Runs progress callbacks on the UI thread.
        /// </summary>
        private class UiProgress : IProgress<ConversionProgress>
        {
            private readonly Action<ConversionProgress> _report;
            private readonly SynchronizationContext _context = SynchronizationContext.Current;

            public UiProgress(Action<ConversionProgress> report)
            {
                _report = report;
            }

            public void Report(ConversionProgress value)
            {
                if (_context == null)
                {
                    lock (this)
                    {
                        _report(value);
                    }
                }
                else
                {
                    _context.Post(_ => _report(value), null);
                }
            }
        }
    }
}
