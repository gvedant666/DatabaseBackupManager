using System.Data;
using DatabaseBackupManager.Configs;
using Google.Apis.Storage.v1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var parser = new CommandLineParser(args);

if (!parser.IsValid())
{
    return;
}

string command = parser.GetCommand();
string configPath = parser.GetOption("--config");

if (!File.Exists(configPath))
{
    Console.WriteLine($"Configuration file not found: {configPath}");
    return;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(configPath, optional: false, reloadOnChange: true)
    .Build();

Console.WriteLine("Configuration Loaded:");
Console.WriteLine($"Databases Section: {configuration.GetSection("Databases").GetChildren().Count()}");



var serviceProvider = new ServiceCollection()
    .AddSingleton<IConfiguration>(configuration)
    .AddSingleton<IDatabaseConnection>(sp =>
    {
        var dbConfigSection = configuration.GetSection("Databases");
        var databases = dbConfigSection.GetChildren().ToList();

        Console.WriteLine("Available database types:");
        var databaseTypes = databases.Select(db => db.GetValue<string>("Type")).Distinct().ToList();
        foreach (var dbType in databaseTypes)
        {
            Console.WriteLine($"- {dbType}");
        }

        Console.Write("Select a database type: ");
        string selectedDbType = Console.ReadLine()?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(selectedDbType) || !databaseTypes.Contains(selectedDbType))
        {
            throw new Exception("Invalid database type selected.");
        }

        var selectedDatabase = databases.FirstOrDefault(db => db.GetValue<string>("Type") == selectedDbType);
        if (selectedDatabase == null)
        {
            throw new Exception($"No database configuration found for type: {selectedDbType}");
        }

        Console.WriteLine($"Selected Database: {selectedDatabase.GetValue<string>("DatabaseName")}");

        if (selectedDbType == "MySql")
        {
            return new MySqlConnectionService(
                selectedDatabase.GetValue<string>("Host") ?? throw new Exception("Host is not configured"),
                selectedDatabase.GetValue<string>("DatabaseName") ?? throw new Exception("DatabaseName is not configured"),
                selectedDatabase.GetValue<string>("Username") ?? throw new Exception("Username is not configured"),
                selectedDatabase.GetValue<string>("Password") ?? throw new Exception("Password is not configured")
            );
        }
        else if (selectedDbType == "PostgreSql")
        {
            return new PostgreSqlConnectionService(
                selectedDatabase.GetValue<string>("Host") ?? throw new Exception("Host is not configured"),
                selectedDatabase.GetValue<string>("DatabaseName") ?? throw new Exception("DatabaseName is not configured"),
                selectedDatabase.GetValue<string>("Username") ?? throw new Exception("Username is not configured"),
                selectedDatabase.GetValue<string>("Password") ?? throw new Exception("Password is not configured")
            );
        }
        else if (selectedDbType == "MongoDb")
        {
            return new MongoDbConnectionService(
                $"mongodb://{selectedDatabase.GetValue<string>("Username")}:{selectedDatabase.GetValue<string>("Password")}@{selectedDatabase.GetValue<string>("Host")}",
                selectedDatabase.GetValue<string>("DatabaseName") ?? throw new Exception("DatabaseName is not configured")
            );
        }
        else
        {
            throw new Exception("Unsupported database type.");
        }
    })
    .BuildServiceProvider();


var logger = serviceProvider.GetService<ILoggingService>() ?? throw new Exception("Logging service not found.");
var notificationService = serviceProvider.GetService<INotificationService>() ?? throw new Exception("Notification service not found.");
var storageService = serviceProvider.GetService<IStorageService>() ?? throw new Exception("Storage service not found.");
var databaseConnection = serviceProvider.GetService<IDatabaseConnection>() ?? throw new Exception("Database connection service not found.");

var localPath = configuration.GetValue<string>("Storage:LocalPath");

if (string.IsNullOrEmpty(localPath))
{
    throw new Exception("Local path for storage is not configured.");
}

string backupFilePath = Path.Combine(localPath, "backup " + DateTime.Now.ToString("yyyyMMddHHmmss"));

try
{
    if (command == "backup")
    {
        logger.LogInfo("Starting backup process...");
        databaseConnection.Backup(backupFilePath);
        storageService.SaveBackup(backupFilePath, backupFilePath);
        logger.LogInfo("Backup process completed successfully.");
        if(notificationService != null)
        notificationService.SendNotification("Backup process completed successfully.");
    }
    else if (command == "restore")
    {
        logger.LogInfo("Starting restore process...");
        storageService.LoadBackup(backupFilePath, backupFilePath);
        databaseConnection.Restore(backupFilePath);
        logger.LogInfo("Restore process completed successfully.");
        if(notificationService != null)
        notificationService.SendNotification("Restore process completed successfully.");
    }
}
catch (Exception ex)
{
    logger.LogError($"An error occurred: {ex.Message}");
    if(notificationService != null)
    notificationService.SendNotification($"Process failed: {ex.Message}");
} 

