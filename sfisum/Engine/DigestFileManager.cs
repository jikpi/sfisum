using System.Text;
using sfisum.FileRep;
using sfisum.Utils;
using Exception = System.Exception;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine;

internal static class DigestFileManager
{
    public static bool ReadDigestFile(string digestFilePath, out List<FileInfo> files, out List<Hash> hashes,
        out DateTime generatedTime,
        out string? errorMessage)
    {
        files = [];
        hashes = [];
        generatedTime = DateTime.MinValue;
        errorMessage = null;

        try
        {
            using StreamReader reader = new StreamReader(digestFilePath);

            //header
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine) ||
                !headerLine.StartsWith($"{Constants.CommentChar} Directory digest saved at "))
            {
                errorMessage = "Invalid digest file format: Missing or invalid header";
                return false;
            }

            //digest creation time
            ReadOnlySpan<char> dateSpan = headerLine.AsSpan();
            int atIndex = dateSpan.LastIndexOf("at ");
            if (atIndex == -1)
            {
                errorMessage = "Invalid digest file format: Cannot find date";
                return false;
            }

            dateSpan = dateSpan.Slice(atIndex + 3);
            int firstSpace = dateSpan.IndexOf(' ');
            int secondSpace = dateSpan.Slice(firstSpace + 1).IndexOf(' ') + firstSpace + 1;

            if (!DateTime.TryParse(dateSpan.Slice(0, secondSpace).ToString(), out generatedTime))
            {
                errorMessage = "Invalid digest file format: Cannot parse generation date";
                return false;
            }

            string? infoLine;
            while ((infoLine = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(infoLine))
                    continue;

                //hash
                string? hashLine = reader.ReadLine();
                if (hashLine == null)
                {
                    errorMessage = "Invalid digest file format: Incomplete entry";
                    return false;
                }

                //info line
                ReadOnlySpan<char> infoSpan = infoLine.AsSpan();
                if (!infoSpan.StartsWith($"{Constants.CommentChar} Size: ".AsSpan()))
                {
                    errorMessage = $"Invalid digest file format: Expected size info, got: {infoLine}";
                    return false;
                }

                //parse last modified date and size
                int lastModifiedIndex = infoSpan.IndexOf(", Last modified: ");
                if (lastModifiedIndex == -1)
                {
                    errorMessage = "Invalid digest file format: Missing last modified date";
                    return false;
                }

                ReadOnlySpan<char> sizeSpan = infoSpan.Slice(8, lastModifiedIndex - 8);
                ReadOnlySpan<char> lastModifiedSpan = infoSpan.Slice(lastModifiedIndex + 16);


                //hash and path line
                ReadOnlySpan<char> hashSpan = hashLine.AsSpan();
                int asteriskIndex = hashSpan.IndexOf(" *");
                if (asteriskIndex == -1)
                {
                    errorMessage = "Invalid digest file format: Missing file path separator";
                    return false;
                }

                ReadOnlySpan<char> hashStrSpan = hashSpan.Slice(0, asteriskIndex);
                ReadOnlySpan<char> pathSpan = hashSpan.Slice(asteriskIndex + 2);

                try
                {
                    FileInfo fileInfo = FileInfo.FromText(
                        pathSpan,
                        sizeSpan,
                        lastModifiedSpan);
                    Hash hash = new Hash(hashStrSpan);

                    files.Add(fileInfo);
                    hashes.Add(hash);
                }
                catch (LocalException ex)
                {
                    errorMessage = $"Error parsing entry: {ex.Message}";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error reading digest file: {ex.Message}";
            return false;
        }
    }

    public static bool WriteDigestFile(string? digestFilePath, IEnumerable<(FileInfo File, Hash Hash)> entries,
        int totalEntries)
    {
        try
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm}.ddxxh3";

            string fullPath = string.IsNullOrWhiteSpace(digestFilePath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
                : Path.Combine(digestFilePath, fileName);

            if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            {
                return false;
            }

            using StreamWriter writer = new StreamWriter(fullPath, false, new UTF8Encoding(false));
            StringBuilder sb = new(1000);

            writer.WriteLine(
                $"{Constants.CommentChar} Directory digest saved at {DateTime.Now:yyyy-MM-dd HH:mm:ss} containing {totalEntries} entries");

            int count = 0;
            foreach ((FileInfo file, Hash hash) in entries)
            {
                sb.Clear()
                    .Append(Constants.CommentChar)
                    .Append(" Size: ")
                    .Append(file.Size)
                    .Append(", Last modified: ")
                    .Append(file.LastModifiedToString());
                writer.WriteLine(sb);

                sb.Clear()
                    .Append(hash)
                    .Append(" *")
                    .Append(file.Path);
                writer.WriteLine(sb);

                count++;
            }

            if (count != totalEntries)
            {
                throw new LocalException(
                    "bug: The number of entries written does not match the passed total number of entries.");
            }

            return true;
        }
        catch (Exception e)
        {
            if (e is LocalException)
            {
                throw;
            }

            return false;
        }
    }
}