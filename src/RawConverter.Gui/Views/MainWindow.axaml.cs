using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RawConverter.Common;
using RawConverter.Gui.ViewModels;

namespace RawConverter.Gui.Views
{
    public partial class MainWindow : Window, IDialogService
    {
        private static readonly FilePickerFileType[] InputFileTypes =
        {
            new FilePickerFileType("Thermo RAW Files") { Patterns = new[] { "*.raw", "*.RAW" } },
            new FilePickerFileType("MzXML Files") { Patterns = new[] { "*.mzXML", "*.mzxml" } },
            new FilePickerFileType("MzML Files") { Patterns = new[] { "*.mzML", "*.mzml" } },
            new FilePickerFileType("MS2 Files") { Patterns = new[] { "*.ms2", "*.MS2" } },
            new FilePickerFileType("MS3 Files") { Patterns = new[] { "*.ms3", "*.MS3" } },
            new FilePickerFileType("MGF Files") { Patterns = new[] { "*.mgf", "*.MGF" } },
            new FilePickerFileType("timsTOF .d Archives") { Patterns = new[] { "*.tar", "*.tar.gz", "*.tgz" } },
        };

        private readonly MainWindowViewModel _vm;
        private readonly List<Action> _menuSync = new List<Action>();
        private readonly GuiSettings _settings = GuiSettings.Load();

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainWindowViewModel(this);
            DataContext = _vm;
            _vm.CheckForUpdatesAtStartup = _settings.CheckForUpdatesAtStartup;

            BuildOptionsMenu();
            _vm.PropertyChanged += (sender, e) => SyncMenus();
            _vm.LogLines.CollectionChanged += (sender, e) =>
            {
                if (_vm.LogLines.Count > 0)
                {
                    LogList.ScrollIntoView(_vm.LogLines.Count - 1);
                }
            };

            Log.InfoSink = message => Dispatcher.UIThread.Post(() => _vm.AddLogLine(message));
            Log.WarningSink = message => Dispatcher.UIThread.Post(() => _vm.AddLogLine("WARNING: " + message));

            InputList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            InputList.AddHandler(DragDrop.DropEvent, OnDrop);

            Opened += async (sender, e) => await _vm.CheckForUpdatesAtStartupIfEnabled();
        }

