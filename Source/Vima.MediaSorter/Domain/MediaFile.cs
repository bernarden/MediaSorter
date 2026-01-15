using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaFile(string filePath)
{
    public string FilePath { get; } = filePath;
    public List<string> RelatedFiles { get; } = [];
    public CreatedOn? CreatedOn { get; private set; }

    public void SetCreatedOn(CreatedOn? createdOn)
    {
        CreatedOn = createdOn;
    }
}