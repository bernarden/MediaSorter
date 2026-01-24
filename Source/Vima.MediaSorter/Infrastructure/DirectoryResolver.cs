using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure;

public interface IDirectoryResolver
{
    DateTime? GetDateFromDirectoryName(string directoryName);
    string GetTargetFolderPath(
        DateTime date,
        string? subFolder,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping
    );
}

public class DirectoryResolver(MediaSorterOptions options) : IDirectoryResolver
{
    public DateTime? GetDateFromDirectoryName(string directoryName)
    {
        if (directoryName.Length < options.FolderNameFormat.Length)
            return null;

        string directoryNameBeginning = directoryName.Substring(0, options.FolderNameFormat.Length);
        return DateTime.TryParseExact(
            directoryNameBeginning,
            options.FolderNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime result
        )
            ? result
            : null;
    }

    public string GetTargetFolderPath(
        DateTime date,
        string? subFolder,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping
    )
    {
        if (!dateToExistingDirectoryMapping.TryGetValue(date.Date, out var folderName))
            folderName = date.ToString(options.FolderNameFormat, CultureInfo.InvariantCulture);

        var path = Path.Combine(options.Directory, folderName);
        return string.IsNullOrEmpty(subFolder) ? path : Path.Combine(path, subFolder);
    }
}
