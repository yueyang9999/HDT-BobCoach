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
        private const uint GenericWrite = 0x40000000;
        private const uint SynchronizeAccess = 0x00100000;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileCreate = 2;
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

        private static SafeFileHandle OpenDirectory(string path, string name)
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
                    FileShareRead | FileShareWrite, FileOpenIf,
                    FileDirectoryFile | FileFlagOpenReparsePoint
                        | FileSynchronousIoNonAlert,
                    IntPtr.Zero, 0);
                if (status < 0)
                {
                    if (directoryHandle != null) directoryHandle.Dispose();
                    ThrowNtStatus(status, "cannot open evidence output directory");
                }
                ByHandleFileInformation information;
                if (!GetFileInformationByHandle(directoryHandle, out information))
                {
                    var error = Marshal.GetLastWin32Error();
                    directoryHandle.Dispose();
                    throw new IOException("cannot inspect evidence output directory: "
                        + new System.ComponentModel.Win32Exception(error).Message);
                }
                if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
                {
                    directoryHandle.Dispose();
                    throw new InvalidDataException(
                        "evidence output directory is a reparse point");
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

        private static string FinalPath(SafeFileHandle handle, string name)
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

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("path is required");
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                ? root
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathContained(string parent, string candidate)
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
