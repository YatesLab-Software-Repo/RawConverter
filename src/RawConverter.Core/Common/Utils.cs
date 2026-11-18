using Microsoft.Win32;
using RawConverter.MassSpec;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RawConverter.Common
{
    public static class Utils
    {
        [DllImport("kernel32.dll", SetLastError = false)]
        static extern bool GetProductInfo(
             int dwOSMajorVersion,
             int dwOSMinorVersion,
             int dwSpMajorVersion,
             int dwSpMinorVersion,
             out int pdwReturnedProductType);

        public const double PROTON_MASS = 1.00727826;

        public const double MASS_DIFF_C12_C13 = 1.003354826;

        public static string CleanSequence(string sequence)
        {
            return Regex.Replace(sequence, @"[\+|\d|\-|\.|(|)]", "");
        }

        public static double MassFromMZ(double mz, int charge)
        {
            return mz * Math.Abs(charge) - charge * PROTON_MASS;
        }

        public static double MZFromMass(double mass, int charge)
        {
            return (mass + charge * PROTON_MASS) / Math.Abs(charge);
        }
/*
        /// <summary>
        /// Verify if MSFileReader (Thermo Program) is installed in pc, because the ParserRAW needs a msfilereader DLL
        /// </summary>
        /// <returns></returns>
        public static bool verifyProgramInstalled()
        {
            //Windows RegistryKey
            /*RegistryKey regKey = Registry.LocalMachine;
            regKey = regKey.OpenSubKey("SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
            if (regKey == null)
            {
                return false;
            }
            //Get key vector for each entry
            string[] keys = regKey.GetSubKeyNames();
            if (keys != null && keys.Length > 0)
            {
                //Interates key vector to try to get DisplayName
                for (int i = 0; i < keys.Length; i++)
                {
                    //Open current key
                    RegistryKey k = regKey.OpenSubKey(keys[i]);
                    try
                    {
                        //Get DisplayName
                        String appName = k.GetValue("DisplayName").ToString();

                        if (appName != null && appName.Length > 0 && appName.Contains("Thermo MSFileReader"))
                        {
                            return true;
                        }
                    }
                    catch (Exception) { }
                }
            }
            return false;
        }
*/
        #region MD5 Encoding
        public static string GetMd5Hash(MD5 md5Hash, string input)
        {

            // Convert the input string to a byte array and compute the hash.
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder.ToString();
        }

        // Verify a hash against a string.
        public static bool VerifyMd5Hash(MD5 md5Hash, string input, string hash)
        {
            // Hash the input.
            string hashOfInput = GetMd5Hash(md5Hash, input);

            // Create a StringComparer an compare the hashes.
            StringComparer comparer = StringComparer.OrdinalIgnoreCase;

            if (0 == comparer.Compare(hashOfInput, hash))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion
/*
        public static void checkForUpdates()
        {
            ApplicationDeployment updateCheck = ApplicationDeployment.CurrentDeployment;
            UpdateCheckInfo info;

            try
            {
                info = updateCheck.CheckForDetailedUpdate();

                if (info.UpdateAvailable)
                {
                    DialogResult dialogResult = MessageBox.Show("An update is available. Would you like to update the application now?", "Update available", MessageBoxButtons.OKCancel);

                    if (dialogResult == DialogResult.OK)
                    {
                        updateCheck.Update();
                        MessageBox.Show("The application has been upgraded, and will now restart.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Application.Restart();
                    }

                }
                else
                {
                    MessageBox.Show("No updates are available for the moment.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (DeploymentDownloadException dde)
            {
                MessageBox.Show(dde.Message + "\n" + dde.InnerException);
                return;
            }
            catch (InvalidDeploymentException ide)
            {
                MessageBox.Show(ide.Message + "\n" + ide.InnerException);
                return;
            }
            catch (InvalidOperationException ioe)
            {
                MessageBox.Show(ioe.Message + "\n" + ioe.InnerException);
                return;
            }
            catch (Exception e10)
            {
                MessageBox.Show(e10.Message);
                return;
            }
        }
*/
        public enum WindowsVersion
        {
            None = 0,
            Windows_1_01,
            Windows_2_03,
            Windows_2_10,
            Windows_2_11,
            Windows_3_0,
            Windows_for_Workgroups_3_1,
            Windows_for_Workgroups_3_11,
            Windows_3_2,
            Windows_NT_3_5,
            Windows_NT_3_51,
            Windows_95,
            Windows_NT_4_0,
            Windows_98,
            Windows_98_SE,
            Windows_2000,
            Windows_Me,
            Windows_XP,
            Windows_Server_2003,
            Windows_Vista,
            Windows_Home_Server,
            Windows_7,
            Windows_2008_R2,
            Windows_8,
        }

        public static string Is64Bits()
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return "64 bits";
            }
            else
            {
                return "32 bits";
            }
        }

/*
        public static string GetWindowsVersion()
        {
            string platform = System.Environment.OSVersion.Platform.ToString();
            if (platform.ToLower().Contains("nt"))
            {
                platform = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", null).ToString();
            }
            else
            {
                platform = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion", "ProductName", null).ToString();
            }
            return platform;
        }
        */

        /// <summary>
        /// Decompress a zlib (RFC 1950) buffer, as used for binary data arrays in mzML.
        /// </summary>
        public static byte[] ZlibDecompress(byte[] compressed)
        {
            using (var input = new MemoryStream(compressed))
            using (var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                zlib.CopyTo(output);
                return output.ToArray();
            }
        }
    }

    public class TaskProgress
    {
        private int _currentProgress;
        private bool _aborted;

        /// <summary>
        /// Raised with the new percentage whenever CurrentProgress changes.
        /// </summary>
        public event Action<int> ProgressChanged;

        public CancellationToken CancellationToken { get; set; }

        public int CurrentProgress
        {
            get { return _currentProgress; }
            set
            {
                if (_currentProgress != value)
                {
                    _currentProgress = value;
                    ProgressChanged?.Invoke(value);
                }
            }
        }

        public bool Aborted
        {
            get { return _aborted || CancellationToken.IsCancellationRequested; }
            set { _aborted = value; }
        }

        public TaskProgress()
        {
            CurrentProgress = 0;
            Aborted = false;
        }

        public void Reset() 
        {
            CurrentProgress = 0;
            Aborted = false;
        }
    }
}
