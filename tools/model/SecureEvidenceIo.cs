using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BobCoach.ModelTools
{
    public sealed class SecureEvidenceDirectory : IDisposable
    {
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileTraverse = 0x00000020;
        private const uint DeleteAccess = 0x00010000;
        private const uint OpenExisting = 3;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileAttributeReparsePoint = 0x00000400;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint SynchronizeAccess = 0x00100000;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileCreate = 2;
        private const uint FileOpen = 1;
        private const uint FileOpenIf = 3;
        private const uint FileDirectoryFile = 0x00000001;
        private const uint FileWriteThrough = 0x00000002;
        private const uint FileSynchronousIoNonAlert = 0x00000020;
        private const uint FileNonDirectoryFile = 0x00000040;
        private const uint ObjectCaseInsensitive = 0x00000040;
        private const int FileRenameInformation = 10;
        private const int FileDispositionInformation = 13;

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            public IntPtr Status;
            public UIntPtr Information;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file,
            out ByHandleFileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,
            StringBuilder path, uint pathLength, uint flags);

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(out SafeFileHandle fileHandle,
            uint desiredAccess, ref ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock, IntPtr allocationSize, uint fileAttributes,
            uint shareAccess, uint createDisposition, uint createOptions,
            IntPtr eaBuffer, uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(SafeFileHandle fileHandle,
            out IoStatusBlock ioStatusBlock, IntPtr fileInformation,
            uint length, int fileInformationClass);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        private readonly IList<SafeFileHandle> _directoryHandles;
        private bool _disposed;

        private SecureEvidenceDirectory(IList<SafeFileHandle> directoryHandles)
        {
            _directoryHandles = directoryHandles;
        }

        public static SecureEvidenceDirectory Acquire(string trustedRoot, string outputDirectory)
        {
            var root = Normalize(trustedRoot);
            var output = Normalize(outputDirectory);
            if (!PathContained(root, output))
                throw new InvalidDataException("output directory escapes trusted root");

            var handles = new List<SafeFileHandle>();
            try
            {
                var rootHandle = OpenDirectory(root, "trusted root");
                handles.Add(rootHandle);
                var physicalRoot = FinalPath(rootHandle, "trusted root");
                var relative = output.Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar);
                foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var handle = OpenOrCreateRelativeDirectory(
                        handles[handles.Count - 1], segment);
                    handles.Add(handle);
                    var physicalSegment = FinalPath(handle, "output directory segment");
                    if (!PathContained(physicalRoot, physicalSegment))
                        throw new InvalidDataException("output directory escapes trusted root");
                }
                return new SecureEvidenceDirectory(handles);
            }
            catch
            {
                for (var index = handles.Count - 1; index >= 0; index--)
                    handles[index].Dispose();
                throw;
            }
        }

        public void WriteAllText(string fileName, string content)
        {
            if (_disposed) throw new ObjectDisposedException("SecureEvidenceDirectory");
            if (string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                throw new ArgumentException("evidence file name must be a direct child", "fileName");

            var outputHandle = _directoryHandles[_directoryHandles.Count - 1];
            var stagingName = ".bobcoach-staging-" + Guid.NewGuid().ToString("N");
            using (var stagingHandle = CreateRelativeFile(outputHandle, stagingName))
            {
                var published = false;
                try
                {
                    var handleReferenceAdded = false;
                    try
                    {
                        stagingHandle.DangerousAddRef(ref handleReferenceAdded);
                        using (var streamHandle = new SafeFileHandle(
                            stagingHandle.DangerousGetHandle(), false))
                        using (var stream = new FileStream(streamHandle, FileAccess.Write,
                            4096, false))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            writer.Write(content);
                            writer.Flush();
                            stream.Flush(true);
                        }
                    }
                    finally
                    {
                        if (handleReferenceAdded) stagingHandle.DangerousRelease();
                    }

                    RenameRelative(stagingHandle, outputHandle, fileName);
                    published = true;
                }
                finally
                {
                    if (!published) TryMarkForDeletion(stagingHandle);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _directoryHandles.Count - 1; index >= 0; index--)
                _directoryHandles[index].Dispose();
        }

        internal static SafeFileHandle OpenDirectory(string path, string name)
        {
            var handle = CreateFile(path, FileReadAttributes | FileTraverse,
                FileShareRead | FileShareWrite, IntPtr.Zero,
                OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new IOException("cannot lock " + name + ": "
                    + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            ByHandleFileInformation information;
            if (!GetFileInformationByHandle(handle, out information))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException("cannot inspect " + name + ": "
                    + new System.ComponentModel.Win32Exception(error).Message);
            }
            if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
            {
                handle.Dispose();
                throw new InvalidDataException(name + " is a reparse point");
            }
            return handle;
        }

        private static SafeFileHandle OpenOrCreateRelativeDirectory(
            SafeFileHandle parent, string segment)
        {
            return OpenRelativeDirectory(parent, segment, FileOpenIf,
                "evidence output directory");
        }

        internal static SafeFileHandle OpenExistingRelativeDirectory(
            SafeFileHandle parent, string segment, string name)
        {
            return OpenRelativeDirectory(parent, segment, FileOpen, name);
        }

        private static SafeFileHandle OpenRelativeDirectory(
            SafeFileHandle parent, string segment, uint createDisposition, string displayName)
        {
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr nameStructure = IntPtr.Zero;
            var parentReferenceAdded = false;
            try
            {
                nameBuffer = Marshal.StringToHGlobalUni(segment);
                var nameBytes = checked(segment.Length * 2);
                if (nameBytes > ushort.MaxValue - 2)
                    throw new PathTooLongException("evidence directory segment is too long");
                var name = new UnicodeString {
                    Length = (ushort)nameBytes,
                    MaximumLength = (ushort)(nameBytes + 2),
                    Buffer = nameBuffer
                };
                nameStructure = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
                Marshal.StructureToPtr(name, nameStructure, false);
                parent.DangerousAddRef(ref parentReferenceAdded);
                var attributes = new ObjectAttributes {
                    Length = Marshal.SizeOf(typeof(ObjectAttributes)),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = nameStructure,
                    Attributes = ObjectCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };
                IoStatusBlock ioStatus;
                SafeFileHandle directoryHandle;
                var status = NtCreateFile(out directoryHandle,
                    FileReadAttributes | FileTraverse | SynchronizeAccess,
                    ref attributes, out ioStatus, IntPtr.Zero, FileAttributeNormal,
                    FileShareRead | FileShareWrite, createDisposition,
                    FileDirectoryFile | FileFlagOpenReparsePoint
                        | FileSynchronousIoNonAlert,
                    IntPtr.Zero, 0);
                if (status < 0)
                {
                    if (directoryHandle != null) directoryHandle.Dispose();
                    ThrowNtStatus(status, "cannot open " + displayName);
                }
                ByHandleFileInformation information;
                if (!GetFileInformationByHandle(directoryHandle, out information))
                {
                    var error = Marshal.GetLastWin32Error();
                    directoryHandle.Dispose();
                    throw new IOException("cannot inspect " + displayName + ": "
                        + new System.ComponentModel.Win32Exception(error).Message);
                }
                if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
                {
                    directoryHandle.Dispose();
                    throw new InvalidDataException(
                        displayName + " is a reparse point");
                }
                return directoryHandle;
            }
            finally
            {
                if (parentReferenceAdded) parent.DangerousRelease();
                if (nameStructure != IntPtr.Zero) Marshal.FreeHGlobal(nameStructure);
                if (nameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(nameBuffer);
            }
        }

        internal static SafeFileHandle OpenExistingRelativeFile(
            SafeFileHandle directory, string fileName, string name)
        {
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr nameStructure = IntPtr.Zero;
            var directoryReferenceAdded = false;
            try
            {
                nameBuffer = Marshal.StringToHGlobalUni(fileName);
                var nameBytes = checked(fileName.Length * 2);
                if (nameBytes > ushort.MaxValue - 2)
                    throw new PathTooLongException("input file name is too long");
                var relativeName = new UnicodeString {
                    Length = (ushort)nameBytes,
                    MaximumLength = (ushort)(nameBytes + 2),
                    Buffer = nameBuffer
                };
                nameStructure = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
                Marshal.StructureToPtr(relativeName, nameStructure, false);
                directory.DangerousAddRef(ref directoryReferenceAdded);
                var attributes = new ObjectAttributes {
                    Length = Marshal.SizeOf(typeof(ObjectAttributes)),
                    RootDirectory = directory.DangerousGetHandle(),
                    ObjectName = nameStructure,
                    Attributes = ObjectCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };
                IoStatusBlock ioStatus;
                SafeFileHandle fileHandle;
                var status = NtCreateFile(out fileHandle,
                    GenericRead | FileReadAttributes | SynchronizeAccess,
                    ref attributes, out ioStatus, IntPtr.Zero, FileAttributeNormal,
                    FileShareRead, FileOpen,
                    FileFlagOpenReparsePoint | FileSynchronousIoNonAlert
                        | FileNonDirectoryFile,
                    IntPtr.Zero, 0);
                if (status < 0)
                {
                    if (fileHandle != null) fileHandle.Dispose();
                    ThrowNtStatus(status, "cannot open " + name);
                }
                ByHandleFileInformation information;
                if (!GetFileInformationByHandle(fileHandle, out information))
                {
                    var error = Marshal.GetLastWin32Error();
                    fileHandle.Dispose();
                    throw new IOException("cannot inspect " + name + ": "
                        + new System.ComponentModel.Win32Exception(error).Message);
                }
                if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
                {
                    fileHandle.Dispose();
                    throw new InvalidDataException(name + " is a reparse point");
                }
                if ((information.FileAttributes & FileAttributeDirectory) != 0)
                {
                    fileHandle.Dispose();
                    throw new InvalidDataException(name + " is not a file");
                }
                return fileHandle;
            }
            finally
            {
                if (directoryReferenceAdded) directory.DangerousRelease();
                if (nameStructure != IntPtr.Zero) Marshal.FreeHGlobal(nameStructure);
                if (nameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(nameBuffer);
            }
        }

        private static SafeFileHandle CreateRelativeFile(SafeFileHandle directory,
            string fileName)
        {
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr nameStructure = IntPtr.Zero;
            var directoryReferenceAdded = false;
            try
            {
                nameBuffer = Marshal.StringToHGlobalUni(fileName);
                var nameBytes = checked(fileName.Length * 2);
                if (nameBytes > ushort.MaxValue - 2)
                    throw new PathTooLongException("evidence file name is too long");
                var name = new UnicodeString {
                    Length = (ushort)nameBytes,
                    MaximumLength = (ushort)(nameBytes + 2),
                    Buffer = nameBuffer
                };
                nameStructure = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
                Marshal.StructureToPtr(name, nameStructure, false);
                directory.DangerousAddRef(ref directoryReferenceAdded);
                var attributes = new ObjectAttributes {
                    Length = Marshal.SizeOf(typeof(ObjectAttributes)),
                    RootDirectory = directory.DangerousGetHandle(),
                    ObjectName = nameStructure,
                    Attributes = ObjectCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };
                IoStatusBlock ioStatus;
                SafeFileHandle fileHandle;
                var status = NtCreateFile(out fileHandle,
                    GenericWrite | DeleteAccess | SynchronizeAccess,
                    ref attributes, out ioStatus, IntPtr.Zero, FileAttributeNormal,
                    FileShareRead, FileCreate,
                    FileWriteThrough | FileSynchronousIoNonAlert | FileNonDirectoryFile,
                    IntPtr.Zero, 0);
                if (status < 0)
                {
                    if (fileHandle != null) fileHandle.Dispose();
                    ThrowNtStatus(status, "cannot create evidence staging file");
                }
                return fileHandle;
            }
            finally
            {
                if (directoryReferenceAdded) directory.DangerousRelease();
                if (nameStructure != IntPtr.Zero) Marshal.FreeHGlobal(nameStructure);
                if (nameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(nameBuffer);
            }
        }

        private static void RenameRelative(SafeFileHandle file, SafeFileHandle directory,
            string fileName)
        {
            var fileNameBytes = Encoding.Unicode.GetBytes(fileName);
            var rootOffset = IntPtr.Size == 8 ? 8 : 4;
            var lengthOffset = rootOffset + IntPtr.Size;
            var nameOffset = lengthOffset + sizeof(uint);
            var bufferLength = checked(nameOffset + fileNameBytes.Length);
            var buffer = Marshal.AllocHGlobal(bufferLength);
            var directoryReferenceAdded = false;
            try
            {
                Marshal.Copy(new byte[bufferLength], 0, buffer, bufferLength);
                directory.DangerousAddRef(ref directoryReferenceAdded);
                Marshal.WriteByte(buffer, 0, 0);
                Marshal.WriteIntPtr(buffer, rootOffset, directory.DangerousGetHandle());
                Marshal.WriteInt32(buffer, lengthOffset, fileNameBytes.Length);
                Marshal.Copy(fileNameBytes, 0, IntPtr.Add(buffer, nameOffset),
                    fileNameBytes.Length);
                IoStatusBlock ioStatus;
                var status = NtSetInformationFile(file, out ioStatus, buffer,
                    (uint)bufferLength, FileRenameInformation);
                if (status < 0) ThrowNtStatus(status, "cannot publish evidence file");
            }
            finally
            {
                if (directoryReferenceAdded) directory.DangerousRelease();
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void TryMarkForDeletion(SafeFileHandle file)
        {
            var buffer = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(buffer, 1);
                IoStatusBlock ioStatus;
                NtSetInformationFile(file, out ioStatus, buffer, 1,
                    FileDispositionInformation);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void ThrowNtStatus(int status, string action)
        {
            var error = unchecked((int)RtlNtStatusToDosError(status));
            throw new IOException(action + ": "
                + new System.ComponentModel.Win32Exception(error).Message);
        }

        internal static string FinalPath(SafeFileHandle handle, string name)
        {
            var buffer = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
                throw new IOException("cannot resolve locked " + name + ": "
                    + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            var value = buffer.ToString();
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                value = @"\\" + value.Substring(8);
            else if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(4);
            return Normalize(value);
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("path is required");
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                ? root
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static bool PathContained(string parent, string candidate)
        {
            var prefix = parent.EndsWith(Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal)
                || parent.EndsWith(Path.AltDirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal)
                ? parent
                : parent + Path.DirectorySeparatorChar;
            return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class SecureInputSnapshot : IDisposable
    {
        private readonly IList<SafeFileHandle> _handles;
        private readonly IDictionary<string, byte[]> _bytes;
        private bool _disposed;

        private SecureInputSnapshot(IList<SafeFileHandle> handles,
            IDictionary<string, byte[]> bytes)
        {
            _handles = handles;
            _bytes = bytes;
        }

        public static SecureInputSnapshot Capture(string trustedRoot,
            IDictionary<string, string> inputs)
        {
            if (inputs == null || inputs.Count == 0)
                throw new ArgumentException("at least one input is required", "inputs");
            var root = SecureEvidenceDirectory.Normalize(trustedRoot);
            var handles = new List<SafeFileHandle>();
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                var rootHandle = SecureEvidenceDirectory.OpenDirectory(root, "trusted input root");
                handles.Add(rootHandle);
                var physicalRoot = SecureEvidenceDirectory.FinalPath(rootHandle,
                    "trusted input root");
                foreach (var input in inputs)
                {
                    if (string.IsNullOrWhiteSpace(input.Key))
                        throw new ArgumentException("input name is required", "inputs");
                    var path = SecureEvidenceDirectory.Normalize(input.Value);
                    if (!SecureEvidenceDirectory.PathContained(root, path)
                        || path.Equals(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(input.Key + " escapes trusted input root");
                    var segments = path.Substring(root.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                            StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0)
                        throw new InvalidDataException(input.Key + " input path is invalid");

                    var parent = rootHandle;
                    for (var index = 0; index < segments.Length - 1; index++)
                    {
                        var directory = SecureEvidenceDirectory.OpenExistingRelativeDirectory(
                            parent, segments[index], input.Key + " input directory");
                        handles.Add(directory);
                        var physicalDirectory = SecureEvidenceDirectory.FinalPath(directory,
                            input.Key + " input directory");
                        if (!SecureEvidenceDirectory.PathContained(physicalRoot, physicalDirectory))
                            throw new InvalidDataException(input.Key + " escapes trusted input root");
                        parent = directory;
                    }

                    var file = SecureEvidenceDirectory.OpenExistingRelativeFile(parent,
                        segments[segments.Length - 1], input.Key + " input file");
                    handles.Add(file);
                    var physicalFile = SecureEvidenceDirectory.FinalPath(file, input.Key + " input file");
                    if (!SecureEvidenceDirectory.PathContained(physicalRoot, physicalFile))
                        throw new InvalidDataException(input.Key + " escapes trusted input root");
                    bytes.Add(input.Key, ReadAllBytes(file));
                }
                return new SecureInputSnapshot(handles, bytes);
            }
            catch
            {
                for (var index = handles.Count - 1; index >= 0; index--)
                    handles[index].Dispose();
                throw;
            }
        }

        public byte[] GetBytes(string inputName)
        {
            if (_disposed) throw new ObjectDisposedException("SecureInputSnapshot");
            byte[] value;
            if (!_bytes.TryGetValue(inputName, out value))
                throw new KeyNotFoundException("unknown secure input: " + inputName);
            return (byte[])value.Clone();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _handles.Count - 1; index >= 0; index--)
                _handles[index].Dispose();
        }

        private static byte[] ReadAllBytes(SafeFileHandle handle)
        {
            var handleReferenceAdded = false;
            try
            {
                handle.DangerousAddRef(ref handleReferenceAdded);
                using (var streamHandle = new SafeFileHandle(handle.DangerousGetHandle(), false))
                using (var stream = new FileStream(streamHandle, FileAccess.Read, 4096, false))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
            finally
            {
                if (handleReferenceAdded) handle.DangerousRelease();
            }
        }
    }

    public static class SecureEvidenceIo
    {
        public static void WriteAllText(string trustedRoot, string outputPath, string content)
        {
            var fullOutput = Path.GetFullPath(outputPath);
            var parent = Path.GetDirectoryName(fullOutput);
            using (var directory = SecureEvidenceDirectory.Acquire(trustedRoot, parent))
                directory.WriteAllText(Path.GetFileName(fullOutput), content);
        }
    }
}
