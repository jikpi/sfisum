using System.Diagnostics;
using sfisum.Engine.Modes.Base;
using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine.Modes;

internal class ModeInstanceFastRefresh(string directoryPath, string digestPath)
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

        //Get dirty files (different metadata)

        List<(int, int)> dirtyFilesImap = new((int)((PrimaryFiles.Count + SecondaryFiles.Count) * 0.5f *
                                                    Constants.PredictedStructureCapacityPercentage));
        //(primary_id, secondary_id)

        foreach ((int primaryIndex, int secondaryIndex) in inBothImap)
        {
            FileInfo primaryFile = PrimaryFiles[primaryIndex];
            FileInfo secondaryFile = SecondaryFiles[secondaryIndex];

            if (primaryFile.Size != secondaryFile.Size ||
                IsDateTimeDifferent(secondaryFile.LastModified, primaryFile.LastModified))
            {
                dirtyFilesImap.Add((primaryIndex, secondaryIndex));
            }
        }

        //Add dirty and 'only in primary' files to the list of files to hash
        List<int> filesToBeHashedImap = new List<int>(onlyInPrimaryImap.Count + dirtyFilesImap.Count);
        filesToBeHashedImap.AddRange(onlyInPrimaryImap);
        filesToBeHashedImap.AddRange(dirtyFilesImap.Select(tuple => tuple.Item1));

        sw.Stop();
        Console.WriteLine(
            $"Compared files in {sw.ElapsedMilliseconds / 1000.0} seconds.");
        if (filesToBeHashedImap.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No files to hash.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"Hashing {filesToBeHashedImap.Count} files...");
        }

        sw.Restart();

        var hashResult = FileHasher.HashFiles(DirectoryPath, PrimaryFiles, filesToBeHashedImap);
        sw.Stop();

        if (!hashResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Hashing complete. Failed to hash {hashResult.ErrorImap.Count} files (Omitted).");
            //note: deal with scenario where unsuccessfully hashed files have a hash in secondary
        }
        else
        {
            if (filesToBeHashedImap.Count != 0)
            {
                Console.WriteLine("Hashing complete.");
            }
        }

        Console.ResetColor();

        Console.WriteLine($@"Time to hash: {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds):hh\:mm\:ss}");

        PrimaryHashes = hashResult.Hashes;
        UnhashableFilesImap = hashResult.ErrorImap;


        sw.Restart();
        Console.WriteLine("Comparing hashes...");
        //Categorize files with available metadata and hash information.
        foreach ((int primaryFileIndex, int secondaryFileIndex) in dirtyFilesImap)
        {
            FileInfo primaryFile = PrimaryFiles[primaryFileIndex];
            FileInfo secondaryFile = SecondaryFiles[secondaryFileIndex];

            if (PrimaryHashes[primaryFileIndex] is null)
            {
                throw new Exception("bug: primary file hash is null");
            }

            if (PrimaryHashes[primaryFileIndex] != SecondaryHashes[secondaryFileIndex])
            {
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
            else
            {
                DirtyValidFilesImap.Add(primaryFileIndex);
            }
        }

        //Cross compare the files that are only in primary and secondary
        Dictionary<Hash, List<int>> primaryNewFilesHashImap =
            new Dictionary<Hash, List<int>>(onlyInPrimaryImap.Count);
        foreach (int idx in onlyInPrimaryImap)
        {
            if (PrimaryHashes[idx] == null)
            {
                if (hashResult.ErrorImap.Contains(idx))
                {
                    //just ignore files that failed to hash
                    //there shouldn't ever be too many of these in this mode
                    continue;
                }

                throw new Exception("bug: primary file hash in crosscheck is null");
            }

            if (primaryNewFilesHashImap.TryGetValue((Hash)PrimaryHashes[idx]!, out List<int>? primarySet))
            {
                primarySet.Add(idx);
            }
            else
            {
                primaryNewFilesHashImap.Add((Hash)PrimaryHashes[idx]!, [idx]);
            }
        }

        Dictionary<Hash, List<int>> secondaryFilesHashImap =
            new Dictionary<Hash, List<int>>(SecondaryFiles.Count);
        for (int i = 0; i < SecondaryFiles.Count; i++)
        {
            //hash is never null

            if (secondaryFilesHashImap.TryGetValue(SecondaryHashes[i], out List<int>? secondarySet))
            {
                secondarySet.Add(i);
            }
            else
            {
                secondaryFilesHashImap.Add(SecondaryHashes[i], [i]);
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
                if (secondaryFilesHashImap.TryGetValue(SecondaryHashes[index], out List<int>? indexes))
                {
                    if (indexes.Count == 1)
                    {
                        CrosscheckSecondaryOrphansImap.Add(index);
                    }
                    else
                    {
                        CrosscheckSecondaryOrphanButDuplicateImap.Add(index);
                    }
                }
                else
                {
                    throw new Exception("bug: secondary file hash not found in secondary hash index");
                }
            }
        }

        foreach (int index in onlyInPrimaryImap)
        {
            if (!foundPrimaryIndexes.Contains(index))
            {
                CrosscheckPrimaryOrphansImap.Add(index);
            }
        }

        //Fill the rest

        foreach ((Hash hash, (List<int> primaryIndexes, List<int> _)) in CrosscheckPrimaryToSecondaryFoundImap)
        {
            foreach (int i in primaryIndexes)
            {
                PrimaryHashes[i] = hash;
            }
        }

        foreach ((int primaryIndex, int secondaryIndex) in inBothImap)
        {
            PrimaryHashes[primaryIndex] = SecondaryHashes[secondaryIndex];
        }

        //validation check

        int expectedHashesCount = hashResult.SuccessImap.Count + inBothImap.Count - dirtyFilesImap.Count;

        int nonNullHashes = 0;
        for (int i = 0; i < PrimaryHashes.Count; i++)
        {
            if (PrimaryHashes[i] is not null)
            {
                nonNullHashes++;
                continue;
            }


            //todo remove this after validation
            if (UnhashableFilesImap.Contains(i)) continue;
            Console.WriteLine($"Primary hash is null for file: {PrimaryFiles[i].Path}");
            throw new Exception("bug: primary hash is null");
        }

        if (nonNullHashes != expectedHashesCount)
        {
            throw new Exception("bug: nonNullHashes != expectedHashesCount");
        }

        TotalToSave = expectedHashesCount;

        sw.Stop();
        Console.WriteLine(
            $"Compared hashes in {sw.ElapsedMilliseconds / 1000.0} seconds.");
    }

    public override bool SaveDigest(string path)
    {
        return DigestFileManager.WriteDigestFile(path, ValidEntries(), TotalToSave);

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
        PrintRefreshLog(toConsole, true);
    }

    public override int GetEventCount()
    {
        return UnhashableFilesImap.Count
               + InvalidHashImap.Count
               + DirtyPotentiallyInvalidDateFilesImap.Count
               + DirtyPotentiallyInvalidSizeFilesImap.Count
               + DirtyPotentiallyInvalidSizeDateFilesImap.Count
               + DirtyValidFilesImap.Count
               + CrosscheckSecondaryOrphansImap.Count
               + CrosscheckPrimaryOrphansImap.Count
               + CrosscheckSecondaryOrphanButDuplicateImap.Count
               + CrosscheckPrimaryToSecondaryFoundImap.Count;
    }
}