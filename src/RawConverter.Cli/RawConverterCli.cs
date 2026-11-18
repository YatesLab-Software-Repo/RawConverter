using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using RawConverter.Common;

namespace RawConverter.Cli
{
    internal static class RawConverterCli
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

            ConsoleProgress progress = cl.Quiet ? null : new ConsoleProgress(cl.InputFiles);
            IReadOnlyList<FileConversionResult> results = ConversionRunner.ConvertFiles(cl.InputFiles, cl.Options, progress, cts.Token);
            if (progress != null)
            {
                Console.WriteLine(" Finished in " + BatchProgress.FormatDuration(progress.Batch.Elapsed) + ".");
            }

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
            Console.WriteLine("\t\t--tar\tpack each timsTOF .d folder into <name>.d.tar (next to the .d, or in --out_dir);");
            Console.WriteLine("\t\t     \talone it only packs, with other formats it packs and then converts.");
            Console.WriteLine("\t\t--out_dir <dir>\toutput folder (default: next to each input file).");
            Console.WriteLine("\t\t--select_mono_prec\tselect the monoisotopic m/z values of precursors in DDA data.");
            Console.WriteLine("\t\t--predict_precursors\tpredict the precursors for DIA data.");
            Console.WriteLine("\t\t--parallel <n>\tconvert n files at the same time (default: 1).");
            Console.WriteLine("\t\t--quiet\tonly print errors.");
            Console.WriteLine("\t\t--verbose\tprint full error details.");
            Console.WriteLine("\t\t--version\tprint the version.");
            Console.WriteLine("\t\t--check_update\tcheck GitHub for a newer release.");
        }
    }
}
