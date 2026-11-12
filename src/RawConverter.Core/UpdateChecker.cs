using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        public ReleaseInfo(string version, string url)
        {
            Version = version;
            Url = url;
        }

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
        public static async Task<ReleaseInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            ReleaseInfo latest = await GetLatestReleaseAsync(null, cancellationToken).ConfigureAwait(false);
            return latest != null && IsNewer(latest.Version, ConversionRunner.Version) ? latest : null;
        }

        /// <summary>
        /// The latest (non-pre-release) release, or null if none is published.
        /// </summary>
        /// <param name="handler">for tests; null uses the default network handler</param>
        public static async Task<ReleaseInfo> GetLatestReleaseAsync(HttpMessageHandler handler,
            CancellationToken cancellationToken = default)
        {
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, false);
            client.Timeout = Timeout;
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
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
            string url = root.TryGetProperty("html_url", out JsonElement u) ? u.GetString() : null;
            return new ReleaseInfo(NormalizeVersion(tag), string.IsNullOrEmpty(url) ? ReleasesPage : url);
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
