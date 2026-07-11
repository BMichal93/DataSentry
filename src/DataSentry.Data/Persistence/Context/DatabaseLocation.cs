using System.IO;

namespace DataSentry.Data.Persistence.Context;

/// <summary>
/// Where the database file lives. It ships with the app rather than with a server, so there is
/// nothing for the user to install and no connection string to configure.
/// </summary>
public static class DatabaseLocation
{
    private const string DatabaseFileName = "datasentry.db";

    /// <summary>%AppData%/DataSentry/datasentry.db. Creates the folder if it is not there yet.</summary>
    public static string GetDefaultDatabasePath()
    {
        string applicationDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataSentry");

        Directory.CreateDirectory(applicationDataFolder);

        return Path.Combine(applicationDataFolder, DatabaseFileName);
    }

    public static string ToConnectionString(string databasePath) => $"Data Source={databasePath}";
}
