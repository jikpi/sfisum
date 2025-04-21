using System.Diagnostics;
using sfisum.Engine.Modes.Base;
using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine.Modes;

internal class ModeInstanceFullRefresh(string directoryPath, string digestPath)
    : ModeInstanceRefreshBase(directoryPath, digestPath)
{
    public override void Start()
    {
        Stopwatch sw = new Stopwatch();

        //Load digest file into secondary snap
        if (!Directory.Exists(DirectoryPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Directory does not exist: {DirectoryPath}");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("Loading digest file...");


        sw.Restart();
        if (!DigestFileManager.ReadDigestFile(DigestPath, out List<FileInfo> readFiles, out List<Hash> readHashes,
                out DigestGeneratedDate,
                out string? errorMessage))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to load digest file: {errorMessage}");
            Console.ResetColor();
            return;
        }

        SecondaryFiles = readFiles;
        SecondaryHashes = readHashes;

        if (SecondaryFiles.Count < 1)
        {
            throw new LocalException("No files found in digest file.");
        }

        sw.Stop();
        Console.WriteLine(
            $"Loaded digest file in {sw.ElapsedMilliseconds / 1000.0} seconds with {SecondaryFiles.Count} files.");
        Console.ResetColor();

        //Walk directory to primary snap
        Console.WriteLine("Walking directory...");

        sw.Restart();
        try
        {
            PrimaryFiles = FileInfo.WalkDirectory(DirectoryPath,
                out List<string> badFiles);
            sw.Stop();

            if (badFiles.Count > 0)
            {
                DirectoryInaccessibleFiles = badFiles;
            }
        }
        catch (Exception)
        {
            PrimaryFiles = [];
            DirectoryInaccessibleFiles = null;
            throw;
        }

        if (PrimaryFiles.Count == 0)
        {
            throw new LocalException("No files found in directory.");
        }

        if (DirectoryInaccessibleFiles is not null) Console.ForegroundColor = ConsoleColor.Yellow;

        Console.WriteLine(
            $"In directory found {PrimaryFiles.Count} files{(DirectoryInaccessibleFiles is not null ? $" and {DirectoryInaccessibleFiles.Count} inaccessible files (Omitted)" : "")} in {sw.ElapsedMilliseconds / 1000.0} seconds.");
        Console.ResetColor();

        sw.Restart();

        var hashResult = FileHasher.HashFiles(DirectoryPath, PrimaryFiles);
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

        PrimaryHashes = hashResult.Hashes;
        UnhashableFilesImap = hashResult.ErrorImap;

        sw.Restart();
        Console.WriteLine("Comparing files...");

        //Dictionary of Path->Index for Primary snap
        Dictionary<string, int> primaryPathsImap = new Dictionary<string, int>(PrimaryFiles.Count);
        for (int i = 0; i < PrimaryFiles.Count; i++)
        {
            primaryPathsImap.Add(PrimaryFiles[i].Path, i);
        }

        //Dictionary of Path->Index for secondary snap
        Dictionary<string, int> secondaryPathsImap = new Dictionary<string, int>(SecondaryFiles.Count);
        for (int i = 0; i < SecondaryFiles.Count; i++)
        {
            secondaryPathsImap.Add(SecondaryFiles[i].Path, i);
        }

        List<int> onlyInPrimaryImap =
            new((int)(PrimaryFiles.Count * Constants.PredictedStructureCapacityPercentage));
        List<int> onlyInSecondaryImap =
            new((int)(SecondaryFiles.Count * Constants.PredictedStructureCapacityPercentage));
        List<(int, int)> inBothImap =
            new(Math.Min(PrimaryFiles.Count, SecondaryFiles.Count)); //(primary_id, secondary_id)

        foreach (KeyValuePair<string, int> primaryEntry in primaryPathsImap)
        {
            if (secondaryPathsImap.TryGetValue(primaryEntry.Key, out int secondaryValue))
            {
                inBothImap.Add((primaryEntry.Value, secondaryValue));
            }
            else
            {
                onlyInPrimaryImap.Add(primaryEntry.Value);
            }
        }

        foreach (KeyValuePair<string, int> secondaryEntry in secondaryPathsImap)
        {
            if (!primaryPathsImap.ContainsKey(secondaryEntry.Key))
            {
                onlyInSecondaryImap.Add(secondaryEntry.Value);
            }
        }

        //Get dirty files (different hash)

        List<(int, int)> dirtyFilesImap = new((int)((PrimaryFiles.Count + SecondaryFiles.Count) * 0.5f *
                                                     Constants.PredictedStructureCapacityPercentage));
        //(primary_id, secondary_id)

        foreach ((int primaryIndex, int secondaryIndex) in inBothImap)
        {
            if (PrimaryHashes[primaryIndex] != SecondaryHashes[secondaryIndex])
            {
                dirtyFilesImap.Add((primaryIndex, secondaryIndex));
            }
        }

        sw.Stop();
        Console.WriteLine(
            $"Compared files in {sw.ElapsedMilliseconds / 1000.0} seconds.");

        sw.Restart();
        Console.WriteLine("Comparing hashes...");
        //Categorize files with available metadata.
        foreach ((int primaryFileIndex, int secondaryFileIndex) in dirtyFilesImap)
        {
            FileInfo primaryFile = PrimaryFiles[primaryFileIndex];
            FileInfo secondaryFile = SecondaryFiles[secondaryFileIndex];

            //both date and size are the same
            if (primaryFile.Size == secondaryFile.Size &&
                (!IsDateTimeDifferent(primaryFile.LastModified, secondaryFile.LastModified)))
            {
                InvalidHashImap.Add(primaryFileIndex);
            } //only the size is different
            else if (primaryFile.Size != secondaryFile.Size &&
                     (!IsDateTimeDifferent(primaryFile.LastModified, secondaryFile.LastModified)))
            {
                DirtyPotentiallyInvalidSizeFilesImap.Add(primaryFileIndex);
            } //only the date is different
            else if (primaryFile.Size == secondaryFile.Size &&
                     (IsDateTimeDifferent(primaryFile.LastModified, secondaryFile.LastModified)))
            {
                DirtyPotentiallyInvalidDateFilesImap.Add(primaryFileIndex);
            } //both date and size are different
            else
            {
                DirtyPotentiallyInvalidSizeDateFilesImap.Add(primaryFileIndex);
            }
        }

        //Cross compare the files that are only in primary and secondary
        Dictionary<Hash, List<int>> primaryNewFilesHashImap =
            new Dictionary<Hash, List<int>>(onlyInPrimaryImap.Count);
        for (int primaryIndex = 0; primaryIndex < PrimaryFiles.Count; primaryIndex++)
        {
            if (PrimaryHashes[primaryIndex] == null)
            {
                if (hashResult.ErrorImap.Contains(primaryIndex))
                {
                    //just ignore files that failed to hash
                    //there shouldn't ever be too many of these in this mode
                    continue;
                }

                throw new Exception("bug: primary file hash in crosscheck is null");
            }

            if (primaryNewFilesHashImap.TryGetValue((Hash)PrimaryHashes[primaryIndex]!, out List<int>? primarySet))
            {
                primarySet.Add(primaryIndex);
            }
            else
            {
                primaryNewFilesHashImap.Add((Hash)PrimaryHashes[primaryIndex]!, [primaryIndex]);
            }
        }

        HashSet<int> foundPrimaryIndexes =
            new((int)(PrimaryFiles.Count * Constants.PredictedStructureCapacityPercentage));

        foreach (int index in onlyInSecondaryImap)
        {
            if (primaryNewFilesHashImap.TryGetValue(SecondaryHashes[index], out List<int>? primaryIndexes))
            {
                //Update found_primary_indexes
                foundPrimaryIndexes.UnionWith(primaryIndexes);

                if (CrosscheckPrimaryToSecondaryFoundImap.TryGetValue(SecondaryHashes[index],
                        out (List<int>, List<int>) tuple))
                {
                    tuple.Item2.Add(index);
                }
                else
                {
                    CrosscheckPrimaryToSecondaryFoundImap.Add(SecondaryHashes[index],
                        (primaryIndexes, [index]));
                    //NOTE: does not clone primary_indexes
                }
            }
            else
            {
                CrosscheckSecondaryOrphansImap.Add(index);
            }
        }

        foreach (int index in onlyInPrimaryImap)
        {
            if (!foundPrimaryIndexes.Contains(index))
            {
                CrosscheckPrimaryOrphansImap.Add(index);
            }
        }

        //validation check
        //todo this is also redundant
        int nonNullHashes = 0;
        for (int i = 0; i < PrimaryHashes.Count; i++)
        {
            if (PrimaryHashes[i] is not null)
            {
                nonNullHashes++;
                continue;
            }


            //todo remove this
            if (UnhashableFilesImap.Contains(i)) continue;
            Console.WriteLine($"Primary hash is null for file: {PrimaryFiles[i].Path}");
            throw new Exception("bug: primary hash is null");
        }

        if (nonNullHashes != hashResult.SuccessImap.Count)
        {
            throw new Exception("bug: non_null_hashes != hash_result.success_index.Count");
        }

        TotalToSave = nonNullHashes;

        sw.Stop();
        Console.WriteLine(
            $"Compared hashes in {sw.ElapsedMilliseconds / 1000.0} seconds.");
    }

    public override bool SaveDigest(string path, string? digestFilenamePrefix)
    {
        return DigestFileManager.WriteDigestFile(path, ValidEntries(), TotalToSave, digestFilenamePrefix);

        IEnumerable<(FileInfo File, Hash Hash)> ValidEntries()
        {
            for (int i = 0; i < PrimaryHashes.Count; i++)
            {
                if (PrimaryHashes[i].HasValue)
                {
                    yield return (PrimaryFiles[i], PrimaryHashes[i]!.Value);
                }
            }
        }
    }

    public override void PrintGeneralEvents(bool toConsole)
    {
        PrintRefreshLog(toConsole, false);
    }

    public override int GetEventCount()
    {
        return UnhashableFilesImap.Count +
               InvalidHashImap.Count +
               DirtyPotentiallyInvalidDateFilesImap.Count +
               DirtyPotentiallyInvalidSizeFilesImap.Count +
               DirtyPotentiallyInvalidSizeDateFilesImap.Count +
               CrosscheckSecondaryOrphansImap.Count +
               CrosscheckPrimaryOrphansImap.Count +
               CrosscheckPrimaryToSecondaryFoundImap.Count;
    }
}