#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AppPofo.Presentation.Common
{
    // CNG CurrentUser persisted P-256 key, non-exportable private component.
    // Only the public blob and DER signature leave this class. No admin credentials.
    internal sealed class WindowsDeviceKey : IDisposable
    {
        private IntPtr provider;
        private IntPtr key;
        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptOpenStorageProvider(out IntPtr provider, string name, int flags);
        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptOpenKey(IntPtr provider, out IntPtr key, string name, int legacy, int flags);
        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptCreatePersistedKey(IntPtr provider, out IntPtr key, string algorithm, string name, int legacy, int flags);
        [DllImport("ncrypt.dll")] private static extern int NCryptFinalizeKey(IntPtr key, int flags);
        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptExportKey(IntPtr key, IntPtr exportKey, string blob, IntPtr parameters,
            byte[] output, int length, out int written, int flags);
        [DllImport("ncrypt.dll")]
        private static extern int NCryptSignHash(IntPtr key, IntPtr padding, byte[] hash, int length,
            byte[] signature, int signatureLength, out int written, int flags);
        [DllImport("ncrypt.dll")] private static extern int NCryptFreeObject(IntPtr handle);

        internal WindowsDeviceKey(string name, bool allowCreate)
        {
            try
            {
                Check(NCryptOpenStorageProvider(out provider, "Microsoft Software Key Storage Provider", 0));
                int opened = name == null ? unchecked((int)0x80090016) : NCryptOpenKey(provider, out key, name, 0, 0);
                if (opened == unchecked((int)0x80090016) && allowCreate)
                {
                    // Null name is an ephemeral key for synthetic contract tests.
                    Check(NCryptCreatePersistedKey(provider, out key, "ECDSA_P256", name, 0, 0));
                    Check(NCryptFinalizeKey(key, 0));
                }
                else Check(opened); // Never replace a missing remembered identity key.
            }
            catch { Dispose(); throw; }
        }
        private static void Check(int status)
        {
            if (status != 0) throw new CryptographicException("Windows device key operation failed.");
        }
        internal byte[] PublicCoordinates()
        {
            var blob = new byte[72];
            Check(NCryptExportKey(key, IntPtr.Zero, "ECCPUBLICBLOB", IntPtr.Zero, blob, blob.Length, out int size, 0));
            if (size != 72 || BitConverter.ToUInt32(blob, 0) != 0x31534345 || BitConverter.ToUInt32(blob, 4) != 32)
                throw new CryptographicException("Unexpected public key format.");
            var coordinates = new byte[64];
            Buffer.BlockCopy(blob, 8, coordinates, 0, 64);
            return coordinates;
        }
        internal string Sign(string message)
        {
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(message));
            try
            {
                var raw = new byte[64];
                Check(NCryptSignHash(key, IntPtr.Zero, hash, hash.Length, raw, raw.Length, out int size, 0));
                if (size != 64) throw new CryptographicException("Unexpected signature format.");
                return Base64Url(ToDer(raw));
            }
            finally { Array.Clear(hash, 0, hash.Length); }
        }
        internal static string Base64Url(byte[] value) => Convert.ToBase64String(value)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        internal static byte[] ToDer(byte[] raw)
        {
            if (raw == null || raw.Length != 64) throw new ArgumentException("P-256 signature required.");
            var body = new List<byte>();
            for (int part = 0; part < 2; part++)
            {
                int start = part * 32, end = start + 32;
                while (start < end - 1 && raw[start] == 0) start++;
                bool pad = (raw[start] & 128) != 0;
                body.Add(2); body.Add((byte)(end - start + (pad ? 1 : 0)));
                if (pad) body.Add(0);
                for (int i = start; i < end; i++) body.Add(raw[i]);
            }
            var der = new List<byte> { 0x30, (byte)body.Count };
            der.AddRange(body); return der.ToArray();
        }
        public void Dispose()
        {
            if (key != IntPtr.Zero) { NCryptFreeObject(key); key = IntPtr.Zero; }
            if (provider != IntPtr.Zero) { NCryptFreeObject(provider); provider = IntPtr.Zero; }
        }
    }
}
#endif
