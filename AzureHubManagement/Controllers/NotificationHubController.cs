using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Azure.NotificationHubs;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;

namespace AzureHubManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationHubController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public NotificationHubController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        //[HttpPost("import")]
        //public async Task<IActionResult> ImportData()
        //{


        //    // Import this file
        // //   await ImportData(CONNECTION_STRING, HUB_NAME, STORAGE_ACCOUNT_CONNECTIONSTRING, CONTAINER_NAME);


        //    return Ok("1");
        //}


        [HttpPost("export")]
        public async Task<IActionResult> ExportData()
        {
            var NotificationHubConnectionString = _configuration["Azure:NotificationHubConnectionString"];
            var HUB_NAME = _configuration["Azure:NotificationHubName"];

            var STORAGE_ACCOUNT_CONNECTIONSTRING = _configuration["Azure:STORAGE_ACCOUNT_CONNECTIONSTRING"];  
            var CONTAINER_NAME = _configuration["Azure:CONTAINER_NAME"];  

 
   
            await ExportData(NotificationHubConnectionString, HUB_NAME, STORAGE_ACCOUNT_CONNECTIONSTRING, CONTAINER_NAME);
            return Ok("1");
        }


        static async Task ExportData(string connectionString, string hubName, string storageAccountConnectionString, string containerName)
        {
            try
            {
                var container = new BlobContainerClient(storageAccountConnectionString, containerName);
                await container.CreateIfNotExistsAsync();

                var builder = new BlobSasBuilder(BlobSasPermissions.Write | BlobSasPermissions.List | BlobSasPermissions.Read, DateTime.UtcNow.AddDays(1))
                {
                    StartsOn = DateTime.UtcNow.AddMinutes(-5)   
                };
                var outputContainerSasUri = container.GenerateSasUri(builder);

                Console.WriteLine($"Generated SAS URI: {outputContainerSasUri}");

                var client = NotificationHubClient.CreateClientFromConnectionString(connectionString, hubName);
                var job = await client.SubmitNotificationHubJobAsync(
                    new NotificationHubJob
                    {
                        JobType = NotificationHubJobType.ExportRegistrations,
                        OutputContainerUri = outputContainerSasUri
                    }
                );

                // Monitor the job status
                int attempt = 0;
                while (true)
                {
                    await Task.Delay(1000); // Poll every second
                    job = await client.GetNotificationHubJobAsync(job.JobId);
                    Console.WriteLine($"Attempt {++attempt}: {job.Status} - {job.Progress}");

                    if (job.Status == NotificationHubJobStatus.Completed)
                    {
                        Console.WriteLine($"Export completed, output file: {job.OutputFileName}");
                        break;
                    }
                    else if (job.Status == NotificationHubJobStatus.Failed)
                    {
                        Console.WriteLine($"Job failed: {job.Failure}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
                throw;
            }
        }


        [HttpPost("create-installation")]
        public async Task<IActionResult> CreateOrUpdateInstallation([FromBody] Installation installation)
        {
            var connectionString = _configuration["Azure:NotificationHubConnectionString"];
            var hubName = _configuration["Azure:NotificationHubName"];

            // Parse the connection string and extract information
            var (endpoint, sasKeyName, sasKeyValue) = ParseConnectionString(connectionString);

            var uri = $"{endpoint}/{hubName}/installations/{installation.InstallationId}?api-version=2020-06";
            var sasToken = GenerateSasToken(uri, sasKeyName, sasKeyValue);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", sasToken);

            var jsonContent = JsonConvert.SerializeObject(installation);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await client.PutAsync(uri, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, error);
            }

            return Ok("Installation created/updated successfully.");
        }

        [HttpGet("read-installation/{installationId}")]
        public async Task<IActionResult> ReadInstallation(string installationId)
        {
            var connectionString = _configuration["Azure:NotificationHubConnectionString"];
            var hubName = _configuration["Azure:NotificationHubName"];

            // Parse the connection string to extract information
            var (endpoint, sasKeyName, sasKeyValue) = ParseConnectionString(connectionString);

            // Construct the URI for the GET request
            var uri = $"{endpoint}/{hubName}/installations/{installationId}?api-version=2020-06";
            var sasToken = GenerateSasToken(uri, sasKeyName, sasKeyValue);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", sasToken);

            // Send the GET request to the Azure Notification Hub REST API
            var response = await client.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Ok(content);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, error);
            }
        }



        [HttpGet("read-all-registrations")]
        public async Task<IActionResult> ReadAllRegistrations()
        {
            var connectionString = _configuration["Azure:NotificationHubConnectionString"];
            var hubName = _configuration["Azure:NotificationHubName"];

            // Parse the connection string to extract information
            var (endpoint, sasKeyName, sasKeyValue) = ParseConnectionString(connectionString);

            // Construct the URI for the GET request
            var uri = $"{endpoint}/{hubName}/registrations/?api-version=2020-06";
            var sasToken = GenerateSasToken(uri, sasKeyName, sasKeyValue);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", sasToken);

            // Send the GET request to the Azure Notification Hub REST API
            var response = await client.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Ok(content);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, error);
            }
        }


        private (string endpoint, string sasKeyName, string sasKeyValue) ParseConnectionString(string connectionString)
        {
            var parts = connectionString.Split(';').ToDictionary(
                part => part.Substring(0, part.IndexOf('=')),
                part => part.Substring(part.IndexOf('=') + 1));

            var endpoint = parts["Endpoint"].Replace("sb://", "https://").TrimEnd('/');
            return (endpoint, parts["SharedAccessKeyName"], parts["SharedAccessKey"]);
        }


        private string GenerateSasToken(string resourceUri, string keyName, string key)
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var stringToSign = HttpUtility.UrlEncode(resourceUri) + "\n" + expiry;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));

            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            var sasToken = String.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                HttpUtility.UrlEncode(resourceUri), HttpUtility.UrlEncode(signature), expiry, keyName);

            return sasToken;
        }
        public class Installation
        {
            public string InstallationId { get; set; }
            public string Platform { get; set; }
            public string PushChannel { get; set; }
            public List<string> Tags { get; set; }
        }
    }
}
