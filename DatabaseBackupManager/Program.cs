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
        string selectedDbType = Console.ReadLine()?.Trim();

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
                selectedDatabase.GetValue<string>("Host"),
                selectedDatabase.GetValue<string>("DatabaseName"),
                selectedDatabase.GetValue<string>("Username"),
                selectedDatabase.GetValue<string>("Password")
            );
        }
        else if (selectedDbType == "PostgreSql")
        {
            return new PostgreSqlConnectionService(
                selectedDatabase.GetValue<string>("Host"),
                selectedDatabase.GetValue<string>("DatabaseName"),
                selectedDatabase.GetValue<string>("Username"),
                selectedDatabase.GetValue<string>("Password")
            );
        }
        else if (selectedDbType == "MongoDb")
        {
            return new MongoDbConnectionService(
                $"mongodb://{selectedDatabase.GetValue<string>("Username")}:{selectedDatabase.GetValue<string>("Password")}@{selectedDatabase.GetValue<string>("Host")}",
                selectedDatabase.GetValue<string>("DatabaseName")
            );
        }
        else
        {
            throw new Exception("Unsupported database type.");
        }
    })
    .BuildServiceProvider();



var databaseConnection = serviceProvider.GetService<IDatabaseConnection>();
//var backupService = serviceProvider.GetService<IBackupService>();
//var restoreService = serviceProvider.GetService<IRestoreService>();

//if (backupService == null)
//{
//    Console.WriteLine("It is null");
//    return;
//}

var localPath = configuration.GetValue<string>("Storage:LocalPath");
var backupFilePath = Path.Combine(localPath, "backup.sql");

databaseConnection.Backup(backupFilePath);

Console.WriteLine("Reached till this ");

