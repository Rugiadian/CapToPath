using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CapToPath
{
    public static class PicPickHelper
    {
        /// <summary>
        /// Attempts to find the actual screenshot file when PicPick passes literal "%1" or invalid arguments
        /// </summary>
        public static string? DetectLatestScreenshot(int timeoutMs = 1500)
        {
            string? autoSaveFolder = GetPicPickAutoSaveFolder();

            // Candidates to search for screenshots
            var candidateFolders = new[]
            {
                autoSaveFolder,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox", "스크린샷"),
                @"F:\Dropbox\스크린샷",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Pictures", "Screenshots")
            }
            .Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            if (candidateFolders.Count == 0)
            {
                return null;
            }

            int waitInterval = 50;
            int maxAttempts = Math.Max(1, timeoutMs / waitInterval);

            var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"
            };

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                DateTime now = DateTime.Now;

                foreach (var folder in candidateFolders)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(folder!);
                        var latestFile = dirInfo.EnumerateFiles()
                            .Where(f => validExtensions.Contains(f.Extension))
                            .OrderByDescending(f => f.LastWriteTime)
                            .FirstOrDefault();

                        if (latestFile != null)
                        {
                            // If modified in the last 15 seconds, this is definitely the newly captured screenshot!
                            TimeSpan age = now - latestFile.LastWriteTime;
                            if (age.TotalSeconds <= 15 && latestFile.Length > 0)
                            {
                                return latestFile.FullName;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore folder access exceptions
                    }
                }

                Thread.Sleep(waitInterval);
            }

            // Fallback: If no file created in the last 15 seconds, return the most recent file from the primary folder
            if (!string.IsNullOrWhiteSpace(autoSaveFolder) && Directory.Exists(autoSaveFolder))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(autoSaveFolder);
                    var mostRecent = dirInfo.EnumerateFiles()
                        .Where(f => validExtensions.Contains(f.Extension))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (mostRecent != null)
                    {
                        return mostRecent.FullName;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads PicPick configuration (picpick.ini) to locate the AutoSaveFolder
        /// </summary>
        public static string? GetPicPickAutoSaveFolder()
        {
            string[] possibleIniPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PicPick", "picpick.ini"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicPick", "picpick.ini"),
                @"C:\Program Files (x86)\PicPick\picpick.ini",
                @"C:\Program Files\PicPick\picpick.ini"
            };

            foreach (var iniPath in possibleIniPaths)
            {
                if (File.Exists(iniPath))
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(iniPath))
                        {
                            string trimmed = line.Trim();
                            if (trimmed.StartsWith("AutoSaveFolder=", StringComparison.OrdinalIgnoreCase))
                            {
                                string folder = trimmed.Substring("AutoSaveFolder=".Length).Trim().Trim('"', '\'');
                                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                                {
                                    return folder;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore read errors
                    }
                }
            }

            return null;
        }
    }
}
