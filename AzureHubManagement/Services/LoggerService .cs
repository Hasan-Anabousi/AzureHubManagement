using AzureHubManagement.Interfaces;
using Newtonsoft.Json;

namespace AzureHubManagement.Services
{ 
        public class LoggerService : ILoggerService
        {
            private readonly List<string> _logs = new List<string>();
            private readonly string _logFilePath;

            public LoggerService(IConfiguration configuration)
            {
                // Specify the path to the logs folder
                var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");

                // Ensure the directory exists
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // Set the full path for the log file within the logs directory
                _logFilePath = Path.Combine(logDirectory, configuration.GetValue<string>("LogFileName", "logs.json"));
            }

            public void Log(string message)
            {
                _logs.Add($"{DateTime.UtcNow}: {message}");
            }

            public async Task SaveLogsAsync()
            {
                var json = JsonConvert.SerializeObject(_logs, Formatting.Indented);
                await File.WriteAllTextAsync(_logFilePath, json);
            }
        }
    }