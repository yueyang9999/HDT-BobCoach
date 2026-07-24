using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    public sealed class PluginIntegrityResult
    {
        public bool IsValid { get; private set; }
        public string Reason { get; private set; }

        private PluginIntegrityResult(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static PluginIntegrityResult Valid()
        {
            return new PluginIntegrityResult(true, "ok");
        }

        public static PluginIntegrityResult Invalid(string reason)
        {
            return new PluginIntegrityResult(false, reason);
        }
    }

    public static class PluginIntegrityVerifier
    {
        private static readonly Regex ChecksumPattern = new Regex(
            "^([A-F0-9]{64})  BobCoach\\.dll(?:\\r?\\n)?$",
            RegexOptions.CultureInvariant);

        public static PluginIntegrityResult Verify(string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
                return PluginIntegrityResult.Invalid("dll-missing");

            var checksumPath = dllPath + ".sha256";
            if (!File.Exists(checksumPath))
                return PluginIntegrityResult.Invalid("checksum-missing");

            try
            {
                var checksumText = File.ReadAllText(checksumPath);
                var match = ChecksumPattern.Match(checksumText);
                if (!match.Success)
                    return PluginIntegrityResult.Invalid("checksum-format-invalid");

                string actualHash;
                using (var stream = File.OpenRead(dllPath))
                using (var sha256 = SHA256.Create())
                    actualHash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");

                return string.Equals(match.Groups[1].Value, actualHash, StringComparison.Ordinal)
                    ? PluginIntegrityResult.Valid()
                    : PluginIntegrityResult.Invalid("checksum-mismatch");
            }
            catch (IOException)
            {
                return PluginIntegrityResult.Invalid("integrity-read-failed");
            }
            catch (UnauthorizedAccessException)
            {
                return PluginIntegrityResult.Invalid("integrity-read-failed");
            }
            catch (CryptographicException)
            {
                return PluginIntegrityResult.Invalid("integrity-hash-failed");
            }
        }
    }
}
