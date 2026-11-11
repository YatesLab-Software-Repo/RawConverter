using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using RawConverter.Common;
using RawConverter.MassSpec;

namespace RawConverter.Cli
{
    internal class CommandLine
    {
        public List<string> InputFiles { get; } = new List<string>();
        public ConversionOptions Options { get; } = new ConversionOptions();
        public bool ShowHelp { get; private set; }
        public bool ShowVersion { get; private set; }
        public bool CheckUpdate { get; private set; }
        public bool Quiet { get; private set; }
        public bool Verbose { get; private set; }
        public string Error { get; private set; }

        /// <summary>
        /// Options are case-insensitive and accept any number of leading dashes (--ms2, -ms2, ms2),
        /// as earlier versions did.
        /// </summary>
        public static CommandLine Parse(IReadOnlyList<string> args)
        {
            CommandLine cl = new CommandLine();
            bool hasExpType = false;

            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                string name = arg.TrimStart('-').ToLowerInvariant();
                bool isOption = arg.StartsWith("-") || IsBareOption(name);
                if (!isOption)
                {
                    cl.InputFiles.Add(arg);
                    continue;
                }

                switch (name)
                {
                    case "h":
                    case "help":
                        cl.ShowHelp = true;
                        break;
                    case "version":
                        cl.ShowVersion = true;
                        break;
                    case "check_update":
                    case "check-update":
                        cl.CheckUpdate = true;
                        break;
                    case "q":
                    case "quiet":
                        cl.Quiet = true;
                        break;
                    case "v":
                    case "verbose":
                        cl.Verbose = true;
                        break;
                    case "ms1":
                        cl.Options.OutputFormats |= OutputFormat.MS1;
                        break;
                    case "ms2":
                        cl.Options.OutputFormats |= OutputFormat.MS2;
                        break;
                    case "ms3":
                        cl.Options.OutputFormats |= OutputFormat.MS3;
                        break;
                    case "mgf":
                        cl.Options.OutputFormats |= OutputFormat.MGF;
                        break;
                    case "mzxml":
                        cl.Options.OutputFormats |= OutputFormat.MzXML;
                        break;
                    case "mzparquet":
                        cl.Options.OutputFormats |= OutputFormat.MzParquet;
                        break;
                    case "log":
                        // accepted for compatibility; the log file was never implemented;
                        break;
                    case "out_folder":
                        if (++i >= args.Count)
                        {
                            cl.Error = "--out_folder requires a folder.";
                            return cl;
                        }
                        cl.Options.OutputFolder = args[i];
                        break;
                    case "parallel":
                        if (++i >= args.Count || !int.TryParse(args[i], out int parallel) || parallel < 1)
                        {
                            cl.Error = "--parallel requires a positive number.";
                            return cl;
                        }
                        cl.Options.MaxParallelFiles = parallel;
                        break;
                    case "select_mono_prec":
                    case "predict_precursors":
                        if (hasExpType)
                        {
                            cl.Error = "--select_mono_prec and --predict_precursors are two mutex options, you can only select one of them!";
                            return cl;
                        }
                        hasExpType = true;
                        if (name == "select_mono_prec")
                        {
                            cl.Options.ExperimentType = ExperimentType.DDA;
                            cl.Options.CorrectPrecursorMz = true;
                        }
                        else
                        {
                            cl.Options.ExperimentType = ExperimentType.DIA;
                            cl.Options.PredictPrecursors = true;
                        }
                        break;
                    default:
                        cl.Error = "Unknown option: " + arg;
                        return cl;
                }
            }

            if (!cl.ShowHelp && !cl.ShowVersion && !cl.CheckUpdate)
            {
                if (cl.InputFiles.Count == 0)
                {
                    cl.Error = "No input file given.";
                }
                else if (cl.Options.OutputFormats == OutputFormat.None)
                {
                    cl.Error = "No output format given (--ms1, --ms2, --ms3, --mgf, --mzxml or --mzparquet).";
                }
            }
            return cl;
        }

