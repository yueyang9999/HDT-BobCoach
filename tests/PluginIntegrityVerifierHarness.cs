using System;
using System.IO;
using System.Security.Cryptography;
using BobCoach.Engine;

internal static class PluginIntegrityVerifierHarness
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "bobcoach-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dllPath = Path.Combine(root, "BobCoach.dll");
            File.WriteAllBytes(dllPath, new byte[] { 1, 2, 3, 4, 5 });
            var checksumPath = dllPath + ".sha256";

            if (!Rejects(dllPath, "checksum-missing"))
                return Fail("missing checksum sidecar was accepted");

            File.WriteAllText(checksumPath, "not-a-checksum\n");
            if (!Rejects(dllPath, "checksum-format-invalid"))
                return Fail("malformed checksum sidecar was accepted");

            File.WriteAllText(checksumPath, new string('0', 64) + "  BobCoach.dll\n");
            if (!Rejects(dllPath, "checksum-mismatch"))
                return Fail("mismatched checksum sidecar was accepted");

            var hash = GetSha256(dllPath);
            File.WriteAllText(checksumPath, hash.ToLowerInvariant() + "  BobCoach.dll\n");
            if (!Rejects(dllPath, "checksum-format-invalid"))
                return Fail("non-canonical lowercase checksum was accepted");

            File.WriteAllText(checksumPath, hash + "  Other.dll\n");
            if (!Rejects(dllPath, "checksum-format-invalid"))
                return Fail("checksum for another file was accepted");

            File.WriteAllText(checksumPath, hash + "  BobCoach.dll\n");
            var valid = PluginIntegrityVerifier.Verify(dllPath);
            if (!valid.IsValid || valid.Reason != "ok")
                return Fail("valid DLL/checksum pair was rejected: " + valid.Reason);

            Console.WriteLine("PASS runtime DLL/checksum sidecar integrity verification");
            return 0;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static bool Rejects(string dllPath, string reason)
    {
        var result = PluginIntegrityVerifier.Verify(dllPath);
        return !result.IsValid && result.Reason == reason;
    }

    private static string GetSha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha256 = SHA256.Create())
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
