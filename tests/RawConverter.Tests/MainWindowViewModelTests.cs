using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        [InlineData("x.raw", 5, true, true, true, true, true, null)]
        [InlineData("x.mzXML", 0, false, true, true, false, false, null)]
        [InlineData("x.ms2", 0, false, false, false, false, false, "The input and the output formats cannot be the same!")]
        [InlineData("x.ms2", 1, false, false, false, false, false, null)]
        [InlineData("x.mgf", 2, false, false, false, false, false, "The input can only be RAW files if mzXML is selected")]
        [InlineData("x.d.tar", 0, false, true, false, false, false, null)]
        [InlineData("x.d.tar", 1, false, false, false, false, false, "timsTOF input can only be converted to MS2, mzXML and mzParquet!")]
        [InlineData("x.d.tar", 2, false, false, false, false, false, null)]
        [InlineData("x.d.tar", 3, false, false, false, false, false, "timsTOF input can only be converted to MS2, mzXML and mzParquet!")]
        public async Task OutputChoicesFollowTheLegacyRules(string input, int formatIndex,
            bool ms1, bool ms2, bool ms3, bool mgf, bool mzXml, string warning)
        {
            await _vm.AddInputs(new[] { Path.Combine(_dir, input) });

            _vm.OutputFormatIndex = formatIndex;

            Assert.Equal(new[] { ms1, ms2, ms3, mgf, mzXml },
                new[] { _vm.Ms1Enabled, _vm.Ms2Enabled, _vm.Ms3Enabled, _vm.MgfEnabled, _vm.MzXmlEnabled });
            Assert.Equal(warning, _dialogs.Messages.SingleOrDefault());
        }

        [Theory]
        [InlineData("x.raw", true, null)]
        [InlineData("x.mzML", true, null)]
        [InlineData("x.ms2", true, null)]
        [InlineData("x.d.tar", true, null)]
        [InlineData("x.mgf", false, "MGF input cannot be converted to mzParquet!")]
        public async Task MzParquetIsCheckedForSupportedInputs(string input, bool isChecked, string warning)
        {
            await _vm.AddInputs(new[] { Path.Combine(_dir, input) });

            _vm.OutputFormatIndex = 4;

            Assert.Equal(isChecked, _vm.MzParquetChecked);
            Assert.False(_vm.Ms2Checked || _vm.MzXmlChecked);
            Assert.Equal(warning, _dialogs.Messages.SingleOrDefault());
            if (isChecked)
            {
                Assert.Equal(OutputFormat.MzParquet, _vm.BuildOptions().Options.OutputFormats);
            }
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
        public async Task DTarIsOfferedOnlyForTimsTofFolders()
        {
            string d = Path.Combine(_dir, "sample.d");
            Directory.CreateDirectory(d);
            await _vm.AddInputs(new[] { d });

            Assert.True(_vm.DTarEnabled);
            _vm.DTarChecked = true;
            (ConversionOptions options, string error) = _vm.BuildOptions();
            Assert.Null(error);
            Assert.Equal(OutputFormat.DTar, options.OutputFormats);

            _vm.OutputFormatIndex = 0;
            Assert.True(_vm.DTarChecked);
            Assert.Equal(OutputFormat.DTar | OutputFormat.MS2, _vm.BuildOptions().Options.OutputFormats);

            _vm.SelectedInputFile = d;
            _vm.RemoveSelectedInput();
            await _vm.AddInputs(new[] { Path.Combine(_dir, "a.raw") });
            Assert.False(_vm.DTarEnabled);
            Assert.False(_vm.DTarChecked);
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
            _vm.OutputFormatIndex = 5;
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

        [Fact]
        public async Task StartupCheckShowsUpdateWithoutDialog()
        {
            _vm.UpdateSource = ct => Task.FromResult(new ReleaseInfo("9.9.9", "https://example.org/release"));

            await _vm.CheckForUpdates(false);

            Assert.True(_vm.IsUpdateAvailable);
            Assert.Equal("Update available: 9.9.9", _vm.UpdateMenuText);
            Assert.Contains(_vm.LogLines, l => l.Contains("9.9.9") && l.Contains("https://example.org/release"));
            Assert.Empty(_dialogs.Messages);

            await _vm.OpenUpdatePage();
            Assert.Equal(new[] { "https://example.org/release" }, _dialogs.OpenedUrls);
        }

        [Fact]
        public async Task StartupCheckIsSilentWhenOffline()
        {
            _vm.UpdateSource = ct => throw new HttpRequestException("no network");

            await _vm.CheckForUpdates(false);

            Assert.False(_vm.IsUpdateAvailable);
            Assert.Empty(_dialogs.Messages);
            Assert.Empty(_vm.LogLines);
        }

        [Fact]
        public async Task StartupCheckCanBeTurnedOff()
        {
            bool called = false;
            _vm.UpdateSource = ct =>
            {
                called = true;
                return Task.FromResult<ReleaseInfo>(null);
            };
            _vm.CheckForUpdatesAtStartup = false;

            await _vm.CheckForUpdatesAtStartupIfEnabled();

            Assert.False(called);
        }

        private static readonly ReleaseInfo Nightly =
            new ReleaseInfo("9.9.9-nightly.20991231.1", "https://example.org/nightly", "https://example.org/setup.exe",
                "https://example.org/SHA256SUMS.txt");

        private string FakeInstaller => Path.Combine(_dir, "setup.exe");

        private List<string> UseFakeInstaller(bool nightlyBuild, Func<string> download = null)
        {
            List<string> events = new List<string>();
            _vm.IsNightlyBuild = nightlyBuild;
            _vm.CanInstallUpdates = true;
            _vm.UpdateSource = ct => Task.FromResult(Nightly);
            _vm.InstallerDownloader = (release, ct) =>
            {
                events.Add("download " + release.Version);
                if (download != null)
                {
                    return Task.FromResult(download());
                }
                File.WriteAllText(FakeInstaller, "");
                return Task.FromResult(FakeInstaller);
            };
            _vm.InstallerStarter = path => events.Add("start " + path);
            _vm.ExitRequested += () => events.Add("exit");
            return events;
        }

        [Fact]
        public async Task NightlyBuildInstallsNewerNightlyAtStartup()
        {
            List<string> events = UseFakeInstaller(true);

            await _vm.CheckForUpdatesAtStartupIfEnabled();

            Assert.Equal(new[] { "download 9.9.9-nightly.20991231.1", "start " + FakeInstaller, "exit" }, events);
            Assert.Empty(_dialogs.Messages);
            Assert.Contains(_vm.LogLines, l => l.StartsWith("Installing RawConverter 9.9.9-nightly"));
        }

        [Fact]
        public async Task ReleaseBuildDoesNotInstallAtStartup()
        {
            List<string> events = UseFakeInstaller(false);

            await _vm.CheckForUpdatesAtStartupIfEnabled();

            Assert.Empty(events);
            Assert.True(_vm.IsUpdateAvailable);
        }

        [Fact]
        public async Task AutomaticNightlyInstallCanBeTurnedOff()
        {
            List<string> events = UseFakeInstaller(true);
            _vm.InstallNightlyUpdatesAutomatically = false;

            await _vm.CheckForUpdatesAtStartupIfEnabled();

            Assert.Empty(events);
            Assert.True(_vm.IsUpdateAvailable);
        }

        [Fact]
        public async Task NothingIsInstalledDuringAConversion()
        {
            List<string> events = UseFakeInstaller(true);
            _vm.IsRunning = true;

            await _vm.CheckForUpdatesAtStartupIfEnabled();
            Assert.Equal(new[] { "download 9.9.9-nightly.20991231.1" }, events);

            // when the conversion has finished, the menu item installs the downloaded installer;
            _vm.IsRunning = false;
            await _vm.ApplyAvailableUpdate();
            Assert.Equal(new[] { "download 9.9.9-nightly.20991231.1", "start " + FakeInstaller, "exit" }, events);
        }

        [Fact]
        public async Task FailedDownloadKeepsRawConverterOpen()
        {
            List<string> events = UseFakeInstaller(true, () => throw new InvalidDataException("checksum mismatch"));

            await _vm.CheckForUpdatesAtStartupIfEnabled();

            Assert.DoesNotContain("exit", events);
            Assert.Contains(_vm.LogLines, l => l.Contains("checksum mismatch"));
        }

        [Fact]
        public async Task ExplicitCheckOffersToInstall()
        {
            List<string> events = UseFakeInstaller(false);

            await _vm.CheckForUpdates(true);

            Assert.Contains(_dialogs.Messages, m => m.Contains("Install it now?"));
            Assert.Equal(new[] { "download 9.9.9-nightly.20991231.1", "start " + FakeInstaller, "exit" }, events);
        }

        [Fact]
        public async Task DeclinedInstallDoesNothing()
        {
            List<string> events = UseFakeInstaller(false);
            _dialogs.ConfirmResult = false;

            await _vm.CheckForUpdates(true);

            Assert.Empty(events);
        }

        [Fact]
        public async Task ExplicitCheckOffersDownloadPage()
        {
            _vm.UpdateSource = ct => Task.FromResult(new ReleaseInfo("9.9.9", "https://example.org/release"));

            await _vm.CheckForUpdates(true);

            Assert.Contains(_dialogs.Messages, m => m.Contains("9.9.9 is available"));
            Assert.Equal(new[] { "https://example.org/release" }, _dialogs.OpenedUrls);
        }

        [Fact]
        public async Task ExplicitCheckReportsLatestAndErrors()
        {
            _vm.UpdateSource = ct => Task.FromResult<ReleaseInfo>(null);
            await _vm.CheckForUpdates(true);
            Assert.Contains(_dialogs.Messages, m => m.Contains("is the latest version"));

            _vm.UpdateSource = ct => throw new TaskCanceledException("timed out");
            await _vm.CheckForUpdates(true);
            Assert.Contains(_dialogs.Messages, m => m.StartsWith("Could not check for updates"));
            Assert.Empty(_dialogs.OpenedUrls);
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
                return Task.FromResult(ConfirmResult);
            }

            public bool ConfirmResult { get; set; } = true;
            public List<string> OpenedUrls { get; } = new List<string>();

            public Task OpenUrl(string url)
            {
                OpenedUrls.Add(url);
                return Task.CompletedTask;
            }
        }
    }
}
