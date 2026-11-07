namespace GameScanner;

/// <summary>Log ra stderr để không lẫn vào JSON ở stdout.</summary>
internal static class Log
{
    public static void Info(string message) => Console.Error.WriteLine(message);
}
