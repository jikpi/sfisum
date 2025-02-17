using sfisum.Engine.Modes.Utils;
using sfisum.FileRep;
using sfisum.Utils;

namespace sfisum.Engine.Modes.Base;

using FileInfo = FileRep.FileInfo;

internal abstract class ModeInstanceRefreshBase(string directoryPath, string digestPath) : ModeInstanceBase
{
    protected string DirectoryPath { get; } = directoryPath;
    protected string DigestPath { get; } = digestPath;
    protected List<string>? DirectoryInaccessibleFiles { get; set; }

    protected List<FileInfo> PrimaryFiles = [];
    protected List<Hash?> PrimaryHashes = [];

    protected List<FileInfo> SecondaryFiles = [];
    protected List<Hash> SecondaryHashes = [];

    protected DateTime DigestGeneratedDate;

    protected List<int> UnhashableFilesImap { get; set; } = [];

    protected readonly List<int> InvalidHashImap = [];
    protected readonly List<int> DirtyPotentiallyInvalidSizeFilesImap = [];
    protected readonly List<int> DirtyPotentiallyInvalidDateFilesImap = [];
    protected readonly List<int> DirtyPotentiallyInvalidSizeDateFilesImap = [];
    protected readonly List<int> DirtyValidFilesImap = [];
    protected readonly Dictionary<Hash, (List<int>, List<int>)> CrosscheckPrimaryToSecondaryFoundImap = new();
    protected readonly List<int> CrosscheckSecondaryOrphansImap = [];
    protected readonly List<int> CrosscheckPrimaryOrphansImap = [];
    protected readonly List<int> CrosscheckSecondaryOrphanButDuplicateImap = [];

    public int TotalToSave { get; protected set; }


    protected static bool IsDateTimeDifferent(DateTime first, DateTime second)
    {
        return Math.Abs((first - second).TotalSeconds) >= 1;
    }

    protected void PrintRefreshLog(bool toConsole, bool fastRefresh)
    {
        if (!Glob.Config.PrintToLog && !toConsole) return;

        using Logger logger = new Logger(toConsole, Glob.Config.PrintToLog);
        logger.Log(PrintUtils.ReportHeader("Report"), ConsoleColor.Cyan);
        logger.LogFileOnly($"Mode: {(fastRefresh ? "Fast refresh" : "Full refresh")}");

        int ok = CrosscheckPrimaryToSecondaryFoundImap.Count + DirtyValidFilesImap.Count;

        int warning = DirtyPotentiallyInvalidDateFilesImap.Count +
                      DirtyPotentiallyInvalidSizeFilesImap.Count +
                      DirtyPotentiallyInvalidSizeDateFilesImap.Count +
                      //todo remove these 2 after validation
                      CrosscheckSecondaryOrphansImap.Count +
                      CrosscheckPrimaryOrphansImap.Count;

        //todo: add this after validation
        //if (fastRefresh) warning += crosscheck_secondary_orphan_but_duplicate_index.Count;

        int error = InvalidHashImap.Count + UnhashableFilesImap.Count;

        int onlyInPrimary = CrosscheckPrimaryOrphansImap.Count;
        int onlyInSecondary = CrosscheckSecondaryOrphansImap.Count;

        logger.Log(
            $"Out of {PrimaryFiles.Count} files on disk and {SecondaryFiles.Count} files in the digest file, {onlyInPrimary} files could only be found on disk and {onlyInSecondary} files could only be found in the digest file.",
            ConsoleColor.Cyan);

        if (!fastRefresh)
        {
            int invalid = InvalidHashImap.Count +
                          DirtyPotentiallyInvalidDateFilesImap.Count +
                          DirtyPotentiallyInvalidSizeFilesImap.Count +
                          DirtyPotentiallyInvalidSizeDateFilesImap.Count;

            Console.ForegroundColor = ConsoleColor.Cyan;
            logger.Log($"{invalid} out of {PrimaryFiles.Count} have a different hash.");
        }

        logger.Log($"There are {ok} successful operations, {warning} warnings and {error} errors.",
            ConsoleColor.Cyan);
        logger.Log(PrintUtils.Separator(), ConsoleColor.Cyan);
        logger.Log(PrintUtils.Separator(), ConsoleColor.Cyan);

        if (error > 0)
        {
            logger.Log("Errors:", ConsoleColor.Red);
            logger.Log(PrintUtils.Separator(), ConsoleColor.Red);
        }

        //print all files that failed to hash
        if (UnhashableFilesImap.Count > 0)
        {
            logger.Log(PrintUtils.SmallHeader("Could not hash these files:"), ConsoleColor.Red);

            foreach (int index in UnhashableFilesImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Red);

            logger.Log(PrintUtils.Separator(), ConsoleColor.Cyan);

            logger.Log(
                "APPLICATION WARNING: THIS REPORT IS NOT VALID DUE TO INACCESSIBLE FILES (THOUGH THEY ARE ONLY EXCLUDED FROM THE DIGEST THAT WILL BE SAVED)",
                ConsoleColor.Cyan);
            logger.Log(PrintUtils.Separator(), ConsoleColor.Cyan);
        }

        //print all files that have invalid hashes
        if (InvalidHashImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({InvalidHashImap.Count}) Files that have invalid hashes and identical size and last modified date:"),
                ConsoleColor.Red);

            foreach (int index in InvalidHashImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Red);
        }

