using System.Collections.Generic;
using System.IO;

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
                    case "tar":
                    case "d_tar":
                        cl.Options.OutputFormats |= OutputFormat.DTar;
                        break;
                    case "log":
                        // accepted for compatibility; the log file was never implemented;
                        break;
                    case "out_dir":
                    case "out_folder":
                        // --out_folder is the old name, kept for existing scripts and right-click menus;
                        if (++i >= args.Count)
                        {
                            cl.Error = "--out_dir requires a folder.";
                            return cl;
                        }
                        // GetFullPath resolves "..", which the right-click menu uses for .d folders;
                        cl.Options.OutputFolder = Path.GetFullPath(args[i]);
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
                    cl.Error = "No output format given (--ms1, --ms2, --ms3, --mgf, --mzxml, --mzparquet or --tar).";
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
}