        internal MainWindowViewModel ViewModel => _vm;

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            Log.InfoSink = null;
            Log.WarningSink = null;
            base.OnClosing(e);
        }

        /// <summary>
        /// The Options menu: decimal places and charge states as in the legacy menus, which stay open while
        /// choices are toggled.
        /// </summary>
        private void BuildOptionsMenu()
        {
            AddChoices(MzDecimalMenu, Enumerable.Range(1, 5), v => v.ToString(), v => _vm.MzDecimalPlace == v, v => _vm.MzDecimalPlace = v, "mz");
            AddChoices(IntensityDecimalMenu, Enumerable.Range(0, 6), v => v.ToString(), v => _vm.IntensityDecimalPlace == v, v => _vm.IntensityDecimalPlace = v, "intensity");
            AddChoices(ChargeStateMenu, Enumerable.Range(1, 10), v => v + "+", v => _vm.ChargeStates.Contains(v), v => _vm.ToggleChargeState(v), null);

            AddToggle(ExtractPrecursorByMzMenu, () => _vm.ExtractPrecursorByMz, v => _vm.ExtractPrecursorByMz = v);
            AddToggle(ShowPeakChargeStatesMenu, () => _vm.ShowPeakChargeStates, v => _vm.ShowPeakChargeStates = v);
            AddToggle(ShowPeakResolutionMenu, () => _vm.ShowPeakResolution, v => _vm.ShowPeakResolution = v);
            AddToggle(ExportChargeStateMenu, () => _vm.ExportChargeState, v => _vm.ExportChargeState = v);
            AddToggle(BypassThermoMenu, () => _vm.BypassThermoAlgorithm, v => _vm.BypassThermoAlgorithm = v);
            AddToggle(CheckUpdatesAtStartupMenu, () => _vm.CheckForUpdatesAtStartup, v =>
            {
                _vm.CheckForUpdatesAtStartup = v;
                _settings.CheckForUpdatesAtStartup = v;
                _settings.Save();
            });
            SyncMenus();
        }

        /// <param name="radioGroup">null for independent check boxes</param>
        private void AddChoices(MenuItem parent, IEnumerable<int> values, Func<int, string> header,
            Func<int, bool> isChecked, Action<int> select, string radioGroup)
        {
            List<MenuItem> items = new List<MenuItem>();
            foreach (int value in values)
            {
                MenuItem item = new MenuItem
                {
                    Header = header(value),
                    ToggleType = radioGroup == null ? MenuItemToggleType.CheckBox : MenuItemToggleType.Radio,
                    GroupName = radioGroup,
                    StaysOpenOnClick = true,
                };
                item.Click += (sender, e) =>
                {
                    select(value);
                    Dispatcher.UIThread.Post(SyncMenus);
                };
                items.Add(item);
                _menuSync.Add(() => item.IsChecked = isChecked(value));
            }
            parent.ItemsSource = items;
        }

        private void AddToggle(MenuItem item, Func<bool> get, Action<bool> set)
        {
            item.StaysOpenOnClick = true;
            item.Click += (sender, e) =>
            {
                set(!get());
                Dispatcher.UIThread.Post(SyncMenus);
            };
            _menuSync.Add(() => item.IsChecked = get());
        }

        private void SyncMenus()
        {
            foreach (Action sync in _menuSync)
            {
                sync();
            }
        }

        private async void OnAddFiles(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Files",
                AllowMultiple = true,
                FileTypeFilter = InputFileTypes,
            });
            await AddInputs(files);
        }

        private async void OnAddFolders(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Add timsTOF .d Folders",
                AllowMultiple = true,
            });
            List<string> paths = PathsOf(folders);
            List<string> notTimsTof = paths.Where(p => !ConversionRunner.IsTimsTofInput(p)).ToList();
            if (notTimsTof.Count > 0)
            {
                await ShowMessage("Not a timsTOF .d folder (it needs analysis.tdf and analysis.tdf_bin):\n"
                    + string.Join("\n", notTimsTof), "Warning");
            }
            await _vm.AddInputs(paths.Except(notTimsTof));
        }

        private void OnRemoveFile(object sender, RoutedEventArgs e)
        {
            _vm.RemoveSelectedInput();
        }

        private async void OnBrowse(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Destination Directory",
                AllowMultiple = false,
            });
            List<string> paths = PathsOf(folders);
            if (paths.Count > 0)
            {
                _vm.OutputDirectory = paths[0];
            }
        }

        private async void OnGo(object sender, RoutedEventArgs e)
        {
            await _vm.GoOrStop();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = _vm.IsIdle && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (_vm.IsIdle)
            {
                await AddInputs(e.DataTransfer.TryGetFiles());
            }
        }

        private async Task AddInputs(IEnumerable<IStorageItem> items)
        {
            await _vm.AddInputs(PathsOf(items));
        }

        private static List<string> PathsOf(IEnumerable<IStorageItem> items)
        {
            return (items ?? Enumerable.Empty<IStorageItem>()).Select(i => i.TryGetLocalPath()).Where(p => p != null).ToList();
        }

        private void OnExit(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            new AboutWindow().ShowDialog(this);
        }

        private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
        {
            await _vm.CheckForUpdates(true);
        }

        private async void OnOpenUpdatePage(object sender, RoutedEventArgs e)
        {
            await _vm.OpenUpdatePage();
        }

        private void OnDdaSingleSpectrum(object sender, RoutedEventArgs e)
        {
            new SingleSpectrumWindow(SingleSpectrumWindow.Mode.Dda).Show();
        }

        private void OnDiaSingleSpectrum(object sender, RoutedEventArgs e)
        {
            new SingleSpectrumWindow(SingleSpectrumWindow.Mode.Dia).Show();
        }

        public Task ShowMessage(string text, string title)
        {
            return MessageBoxWindow.Show(this, text, title, false);
        }

        public Task<bool> Confirm(string text, string title)
        {
            return MessageBoxWindow.Show(this, text, title, true);
        }

        public async Task OpenUrl(string url)
        {
            if (!await Launcher.LaunchUriAsync(new Uri(url)))
            {
                await ShowMessage("Could not open a web browser. The page is:\n" + url, "RawConverter");
            }
        }
    }
}
