using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace UploadEncodeAndStreamFiles
{
    class Program
    {
        private const string AdaptiveStreamingTransformName = "MyTransformWithAdaptiveStreamingPreset";
        private const string InputMP4FileName = @"ignite.mp4";
        private const string OutputFolder = @"Output";

        static void Main(string[] args)
        {
            ConfigWrapper config = new ConfigWrapper();

            try{
                IAzureMediaServicesClient client = CreateMediaServicesClient(config);

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                // Creating a unique suffix so that we don't have name collisions if you run the sample
                // multiple times without cleaning up.
                string uniqueness = Guid.NewGuid().ToString().Substring(0, 13);

                string jobName = "job-" + uniqueness;
                string locatorName = "locator-" + uniqueness;
                string outputAssetName = "output-" + uniqueness;
                string inputAssetName = "input-" + uniqueness;

                // Ensure that you have the desired encoding Transform. This is really a one time setup operation.
                Transform transform = EnsureTransformExists(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName);

                // Create a new input Asset and upload the specified local video file into it.
                CreateInputAsset(client, config.ResourceGroup, config.AccountName, inputAssetName, InputMP4FileName);

                // Use the name of the created input asset to create the job input.
                JobInput jobInput = new JobInputAsset(assetName: inputAssetName);

                // Output from the encoding Job must be written to an Asset, so let's create one
                Asset outputAsset = CreateOutputAsset(client, config.ResourceGroup, config.AccountName, outputAssetName);

                Job job = SubmitJob(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, jobName, jobInput, outputAssetName);

                // In this demo code, we will poll for Job status
                // Polling is not a recommended best practice for production applications because of the latency it introduces.
                // Overuse of this API may trigger throttling. Developers should instead use Event Grid.
                job = WaitForJobToFinish(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, jobName);

                if (job.State == JobState.Finished)
                {
                    Console.WriteLine("Job finished.");
                    if (!Directory.Exists(OutputFolder))
                        Directory.CreateDirectory(OutputFolder);

                    DownloadResults(client, config.ResourceGroup, config.AccountName, outputAssetName, OutputFolder);

                    StreamingLocator locator = CreateStreamingLocator(client, config.ResourceGroup, config.AccountName, outputAsset.Name, locatorName);

                    IList<string> urls = GetStreamingURLs(client, config.ResourceGroup, config.AccountName, locator.Name);
                    foreach (var url in urls)
                    {
                        Console.WriteLine(url);
                    }
                }

                Console.WriteLine("Done. Copy and paste the Streaming URL into the Azure Media Player at http://ampdemo.azureedge.net/");
                Console.WriteLine("Press Enter to Continue");
                Console.ReadLine();
            }
            catch (ApiErrorException ex)
            {
                Console.WriteLine("{0}", ex.Message);

                Console.WriteLine("ERROR:API call failed with error code: {0} and message: {1}",
                    ex.Body.Error.Code, ex.Body.Error.Message);
            }
        }

        /// <summary>
        /// Creates the AzureMediaServicesClient object based on the credentials
        /// supplied in App.config.
        /// </summary>
        /// <param name="config">The parm is of type ConfigWrapper. This class reads values from app.config.</param>
        /// <returns></returns>
        // <CreateMediaServicesClient>
        private static IAzureMediaServicesClient CreateMediaServicesClient(ConfigWrapper config)
        {
            ArmClientCredentials credentials = new ArmClientCredentials(config);

            return new AzureMediaServicesClient(config.ArmEndpoint, credentials)
            {
                SubscriptionId = config.SubscriptionId,
            };
        }
        // </CreateMediaServicesClient>

        /// <summary>
        /// Creates a new input Asset and uploads the specified local video file into it.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The asset name.</param>
        /// <param name="fileToUpload">The file you want to upload into the asset.</param>
        /// <returns></returns>
        // <CreateInputAsset>
        private static Asset CreateInputAsset(IAzureMediaServicesClient client, 
            string resourceGroupName, 
            string accountName, 
            string assetName, 
            string fileToUpload)
        {
            // In this example, we are assuming that the asset name is unique.
            //
            // If you might already have an asset with the desired name, use the **Assets.Get** method
            // to get the existing asset. In Media Services v3, **Get** methods on entities returns null 
            // if the entity doesn’t exist (a case-insensitive check on the name).

            // Call Media Services API to create an Asset.
            // This method creates a container in storage for the Asset.
            // The files (blobs) associated with the asset will be stored in this container.
            Asset asset = client.Assets.CreateOrUpdate(resourceGroupName, accountName, assetName, new Asset());

            // Use Media Services API to get back a response that contains
            // SAS URL for the Asset container into which to upload blobs.
            // That is where you would specify read-write permissions 
            // and the exparation time for the SAS URL.
            var response = client.Assets.ListContainerSas(
                  resourceGroupName,
                  accountName,
                  assetName,
                  permissions: AssetContainerPermission.ReadWrite,
                  expiryTime: DateTime.UtcNow.AddHours(4).ToUniversalTime()
              );

            var sasUri = new Uri(response.AssetContainerSasUrls.First());

            // Use Storage API to get a reference to the Asset container
            // that was created by calling Asset's CreateOrUpdate method.  
            CloudBlobContainer container = new CloudBlobContainer(sasUri);
            var blob = container.GetBlockBlobReference(Path.GetFileName(fileToUpload));

            // Use Strorage API to upload the file into the container in storage.
            blob.UploadFromFile(fileToUpload);

            return asset;
        }
        // </CreateInputAsset>

        /// <summary>
        /// Creates an ouput asset. The output from the encoding Job must be written to an Asset.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The output asset name.</param>
        /// <returns></returns>
        // <CreateOutputAsset>       
        private static Asset CreateOutputAsset(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            // Check if an Asset already exists
            Asset outputAsset = client.Assets.Get(resourceGroupName, accountName, assetName);
            Asset asset = new Asset();
            string outputAssetName = assetName;

            if (outputAsset != null)
            {
                // Name collision! In order to get the sample to work, let's just go ahead and create a unique asset name
                // Note that the returned Asset can have a different name than the one specified as an input parameter.
                // You may want to update this part to throw an Exception instead, and handle name collisions differently.
                string uniqueness = @"-" + Guid.NewGuid().ToString();
                outputAssetName += uniqueness;
            }

            return client.Assets.CreateOrUpdate(resourceGroupName, accountName, outputAssetName, asset);
        }
        // </CreateOutputAsset>  

        /// <summary>
        /// If the specified transform exists, get that transform.
        /// If the it does not exist, creates a new transform with the specified output. 
        /// In this case, the output is set to encode a video using one of the built-in encoding presets.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <returns></returns>
        // <EnsureTransformExists>
        private static Transform EnsureTransformExists(IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {

            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            Transform transform = client.Transforms.Get(resourceGroupName, accountName, transformName);

            if (transform == null)
            {
                // You need to specify what you want it to produce as an output
                TransformOutput[] output = new TransformOutput[]
                {
                    new TransformOutput
                    {
                        // The preset for the Transform is set to one of Media Services built-in sample presets.
                        // You can  customize the encoding settings by changing this to use "StandardEncoderPreset" class.
                        Preset = new BuiltInStandardEncoderPreset()
                        {
                            // This sample uses the built-in encoding preset for Adaptive Bitrate Streaming.
                            PresetName = EncoderNamedPreset.AdaptiveStreaming
                        }
                    }
                };

                // Create the Transform with the output defined above
                transform = client.Transforms.CreateOrUpdate(resourceGroupName, accountName, transformName, output);
            }

            return transform;
        }
        // </EnsureTransformExists>

        /// <summary>
        /// Submits a request to Media Services to apply the specified Transform to a given input video.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <param name="jobName">The (unique) name of the job.</param>
        /// <param name="jobInput"></param>
        /// <param name="outputAssetName">The (unique) name of the  output asset that will store the result of the encoding job. </param>
        // <SubmitJob>
        private static Job SubmitJob(IAzureMediaServicesClient client, 
            string resourceGroupName, 
            string accountName, 
            string transformName, 
            string jobName, 
            JobInput jobInput, 
            string outputAssetName)
        {
            JobOutput[] jobOutputs =
            {
                new JobOutputAsset(outputAssetName),
            };

            // In this example, we are assuming that the job name is unique.
            //
            // If you might already have a job with the desired name, use the **Jobs.Get** method
            // to get the existing job. In Media Services v3, **Get** methods on entities returns null 
            // if the entity doesn’t exist (a case-insensitive check on the name).
            Job job = client.Jobs.Create(
                resourceGroupName,
                accountName,
                transformName,
                jobName,
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs,
                });

            return job;
        }
        // </SubmitJob>

        /// <summary>
        /// Polls Media Services for the status of the Job.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <param name="jobName">The name of the job you submitted.</param>
        /// <returns></returns>
        // <WaitForJobToFinish>
        private static Job WaitForJobToFinish(IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName,
            string jobName)
        {
            int SleepInterval = 60 * 1000;

            Job job = null;

            while (true)
            {
                job = client.Jobs.Get(resourceGroupName, accountName, transformName, jobName);

                if (job.State == JobState.Finished || job.State == JobState.Error || job.State == JobState.Canceled)
                {
                    break;
                }

                Console.WriteLine($"Job is {job.State}.");
                for (int i = 0; i < job.Outputs.Count; i++)
                {
                    JobOutput output = job.Outputs[i];
                    Console.Write($"\tJobOutput[{i}] is {output.State}.");
                    if (output.State == JobState.Processing)
                    {
                        Console.Write($"  Progress: {output.Progress}");
                    }
                    Console.WriteLine();
                }
                System.Threading.Thread.Sleep(SleepInterval);
            }

            return job;
        }
        // </WaitForJobToFinish>

        /// <summary>
        /// Creates a StreamingLocator for the specified asset and with the specified streaming policy name.
        /// Once the StreamingLocator is created the output asset is available to clients for playback.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The name of the output asset.</param>
        /// <param name="locatorName">The StreamingLocator name (unique in this case).</param>
        /// <returns></returns>
        // <CreateStreamingLocator>
        private static StreamingLocator CreateStreamingLocator(IAzureMediaServicesClient client,
                                                                string resourceGroup,
                                                                string accountName,
                                                                string assetName,
                                                                string locatorName)
        {
            StreamingLocator locator =
                client.StreamingLocators.Create(resourceGroup,
                accountName,
                locatorName,
                new StreamingLocator()
                {
                    AssetName = assetName,
                    StreamingPolicyName = PredefinedStreamingPolicy.ClearStreamingOnly,
                });

            return locator;
        }
        // </CreateStreamingLocator>

        /// <summary>
        /// Checks if the "default" streaming endpoint is in the running state,
        /// if not, starts it.
        /// Then, builds the streaming URLs.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="locatorName">The name of the StreamingLocator that was created.</param>
        /// <returns></returns>
        // <GetStreamingURLs>
        static IList<string> GetStreamingURLs(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            String locatorName)
        {
            IList<string> streamingURLs = new List<string>();

            string streamingUrlPrefx = "";

            StreamingEndpoint streamingEndpoint = client.StreamingEndpoints.Get(resourceGroupName, accountName, "default");

            if (streamingEndpoint != null)
            {
                streamingUrlPrefx = streamingEndpoint.HostName;

                if (streamingEndpoint.ResourceState != StreamingEndpointResourceState.Running)
                    client.StreamingEndpoints.Start(resourceGroupName, accountName, "default");
            }

            foreach (var path in client.StreamingLocators.ListPaths(resourceGroupName, accountName, locatorName).StreamingPaths)
            {
                streamingURLs.Add("http://" + streamingUrlPrefx + path.Paths[0].ToString());
            }

            return streamingURLs;
        }
        // </GetStreamingURLs>

        /// <summary>
        ///  Downloads the results from the specified output asset, so you can see what you got.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The output asset.</param>
        /// <param name="resultsFolder">The name of the folder into which to download the results.</param>
        // <DownloadResults>
        private static void DownloadResults(IAzureMediaServicesClient client,
          string resourceGroup,
          string accountName,
          string assetName,
          string resultsFolder)
        {
            AssetContainerSas assetContainerSas = client.Assets.ListContainerSas(
                   resourceGroup,
                   accountName,
                   assetName,
                   permissions: AssetContainerPermission.Read,
                   expiryTime: DateTime.UtcNow.AddHours(1).ToUniversalTime()
                   );

            Uri containerSasUrl = new Uri(assetContainerSas.AssetContainerSasUrls.FirstOrDefault());
            CloudBlobContainer container = new CloudBlobContainer(containerSasUrl);

            string directory = Path.Combine(resultsFolder, assetName);
            Directory.CreateDirectory(directory);

            Console.WriteLine("Downloading results to {0}.", directory);

            foreach (IListBlobItem blobItem in container.ListBlobs(null, true, BlobListingDetails.None))
            {
                if (blobItem is CloudBlockBlob)
                {
                    CloudBlockBlob blob = blobItem as CloudBlockBlob;
                    string filename = Path.Combine(directory, blob.Name);

                    blob.DownloadToFile(filename, FileMode.Create);
                }
            }

            Console.WriteLine("Download complete.");
        }
        // </DownloadResults>

        /// <summary>
        /// Deletes the jobs and assets that were created.
        /// Generally, you should clean up everything except objects 
        /// that you are planning to reuse (typically, you will reuse Transforms, and you will persist StreamingLocators).
        /// </summary>
        /// <param name="client"></param>
        /// <param name="resourceGroupName"></param>
        /// <param name="accountName"></param>
        /// <param name="transformName"></param>
        // <CleanUp>
        static void CleanUp(IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {
            foreach (var job in client.Jobs.List(resourceGroupName, accountName, transformName))
            {
                client.Jobs.Delete(resourceGroupName, accountName, transformName, job.Name);
            }

            foreach (var asset in client.Assets.List(resourceGroupName, accountName))
            {
                client.Assets.Delete(resourceGroupName, accountName, asset.Name);
            }
        }
        // </CleanUp>       
    }
}
