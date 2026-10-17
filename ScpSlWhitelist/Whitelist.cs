using System.Collections.Generic;
using System.IO;

namespace ScpSlWhitelist;

public static class Whitelist
{
    public const string EnvVar = "SCP_SL_WHITELIST_FILE";

    public static string GetFilePath(string pluginDirectory) =>
        Path.Combine(pluginDirectory, "whitelist.txt");

    public static HashSet<string> Load(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (!File.Exists(path))
            return ids;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();

            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex).Trim();

            if (line.Length > 0)
                ids.Add(Normalize(line));
        }

        return ids;
    }

    /// <summary>
    /// Strips the authentication provider suffix
    /// </summary>
    public static string Normalize(string userId)
    {
        var at = userId.IndexOf('@');
        return at >= 0 ? userId.Substring(0, at) : userId;
    }
}
