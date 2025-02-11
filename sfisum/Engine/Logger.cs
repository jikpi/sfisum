using System.Text;

namespace sfisum.Engine;

internal class Logger : IDisposable, IAsyncDisposable
{
    public Logger(bool toConsole, bool toFile)
    {
        ToConsole = toConsole;
        ToFile = toFile;

        if (ToFile)
        {
            _fileStream = new FileStream("sfisum-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log",
                FileMode.Create);
        }
    }

    private readonly FileStream? _fileStream;

    public bool ToConsole { get; set; }
    private bool ToFile { get; set; }

    private static void LogToConsole(string message, ConsoleColor? color = null)
    {
        if (color is not null)
        {
            Console.ForegroundColor = color.Value;
        }

        Console.WriteLine(message);

        if (color is not null)
        {
            Console.ResetColor();
        }
    }

    private void LogToFile(string message)
    {
        if (_fileStream is null) return;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
            _fileStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to write to log file: {e.Message}");
            Console.ResetColor();
            ToFile = false;
        }
    }

    public void LogConsoleOnly(string message, ConsoleColor? color = null)
    {
        if (ToConsole)
        {
            LogToConsole(message, color);
        }
    }

    public void LogFileOnly(string message)
    {
        if (ToFile)
        {
            LogToFile(message);
        }
    }

    public void Log(string message, ConsoleColor? color = null)
    {
        if (ToConsole)
        {
            LogToConsole(message, color);
        }

        if (ToFile)
        {
            LogToFile(message);
        }
    }


    public void Dispose()
    {
        _fileStream?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_fileStream != null) await _fileStream.DisposeAsync();
    }
}