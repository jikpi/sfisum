using sfisum.FileRep;
using sfisum.Utils;
using FileInfo = sfisum.FileRep.FileInfo;

namespace sfisum.Engine;

internal static class FileHasher
{
    private class IndexIterator
    {
        private readonly int _totalCount;
        private readonly IReadOnlyList<int>? _indexes;

        public IndexIterator(int totalCount, IReadOnlyList<int>? indexes = null)
        {
            _totalCount = totalCount;
            _indexes = indexes;

            if (_indexes != null)
            {
                if (_indexes.Any(i => i < 0 || i >= _totalCount))
                {
                    throw new ArgumentException("Index out of bounds", nameof(indexes));
                }
            }
        }

        public IEnumerable<int> GetIndexes()
        {
            if (_indexes == null)
            {
                for (int i = 0; i < _totalCount; i++)
                {
                    yield return i;
                }
            }
            else
            {
                foreach (int index in _indexes)
                {
                    yield return index;
                }
            }
        }
    }

    public record HashResult(
        bool Success,
        List<Hash?> Hashes,
        List<int> SuccessImap,
        List<int> ErrorImap
    );

    private static void SetCursorPosition(int originalLeft, int originalTop)
    {
        if (originalLeft >= 0 && originalTop >= 0 &&
            originalLeft < Console.BufferWidth &&
            originalTop < Console.BufferHeight)
        {
            Console.SetCursorPosition(originalLeft, originalTop);
        }
    }

    private static volatile int _processedFiles;

    public static HashResult HashFiles(
        string basePath,
        IReadOnlyList<FileInfo> inputFiles,
        IReadOnlyList<int>? indexesToHash = null,
        CancellationToken inputCancellationToken = default)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Base path cannot be null or empty", nameof(basePath));
        ArgumentNullException.ThrowIfNull(inputFiles);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(inputCancellationToken);
        var cancellationToken = cts.Token;

        List<Hash?> hashes = new Hash?[inputFiles.Count].ToList();
        List<int> successIndex = [];
        List<int> errorIndex = [];

        int originalLeft = Console.CursorLeft;
        int originalTop = Console.CursorTop;
        _processedFiles = 0;

        var iterator = new IndexIterator(inputFiles.Count, indexesToHash);
        int totalToProcess = indexesToHash?.Count ?? inputFiles.Count;

        bool isCancellationHandled = false;

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            if (isCancellationHandled) return;

            e.Cancel = true;
            SetCursorPosition(originalLeft, originalTop);
            Console.Write(new string(' ', 50));
            SetCursorPosition(originalLeft, originalTop);
            Console.WriteLine($"Cancel at {_processedFiles}/{totalToProcess} files...");

            try
            {
                if (cts is { IsCancellationRequested: false, Token.IsCancellationRequested: false })
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            isCancellationHandled = true;
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            var progressTask = Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        SetCursorPosition(originalLeft, originalTop);

                        float percentage = (float)_processedFiles / totalToProcess;
                        int barLength = 16;
                        int filledLength = (int)(barLength * percentage);

                        string progressBar = "[" + new string('#', filledLength) +
                                             new string(' ', barLength - filledLength) + "]";

                        Console.Write(
                            $"Progress: {progressBar} {(_processedFiles * 100 / totalToProcess):0}% ({_processedFiles}/{totalToProcess})"
                                .PadRight(50));

                        await Task.Delay(5000, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, cancellationToken);

            foreach (int i in iterator.GetIndexes())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new LocalException("File hashing operation was cancelled before completion");
                }

                try
                {
                    string fullPath = Path.Combine(basePath, inputFiles[i].Path);
                    hashes[i] = new Hash(fullPath);
                    successIndex.Add(i);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    hashes[i] = null;
                    errorIndex.Add(i);
                }
                finally
                {
                    Interlocked.Increment(ref _processedFiles);
                }
            }

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }

                progressTask.Wait(inputCancellationToken);
            }
            catch (AggregateException)
            {
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;

            if (!isCancellationHandled)
            {
                SetCursorPosition(originalLeft, originalTop);
                Console.Write(new string(' ', 50));
                SetCursorPosition(originalLeft, originalTop);
            }
        }

        if (hashes.Count != inputFiles.Count)
        {
            throw new LocalException("bug: Hashes count does not match input files count");
        }

        if (indexesToHash is not null)
        {
            if (successIndex.Count + errorIndex.Count != indexesToHash.Count)
            {
                throw new LocalException("bug: Hashes with specific indexes do not add up");
            }
        }
        else
        {
            if (successIndex.Count + errorIndex.Count != inputFiles.Count)
            {
                throw new LocalException("bug: Hashes with no specific indexes do not add up");
            }
        }

        return new HashResult(
            Success: errorIndex.Count == 0,
            Hashes: hashes,
            SuccessImap: successIndex,
            ErrorImap: errorIndex
        );
    }
}