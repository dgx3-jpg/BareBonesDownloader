using System;
using System.IO;

namespace BareBonesDownloader
{
    internal static class DownloadFiles
    {
        // Publish only completed audio, without changing any file already in Music.
        public static string PublishAudio(string source, string destination,
            Func<bool> shouldStop, out bool existingFile)
        {
            string target = Path.Combine(Path.GetFullPath(destination), Path.GetFileName(source));
            existingFile = false;
            if (File.Exists(target))
            {
                existingFile = true;
                return target;
            }

            string temporary = Path.Combine(destination,
                ".BareBonesDownloader-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (shouldStop())
                            throw new OperationCanceledException();
                        output.Write(buffer, 0, count);
                    }
                    output.Flush();
                }
                if (shouldStop())
                    throw new OperationCanceledException();
                try
                {
                    File.Move(temporary, target);
                }
                catch (IOException)
                {
                    // Another process may have saved the same title while we copied.
                    if (!File.Exists(target))
                        throw;
                    existingFile = true;
                }
                return target;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public static void CleanupWorkingDirectory(string workRoot, string workingDirectory)
        {
            string root = Path.GetFullPath(workRoot).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string directory = Path.GetFullPath(workingDirectory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(directory);
            if (!String.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to clean outside the download working folder.");
            if (!Directory.Exists(directory))
                return;
            CheckForLinks(root);
            CheckTreeForLinks(directory);
            Directory.Delete(directory, true);
        }

        private static void CheckForLinks(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A download working folder contains a link and was left in place.");
        }

        private static void CheckTreeForLinks(string directory)
        {
            CheckForLinks(directory);
            foreach (string child in Directory.GetFileSystemEntries(directory))
            {
                CheckForLinks(child);
                if (Directory.Exists(child))
                    CheckTreeForLinks(child);
            }
        }
    }
}
