using System.Text;

namespace GameProcessChecker;

/// <summary>Ghi trạng thái vào %USERPROFILE%\gamerunning.sensor ("1" hoặc "0", text, không BOM).</summary>
internal static class SensorFile
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "gamerunning.sensor");

    private static readonly byte[] One = Encoding.ASCII.GetBytes("1");
    private static readonly byte[] Zero = Encoding.ASCII.GetBytes("0");

    public static bool Exists() => File.Exists(FilePath);

    public static void Write(bool running)
    {
        byte[] data = running ? One : Zero;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // FileShare.ReadWrite | Delete: app khác vẫn đọc được kể cả trong lúc đang ghi.
                // Không truncate trước khi ghi => reader không bao giờ thấy file rỗng.
                // using => handle được đóng (mở khóa) ngay khi ghi xong.
                using var fs = new FileStream(
                    FilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);

                fs.Write(data, 0, data.Length);
                fs.SetLength(data.Length);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100); // file đang bị app khác khóa độc quyền, thử lại
            }
        }
    }
}
