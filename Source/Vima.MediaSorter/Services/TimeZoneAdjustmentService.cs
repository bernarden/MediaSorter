using System;
using System.Collections.Generic;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface ITimeZoneAdjustmentService
{
    IReadOnlyList<string> ApplyOffsetsIfNeeded(IEnumerable<MediaFileWithDate> files);
}

public class TimeZoneAdjustmentService : ITimeZoneAdjustmentService
{
    public IReadOnlyList<string> ApplyOffsetsIfNeeded(IEnumerable<MediaFileWithDate> files)
    {
        var result = new List<string>();
        var targetFiles = files
            .Where(f => f.CreatedOn.Source == CreatedOnSource.MetadataUtc)
            .ToList();

        if (targetFiles.Count == 0) return result;

        TimeSpan offset = ConsoleHelper.GetVideoUtcOffsetFromUser();

        foreach (var file in targetFiles)
        {
            var adjustedDate = file.CreatedOn.Date + offset;
            file.SetCreatedOn(new CreatedOn(adjustedDate, CreatedOnSource.MetadataLocal));
            result.Add(file.FilePath);
        }
        return result;
    }
}
