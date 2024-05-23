using System;
using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaFile(string filePath, MediaFileType mediaMediaFileType)
{
    public string FilePath { get; } = filePath;
    public MediaFileType MediaMediaFileType { get; } = mediaMediaFileType;
    public List<string> RelatedFiles { get; } = [];
    public DateTime? CreatedOn { get; private set; }
    public CreatedOnSource? CreatedOnSource { get; private set; }

    public void SetCreatedOn(DateTime? createdOnTime, CreatedOnSource? createdOnSource)
    {
        CreatedOn = createdOnTime;
        CreatedOnSource = createdOnSource;
    }
}