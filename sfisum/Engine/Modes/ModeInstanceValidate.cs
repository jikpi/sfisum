using System.Diagnostics;
using sfisum.Engine.Modes.Base;
using sfisum.Engine.Modes.Utils;
using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine.Modes;

internal class ModeInstanceValidate(string directoryPath, string digestPath) : ModeInstanceBase
{
    private string DirectoryPath { get; set; } = directoryPath;
    private string DigestPath { get; set; } = digestPath;
    private List<FileInfo> Files { get; set; } = [];
    private List<Hash> ReadHashes { get; set; } = [];
    private List<Hash?> Hashes { get; set; } = [];

    private List<int> UnhashableFiles { get; set; } = [];
    private List<int> InvalidHashFiles { get; set; } = [];
    private List<int> ValidHashFiles { get; set; } = [];

    public bool HasEvents => InvalidHashFiles.Count > 0 || UnhashableFiles.Count > 0;

    public int SuccesfullyHashedFiles => ValidHashFiles.Count + InvalidHashFiles.Count;


    public override void Start()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Directory does not exist: {DirectoryPath}");
            Console.ResetColor();
            return;
        }

        Stopwatch sw = new Stopwatch();
        Console.WriteLine("Loading digest file...");


        sw.Restart();
        if (!DigestFileManager.ReadDigestFile(DigestPath, out List<FileInfo> readFiles, out List<Hash> readHashes,
                out _,
                out string? errorMessage))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to load digest file: {errorMessage}");
            Console.ResetColor();
            return;
        }

        Files = readFiles;
        ReadHashes = readHashes;

        if (Files.Count < 1)
        {
            throw new LocalException("No files found in digest file.");
        }

        sw.Stop();
        Console.WriteLine($"Loaded digest file in {sw.ElapsedMilliseconds / 1000.0} seconds with {Files.Count} files.");
        Console.ResetColor();

        Console.WriteLine("Hashing files...");
        sw.Restart();

        var hashResult = FileHasher.HashFiles(DirectoryPath, Files);
        sw.Stop();

        if (!hashResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Hashing complete. Failed to hash {hashResult.ErrorImap.Count} files (Omitted).");
        }
        else
        {
            Console.WriteLine("Hashing complete.");
        }

        Console.ResetColor();

        Console.WriteLine($@"Time to hash: {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds):hh\:mm\:ss}");

        Hashes = hashResult.Hashes;
        UnhashableFiles = hashResult.ErrorImap;

        if (Hashes.Count != ReadHashes.Count)
        {
            throw new LocalException("bug: Loaded hash count does not match file count.");
        }


        ValidHashFiles.Capacity = hashResult.SuccessImap.Count;
        foreach (int successIdx in hashResult.SuccessImap)
        {
            if (Hashes[successIdx] == ReadHashes[successIdx])
            {
                ValidHashFiles.Add(successIdx);
            }
            else
            {
                InvalidHashFiles.Add(successIdx);
            }
        }

        if (InvalidHashFiles.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }

        Console.WriteLine(
            $"Validated {ValidHashFiles.Count} files out of {ValidHashFiles.Count + InvalidHashFiles.Count} total hashed.");
        Console.ResetColor();
    }

    public override bool SaveDigest(string path, string? digestFilenamePrefix)
    {
        return DigestFileManager.WriteDigestFile(path, ValidEntries(), SuccesfullyHashedFiles, digestFilenamePrefix);

        IEnumerable<(FileInfo File, Hash Hash)> ValidEntries()
        {
            for (int i = 0; i < Hashes.Count; i++)
            {
                if (Hashes[i].HasValue)
                {
                    yield return (Files[i], Hashes[i]!.Value);
                }
            }
        }
    }

    public override void PrintGeneralEvents(bool toConsole)
    {
        if (!Glob.Config.PrintToLog && !toConsole) return;

        using Logger logger = new Logger(toConsole, Glob.Config.PrintToLog);
        logger.Log(PrintUtils.ReportHeader("Report"), ConsoleColor.Cyan);
        logger.LogFileOnly($"Mode: Validate");

        if (InvalidHashFiles.Count > 0)
        {
            logger.Log(PrintUtils.SmallHeader("Files with invalid hashes:"), ConsoleColor.Red);

            foreach (var file in InvalidHashFiles)
            {
                logger.Log(Files[file].Path);
            }

            logger.Log(PrintUtils.Separator());
        }

        if (UnhashableFiles.Count > 0)
        {
            logger.Log(PrintUtils.SmallHeader("Could not hash these files:"), ConsoleColor.Yellow);

            foreach (var file in UnhashableFiles)
            {
                logger.Log(Files[file].Path);
            }

            logger.Log(PrintUtils.Separator());
        }

        if (!Glob.Config.PrintToLog)
        {
            logger.Log("");
            return;
        }

        if (SuccesfullyHashedFiles > 0)
        {
            logger.ToConsole = false;
            logger.Log(PrintUtils.SmallHeader("Valid files:"));

            foreach (var file in ValidHashFiles)
            {
                logger.Log(Files[file].Path);
            }
        }
    }
}