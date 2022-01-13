using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Umbraco.Cms.Core.IO;

namespace Umbraco.Extensions
{
    public static class FileSystemExtensions
    {
        public static string GetStreamHash(this Stream fileStream)
        {
            if (fileStream.CanSeek)
            {
                fileStream.Seek(0, SeekOrigin.Begin);
            }

            using HashAlgorithm alg = SHA1.Create();

            // create a string output for the hash
            var stringBuilder = new StringBuilder();
            var hashedByteArray = alg.ComputeHash(fileStream);
            foreach (var b in hashedByteArray)
            {
                stringBuilder.Append(b.ToString("x2"));
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// Attempts to open the file at <code>filePath</code> up to <code>maxRetries</code> times,
        /// with a thread sleep time of <code>sleepPerRetryInMilliseconds</code> between retries.
        /// </summary>
        public static FileStream OpenReadWithRetry(this FileInfo file, int maxRetries = 5, int sleepPerRetryInMilliseconds = 50)
        {
            var retries = maxRetries;

            while (retries > 0)
            {
                try
                {
                    return File.OpenRead(file.FullName);
                }
                catch(IOException)
                {
                    retries--;

                    if (retries == 0)
                    {
                        throw;
                    }

                    Thread.Sleep(sleepPerRetryInMilliseconds);
                }
            }

            throw new ArgumentException("Retries must be greater than zero");
        }

        public static void CopyFile(this IFileSystem fs, string path, string newPath)
        {
            using (Stream? stream = fs.OpenFile(path))
            {
                if (stream is not null)
                {
                    fs.AddFile(newPath, stream);
                }

            }
        }

        public static string GetExtension(this IFileSystem fs, string path)
        {
            return Path.GetExtension(fs.GetFullPath(path));
        }

        public static string GetFileName(this IFileSystem fs, string path)
        {
            return Path.GetFileName(fs.GetFullPath(path));
        }

        // TODO: Currently this is the only way to do this
        public static void CreateFolder(this IFileSystem fs, string folderPath)
        {
            var path = fs.GetRelativePath(folderPath);
            var tempFile = Path.Combine(path, Guid.NewGuid().ToString("N") + ".tmp");
            using (var s = new MemoryStream())
            {
                fs.AddFile(tempFile, s);
            }
            fs.DeleteFile(tempFile);
        }
    }
}
