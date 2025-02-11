using sfisum.Utils;

namespace sfisum.FileRep;

internal class FileInfo
{
    public string Path { get; }
    public long Size { get; }
    public DateTime LastModified { get; }

    private FileInfo(string path, long size, DateTime lastModified)
    {
        Path = path;
        Size = size;
        LastModified = lastModified;
    }

    public static FileInfo FromFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new LocalException("File from disk: Path cannot be empty");
        }

        if (!File.Exists(path))
        {
            throw new LocalException($"File from disk: File not found: {path}");
        }

        System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
        return new FileInfo(
            path,
            fileInfo.Length,
            fileInfo.LastWriteTime
        );
    }

    public static FileInfo FromText(ReadOnlySpan<char> path, ReadOnlySpan<char> size, ReadOnlySpan<char> lastModified)
    {
        if (path.IsEmpty)
        {
            throw new LocalException("File from text: Path cannot be empty");
        }

        if (!long.TryParse(size, out long fileSize))
        {
            throw new LocalException("File from text: Invalid file size format");
        }

        if (!DateTime.TryParse(lastModified, out DateTime lastModifiedDateTime))
        {
            throw new LocalException("File from text: Invalid last modified date format");
        }

        return new FileInfo(
            path.ToString(),
            fileSize,
            lastModifiedDateTime
        );
    }

    public string LastModifiedToString()
    {
        return LastModified.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static List<FileInfo> WalkDirectory(string directoryPath, out List<string> inaccessiblePaths)
    {
        inaccessiblePaths = [];

        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new LocalException("Walk directory: Path cannot be empty");
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new LocalException($"Walk directory: Directory not found: {directoryPath}");
        }

        List<FileInfo> files = new(65536);

        try
        {
            string normalizedDirectoryPath =
                System.IO.Path.GetFullPath(directoryPath).TrimEnd(System.IO.Path.DirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            

            string dirWalkPattern = Glob.Config.DirectoryWalkPattern ?? "*";

            var filePaths = Directory.EnumerateFiles(directoryPath, dirWalkPattern,
                SearchOption.AllDirectories);

            foreach (string filePath in filePaths)
            {
                try
                {
                    string normalizedFilePath = System.IO.Path.GetFullPath(filePath);
                    string relativePath = filePath;

                    if (normalizedFilePath.StartsWith(normalizedDirectoryPath))
                    {
                        relativePath = normalizedFilePath[normalizedDirectoryPath.Length..];
                    }

                    System.IO.FileInfo sysFileInfo = new System.IO.FileInfo(normalizedFilePath);
                    files.Add(new FileInfo(
                        relativePath,
                        sysFileInfo.Length,
                        sysFileInfo.LastWriteTime
                    ));
                }
                catch (Exception)
                {
                    inaccessiblePaths.Add(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            throw new LocalException($"Walk directory: Error accessing directory: {ex.Message}");
        }

        return files;
    }
}