        private static bool IsBareOption(string name)
        {
            // earlier versions accepted options without dashes;
            switch (name)
            {
                case "ms1":
                case "ms2":
                case "ms3":
                case "mgf":
                case "mzxml":
                case "log":
                case "select_mono_prec":
                case "predict_precursors":
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class Program
    {
        public static int Main(string[] args)
        {
            CommandLine cl = CommandLine.Parse(args);
            if (cl.ShowHelp || args.Length == 0)
            {
                Usage();
                return 0;
            }
            if (cl.ShowVersion)
            {
                Console.WriteLine("RawConverter " + ConversionRunner.Version);
                Console.WriteLine(ConversionRunner.BrukerNotice);
                return 0;
            }
            if (cl.CheckUpdate && cl.Error == null)
            {
                return CheckUpdate();
            }
            if (cl.Error != null)
            {
                Console.Error.WriteLine("ERROR: " + cl.Error);
                Console.Error.WriteLine("Run with --help for usage.");
                return 2;
            }

            if (!cl.Quiet)
            {
                Log.InfoSink = message => Console.WriteLine(message);
            }
            Log.WarningSink = message => Console.Error.WriteLine("WARNING: " + message);

            using CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                if (!cts.IsCancellationRequested)
                {
                    Console.Error.WriteLine(" Cancelling...");
                    e.Cancel = true;
                    cts.Cancel();
                }
            };

            ConsoleProgress progress = cl.Quiet ? null : new ConsoleProgress();
            IReadOnlyList<FileConversionResult> results = ConversionRunner.ConvertFiles(cl.InputFiles, cl.Options, progress, cts.Token);

            int failed = 0;
            foreach (FileConversionResult result in results)
            {
                if (result.Cancelled)
                {
                    Console.Error.WriteLine(" Cancelled: " + result.InputFile);
                    failed++;
                }
                else if (!result.Succeeded)
                {
                    Console.Error.WriteLine(" ERROR converting " + result.InputFile + ": "
                        + (cl.Verbose ? result.Error?.ToString() : result.Error?.Message));
                    failed++;
                }
            }
            if (failed > 0)
            {
                Console.Error.WriteLine(" Converting completed unsuccessfully for " + failed + " of " + results.Count + " file(s)!");
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Look up the latest release on GitHub; 0 if the check worked (whether or not there is an update), 1 if not.
        /// </summary>
        private static int CheckUpdate()
        {
            try
            {
                ReleaseInfo update = UpdateChecker.CheckForUpdateAsync().GetAwaiter().GetResult();
                if (update == null)
                {
                    Console.WriteLine("RawConverter " + ConversionRunner.Version + " is the latest version.");
                }
                else
                {
                    Console.WriteLine("RawConverter " + update.Version + " is available (this is " + ConversionRunner.Version
                        + "): " + update.Url);
                }
                return 0;
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException
                || e is System.Text.Json.JsonException)
            {
                Console.Error.WriteLine("ERROR: could not check for updates (" + e.Message + "). Releases: "
                    + UpdateChecker.ReleasesPage);
                return 1;
            }
        }

        /// <summary>
        /// Print the usage of this program.
        /// </summary>
        private static void Usage()
        {
            Console.WriteLine("RawConverter " + ConversionRunner.Version);
            Console.WriteLine("Usage:");
            Console.WriteLine("\tRawConverter <input_file> [<input_file> ...] [options]");
            Console.WriteLine("\tInput files: .raw, .mzXML, .mzML, .ms2 (to MGF and mzParquet), .mgf (to MS2),");
            Console.WriteLine("\t             Bruker timsTOF DDA-PASEF .d folders (to MS2, mzXML and mzParquet; written inside the .d by default),");
            Console.WriteLine("\t             or a .d folder in a .tar/.tar.gz/.tgz (the .d is extracted next to the archive,");
            Console.WriteLine("\t             and the output is written next to the .d by default)");
            Console.WriteLine("\tOptions:");
            Console.WriteLine("\t\t--ms1\toutput MS1 file.");
            Console.WriteLine("\t\t--ms2\toutput MS2 file.");
            Console.WriteLine("\t\t--ms3\toutput MS3 file.");
            Console.WriteLine("\t\t--mgf\toutput MGF file.");
            Console.WriteLine("\t\t--mzxml\toutput mzXML file.");
            Console.WriteLine("\t\t--mzparquet\toutput mzParquet file (one row per peak; see README).");
            Console.WriteLine("\t\t--out_folder <dir>\toutput folder (default: next to each input file).");
            Console.WriteLine("\t\t--select_mono_prec\tselect the monoisotopic m/z values of precursors in DDA data.");
            Console.WriteLine("\t\t--predict_precursors\tpredict the precursors for DIA data.");
            Console.WriteLine("\t\t--parallel <n>\tconvert n files at the same time (default: 1).");
            Console.WriteLine("\t\t--quiet\tonly print errors.");
            Console.WriteLine("\t\t--verbose\tprint full error details.");
            Console.WriteLine("\t\t--version\tprint the version.");
            Console.WriteLine("\t\t--check_update\tcheck GitHub for a newer release.");
        }
    }

    /// <summary>
    /// Prints per-file progress; overwrites one line on a terminal and prints every 10% when redirected.
    /// </summary>
    internal class ConsoleProgress : IProgress<ConversionProgress>
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, int> _lastPrinted = new Dictionary<int, int>();

        public void Report(ConversionProgress value)
        {
            lock (_lock)
            {
                int step = Console.IsOutputRedirected ? 10 : 1;
                _lastPrinted.TryGetValue(value.FileIndex, out int last);
                if (value.Percent < last + step && value.Percent != 100)
                {
                    return;
                }
                _lastPrinted[value.FileIndex] = value.Percent;

                string line = " Converting file " + (value.FileIndex + 1) + " / " + value.FileCount + ": " + value.Percent + "%";
                if (Console.IsOutputRedirected)
                {
                    Console.WriteLine(line);
                }
                else
                {
                    Console.Write("\r" + line + (value.Percent == 100 ? Environment.NewLine : ""));
                }
            }
        }
    }
}