        if (warning > 0)
        {
            logger.Log("Warnings:", ConsoleColor.Yellow);
            logger.Log(PrintUtils.Separator(), ConsoleColor.Yellow);
        }

        //print all files are potentially invalid due to different size
        if (DirtyPotentiallyInvalidSizeFilesImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({DirtyPotentiallyInvalidSizeFilesImap.Count}) Files that have different size and hash: (High priority warning)"),
                ConsoleColor.Yellow);

            foreach (int index in DirtyPotentiallyInvalidSizeFilesImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Yellow);
        }

        //print all the files that are potentially invalid due to different date
        if (DirtyPotentiallyInvalidDateFilesImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({DirtyPotentiallyInvalidDateFilesImap.Count}) Files that have different last modified date and hash: (Medium priority warning)"),
                ConsoleColor.Yellow);

            foreach (int index in DirtyPotentiallyInvalidDateFilesImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Yellow);
        }

        //print all the files that are potentially invalid due to different size and date
        if (DirtyPotentiallyInvalidSizeDateFilesImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({DirtyPotentiallyInvalidSizeDateFilesImap.Count}) Files that have different size, last modified date and hash: (Low priority warning)"),
                ConsoleColor.Yellow);

            foreach (int index in DirtyPotentiallyInvalidSizeDateFilesImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Yellow);
        }

        if (CrosscheckSecondaryOrphansImap.Count + CrosscheckPrimaryOrphansImap.Count +
            CrosscheckSecondaryOrphanButDuplicateImap.Count > 0)
        {
            logger.Log("Unmatched files:", ConsoleColor.Magenta);
            logger.Log(PrintUtils.Separator(), ConsoleColor.Magenta);
        }

        //print all the files that couldn't be crosschecked, only in secondary
        if (CrosscheckSecondaryOrphansImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({CrosscheckSecondaryOrphansImap.Count}) Files that were only found in the digest file."),
                ConsoleColor.Magenta);

            foreach (int index in CrosscheckSecondaryOrphansImap)
            {
                logger.LogFileOnly("   " + SecondaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Magenta);
        }

        //print all the files that couldn't be crosschecked, only in primary
        if (CrosscheckPrimaryOrphansImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({CrosscheckPrimaryOrphansImap.Count}) Files that were only found on disk."),
                ConsoleColor.Magenta);

            foreach (int index in CrosscheckPrimaryOrphansImap)
            {
                logger.LogFileOnly("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Magenta);
        }

        //print all the duplicate secondary orphans (only in fast refresh mode)
        if (fastRefresh && CrosscheckSecondaryOrphanButDuplicateImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({CrosscheckSecondaryOrphanButDuplicateImap.Count}) Files that were only found in the digest file and have duplicates in it:"),
                ConsoleColor.Magenta);

            foreach (int index in CrosscheckSecondaryOrphanButDuplicateImap)
            {
                logger.Log("   " + SecondaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Magenta);
        }

        if (ok > 0)
        {
            logger.Log("Successes:", ConsoleColor.Green);
            logger.Log(PrintUtils.Separator(), ConsoleColor.Green);
        }

        //print all the files that are validated
        if (fastRefresh && DirtyValidFilesImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({DirtyValidFilesImap.Count}) Files that have different size or last modified date, but identical hashes:"),
                ConsoleColor.Green);

            foreach (int index in DirtyValidFilesImap)
            {
                logger.Log("   " + PrimaryFiles[index].Path);
            }

            logger.Log(PrintUtils.Separator(), ConsoleColor.Green);
        }

        //print all the files that are crosschecked
        if (CrosscheckPrimaryToSecondaryFoundImap.Count > 0)
        {
            logger.Log(
                PrintUtils.SmallHeader(
                    $"({CrosscheckPrimaryToSecondaryFoundImap.Count}) Files from digest that dont exist on disk but were found elsewhere on disk:"),
                ConsoleColor.Green);

            foreach ((Hash _, (List<int> primaryIndexes, List<int> secondaryIndexes)) in
                     CrosscheckPrimaryToSecondaryFoundImap)
            {
                logger.Log("------", ConsoleColor.Green);
                logger.Log("On disk:", ConsoleColor.Green);
                foreach (int index in primaryIndexes)
                {
                    logger.Log("   " + PrimaryFiles[index].Path);
                }

                logger.Log("@ V Only in digest:", ConsoleColor.Green);

                foreach (int index in secondaryIndexes)
                {
                    logger.Log("   " + SecondaryFiles[index].Path);
                }
            }
        }

        logger.Log("------", ConsoleColor.Cyan);
        logger.Log("Unmatched files similarity:", ConsoleColor.Cyan);
        logger.LogConsoleOnly("Calculating...", ConsoleColor.Cyan);

        var matches =
            FindPotentialFileMatches(out HashSet<int> matchedPrimaryImap, out HashSet<int> matchedSecondaryImap);

        logger.Log(PrintUtils.SmallHeader(
                $"Found {matches.Count} potential matches between orphaned files:"),
            ConsoleColor.Green);

        foreach (var match in matches)
        {
            var primary = PrimaryFiles[match.PrimaryIndex];
            var secondary = SecondaryFiles[match.SecondaryIndex];

            logger.Log($"\nPotential match (confidence: {match.Confidence:P}):", ConsoleColor.Green);
            logger.Log($"  Disk:   {primary.Path}");
            logger.Log($"  Digest: {secondary.Path}");
            logger.Log("  Reasons:");
            foreach (var reason in match.MatchReasons)
            {
                logger.Log($"    - {reason}");
            }
        }

        if (matchedPrimaryImap.Count - CrosscheckPrimaryOrphansImap.Count != 0)
        {
            logger.Log("\n" + PrintUtils.SmallHeader(
                    $"({CrosscheckPrimaryOrphansImap.Count - matchedPrimaryImap.Count}) Unmatched files found only on disk:"),
                ConsoleColor.Magenta);

            var unmatchedPrimaryImap = CrosscheckPrimaryOrphansImap.Except(matchedPrimaryImap);

            foreach (var primaryIndex in unmatchedPrimaryImap)
            {
                logger.Log($"  {PrimaryFiles[primaryIndex].Path}");
            }
        }

        if (matchedSecondaryImap.Count - CrosscheckSecondaryOrphansImap.Count != 0)
        {
            logger.Log("\n" + PrintUtils.SmallHeader(
                    $"({CrosscheckSecondaryOrphansImap.Count - matchedSecondaryImap.Count}) Unmatched files found only in digest:"),
                ConsoleColor.Magenta);

            var unmatchedSecondaryImap = CrosscheckSecondaryOrphansImap.Except(matchedSecondaryImap);

            foreach (var secondaryIndex in unmatchedSecondaryImap)
            {
                logger.Log($"  {SecondaryFiles[secondaryIndex].Path}");
            }
        }

        logger.Log(PrintUtils.Separator(), ConsoleColor.Cyan);
    }

    public abstract int GetEventCount();

    private record PotentialMatch
    {
        public int PrimaryIndex { get; init; }
        public int SecondaryIndex { get; init; }
        public double Confidence { get; init; }
        public List<string> MatchReasons { get; init; } = [];
    }

    private List<PotentialMatch> FindPotentialFileMatches(out HashSet<int> matchedPrimaryImap,
        out HashSet<int> matchedSecondaryImap)
    {
        var matches = new List<PotentialMatch>();

        matchedPrimaryImap = [];
        matchedSecondaryImap = [];

        foreach (var primaryIndex in CrosscheckPrimaryOrphansImap)
        {
            foreach (var secondaryIndex in CrosscheckSecondaryOrphansImap)
            {
                double confidence = 0.0;
                List<string> reasons = [];

                //compare file name without path
                string primaryName = Path.GetFileName(PrimaryFiles[primaryIndex].Path);
                string secondaryName = Path.GetFileName(SecondaryFiles[secondaryIndex].Path);

                if (primaryName == secondaryName)
                {
                    const double conf = 0.5;
                    confidence += conf;
                    reasons.Add($"Exact filename match (+{conf:P})");
                }
                else
                {
                    // Compare name similarity using Levenshtein distance
                    var similarity = CalculateLevenshteinSimilarity(primaryName, secondaryName);
                    if (similarity > 0.8)
                    {
                        var conf = 0.3;
                        confidence += conf;
                        reasons.Add($"Similar filename (similarity: {similarity:P}) (+{conf:P})");
                    }
                }

                //compare size
                long primarySize = PrimaryFiles[primaryIndex].Size;
                long secondarySize = SecondaryFiles[secondaryIndex].Size;

                long sizeDiff = Math.Abs(primarySize - secondarySize);
                long sizeRatio = Math.Min(primarySize, secondarySize) /
                                 Math.Max(primarySize, secondarySize);

                if (sizeDiff == 0)
                {
                    const double conf = 0.3;
                    confidence += conf;
                    reasons.Add($"Identical size (+{conf:P})");
                }
                else if (sizeRatio > 0.95)
                {
                    const double conf = 0.15;
                    confidence += conf;
                    reasons.Add($"Similar size (ratio: {sizeRatio:P}) (+{conf:P})");
                }

                //compare extensions
                string primaryExt = Path.GetExtension(PrimaryFiles[primaryIndex].Path);
                string secondaryExt = Path.GetExtension(SecondaryFiles[secondaryIndex].Path);

                if (primaryExt == secondaryExt)
                {
                    const double conf = 0.2;
                    confidence += conf;
                    reasons.Add($"Same file extension (+{conf:P})");
                }

                //compare parent dir name
                string? primaryDir = Path.GetDirectoryName(PrimaryFiles[primaryIndex].Path);
                string? secondaryDir = Path.GetDirectoryName(SecondaryFiles[secondaryIndex].Path);

                if (primaryDir == secondaryDir)
                {
                    const double conf = 0.3;
                    confidence += conf;
                    reasons.Add($"Same parent directory (+{conf:P})");
                }
                else if (primaryDir != null && secondaryDir != null)
                {
                    double dirSimilarity = CalculateLevenshteinSimilarity(primaryDir, secondaryDir);
                    if (dirSimilarity > 0.7)
                    {
                        const double conf = 0.15;
                        confidence += conf;
                        reasons.Add($"Similar parent directory (similarity: {dirSimilarity:P}) (+{conf:P})");
                    }
                }

                //compare last modified date to digest creation date
                if (PrimaryFiles[primaryIndex].LastModified > DigestGeneratedDate)
                {
                    const double reductionFactor = 0.8;
                    confidence *= reductionFactor;
                    reasons.Add($"File is newer than digest date (x{reductionFactor:F2})");
                }

                if (confidence > 0.4)
                {
                    matchedPrimaryImap.Add(primaryIndex);
                    matchedSecondaryImap.Add(secondaryIndex);

                    matches.Add(new PotentialMatch
                    {
                        PrimaryIndex = primaryIndex,
                        SecondaryIndex = secondaryIndex,
                        Confidence = confidence,
                        MatchReasons = reasons
                    });
                }
            }
        }

        return matches.OrderByDescending(m => m.Confidence).ToList();
    }

    private static double CalculateLevenshteinSimilarity(string s1, string s2)
    {
        var distance = CalculateLevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        return 1 - ((double)distance / maxLength);
    }

    private static int CalculateLevenshteinDistance(string s1, string s2)
    {
        var distances = new int[s1.Length + 1, s2.Length + 1];

        for (var i = 0; i <= s1.Length; i++)
            distances[i, 0] = i;
        for (var j = 0; j <= s2.Length; j++)
            distances[0, j] = j;

        for (var i = 1; i <= s1.Length; i++)
        for (var j = 1; j <= s2.Length; j++)
        {
            var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
            distances[i, j] = Math.Min(
                Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                distances[i - 1, j - 1] + cost);
        }

        return distances[s1.Length, s2.Length];
    }
}