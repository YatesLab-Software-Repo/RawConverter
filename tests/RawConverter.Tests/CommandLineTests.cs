using RawConverter.Cli;
using RawConverter.MassSpec;
using Xunit;

namespace RawConverter.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void ParsesInputAndOutputFormats()
        {
            CommandLine cl = CommandLine.Parse(new[] { "a.raw", "--ms1", "--ms2", "--mgf", "--mzxml" });

            Assert.Null(cl.Error);
            Assert.Equal(new[] { "a.raw" }, cl.InputFiles);
            Assert.Equal(OutputFormat.MS1 | OutputFormat.MS2 | OutputFormat.MGF | OutputFormat.MzXML, cl.Options.OutputFormats);
            Assert.Null(cl.Options.OutputFolder);
        }

        [Theory]
        [InlineData("--ms2")]
        [InlineData("-ms2")]
        [InlineData("ms2")]
        [InlineData("--MS2")]
        public void AcceptsLegacyOptionSpellings(string option)
        {
            CommandLine cl = CommandLine.Parse(new[] { "a.raw", option });

            Assert.Null(cl.Error);
            Assert.Equal(OutputFormat.MS2, cl.Options.OutputFormats);
            Assert.Single(cl.InputFiles);
        }

        [Fact]
        public void ParsesMultipleInputsOutFolderAndParallel()
        {
            CommandLine cl = CommandLine.Parse(new[] { "a.raw", "b.raw", "--ms2", "--out_folder", "out", "--parallel", "3" });

            Assert.Null(cl.Error);
            Assert.Equal(new[] { "a.raw", "b.raw" }, cl.InputFiles);
            Assert.Equal("out", cl.Options.OutputFolder);
            Assert.Equal(3, cl.Options.MaxParallelFiles);
        }

        [Fact]
        public void SelectMonoPrecursorEnablesDdaCorrection()
        {
            CommandLine cl = CommandLine.Parse(new[] { "a.raw", "--ms2", "--select_mono_prec" });

            Assert.Null(cl.Error);
            Assert.Equal(ExperimentType.DDA, cl.Options.ExperimentType);
            Assert.True(cl.Options.CorrectPrecursorMz);
            Assert.False(cl.Options.PredictPrecursors);
        }

        [Fact]
        public void PredictPrecursorsEnablesDia()
        {
            CommandLine cl = CommandLine.Parse(new[] { "a.raw", "--ms2", "--predict_precursors" });

            Assert.Null(cl.Error);
            Assert.Equal(ExperimentType.DIA, cl.Options.ExperimentType);
            Assert.True(cl.Options.PredictPrecursors);
            Assert.False(cl.Options.CorrectPrecursorMz);
        }

        [Theory]
        [InlineData("a.raw --ms2 --select_mono_prec --predict_precursors")]
        [InlineData("a.raw")]
        [InlineData("--ms2")]
        [InlineData("a.raw --ms2 --out_folder")]
        [InlineData("a.raw --ms2 --parallel 0")]
        [InlineData("a.raw --ms2 --bogus")]
        public void ReportsErrors(string args)
        {
            Assert.NotNull(CommandLine.Parse(args.Split(' ')).Error);
        }

        [Fact]
        public void HelpNeedsNoInput()
        {
            CommandLine cl = CommandLine.Parse(new[] { "--help" });

            Assert.True(cl.ShowHelp);
            Assert.Null(cl.Error);
        }
    }
}
