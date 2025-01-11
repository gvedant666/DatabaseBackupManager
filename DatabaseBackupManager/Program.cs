using System.Data;
using Amazon.S3;
using Azure.Storage.Blobs;
using DatabaseBackupManager.Configs;
using Google.Apis.Storage.v1;
using Google.Cloud.Storage.V1;
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

        var selectedDbConfig = dbConfigSection.GetChildren().FirstOrDefault(db => db["Type"] == selectedDbType);
        if (selectedDbConfig == null)
        {
            throw new Exception($"No database configuration found for type: {selectedDbType}");
        }

        Console.WriteLine($"Selected Database: {selectedDbConfig.GetValue<string>("DatabaseName")}");

        return selectedDbType switch
        {
            "MySql" => new MySqlConnectionService(
                selectedDbConfig["Host"], selectedDbConfig["DatabaseName"], selectedDbConfig["Username"], selectedDbConfig["Password"]),
            "PostgreSql" => new PostgreSqlConnectionService(
                selectedDbConfig["Host"], selectedDbConfig["DatabaseName"], selectedDbConfig["Username"], selectedDbConfig["Password"]),
            "MongoDb" => new MongoDbConnectionService(
                $"mongodb://{selectedDbConfig["Username"]}:{selectedDbConfig["Password"]}@{selectedDbConfig["Host"]}", selectedDbConfig["DatabaseName"]),
            _ => throw new Exception("Unsupported database type."),
        };
    })
    .AddSingleton<IStorageService>(sp =>
    {
        var storageConfigSection = configuration.GetSection("Storage");
        var storageType = storageConfigSection.GetValue<string>("Type");

        Console.WriteLine("Available storage types:");
        var storageTypes = storageConfigSection.GetChildren().Select(db => db.GetValue<string>("Type")).Distinct().ToList();
        foreach (var stType in storageTypes)
        {
            Console.WriteLine($"- {stType}");
        }
        Console.WriteLine("Select a storage type: ");
        string selectedStorageType = Console.ReadLine()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(selectedStorageType) || !storageTypes.Contains(selectedStorageType))
        {
            throw new Exception("Invalid storage type selected.");
        }
        return selectedStorageType switch
        {
            "LocalStorage" => new LocalStorageService(),

            "GoogleCloud" =>
                new GoogleCloudStorageService(
                    storageConfigSection["CredentialsFilePath"],
                    storageConfigSection["BucketName"]),

            "AmazonS3" =>
                new AwsS3StorageService(
                    new AmazonS3Client(
                        storageConfigSection["AccessKey"],
                        storageConfigSection["SecretKey"],
                        new AmazonS3Config()),
                    storageConfigSection["BucketName"]),

            "AzureBlob" =>
                new AzureBlobStorageService(
                    new BlobServiceClient(storageConfigSection["ConnectionString"]),
                    storageConfigSection["ContainerName"]),

            _ => throw new Exception("Unsupported storage type."),
        };
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

