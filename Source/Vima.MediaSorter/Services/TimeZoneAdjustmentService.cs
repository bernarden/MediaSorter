using System;
using System.Collections.Generic;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface ITimeZoneAdjustmentService
{
    void ApplyOffsetsIfNeeded(IEnumerable<MediaFile> files);
}

public class TimeZoneAdjustmentService : ITimeZoneAdjustmentService
{
    public void ApplyOffsetsIfNeeded(IEnumerable<MediaFile> files)
    {
        var targetFiles = files
            .Where(f => f.CreatedOn?.Source == CreatedOnSource.MetadataUtc)
            .ToList();

        if (targetFiles.Count == 0) return;

        TimeSpan offset = ConsoleHelper.GetVideoUtcOffsetFromUser();

        foreach (var file in targetFiles)
        {
            var adjustedDate = file.CreatedOn!.Date + offset;
            file.SetCreatedOn(new CreatedOn(adjustedDate, CreatedOnSource.MetadataLocal));
        }
    }
}
