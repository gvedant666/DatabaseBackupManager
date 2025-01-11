namespace DatabaseBackupManager.Configs;

public interface INotificationService
{
    void SendNotification(string message);
}