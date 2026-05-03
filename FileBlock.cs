using System.IO;
using System.Text.RegularExpressions;

namespace StoatTote;

/// <summary>
/// Represents a parsed file block from the LLM response.
/// </summary>
public class FileBlock
{
    public string FilePath = "";
    public string Content = "";
    private int index = -1;

    public FileBlock() { }

    public FileBlock(string path, string content, int lastIndex)
    {
        FilePath = path;
        Content = content;
        index = lastIndex;
    }

    public int GetIndex() => index;

    public FileBlock Copy() => new FileBlock
    {
        Content = this.Content,
        FilePath = this.FilePath,
        index = this.index
    };

    public static List<FileBlock> ParseFiles(string content)
    {
        // Normalize line endings to match Config tags (\r\n)
        content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");

        var fileBlocks = new List<FileBlock>();
        var fb = GetNextFileBlock(content, 0);
        
        if(fb != null) fileBlocks.Add(fb.Copy());

        while (fb != null)
        {
            fb = GetNextFileBlock(content, fb.GetIndex());
            if(fb != null) fileBlocks.Add(fb.Copy());
        }

        if (fileBlocks.Count == 0)
            fileBlocks = ParseMarkdownFiles(content);

        return fileBlocks;
    }

    public static List<FileBlock> ParseMarkdownFiles(string content)
    {
        var fileBlocks = new List<FileBlock>();

        content = content.Replace("\r\n", "\n");

        var regex = new Regex(
            @"\*\*(.+?)\*\*\s*```[^\n]*\n(.*?)\n```",
            RegexOptions.Singleline);

        var matches = regex.Matches(content);

        foreach (Match match in matches)
        {
            string fn = match.Groups[1].Value.Trim();
            string fc = match.Groups[2].Value;

            if (!IsValidFilePath(fn))
                continue;

            fc = fc.Replace("\n", "\r\n");

            fileBlocks.Add(new FileBlock(fn, fc, match.Index + match.Length));
        }

        return fileBlocks;
    }

    public static FileBlock? GetNextFileBlock(string content, int startIndex)
    {
        int ft1 = content.IndexOf(Config.FileTag, startIndex);
        if (ft1 < 0) return null;

        int ft2 = content.IndexOf(Config.FileTag, ft1 + Config.FileTag.Length);
        if (ft2 < 0) return null;

        int fc1 = content.IndexOf(Config.FileStartTag, ft2 + Config.FileTag.Length);
        if (fc1 < 0) return null;

        int fc2 = content.IndexOf(Config.FileEndTag, fc1 + Config.FileStartTag.Length);
        if (fc2 < 0) return null;

        string fn = content.Substring(ft1 + Config.FileTag.Length, ft2 - ft1 - Config.FileTag.Length).Trim();
        string fc = content.Substring(fc1 + Config.FileStartTag.Length, fc2 - fc1 - Config.FileStartTag.Length);

        if (!IsValidFilePath(fn))
            return null;

        return new FileBlock(fn, fc, fc2);
    }

    private static bool IsValidFilePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var inv_pc = Path.GetInvalidPathChars();
        var inv_fn = Path.GetInvalidFileNameChars();

        string fileName = Path.GetFileName(path);

        if (path.IndexOfAny(inv_pc) >= 0)
            return false;

        if (fileName.IndexOfAny(inv_fn) >= 0)
            return false;

        return true;
    }

    public static bool SaveFileMD(string content)
    {
        File.WriteAllText(".stoat/" + new Random().Next(100000, 999999) + ".md", content);

        return true;
    }
}
