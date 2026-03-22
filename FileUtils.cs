namespace StoatTote;

internal static class FileUtils
{
    /// <summary>Walks directory recursively, returning paths relative to baseDir.</summary>
    public static List<string> WalkDir(string dir, string? baseDir = null)
    {
        baseDir ??= dir;
        var results = new List<string>();

        try
        {
            foreach (var entry in new DirectoryInfo(dir).GetFileSystemInfos())
            {
                if (Config.Ignore.Contains(entry.Name)) continue;

                var rel = Path.GetRelativePath(baseDir, entry.FullName).Replace('\\', '/');

                if (entry is DirectoryInfo)
                {
                    results.Add(rel + "/");
                    results.AddRange(WalkDir(entry.FullName, baseDir));
                }
                else
                {
                    results.Add(rel);
                }
            }
        }
        catch { /* skip unreadable dirs */ }

        return results;
    }

    /// <summary>Returns true if file appears to be binary (contains null byte).</summary>
    public static bool IsBinary(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[512];
            int read = fs.Read(buf, 0, buf.Length);
            for (int i = 0; i < read; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch { return true; }
    }

    /// <summary>Reads text file, returns null if binary or unreadable.</summary>
    public static string? ReadFileContents(string path)
    {
        try
        {
            if (IsBinary(path)) return null;
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        catch { return null; }
    }

    /// <summary>Creates directory if it doesn't exist.</summary>
    public static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>Backs up file to .stoat-backups/timestamp/ and returns backup path.</summary>
    public static string? BackupFile(string path)
    {
        if (!File.Exists(path)) return null;

        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss-fff");
        var backupRoot = Path.Combine(Directory.GetCurrentDirectory(), Config.BackupDir, timestamp);
        var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        var dest = Path.Combine(backupRoot, rel);

        EnsureDir(Path.GetDirectoryName(dest)!);
        File.Copy(path, dest, overwrite: true);
        return dest;
    }
}
