#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    // Stores the administrator's existing long random bearer token with Windows
    // DPAPI CurrentUser protection, never in .env, PlayerPrefs or Unity assets.
    // Server authorization is still required on every API call.
    internal static class DesktopCloudAdminToken
    {
        private const string FileName = "cloud-admin-dpapi-v1.dat";
        private static bool loaded;
        private static string activeToken;
        private static bool pendingValidation;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob input, string description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        private static string StoragePath =>
            Path.Combine(UnityEngine.Application.persistentDataPath,
                CloudServerConfiguration.BaseUrl == CloudServerConfiguration.ProductionUrl
                    ? FileName : "cloud-admin-" + CloudServerConfiguration.Scope + "-dpapi-v1.dat");

        public static bool HasToken => TryReadToken(out _);

        public static bool TryReadToken(out string token)
        {
            if (!loaded)
            {
                loaded = true;
                try
                {
                    if (File.Exists(StoragePath))
                    {
                        string decoded = Encoding.UTF8.GetString(
                            Unprotect(File.ReadAllBytes(StoragePath)));
                        if (IsValidFormat(decoded)) activeToken = decoded;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AppPofo Cloud] Saved administrator login could not be loaded: " +
                        ex.GetType().Name);
                }
            }
            token = activeToken;
            return IsValidFormat(token);
        }

        public static bool IsValidFormat(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 32 || token.Length > 4096)
                return false;
            foreach (char c in token)
                if (char.IsWhiteSpace(c)) return false;
            return true;
        }

        // In-memory only until the Worker confirms the code is valid.
        public static bool Propose(string token)
        {
            if (!IsValidFormat(token)) return false;
            loaded = true;
            activeToken = token;
            pendingValidation = true;
            return true;
        }

        public static bool RememberValidated()
        {
            if (!pendingValidation) return true;
            pendingValidation = false;
            if (!TryReadToken(out string token)) return false;
            byte[] protectedBytes;
            try
            {
                protectedBytes = Protect(Encoding.UTF8.GetBytes(token));
                Directory.CreateDirectory(Path.GetDirectoryName(StoragePath));
                string temporary = StoragePath + ".new";
                File.WriteAllBytes(temporary, protectedBytes);
                // Atomic replacement on the same volume when a prior credential exists.
                if (File.Exists(StoragePath))
                    File.Replace(temporary, StoragePath, null);
                else File.Move(temporary, StoragePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppPofo Cloud] Administrator code is valid but secure save failed: " +
                    ex.GetType().Name);
                return false;
            }
        }

        // Call only after the Worker positively rejects the credential (401).
        public static void Forget()
        {
            activeToken = null;
            pendingValidation = false;
            loaded = true;
            try
            {
                if (File.Exists(StoragePath)) File.Delete(StoragePath);
                if (File.Exists(StoragePath + ".new")) File.Delete(StoragePath + ".new");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppPofo Cloud] Administrator logout could not delete protected data: " +
                    ex.GetType().Name);
            }
        }

        private static byte[] Protect(byte[] data)
        {
            return Transform(data, true);
        }

        private static byte[] Unprotect(byte[] data)
        {
            return Transform(data, false);
        }

        private static byte[] Transform(byte[] data, bool encrypt)
        {
            if (data == null || data.Length == 0 || data.Length > 16384)
                throw new ArgumentException("Credential data is invalid.", nameof(data));
            var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
            DataBlob output = default;
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                const uint NoUi = 0x1;
                bool ok = encrypt
                    ? CryptProtectData(ref input, "AppPofo administrator credential",
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out output);
                if (!ok) throw new InvalidOperationException(
                    "Windows DPAPI operation failed: " + Marshal.GetLastWin32Error());
                if (output.Size < 1 || output.Size > 16384)
                    throw new InvalidOperationException("Windows credential has invalid size.");
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                if (input.Data != IntPtr.Zero)
                {
                    for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
                    Marshal.FreeHGlobal(input.Data);
                }
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMemory()
        {
            loaded = false;
            activeToken = null;
            pendingValidation = false;
        }
    }
}
#endif
