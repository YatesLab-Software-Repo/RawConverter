using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RawConverter.Gui.ViewModels;
using RawConverter.MassSpec;
using Xunit;

namespace RawConverter.Tests
{
    public class MainWindowViewModelTests : IDisposable
    {
        private readonly string _dir;
        private readonly FakeDialogs _dialogs = new FakeDialogs();
        private readonly MainWindowViewModel _vm;

        public MainWindowViewModelTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _vm = new MainWindowViewModel(_dialogs);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void StartsLikeTheLegacyWindow()
        {
            Assert.True(_vm.IsDda);
            Assert.False(_vm.IsDia);
            Assert.True(_vm.MassCorrectionEnabled);
            Assert.False(_vm.MassCorrectionChecked);
            Assert.False(_vm.PredictDiaEnabled);
            Assert.False(_vm.CentroidEnabled);
            Assert.True(_vm.CentroidChecked);
            Assert.Equal(-1, _vm.OutputFormatIndex);
            Assert.Equal("3", _vm.ThreadCountText);
            Assert.Equal(4, _vm.MzDecimalPlace);
            Assert.Equal(1, _vm.IntensityDecimalPlace);
            Assert.Equal(new[] { 2, 3 }, _vm.ChargeStates);
            Assert.True(_vm.BypassThermoAlgorithm);
            Assert.Equal("Go!", _vm.GoButtonText);
        }

        [Fact]
        public void SwitchesBetweenDdaAndDiaOptions()
        {
            _vm.IsDia = true;

            Assert.False(_vm.IsDda);
            Assert.False(_vm.MassCorrectionEnabled);
            Assert.True(_vm.PredictDiaEnabled);

            _vm.IsDda = true;

            Assert.False(_vm.IsDia);
            Assert.True(_vm.MassCorrectionEnabled);
            Assert.True(_vm.MassCorrectionChecked);
            Assert.False(_vm.PredictDiaEnabled);
            Assert.True(_vm.CentroidEnabled);
        }

        [Fact]
        public async Task AddingFilesSetsDestinationAndWarnsOnMixedFormats()
        {
            await _vm.AddInputs(new[] { Path.Combine(_dir, "a.raw"), Path.Combine(_dir, "a.raw") });

            Assert.Single(_vm.InputFiles);
            Assert.Equal(_dir, _vm.OutputDirectory);
            Assert.Empty(_dialogs.Messages);

            await _vm.AddInputs(new[] { Path.Combine(_dir, "b.mgf") });

            Assert.Equal("Different file formats? Please do NOT!", _dialogs.Messages.Single());
        }

        [Theory]
        [InlineData("x.raw", 0, true, true, true, false, false, null)]
        [InlineData("x.raw", 4, true, true, true, true, true, null)]
        [InlineData("x.mzXML", 0, false, true, true, false, false, null)]
        [InlineData("x.ms2", 0, false, false, false, false, false, "The input and the output formats cannot be the same!")]
        [InlineData("x.ms2", 1, false, false, false, false, false, null)]
        [InlineData("x.mgf", 2, false, false, false, false, false, "The input can only be RAW files if mzXML is selected")]
        [InlineData("x.d.tar", 0, false, true, false, false, false, null)]
        [InlineData("x.d.tar", 1, false, false, false, false, false, "timsTOF input can only be converted to MS2 and mzXML!")]
        [InlineData("x.d.tar", 2, false, false, false, false, false, null)]
        [InlineData("x.d.tar", 3, false, false, false, false, false, "timsTOF input can only be converted to MS2 and mzXML!")]
        public async Task OutputChoicesFollowTheLegacyRules(string input, int formatIndex,
            bool ms1, bool ms2, bool ms3, bool mgf, bool mzXml, string warning)
        {
            await _vm.AddInputs(new[] { Path.Combine(_dir, input) });

            _vm.OutputFormatIndex = formatIndex;

            Assert.Equal(new[] { ms1, ms2, ms3, mgf, mzXml },
                new[] { _vm.Ms1Enabled, _vm.Ms2Enabled, _vm.Ms3Enabled, _vm.MgfEnabled, _vm.MzXmlEnabled });
            Assert.Equal(warning, _dialogs.Messages.SingleOrDefault());
        }

        [Fact]
        public async Task TimsTofFoldersAreTreatedAsTimsTofInput()
        {
            string d = Path.Combine(_dir, "sample.d");
            Directory.CreateDirectory(d);
            await _vm.AddInputs(new[] { d });

            _vm.OutputFormatIndex = 0;

            Assert.Equal(".d", MainWindowViewModel.InputFormatOf(d));
            Assert.True(!_vm.Ms1Enabled && _vm.Ms2Enabled && !_vm.Ms3Enabled);
            Assert.Equal(_dir, _vm.OutputDirectory);
        }

