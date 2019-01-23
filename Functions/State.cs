using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.EventHubs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;

namespace FanController
{
    public class TemperatureItem
    {
        public string PartitionKey {get; set;}

        [JsonProperty("id")]
        public string Id {get; set;}
        public double Temperature {get; set;}
    }
    
    public static class Functions
    {
        static readonly string connectionString = Environment.GetEnvironmentVariable("iotHubConnectionString");
        static readonly RegistryManager registryManager = RegistryManager.CreateFromConnectionString(connectionString);

        [FunctionName("set-temp-threshold")]
        public static async Task<IActionResult> SetThreshold(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "temp-threshold/{devicename}")] HttpRequest req,
            string devicename,
            ILogger log)
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            int temperatureThreshold = data.threshold;
            
            var patch = new
            {
                properties = new
                {
                    desired = new
                    {
                        temperatureThreshold = temperatureThreshold
                    }
                }
            };

            var twin = await registryManager.GetTwinAsync(devicename);
            await registryManager.UpdateTwinAsync(twin.DeviceId, JsonConvert.SerializeObject(patch), twin.ETag);

            return new OkResult();
        }

        static string GetQueryValue(HttpRequest req, string key) => req.Query.FirstOrDefault(q => string.Compare(q.Key, key, true) == 0).Value;  

        [FunctionName("EventHubTrigger")]
        public static void EventHubTrigger([EventHubTrigger("samples-workitems", Connection = "ecs")] string message,
            [CosmosDB(
                databaseName: "Devices",
                collectionName: "Temperatures",
                ConnectionStringSetting = "CosmosDBConnection")] out TemperatureItem temperatureItem,
            ILogger log)
        {
            log.LogInformation($"V2 C# IoT Hub trigger function processed a message: {message}");
            
            dynamic data = JsonConvert.DeserializeObject(message);
            double temperature = data.temperature;

            log.LogInformation($"Temp: {temperature}");

            temperatureItem = new TemperatureItem
            {
                PartitionKey = "temperature",
                Id = "fan-controller",
                Temperature = temperature
            };
        }

        [FunctionName("GetTemperature")]
        public static async Task<IActionResult> GetTemperature(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "temperature/{devicename}")] HttpRequest req,
            [CosmosDB(
                databaseName: "Devices",
                collectionName: "Temperatures",
                ConnectionStringSetting = "CosmosDBConnection",
                PartitionKey = "temperature",
                Id = "{devicename}")] TemperatureItem temperatureItem,
            ILogger log)
        {
            return new OkObjectResult(temperatureItem);
        }
    }
}