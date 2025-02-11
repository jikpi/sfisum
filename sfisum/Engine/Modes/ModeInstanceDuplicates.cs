using System.Diagnostics;
using sfisum.Engine.Modes.Base;
using sfisum.Engine.Modes.Utils;
using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine.Modes;

internal class ModeInstanceDuplicates(string digestPath) : ModeInstanceBase
{
    private string DigestPath { get; } = digestPath;

    private List<FileInfo> _files = [];
    private List<Hash> _hashes = [];

    private readonly Dictionary<Hash, List<int>> _duplicatesImap = new();
    public int DuplicatesCount { get; private set; }

    public override void Start()
    {
        Stopwatch sw = new Stopwatch();

        //Load digest file into secondary snap
        Console.WriteLine("Loading digest file...");


        sw.Restart();
        if (!DigestFileManager.ReadDigestFile(DigestPath, out _files, out _hashes,
                out _,
                out string? errorMessage))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to load digest file: {errorMessage}");
            Console.ResetColor();
            return;
        }

        if (_files.Count < 1)
        {
            throw new LocalException("No files found in digest file.");
        }

        sw.Stop();
        Console.WriteLine(
            $"Loaded digest file in {sw.ElapsedMilliseconds / 1000.0} seconds with {_files.Count} files.");


        _duplicatesImap.EnsureCapacity(_files.Count);
        Console.WriteLine("Comparing hashes...");
        sw.Restart();

        for (int i = 0; i < _files.Count; i++)
        {
            if (_duplicatesImap.TryGetValue(_hashes[i], out List<int>? dupIndexes))
            {
                if (dupIndexes.Count == 1)
                {
                    DuplicatesCount++;
                }

                dupIndexes.Add(i);
            }
            else
            {
                _duplicatesImap.Add(_hashes[i], new List<int>(4) { i });
            }
        }

        sw.Stop();
        Console.WriteLine($"Compared hashes in {sw.ElapsedMilliseconds / 1000.0} seconds.");
    }

    public override bool SaveDigest(string path)
    {
        return false;
    }

    public override void PrintGeneralEvents(bool toConsole)
    {
        if (!Glob.Config.PrintToLog && !toConsole) return;

        using Logger logger = new Logger(toConsole, Glob.Config.PrintToLog);
        logger.Log(PrintUtils.ReportHeader("Report"), ConsoleColor.Cyan);
        logger.LogFileOnly($"Mode: Find duplicates");

        logger.Log($"Found {DuplicatesCount} total duplicates. They make up " +
                   $"{DuplicatesCount * 100.0 / _files.Count:F2}% of the total files.", ConsoleColor.Cyan);

        bool sortByWastedSize = Glob.Config.SortDuplicatesBySize;

        var duplicateGroups = new List<(long wastedSize, List<int> indexes)>();
        long totalSize = 0;

        foreach (var group in _duplicatesImap)
        {
            if (group.Value.Count < 2)
            {
                continue;
            }

            long wastedSize = _files[group.Value[0]].Size * (group.Value.Count - 1);
            totalSize += wastedSize;
            duplicateGroups.Add((wastedSize, group.Value));
        }

        if (sortByWastedSize)
        {
            duplicateGroups.Sort((a, b) => b.wastedSize.CompareTo(a.wastedSize));
        }

        foreach ((long wastedSize, List<int> indexes) in duplicateGroups)
        {
            logger.Log($"#### Wasted {PrintUtils.ToHumanReadableSize(wastedSize)}:", ConsoleColor.Yellow);
            foreach (int index in indexes)
            {
                logger.Log("   " + _files[index].Path);
            }
        }

        logger.Log("------", ConsoleColor.Yellow);
        logger.Log($"Total size wasted: {PrintUtils.ToHumanReadableSize(totalSize)}.", ConsoleColor.Yellow);
        logger.Log("------", ConsoleColor.Cyan);
    }
}