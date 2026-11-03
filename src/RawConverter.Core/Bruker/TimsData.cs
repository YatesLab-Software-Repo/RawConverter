using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RawConverter.Bruker
{
    /// <summary>
    /// Wrapper for Bruker's TDF SDK (timsdata.dll / libtimsdata.so), following Bruker's timsdata.py.
    /// DllImport("timsdata") resolves to timsdata.dll on Windows and libtimsdata.so on Linux.
    /// </summary>
    internal sealed class TimsData : IDisposable
    {
        private const string Lib = "timsdata";

        [DllImport(Lib)]
        private static extern ulong tims_open([MarshalAs(UnmanagedType.LPUTF8Str)] string analysisDirectory, uint useRecalibratedState);

        [DllImport(Lib)]
        private static extern void tims_close(ulong handle);

        [DllImport(Lib)]
        private static extern uint tims_get_last_error_string(byte[] buf, uint len);

        [DllImport(Lib)]
        private static extern void tims_set_num_threads(uint numThreads);

        [DllImport(Lib)]
        private static extern uint tims_read_scans_v2(ulong handle, long frameId, uint scanBegin, uint scanEnd, uint[] buf, uint len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MsMsSpectrumCallback(long precursorId, uint numPeaks, IntPtr mzValues, IntPtr areaValues);

        [DllImport(Lib)]
        private static extern uint tims_read_pasef_msms(ulong handle, long[] precursors, uint numPrecursors, MsMsSpectrumCallback callback);

        [DllImport(Lib)]
        private static extern uint tims_index_to_mz(ulong handle, long frameId, double[] input, double[] output, uint count);

        [DllImport(Lib)]
        private static extern uint tims_scannum_to_oneoverk0(ulong handle, long frameId, double[] input, double[] output, uint count);

        private ulong _handle;
        private uint _frameBufferSize = 128;

        public TimsData(string analysisDirectory, bool useRecalibratedState = false)
        {
            _handle = tims_open(analysisDirectory, useRecalibratedState ? 1u : 0u);
            if (_handle == 0)
            {
                ThrowLastError();
            }
            tims_set_num_threads(4);
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                tims_close(_handle);
                _handle = 0;
            }
        }

        public double[] IndexToMz(long frameId, double[] indices)
        {
            return Convert(frameId, indices, tims_index_to_mz);
        }

        public double[] ScanNumToOneOverK0(long frameId, double[] scanNums)
        {
            return Convert(frameId, scanNums, tims_scannum_to_oneoverk0);
        }

        /// <summary>
        /// Read the peaks of scans [scanBegin, scanEnd) of a frame; returns (TOF indices, intensities) per scan.
        /// </summary>
        public List<(uint[] Indices, uint[] Intensities)> ReadScans(long frameId, uint scanBegin, uint scanEnd)
        {
            uint[] buf;
            while (true)
            {
                buf = new uint[_frameBufferSize];
                uint len = 4 * _frameBufferSize;
                uint requiredLen = tims_read_scans_v2(_handle, frameId, scanBegin, scanEnd, buf, len);
                if (requiredLen == 0)
                {
                    ThrowLastError();
                }
                if (requiredLen <= len)
                {
                    break;
                }
                if (requiredLen > 16777216)
                {
                    throw new InvalidOperationException("Maximum expected frame size exceeded.");
                }
                _frameBufferSize = requiredLen / 4 + 1;
            }

            // layout: peak count per scan, then each scan's indices followed by its intensities;
            List<(uint[], uint[])> result = new List<(uint[], uint[])>((int)(scanEnd - scanBegin));
            int d = (int)(scanEnd - scanBegin);
            for (int i = 0; i < scanEnd - scanBegin; i++)
            {
                int numPeaks = (int)buf[i];
                uint[] indices = new uint[numPeaks];
                uint[] intensities = new uint[numPeaks];
                Array.Copy(buf, d, indices, 0, numPeaks);
                d += numPeaks;
                Array.Copy(buf, d, intensities, 0, numPeaks);
                d += numPeaks;
                result.Add((indices, intensities));
            }
            return result;
        }

        /// <summary>
        /// Peak-picked PASEF MS/MS spectra for the given precursor IDs, as m/z and area arrays.
        /// </summary>
        public Dictionary<long, (double[] Mz, float[] Area)> ReadPasefMsMs(long[] precursorIds)
        {
            Dictionary<long, (double[], float[])> result = new Dictionary<long, (double[], float[])>(precursorIds.Length);
            MsMsSpectrumCallback callback = (precursorId, numPeaks, mzValues, areaValues) =>
            {
                double[] mz = new double[numPeaks];
                float[] area = new float[numPeaks];
                if (numPeaks > 0)
                {
                    Marshal.Copy(mzValues, mz, 0, (int)numPeaks);
                    Marshal.Copy(areaValues, area, 0, (int)numPeaks);
                }
                result[precursorId] = (mz, area);
            };
            uint rc = tims_read_pasef_msms(_handle, precursorIds, (uint)precursorIds.Length, callback);
            GC.KeepAlive(callback);
            if (rc == 0)
            {
                ThrowLastError();
            }
            return result;
        }

        private delegate uint ConversionFunction(ulong handle, long frameId, double[] input, double[] output, uint count);

        private double[] Convert(long frameId, double[] input, ConversionFunction func)
        {
            double[] output = new double[input.Length];
            if (input.Length > 0 && func(_handle, frameId, input, output, (uint)input.Length) == 0)
            {
                ThrowLastError();
            }
            return output;
        }

        private static void ThrowLastError()
        {
            uint len = tims_get_last_error_string(null, 0);
            byte[] buf = new byte[len];
            tims_get_last_error_string(buf, len);
            throw new InvalidOperationException("timsdata: " + Encoding.UTF8.GetString(buf).TrimEnd('\0'));
        }
    }
}
