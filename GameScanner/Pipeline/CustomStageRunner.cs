using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>Chạy một pipeline tùy chỉnh (chương trình ngoài) và lấy packet kết quả.</summary>
internal static class CustomStageRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Mọi sự cố (không tìm thấy chương trình, mã thoát khác 0, quá thời gian, thiếu file kết quả,
    /// JSON sai cấu trúc...) đều ném exception để pipeline dừng lại.
    /// </summary>
    public static async Task<PipelinePacket> RunAsync(
        CustomStage stage, PipelinePacket input, string runDirectory, int stepNumber, CancellationToken cancellationToken)
    {
        string exe = ExecutableLocator.Resolve(stage.ExePath)
            ?? throw new InvalidOperationException($"Không tìm thấy chương trình: {stage.ExePath}");

        string inPath = Path.Combine(runDirectory, $"step{stepNumber}-in.json");
        string outPath = Path.Combine(runDirectory, $"step{stepNumber}-out.json");
        string inputJson = JsonSerializer.Serialize(input, AppJsonContext.Default.PipelinePacket);
        await File.WriteAllTextAsync(inPath, inputJson, Utf8NoBom, cancellationToken);

        string? resultFile = stage.ResultSource switch
        {
            StageResultSource.OutFile => outPath,
            StageResultSource.Path => ResolveResultPath(stage.ResultPath),
            _ => null
        };

        string arguments = StageArguments.Expand(stage.Arguments, inPath, outPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        Log.Info($"[{stage.DisplayName}] Chạy: \"{exe}\" {arguments}");
        DateTime startedUtc = DateTime.UtcNow;

        using var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Log.Info($"[{stage.DisplayName}] {e.Data}");
        };

        process.Start();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (stage.TimeoutSeconds > 0)
            timeout.CancelAfter(TimeSpan.FromSeconds(stage.TimeoutSeconds));

        string stdout;
        try
        {
            // Đọc stdout song song với việc ghi stdin để hai bên không chờ nhau khi packet lớn.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task stdinTask = FeedStdinAsync(process, stage.PipeStdin ? inputJson : null);

            await process.WaitForExitAsync(timeout.Token);
            await stdinTask;
            stdout = await stdoutTask;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"Chương trình chạy quá {stage.TimeoutSeconds} giây nên đã bị dừng.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Chương trình thoát với mã lỗi {process.ExitCode}.");

        string resultJson = resultFile is null
            ? stdout
            : await ReadResultFileAsync(resultFile, stage.ResultSource, startedUtc, cancellationToken);

        try
        {
            return PacketValidator.Parse(resultJson);
        }
        catch (PacketFormatException ex)
        {
            throw new PacketFormatException($"Kết quả ({DescribeSource(stage.ResultSource, resultFile)}) sai cấu trúc packet: {ex.Message}");
        }
    }

    /// <summary>Đường dẫn file kết quả cố định sau khi bung biến môi trường; null nếu không hợp lệ.</summary>
    public static string? TryResolveResultPath(string? raw)
    {
        string? value = PathUtils.Unquote(raw);
        if (value is null)
            return null;

        try
        {
            value = Environment.ExpandEnvironmentVariables(value);
            return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string ResolveResultPath(string? raw) =>
        TryResolveResultPath(raw)
        ?? throw new InvalidOperationException($"Đường dẫn file kết quả không hợp lệ (cần đường dẫn tuyệt đối): {raw}");

    private static async Task<string> ReadResultFileAsync(
        string path, StageResultSource source, DateTime startedUtc, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(source == StageResultSource.OutFile
                ? "Chương trình không ghi kết quả ra file $out."
                : $"Không thấy file kết quả: {path}");
        }

        // File ở đường dẫn cố định có thể là file cũ của lần chạy trước: phải được ghi lại trong lần chạy này.
        if (source == StageResultSource.Path && File.GetLastWriteTimeUtc(path) < startedUtc.AddSeconds(-2))
            throw new InvalidOperationException($"File kết quả không được chương trình ghi lại trong lần chạy này (file cũ): {path}");

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static async Task FeedStdinAsync(Process process, string? content)
    {
        try
        {
            if (content is not null)
                await process.StandardInput.WriteAsync(content);
        }
        catch (IOException)
        {
            // Chương trình không đọc stdin hoặc đã thoát sớm; kết quả sẽ được kiểm tra ở bước sau.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Pipe đã đóng từ phía chương trình.
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Tiến trình đã tự thoát.
        }
    }

    private static string DescribeSource(StageResultSource source, string? file) => source switch
    {
        StageResultSource.Stdout => "stdout",
        StageResultSource.OutFile => "file $out",
        _ => $"file {file}"
    };
}
