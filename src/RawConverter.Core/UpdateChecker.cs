using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RawConverter
{
    /// <summary>
    /// A published RawConverter release.
    /// </summary>
    public sealed class ReleaseInfo
    {
        public ReleaseInfo(string version, string url, string installerUrl = null, string checksumsUrl = null)
        {
            Version = version;
            Url = url;
            InstallerUrl = installerUrl;
            ChecksumsUrl = checksumsUrl;
        }

        /// <summary>The Windows installer (RawConverter-*-win-x64-setup.exe), or null if the release has none.</summary>
        public string InstallerUrl { get; }

        /// <summary>The release's SHA256SUMS.txt, used to verify a downloaded installer.</summary>
        public string ChecksumsUrl { get; }

        public bool IsNightly => UpdateChecker.IsNightlyVersion(Version);

        /// <summary>The release version, without a leading "v" ("1.2.2").</summary>
        public string Version { get; }

        /// <summary>The release page on GitHub.</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Looks up the latest RawConverter release on GitHub. Only the request itself is sent (no data about the user's
    /// files); pre-releases are ignored.
    /// </summary>
    public static class UpdateChecker
    {
        public const string LatestReleaseApi =
            "https://api.github.com/repos/YatesLab-Software-Repo/RawConverter/releases/latest";

        public const string ReleasesPage = "https://github.com/YatesLab-Software-Repo/RawConverter/releases/latest";

        /// <summary>
        /// The rolling nightly pre-release (nightly.yml). Its tag is always "nightly"; the version is in its title.
        /// </summary>
        public const string NightlyReleaseApi =
            "https://api.github.com/repos/YatesLab-Software-Repo/RawConverter/releases/tags/nightly";

        /// <summary>
        /// Whether this is a nightly build ("1.3.0-nightly.20261112.42"); nightly builds also look for newer nightlies.
        /// </summary>
        public static bool IsNightlyBuild => IsNightlyVersion(ConversionRunner.Version);

        public static bool IsNightlyVersion(string version)
        {
            return version != null && version.Contains("-nightly", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether this copy can install updates itself: on Windows, when it was installed with the installer (rather
        /// than unpacked from the portable zip).
        /// </summary>
        public static bool CanInstallUpdates =>
            OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        /// <summary>
        /// Setting this environment variable to anything but "" or "0" turns off the GUI's automatic check at startup
        /// (for example for all users of a managed computer). An explicit check still works.
        /// </summary>
        public const string DisableVariable = "RAWCONVERTER_NO_UPDATE_CHECK";

        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public static bool AutomaticCheckDisabled
        {
            get
            {
                string value = Environment.GetEnvironmentVariable(DisableVariable);
                return !string.IsNullOrEmpty(value) && value != "0";
            }
        }

        /// <summary>
        /// The latest release if it is newer than this version of RawConverter, otherwise null. Throws on network or
        /// GitHub errors (HttpRequestException, TaskCanceledException on timeout).
        /// </summary>
        public static Task<ReleaseInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            return CheckForUpdateAsync(ConversionRunner.Version, null, cancellationToken);
        }

        /// <summary>
        /// The newest release that is newer than currentVersion, or null: the latest release, and for a nightly
        /// build also the latest nightly (so nightly users get the next nightly, or a newer release).
        /// </summary>
        /// <param name="handler">for tests; null uses the default network handler</param>
        public static async Task<ReleaseInfo> CheckForUpdateAsync(string currentVersion, HttpMessageHandler handler,
            CancellationToken cancellationToken = default)
        {
            ReleaseInfo best = null;
            List<string> apis = new List<string> { LatestReleaseApi };
            if (IsNightlyVersion(currentVersion))
            {
                apis.Add(NightlyReleaseApi);
            }
            foreach (string api in apis)
            {
                ReleaseInfo release = await GetReleaseAsync(api, handler, cancellationToken).ConfigureAwait(false);
                if (release != null && IsNewer(release.Version, currentVersion)
                    && (best == null || IsNewer(release.Version, best.Version)))
                {
                    best = release;
                }
            }
            return best;
        }

        /// <summary>
        /// The latest (non-pre-release) release, or null if none is published.
        /// </summary>
        /// <param name="handler">for tests; null uses the default network handler</param>
        public static Task<ReleaseInfo> GetLatestReleaseAsync(HttpMessageHandler handler,
            CancellationToken cancellationToken = default)
        {
            return GetReleaseAsync(LatestReleaseApi, handler, cancellationToken);
        }

        /// <summary>
        /// A release from GitHub's releases API, or null if there is none (404).
        /// </summary>
        public static async Task<ReleaseInfo> GetReleaseAsync(string api, HttpMessageHandler handler,
            CancellationToken cancellationToken = default)
        {
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, false);
            client.Timeout = Timeout;
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, api);
            // GitHub rejects requests without a User-Agent;
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RawConverter", ConversionRunner.Version ?? "0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseRelease(json);
        }

        /// <summary>
        /// The version and page of a release from GitHub's releases API.
        /// </summary>
        public static ReleaseInfo ParseRelease(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }
            string version = NormalizeVersion(tag);
            if (!TryParse(version, out _, out _))
            {
                // the nightly release: tag "nightly", title "RawConverter 1.3.0-nightly.20261112.42";
                string name = root.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                version = name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (!TryParse(version, out _, out _))
                {
                    return null;
                }
                version = NormalizeVersion(version);
            }
            string url = root.TryGetProperty("html_url", out JsonElement u) ? u.GetString() : null;

            string installer = null, checksums = null;
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out JsonElement an) ? an.GetString() ?? "" : "";
                    string download = asset.TryGetProperty("browser_download_url", out JsonElement d) ? d.GetString() : null;
                    if (assetName.EndsWith("-win-x64-setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installer = download;
                    }
                    else if (assetName.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        checksums = download;
                    }
                }
            }
            return new ReleaseInfo(version, string.IsNullOrEmpty(url) ? ReleasesPage : url, installer, checksums);
        }

        /// <summary>
        /// Download the release's Windows installer into folder and check it against the release's SHA256SUMS.txt;
        /// returns its path. Throws InvalidDataException if the release has no installer or checksum, or the
        /// checksum does not match (the file is then deleted).
        /// </summary>
        /// <param name="handler">for tests; null uses the default network handler</param>
        public static async Task<string> DownloadInstallerAsync(ReleaseInfo release, string folder, HttpMessageHandler handler,
            CancellationToken cancellationToken = default)
        {
            if (release.InstallerUrl == null || release.ChecksumsUrl == null)
            {
                throw new InvalidDataException("RawConverter " + release.Version + " has no Windows installer with a checksum.");
            }
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, false);
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RawConverter", ConversionRunner.Version ?? "0"));

            string fileName = Path.GetFileName(new Uri(release.InstallerUrl).LocalPath);
            string checksums = await client.GetStringAsync(release.ChecksumsUrl, cancellationToken).ConfigureAwait(false);
            string expected = ExpectedChecksum(checksums, fileName)
                ?? throw new InvalidDataException("SHA256SUMS.txt has no checksum for " + fileName);

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            using (HttpResponseMessage response = await client.GetAsync(release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using FileStream file = File.Create(path);
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            string actual;
            using (FileStream file = File.OpenRead(path))
            {
                actual = Convert.ToHexString(SHA256.HashData(file));
            }
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException("The downloaded " + fileName + " does not match its checksum.");
            }
            return path;
        }

        /// <summary>
        /// The SHA-256 of fileName in a sha256sum listing ("hex  name" or "hex *name"), or null.
        /// </summary>
        public static string ExpectedChecksum(string sha256Sums, string fileName)
        {
            foreach (string line in sha256Sums.Split('\n'))
            {
                string[] parts = line.Trim().Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].TrimStart('*') == fileName && parts[0].Length == 64)
                {
                    return parts[0];
                }
            }
            return null;
        }

        /// <summary>
        /// Run a downloaded installer to update this installation without questions (Windows asks for administrator
        /// rights); it reopens RawConverter when done (/RELAUNCH=1, see RawConverter.iss). The caller should then exit.
        /// </summary>
        public static void StartInstaller(string installerPath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath,
                "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1")
            {
                UseShellExecute = true,   // lets Windows show the administrator prompt;
            });
        }

        /// <summary>
        /// Whether version a is newer than version b. Versions are "major.minor.patch" with an optional pre-release
        /// suffix ("1.3.0-beta1"), which sorts before the release itself; a leading "v" and a "+build" suffix are
        /// ignored. A version that cannot be parsed is never newer.
        /// </summary>
        public static bool IsNewer(string a, string b)
        {
            if (!TryParse(a, out Version aCore, out string aPre))
            {
                return false;
            }
            if (!TryParse(b, out Version bCore, out string bPre))
            {
                return true;
            }
            int c = aCore.CompareTo(bCore);
            if (c != 0)
            {
                return c > 0;
            }
            if (aPre == null || bPre == null)
            {
                return aPre == null && bPre != null;
            }
            return ComparePreRelease(aPre, bPre) > 0;
        }

        private static string NormalizeVersion(string version)
        {
            version = version.Trim();
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
        }

        private static bool TryParse(string version, out Version core, out string preRelease)
        {
            core = null;
            preRelease = null;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }
            version = NormalizeVersion(version).Split('+')[0];
            int dash = version.IndexOf('-');
            if (dash >= 0)
            {
                preRelease = version.Substring(dash + 1);
                version = version.Substring(0, dash);
            }
            string[] parts = version.Split('.');
            if (parts.Length < 1 || parts.Length > 4)
            {
                return false;
            }
            int[] numbers = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return false;
                }
            }
            core = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        /// <summary>
        /// Semantic-versioning order of pre-release identifiers: dot-separated, numeric ones compared as numbers.
        /// </summary>
        private static int ComparePreRelease(string a, string b)
        {
            string[] x = a.Split('.');
            string[] y = b.Split('.');
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                bool xNum = int.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out int xn);
                bool yNum = int.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out int yn);
                int c = xNum && yNum ? xn.CompareTo(yn)
                    : xNum ? -1
                    : yNum ? 1
                    : string.CompareOrdinal(x[i], y[i]);
                if (c != 0)
                {
                    return c;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
