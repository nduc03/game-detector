namespace GameScanner;

/// <summary>
/// Log dùng chung cho mọi bước. Chế độ dòng lệnh ghi ra stderr (không lẫn vào JSON ở stdout);
/// chế độ GUI đổi đích ghi sang khung nhật ký của cửa sổ chính bằng <see cref="SetSink"/>.
/// </summary>
internal static class Log
{
    private static Action<string> _sink = WriteToStderr;

    /// <summary>Đổi nơi nhận log; truyền null để quay về stderr.</summary>
    public static void SetSink(Action<string>? sink) => _sink = sink ?? WriteToStderr;

    public static void Info(string message)
    {
        try
        {
            _sink(message);
        }
        catch
        {
            // Ghi log thất bại không được làm hỏng việc quét.
        }
    }

    private static void WriteToStderr(string message) => Console.Error.WriteLine(message);
}
