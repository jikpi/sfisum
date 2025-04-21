using System.Diagnostics;
using sfisum.Engine.Modes.Base;
using sfisum.Engine.Modes.Utils;
using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine.Modes;

internal class ModeInstanceGenerate(string directoryPath) : ModeInstanceBase
{
    private string DirectoryPath { get; set; } = directoryPath;
    private List<FileInfo> Files { get; set; } = [];
    private List<Hash?> Hashes { get; set; } = [];
    private List<string>? InaccessibleFiles { get; set; }
    private List<string>? HashErrors { get; set; }

    public int SuccesfullyHashedFiles { get; private set; }
    public bool HasInaccessibleFiles => InaccessibleFiles is not null;
    public bool HasHashErrors => HashErrors is not null;


    public override void Start()
    {
        Stopwatch sw = new Stopwatch();
        sw.Restart();
        Console.WriteLine("Walking directory...");

        try
        {
            Files = FileInfo.WalkDirectory(DirectoryPath,
                out List<string> badFiles);
            sw.Stop();

            if (badFiles.Count > 0)
            {
                InaccessibleFiles = badFiles;
            }
        }
        catch (Exception)
        {
            Files = [];
            InaccessibleFiles = null;
            throw;
        }

        if (Files.Count == 0)
        {
            throw new LocalException("No files found in directory.");
        }

        if (InaccessibleFiles is not null) Console.ForegroundColor = ConsoleColor.Yellow;

        Console.WriteLine(
            $"Found {Files.Count} files{(InaccessibleFiles is not null ? $" and {InaccessibleFiles.Count} inaccessible files (Omitted)" : "")} in {sw.ElapsedMilliseconds / 1000.0} seconds.");
        Console.ResetColor();

        Console.WriteLine("Hashing files...");
        sw.Restart();

        var hashResult = FileHasher.HashFiles(DirectoryPath, Files);
        sw.Stop();

        SuccesfullyHashedFiles = hashResult.SuccessImap.Count;
        Hashes = hashResult.Hashes;

        if (!hashResult.Success)
        {
            HashErrors = new List<string>(hashResult.ErrorImap.Count);
            foreach (int errorIdx in hashResult.ErrorImap)
            {
                HashErrors.Add(Path.Combine(DirectoryPath, Files[errorIdx].Path));
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Hashing complete. Failed to hash {HashErrors.Count} files (Omitted).");
        }
        else
        {
            Console.WriteLine("Hashing complete.");
        }

        Console.ResetColor();
        Console.WriteLine($@"Time to hash: {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds):hh\:mm\:ss}");
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
        if (InaccessibleFiles is null && HashErrors is null) return;

        using Logger logger = new Logger(toConsole, Glob.Config.PrintToLog);
        logger.Log(PrintUtils.ReportHeader("Report"), ConsoleColor.Cyan);
        logger.LogFileOnly($"Mode: Generate");

        if (InaccessibleFiles is not null)
        {
            logger.Log(PrintUtils.SmallHeader("Inaccessible files:"), ConsoleColor.Yellow);

            foreach (var file in InaccessibleFiles)
            {
                logger.Log(file);
            }

            logger.Log(PrintUtils.Separator());
        }

        if (HashErrors is not null)
        {
            logger.Log(PrintUtils.SmallHeader("Could not hash these files:"), ConsoleColor.Yellow);

            foreach (var file in HashErrors)
            {
                logger.Log(file);
            }

            logger.Log(PrintUtils.Separator());
        }

        logger.Log("");
    }
}