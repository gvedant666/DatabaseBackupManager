namespace DatabaseBackupManager.Configs;

public interface IBackupService
{
    void CreateBackup(string backupFilePath);
}