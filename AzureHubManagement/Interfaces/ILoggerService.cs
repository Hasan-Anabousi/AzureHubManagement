namespace AzureHubManagement.Interfaces
{
    public interface ILoggerService
    {
        void Log(string message);
        Task SaveLogsAsync();
    }
}
