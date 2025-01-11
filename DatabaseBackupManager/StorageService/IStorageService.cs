namespace DatabaseBackupManager.Configs;

public interface IStorageService
{
    void SaveBackup(string sourceFilePath, string destinationPath);
    void LoadBackup(string backupFilePath, string destinationPath);
}