using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public interface IFindDuplicatesCacheService
{
    IDictionary<string, FindDuplicatesFile> Load();

    Task StartWriterAsync(CancellationToken token = default);

    void QueueToPersist(FindDuplicatesFile file);

    Task CommitAsync();
}

public class FindDuplicatesCacheService(
    IFileSystem fileSystem,
    IOptions<MediaSorterOptions> options,
    IOutputService outputService
) : IFindDuplicatesCacheService
{
    private readonly string _cachePath = Path.Combine(
        options.Value.Directory,
        "Vima.MediaSorter.Hashes.jsonl"
    );
    private readonly string _tempCachePath = Path.Combine(
        options.Value.Directory,
        "Vima.MediaSorter.Hashes.jsonl.tmp"
    );

    private Channel<FindDuplicatesFile>? _channel;
    private Task? _writerTask;

    public IDictionary<string, FindDuplicatesFile> Load()
    {
        var cache = new Dictionary<string, FindDuplicatesFile>();
        if (!fileSystem.FileExists(_cachePath))
            return cache;

        try
        {
            using var stream = fileSystem.CreateFileStream(
                _cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(
                        line,
                        SourceGenerationContext.Default.FindDuplicatesFile
                    );
                    if (entry != null)
                    {
                        cache[entry.Path] = entry;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            outputService.WriteLine($"Cache load failed: {ex.Message}", OutputLevel.Error);
        }
        return cache;
    }

    public Task StartWriterAsync(CancellationToken token = default)
    {
        _channel = Channel.CreateUnbounded<FindDuplicatesFile>(
            new UnboundedChannelOptions { SingleReader = true }
        );

        _writerTask = Task.Run(
            async () =>
            {
                int batchSize = 100;
                var batch = new List<FindDuplicatesFile>(batchSize);
                using var stream = fileSystem.CreateFileStream(
                    _tempCachePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read
                );
                using var writer = new StreamWriter(stream);

                await foreach (var entry in _channel.Reader.ReadAllAsync())
                {
                    batch.Add(entry);

                    if (batch.Count >= batchSize)
                    {
                        batch.ForEach(item =>
                            writer.WriteLine(
                                JsonSerializer.Serialize(
                                    item,
                                    SourceGenerationContext.Default.FindDuplicatesFile
                                )
                            )
                        );
                        await writer.FlushAsync();
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    batch.ForEach(item =>
                        writer.WriteLine(
                            JsonSerializer.Serialize(
                                item,
                                SourceGenerationContext.Default.FindDuplicatesFile
                            )
                        )
                    );
                    await writer.FlushAsync();
                    batch.Clear();
                }
            },
            token
        );

        return Task.CompletedTask;
    }

    public void QueueToPersist(FindDuplicatesFile file) => _channel?.Writer.TryWrite(file);

    public async Task CommitAsync()
    {
        _channel?.Writer.Complete();
        if (_writerTask != null)
            await _writerTask;

        if (fileSystem.FileExists(_tempCachePath))
        {
            if (fileSystem.FileExists(_cachePath))
                fileSystem.DeleteFile(_cachePath);
            fileSystem.MoveFile(_tempCachePath, _cachePath);
        }
    }
}
