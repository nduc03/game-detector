using System.Text;

namespace GameScanner;

internal static class AtomicFile
{
    /// <summary>Ghi ra file tạm rồi đổi tên đè lên, để file đích không bao giờ ở trạng thái ghi dở.</summary>
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string temp = path + ".tmp";
        File.WriteAllText(temp, content, encoding);
        File.Move(temp, path, overwrite: true);
    }
}
