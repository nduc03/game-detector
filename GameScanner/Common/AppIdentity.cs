namespace GameScanner;

/// <summary>
/// Quy tắc nhận ra "cùng một game" giữa hai mục (thư viện, packet, danh sách nhớ), dựa trên THƯ MỤC
/// thay vì exePath, để đổi exe chính (vd. sửa tay, promote từ candidateExes) vẫn được coi là cùng game.
/// Mọi phép so sánh đường dẫn đều không phân biệt hoa thường và coi '/' như '\'.
/// </summary>
internal static class AppIdentity
{
    /// <summary>Thư mục đại diện của một mục: installDir, không có thì thư mục chứa exe.</summary>
    public static string? FolderOf(string? installDir, string? exePath)
    {
        if (!string.IsNullOrWhiteSpace(installDir))
            return installDir;

        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        try
        {
            return Path.GetDirectoryName(exePath.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cùng một game khi:
    /// - cùng thư mục đại diện; hoặc
    /// - cùng file exe; hoặc
    /// - exe của bên này nằm trong thư mục của bên kia VÀ ngược lại. Trường hợp này gặp khi một bên ghi
    ///   thư mục gốc của game còn bên kia ghi thư mục con chứa exe (vd. Unreal: ...\Binaries\Win64).
    ///   Điều kiện hai chiều tránh nhầm launcher (vd. thư mục Steam) với các game nằm bên trong nó.
    /// </summary>
    public static bool IsSame(string? installDirA, string? exeA, string? installDirB, string? exeB)
    {
        string? folderA = FolderOf(installDirA, exeA);
        string? folderB = FolderOf(installDirB, exeB);

        if (folderA is not null && folderB is not null && SamePath(folderA, folderB))
            return true;

        if (!string.IsNullOrWhiteSpace(exeA) && !string.IsNullOrWhiteSpace(exeB) && SamePath(exeA, exeB))
            return true;

        return folderA is not null && folderB is not null
            && !string.IsNullOrWhiteSpace(exeA) && !string.IsNullOrWhiteSpace(exeB)
            && IsUnder(exeA, folderB) && IsUnder(exeB, folderA);
    }

    public static bool IsSame(LibraryApp a, AppEntry b) => IsSame(a.InstallDir, a.ExePath, b.InstallDir, b.ExePath);

    public static bool IsSame(LibraryApp a, LibraryApp b) => IsSame(a.InstallDir, a.ExePath, b.InstallDir, b.ExePath);

    public static bool IsSame(RememberedApp a, AppEntry b) => IsSame(a.InstallDir, a.ExePath, b.InstallDir, b.ExePath);

    public static bool IsSame(RememberedApp a, LibraryApp b) => IsSame(a.InstallDir, a.ExePath, b.InstallDir, b.ExePath);

    /// <summary>Hai đường dẫn trỏ cùng một chỗ (bỏ qua hoa thường, dấu phân cách và dấu '\' ở cuối).</summary>
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="path"/> chính là <paramref name="folder"/> hoặc nằm bên trong nó (đúng ranh giới thư mục).</summary>
    public static bool IsUnder(string? path, string? folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
            return false;

        string p = Normalize(path);
        string f = Normalize(folder);
        if (f.Length == 0)
            return false;

        return p.Equals(f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + '\\', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Đưa đường dẫn về dạng so sánh được: '/' thành '\', bỏ dấu nháy, khoảng trắng và '\' ở cuối.</summary>
    public static string Normalize(string path) =>
        path.Trim().Trim('"').Trim().Replace('/', '\\').TrimEnd('\\');
}
