using System;
using System.IO;
using System.Text.Json;

namespace RawConverter.Gui
{
    /// <summary>
    /// GUI preferences kept between sessions, in settings.json in the user's application-data folder
    /// (%APPDATA%\RawConverter on Windows, ~/.config/RawConverter on Linux).
    /// </summary>
    public sealed class GuiSettings
    {
        public bool CheckForUpdatesAtStartup { get; set; } = true;

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RawConverter", "settings.json");

        /// <summary>
        /// The saved settings, or the defaults if there are none or they cannot be read.
        /// </summary>
        public static GuiSettings Load(string path = null)
        {
            try
            {
                path ??= DefaultPath;
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(path)) ?? new GuiSettings();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
            }
            return new GuiSettings();
        }

        /// <summary>
        /// Save the settings; failures are ignored (the preference is then only kept for this session).
        /// </summary>
        public void Save(string path = null)
        {
            try
            {
                path ??= DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
