using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.Hashers;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public interface IFindDuplicatesHasherService
{
    Task<(ConcurrentDictionary<string, FindDuplicatesFile> Hashes, ConcurrentBag<(string Path, Exception Ex)> Errors)> GenerateHashes(List<string> allFiles, CancellationToken token = default);
}

public class FindDuplicatesHasherService(
    IFileHasher fileHasher,
    IEnumerable<IVisualFileHasher> visualHashers,
    IFindDuplicatesCacheService findDuplicatesCacheService,
    IFileSystem fileSystem,
    IOutputService outputService) : IFindDuplicatesHasherService
{
    private readonly (IVisualFileHasher Avg, IVisualFileHasher Diff, IVisualFileHasher Perc) _hashers = (
        Avg: visualHashers.First(x => x.Type == VisualHasherType.Average),
        Diff: visualHashers.First(x => x.Type == VisualHasherType.Difference),
        Perc: visualHashers.First(x => x.Type == VisualHasherType.Perceptual)
    );

    public async Task<(ConcurrentDictionary<string, FindDuplicatesFile> Hashes, ConcurrentBag<(string Path, Exception Ex)> Errors)> GenerateHashes(List<string> allFiles, CancellationToken token = default)
    {
        var cache = findDuplicatesCacheService.Load();
        var results = new ConcurrentDictionary<string, FindDuplicatesFile>();
        var errors = new ConcurrentBag<(string Path, Exception Ex)>();

        await findDuplicatesCacheService.StartWriterAsync(token);

        outputService.ExecuteWithProgress(
            "Generating hashes",
            p =>
            {
                int processed = 0;

                Parallel.ForEach(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = 25 },
                    path =>
                    {
                        try
                        {
                            var lastWrite = fileSystem.GetLastWriteTimeUtc(path);
                            long fileSize = fileSystem.GetFileSize(path);

                            if (
                                cache.TryGetValue(path, out var cachedEntry)
                                && cachedEntry.LastModified == lastWrite
                                && cachedEntry.Size == fileSize
                            )
                            {
                                results[path] = cachedEntry;
                                findDuplicatesCacheService.QueueToPersist(cachedEntry);
                            }
                            else
                            {
                                var file = CreateFindDuplicatesFile(path, fileSize, lastWrite);
                                findDuplicatesCacheService.QueueToPersist(file);
                                results[path] = file;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add((path, ex));
                        }

                        var current = Interlocked.Increment(ref processed);
                        if (current % 20 == 0)
                            p.Report((double)current / allFiles.Count);
                    }
                );
            }
        );

        await findDuplicatesCacheService.CommitAsync();

        return (results, errors);
    }

    private FindDuplicatesFile CreateFindDuplicatesFile(
        string path,
        long fileSize,
        DateTime lastWrite
    )
    {
        string bHash = fileHasher.GetHash(path);

        if (!FindDuplicatesConstants.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return new FindDuplicatesFile(path, bHash, 0, 0, 0, fileSize, 0, 0, lastWrite);
        }

        using var stream = fileSystem.CreateFileStream(path, FileMode.Open, FileAccess.Read);
        using var bitmap = SKBitmap.Decode(stream);

        if (bitmap == null)
        {
            return new FindDuplicatesFile(path, bHash, 0, 0, 0, fileSize, 0, 0, lastWrite);
        }

        return new FindDuplicatesFile(
            path,
            bHash,
            _hashers.Avg?.GetHash(bitmap) ?? 0,
            _hashers.Diff?.GetHash(bitmap) ?? 0,
            _hashers.Perc?.GetHash(bitmap) ?? 0,
            fileSize,
            bitmap.Width,
            bitmap.Height,
            lastWrite
        );
    }
}