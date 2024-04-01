﻿using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using System.Diagnostics;
using System.Net;

namespace PowerPlatform.Dataverse.CodeSamples
{
    class Program
    {
        /// <summary>
        /// Contains the application's configuration settings. 
        /// </summary>
        IConfiguration Configuration { get; }


        /// <summary>
        /// Constructor. Loads the application configuration settings from a JSON file.
        /// </summary>
        Program()
        {

            // Get the path to the appsettings file. If the environment variable is set,
            // use that file path. Otherwise, use the runtime folder's settings file.
            string? path = Environment.GetEnvironmentVariable("DATAVERSE_APPSETTINGS");
            path ??= "appsettings.json";

            // Load the app's configuration settings from the JSON file.
            Configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false, reloadOnChange: true)
                .Build();
        }
        static async Task Main()
        {
            Program app = new();

            int numberOfRecords = Settings.NumberOfRecords; //100 by default
            string tableSchemaName = "sample_Example";
            string tableLogicalName = tableSchemaName.ToLower(); //sample_example

            #region Optimize Connection settings

            //Change max connections from .NET to a remote service default: 2
            ServicePointManager.DefaultConnectionLimit = 65000;
            //Bump up the min threads reserved for this app to ramp connections faster - minWorkerThreads defaults to 4, minIOCP defaults to 4
            ThreadPool.SetMinThreads(100, 100);
            //Turn off the Expect 100 to continue message - 'true' will cause the caller to wait until it round-trip confirms a connection to the server
            ServicePointManager.Expect100Continue = false;
            //Can decreas overall transmission overhead but can cause delay in data packet arrival
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            #endregion Optimize Connection settings

            // Create a Dataverse service client using the default connection string.
            ServiceClient serviceClient =
                new(app.Configuration.GetConnectionString("default"))
                {
                    // Disable affinity cookie for these operations.
                    EnableAffinityCookie = false,
                    // Avoids issues when working with tables created and deleted recently.
                    UseWebApi = false
                };

            Console.WriteLine($"RecommendedDegreesOfParallelism:{serviceClient.RecommendedDegreesOfParallelism}\n");

            // Create sample_Example table for this sample.
            Utility.CreateExampleTable(
                serviceClient: serviceClient,
                tableSchemaName: tableSchemaName, 
                isElastic: Settings.UseElastic);

            // Create a List of entity instances.
            Console.WriteLine($"\nPreparing {numberOfRecords} records to create..");
            List<Entity> entityList = new();
            // Populate the list with the number of records to test.
            for (int i = 0; i < numberOfRecords; i++)
            {
                entityList.Add(new Entity(tableLogicalName)
                {
                    Attributes = {
                            // Example: 'sample record 0000001'
                            { "sample_name", $"sample record {i+1:0000000}" }
                        }
                });
            }

            

            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = serviceClient.RecommendedDegreesOfParallelism
            };

            Console.WriteLine($"Sending create requests in parallel...");
            Stopwatch createStopwatch = Stopwatch.StartNew();

            await Parallel.ForEachAsync(
                source: entityList,
                parallelOptions: parallelOptions,
                async (entity, token) => {

                    // In each thread, create entities and update the Id.
                    CreateRequest createRequest = new()
                    {
                        Target = entity
                    };
                    // Add Shared Variable with request to detect in a plug-in.
                    createRequest["tag"] = "ParallelCreateUpdate";
                    if (Settings.BypassCustomPluginExecution)
                    {
#pragma warning disable CS0162 // Unreachable code detected: Configurable by setting
                        createRequest["BypassCustomPluginExecution"] = true;
#pragma warning restore CS0162 // Unreachable code detected: Configurable by setting
                    }
                    var createResponse = (CreateResponse)await serviceClient.ExecuteAsync(createRequest,token);

                    entity.Id = createResponse.id;

                });
            createStopwatch.Stop();

            Console.WriteLine($"\tCreated {entityList.Count} records " +
                $"in {Math.Round(createStopwatch.Elapsed.TotalSeconds)} seconds.");

            Console.WriteLine($"\nPreparing {numberOfRecords} records to update..");

            // Update the sample_name value:
            foreach (Entity entity in entityList)
            {
                entity["sample_name"] += " Updated";
            }

            Console.WriteLine($"Sending update requests in parallel...");
            Stopwatch updateStopwatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                source: entityList,
                parallelOptions: parallelOptions,
                async (entity, token) => {

                    UpdateRequest updateRequest = new() { Target = entity };
                    // Add Shared Variable with request to detect in a plug-in.
                    updateRequest["tag"] = "ParallelCreateUpdate";

                    if (Settings.BypassCustomPluginExecution)
                    {
#pragma warning disable CS0162 // Unreachable code detected: Configurable by setting
                        updateRequest["BypassCustomPluginExecution"] = true;
#pragma warning restore CS0162 // Unreachable code detected: Configurable by setting
                    }

                   await serviceClient.ExecuteAsync(updateRequest, token);

                });

            updateStopwatch.Stop();
            Console.WriteLine($"\tUpdated {numberOfRecords} records " +
                $"in {Math.Round(updateStopwatch.Elapsed.TotalSeconds)} seconds.");

           
            if (Settings.UseElastic)
            {

                Console.WriteLine($"\nPreparing {numberOfRecords} records to delete..");
                // Delete created rows with DeleteMultiple
                EntityReferenceCollection targets = new();
                foreach (Entity entity in entityList)
                {
                    targets.Add(entity.ToEntityReference());
                }

                OrganizationRequest deleteMultipleRequest = new("DeleteMultiple")
                {
                    Parameters = {
                                {"Targets", targets }
                            }
                };

                Console.WriteLine($"Sending DeleteMultipleRequest...");
                Stopwatch deleteStopwatch = Stopwatch.StartNew();
                serviceClient.Execute(deleteMultipleRequest);
                deleteStopwatch.Stop();

                Console.WriteLine($"\tDeleted {entityList.Count} records " +
                    $"in {Math.Round(deleteStopwatch.Elapsed.TotalSeconds)} seconds.");

            }
            else
            {
                // Delete created rows asynchronously
                Console.WriteLine($"\nStarting asynchronous bulk delete of {numberOfRecords} created records...");

                Guid[] iDs = new Guid[entityList.Count];

                for (int i = 0; i < entityList.Count; i++)
                {
                    iDs[i] = entityList.ToList()[i].Id;
                }

                string deleteJobStatus = Utility.BulkDeleteRecordsByIds(
                service: serviceClient,
                tableLogicalName: tableLogicalName,
                iDs: iDs,
                jobName: "Deleting records created by ParallelCreateUpdate Sample.");

                Console.WriteLine($"\tBulk Delete status: {deleteJobStatus}");
            }



            // Delete sample_example table
            Utility.DeleteExampleTable(
                service: serviceClient,
                tableSchemaName: tableSchemaName);
        }
    }
}