        [Fact]
        public async Task RemovingSelectsThePreviousFile()
        {
            await _vm.AddInputs(new[] { "/a.raw", "/b.raw", "/c.raw" });

            _vm.SelectedInputFile = "/b.raw";
            _vm.RemoveSelectedInput();
            Assert.Equal("/a.raw", _vm.SelectedInputFile);

            _vm.RemoveSelectedInput();
            Assert.Equal("/c.raw", _vm.SelectedInputFile);

            _vm.RemoveSelectedInput();
            Assert.Empty(_vm.InputFiles);
            Assert.Null(_vm.CurrentInputFormat);
        }

        [Theory]
        [InlineData("", "Please Enter Number of Threads to Convert!")]
        [InlineData("two", "Threads count only in Numbers!")]
        public async Task ValidatesThreadCount(string threads, string error)
        {
            await _vm.AddInputs(new[] { "/a.raw" });
            _vm.OutputFormatIndex = 0;
            _vm.ThreadCountText = threads;

            Assert.Equal(error, _vm.BuildOptions().Error);
        }

        [Fact]
        public void ValidatesInputsAndFormats()
        {
            Assert.Equal("No input file! Please select input file(s).", _vm.BuildOptions().Error);
        }

        [Fact]
        public async Task BuildsConversionOptions()
        {
            await _vm.AddInputs(new[] { "/data/a.raw" });
            _vm.IsDia = true;
            _vm.PredictDiaChecked = true;
            _vm.OutputFormatIndex = 4;
            _vm.Ms1Checked = true;
            _vm.MgfChecked = true;
            _vm.MzDecimalPlace = 2;
            _vm.ToggleChargeState(2);
            _vm.ToggleChargeState(3);
            _vm.ShowPeakResolution = false;
            _vm.ThreadCountText = "2";

            (ConversionOptions options, string error) = _vm.BuildOptions();

            Assert.Null(error);
            Assert.Equal(OutputFormat.MS1 | OutputFormat.MS2 | OutputFormat.MGF, options.OutputFormats);
            Assert.Equal("/data", options.OutputFolder);
            Assert.Equal(ExperimentType.DIA, options.ExperimentType);
            Assert.True(options.PredictPrecursors);
            Assert.Equal(2, options.MzDecimalPlace);
            Assert.False(options.ShowPeakResolution);
            Assert.Equal(2, options.MaxParallelFiles);
            // no charge state selected falls back to 2+ and 3+, as before;
            Assert.Equal(new[] { 2, 3 }, options.Ms2PrecursorCharges);
        }

        [Fact]
        public async Task ConvertsAndReportsSuccess()
        {
            string mgf = Path.Combine(_dir, "test.mgf");
            File.WriteAllText(mgf, "BEGIN IONS\nPEPMASS=500.25\nCHARGE=2+\nSCANS=1\n150.125 10.5\nEND IONS\n");
            await _vm.AddInputs(new[] { mgf });
            _vm.OutputFormatIndex = 0;
            _vm.OutputDirectory = Path.Combine(_dir, "out");
            _vm.LogChecked = true;

            await _vm.GoOrStop();

            Assert.Equal("Finished converting successfully!", _dialogs.Messages.Last());
            Assert.True(File.Exists(Path.Combine(_dir, "out", "test.ms2")));
            Assert.Single(Directory.GetFiles(Path.Combine(_dir, "out"), "RawConverter_*.log"));
            Assert.Equal(100, _vm.ProgressPercent);
            Assert.False(_vm.IsRunning);
        }

        [Fact]
        public async Task ReportsFailures()
        {
            await _vm.AddInputs(new[] { Path.Combine(_dir, "missing.raw") });
            _vm.OutputFormatIndex = 0;

            await _vm.GoOrStop();

            Assert.Equal("Finished converting unsuccessfully!", _dialogs.Messages.Last());
            Assert.Contains(_vm.LogLines, l => l.Contains("ERROR converting"));
        }

        private class FakeDialogs : IDialogService
        {
            public List<string> Messages { get; } = new List<string>();

            public Task ShowMessage(string text, string title)
            {
                Messages.Add(text);
                return Task.CompletedTask;
            }

            public Task<bool> Confirm(string text, string title)
            {
                Messages.Add(text);
                return Task.FromResult(true);
            }
        }
    }
}
