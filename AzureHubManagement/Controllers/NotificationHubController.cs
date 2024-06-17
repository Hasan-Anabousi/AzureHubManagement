using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.NotificationHubs;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;
using AzureHubManagement.Interfaces;

namespace AzureHubManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationHubController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;

        public NotificationHubController(IConfiguration configuration, ILoggerService logger)
        {
            _configuration = configuration;
            _logger = logger;
        }


        //for a some reason the import did work unless 
        [HttpPost("import2")]
        public async Task<IActionResult> ImportData2()
        {
            string CONNECTION_STRING = _configuration["Azure:NotificationHubConnectionString"];
            string HUB_NAME = _configuration["Azure:NotificationHubName"];
            string STORAGE_ACCOUNT_CONNECTIONSTRING = _configuration["Azure:STORAGE_ACCOUNT_CONNECTIONSTRING"];
            string CONTAINER_NAME = _configuration["Azure:CONTAINER_NAME"];
            //string INPUT_FILE_NAME = _configuration["LastExportedFileName"];
            string INPUT_FILE_NAME = "";
            var descriptions = new[]
           {
                new MpnsRegistrationDescription(@"http://dm2.notify.live.net/throttledthirdparty/01.00/12G9Ed13dLb5RbCii5fWzpFpAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMkUxREQFBlVTTkMwMQ"),
                new MpnsRegistrationDescription(@"http://dm2.notify.live.net/throttledthirdparty/01.00/12G9Ed13dLb5RbCii5fWzpFpAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMjUxREQFBlVTTkMwMQ"),
                new MpnsRegistrationDescription(@"http://dm2.notify.live.net/throttledthirdparty/01.00/12G9Ed13dLb5RbCii5fWzpFpAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMhUxREQFBlVTTkMwMQ"),
                new MpnsRegistrationDescription(@"http://dm2.notify.live.net/throttledthirdparty/01.00/12G9Ed13dLb5RbCii5fWzpFpAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMdUxREQFBlVTTkMwMQ"),
            };

            // Get a reference to a container named "sample-container" and then create it
            BlobContainerClient container = new BlobContainerClient(STORAGE_ACCOUNT_CONNECTIONSTRING, CONTAINER_NAME);

            await container.CreateIfNotExistsAsync();

            await SerializeToBlobAsync(container, descriptions, INPUT_FILE_NAME);

            // TODO then create Sas
            var outputContainerSasUri = GetOutputDirectoryUrl(container);

            BlobContainerClient inputcontainer = new BlobContainerClient(STORAGE_ACCOUNT_CONNECTIONSTRING, STORAGE_ACCOUNT_CONNECTIONSTRING + "/" + INPUT_FILE_NAME);

            var inputFileSasUri = GetInputFileUrl(inputcontainer, INPUT_FILE_NAME);


            // Import this file
            NotificationHubClient client = NotificationHubClient.CreateClientFromConnectionString(CONNECTION_STRING, HUB_NAME);
            var job = await client.SubmitNotificationHubJobAsync(
                new NotificationHubJob
                {
                    JobType = NotificationHubJobType.ImportCreateRegistrations,
                    OutputContainerUri = outputContainerSasUri,
                    ImportFileUri = inputFileSasUri
                }
            );

            long i = 10;
            while (i > 0 && job.Status != NotificationHubJobStatus.Completed)
            {
                job = await client.GetNotificationHubJobAsync(job.JobId);
                await Task.Delay(1000);
                i--;
            }

            return Ok(" completed successfully.");
        }
        static Uri GetOutputDirectoryUrl(BlobContainerClient container)
        {
            Console.WriteLine(container.CanGenerateSasUri);
            BlobSasBuilder builder = new BlobSasBuilder(BlobSasPermissions.All, DateTime.UtcNow.AddDays(1));
            return container.GenerateSasUri(builder);
        }

        static Uri GetInputFileUrl(BlobContainerClient container, string filePath)
        {
            Console.WriteLine(container.CanGenerateSasUri);
            BlobSasBuilder builder = new BlobSasBuilder(BlobSasPermissions.Read, DateTime.UtcNow.AddDays(1));
            return container.GenerateSasUri(builder);

        }
        private static async Task SerializeToBlobAsync(BlobContainerClient container, RegistrationDescription[] descriptions,string INPUT_FILE_NAME)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var registrationDescription in descriptions)
            {
                builder.AppendLine(registrationDescription.Serialize());
            }
 
            var inputBlob = container.GetBlobClient(INPUT_FILE_NAME);
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString())))
            {
                await inputBlob.UploadAsync(stream);
            }
        }

        [HttpPost("import")]
        public async Task<IActionResult> ImportData()
        {
            var connectionString = _configuration["Azure:NotificationHubConnectionString"];
            var hubName = _configuration["Azure:NotificationHubName"];
            var storageAccountConnectionString = _configuration["Azure:STORAGE_ACCOUNT_CONNECTIONSTRING"];
            var containerName = _configuration["Azure:CONTAINER_NAME"];
            var fileName = _configuration["LastExportedFileName"];

            if (string.IsNullOrEmpty(fileName))
            {
                _logger.Log("No exported file name found to import.");
                return BadRequest("No file name found to import.");
            }
            try
            {
                var container = new BlobContainerClient(storageAccountConnectionString, containerName);
                await container.CreateIfNotExistsAsync();
                _logger.Log("Checked container existence.");

                var blobClient = container.GetBlobClient(fileName);
                var readSasBuilder = new BlobSasBuilder(BlobSasPermissions.Write | BlobSasPermissions.List | BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1))
                {
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5)
                };
                var importBlobSasUri = blobClient.GenerateSasUri(readSasBuilder);
                _logger.Log($"Generated read SAS URI: {importBlobSasUri}");

                // Download the blob
                var blobDownloadResponse = await blobClient.DownloadAsync();
                using (var reader = new StreamReader(blobDownloadResponse.Value.Content, Encoding.UTF8, leaveOpen: false))
                {
                    var content = await reader.ReadToEndAsync();
                    _logger.Log($"Read content from blob: {content.Substring(0, Math.Min(500, content.Length))}");
                }

                var writeSasBuilder = new BlobSasBuilder(BlobSasPermissions.Write | BlobSasPermissions.List | BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1))
                {
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5)
                };
                var outputContainerSasUri = container.GenerateSasUri(writeSasBuilder);
                _logger.Log($"Generated write SAS URI: {outputContainerSasUri}");

                var client = NotificationHubClient.CreateClientFromConnectionString(connectionString, hubName);
                var job = new NotificationHubJob
                {
                    JobType = NotificationHubJobType.ImportCreateRegistrations,
                    ImportFileUri = new Uri(importBlobSasUri.ToString()),
                    OutputContainerUri = new Uri(outputContainerSasUri.ToString())
                };

                var response = await client.SubmitNotificationHubJobAsync(job);
                _logger.Log("Submitted import job.");

                NotificationHubJob updatedJob = null;
                do
                {
                    await Task.Delay(1000);
                    updatedJob = await client.GetNotificationHubJobAsync(response.JobId);
                    _logger.Log($"Job Status: {updatedJob.Status} - {updatedJob.Progress}");
                } while (updatedJob.Status == NotificationHubJobStatus.Running || updatedJob.Status == NotificationHubJobStatus.Started);

                if (updatedJob.Status == NotificationHubJobStatus.Completed)
                {
                    _logger.Log("Import job completed successfully.");
                    await _logger.SaveLogsAsync();  
                    return Ok("Import completed successfully.");
                }
                else
                {
                    _logger.Log($"Import job failed: {updatedJob.Failure}");
                    await _logger.SaveLogsAsync();  
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Import failed: {updatedJob.Failure}");
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"An exception occurred: {ex.Message}");
                await _logger.SaveLogsAsync();  
                return StatusCode(StatusCodes.Status500InternalServerError, $"Server error: {ex.Message}");
            }
        }
   
         [HttpPost("export")]
        public async Task<IActionResult> ExportData()
        {
            var connectionString = _configuration["Azure:NotificationHubConnectionString"];
            var hubName = _configuration["Azure:NotificationHubName"];
            var storageAccountConnectionString = _configuration["Azure:STORAGE_ACCOUNT_CONNECTIONSTRING"];
            var containerName = _configuration["Azure:CONTAINER_NAME"];

            try
            {
                var container = new BlobContainerClient(storageAccountConnectionString, containerName);
                await container.CreateIfNotExistsAsync();
                _logger.Log("Checked container existence.");
                var builder = new BlobSasBuilder(BlobSasPermissions.Write | BlobSasPermissions.List | BlobSasPermissions.Read, DateTime.UtcNow.AddDays(1))
                {
                    StartsOn = DateTime.UtcNow.AddMinutes(-5)
                };
                var outputContainerSasUri = container.GenerateSasUri(builder);
                _logger.Log($"Generated Write SAS URI: {outputContainerSasUri}");
            
                var client = NotificationHubClient.CreateClientFromConnectionString(connectionString, hubName);
                var job = await client.SubmitNotificationHubJobAsync(
                    new NotificationHubJob
                    {
                        JobType = NotificationHubJobType.ExportRegistrations,
                        OutputContainerUri = outputContainerSasUri
                    }
                );
                _logger.Log("Submitted Export job.");
                // Monitor the job status
                int attempt = 0;
                while (true)
                {
                    await Task.Delay(1000); // Poll every second
                    job = await client.GetNotificationHubJobAsync(job.JobId);
                    _logger.Log($"Attempt {++attempt}: {job.Status} - {job.Progress}");
                    if (job.Status == NotificationHubJobStatus.Completed)
                    {
                        _logger.Log($"Export completed, output file: {job.OutputFileName}");
                        _configuration["LastExportedFileName"] = job.OutputFileName;  
          
                        await _logger.SaveLogsAsync();
                        return Ok("Export completed successfully.");
                    }
                    else if (job.Status == NotificationHubJobStatus.Failed)
                    {
                        _logger.Log($"Job failed: {job.Failure}");
                        await _logger.SaveLogsAsync();
                        return StatusCode(StatusCodes.Status500InternalServerError, $"Export failed: {job.Failure}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"An exception occurred: {ex.Message}");
                await _logger.SaveLogsAsync();
                return StatusCode(StatusCodes.Status500InternalServerError, $"Server error: {ex.Message}");
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
