using System.IO;

namespace Kniip.Settings;

public static class SaveLocation
{
    /// <summary>Pad in de map dat nog niet bestaat: "naam.png", anders "naam (2).png", "naam (3).png", ...</summary>
    public static string UniquePath(string folder, string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string path = Path.Combine(folder, fileName);
        for (int i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{name} ({i}){extension}");
        }

        return path;
    }
}
