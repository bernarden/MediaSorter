namespace Vima.MediaSorter.Domain;

public class MediaFileWithDate(string filePath, CreatedOn createdOn) : MediaFile(filePath)
{
    public CreatedOn CreatedOn { get; private set; } = createdOn;

    public void SetCreatedOn(CreatedOn createdOn) => CreatedOn = createdOn;
}
