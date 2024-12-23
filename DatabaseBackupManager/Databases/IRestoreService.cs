namespace DatabaseBackupManager.Configs;

public interface IRestoreService
{
    void RestoreDatabase(string backupFilePath);
}