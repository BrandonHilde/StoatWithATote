namespace StoatTote;

internal static class Ansi
{
    public static string Dim(string s)    => $"\x1b[2m{s}\x1b[0m";
    public static string Bold(string s)   => $"\x1b[1m{s}\x1b[0m";
    public static string Green(string s)  => $"\x1b[32m{s}\x1b[0m";
    public static string Red(string s)    => $"\x1b[31m{s}\x1b[0m";
    public static string Yellow(string s) => $"\x1b[33m{s}\x1b[0m";
    public static string Cyan(string s)   => $"\x1b[36m{s}\x1b[0m";
    public static string Magenta(string s) => $"\x1b[35m{s}\x1b[0m";
}
