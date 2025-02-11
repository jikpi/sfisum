using System.Text;

namespace sfisum.Engine.Modes.Utils;

internal abstract class PrintUtils
{
    public static string Separator()
    {
        return "".PadRight(40, '-');
    }

    public static string ReportHeader(string header)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Separator());
        sb.AppendLine(header);
        sb.AppendLine(Separator());

        return sb.ToString();
    }

    public static string SmallHeader(string text)
    {
        return "### " + text;
    }

    public static string ToHumanReadableSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}