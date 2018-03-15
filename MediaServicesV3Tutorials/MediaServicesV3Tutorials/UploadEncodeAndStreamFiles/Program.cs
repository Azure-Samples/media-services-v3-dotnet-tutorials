using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Media.Encoding.Rest.ArmClient;
using Microsoft.Media.Encoding.Rest.ArmClient.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace UploadEncodeAndStreamFiles
{
    class Program
    {

        const String inputMP4FileName = @"Input\ignite.mp4";
        const String outputFolder = @"Output";
        static void Main(string[] args)
        {
            ConfigWrapper config = new ConfigWrapper();

            IAzureMediaServicesClient client = CreateMediaServicesClient(config);

            //foreach(var a in client.Assets.List())
            //{
            //    Console.WriteLine(a.Name);
            //    client.Assets.Delete(a.Name);
            //}


            //return;
            String transformName = "MyTransformWithAdaptiveStreamingPreset";

            String jobName = "job-" + Guid.NewGuid().ToString();
            
            string inputAssetName = Guid.NewGuid().ToString() + "-input";
            string outputAssetName = Guid.NewGuid().ToString() + "-output";

            CreateInputAsset(client, inputAssetName, inputMP4FileName);

            JobInput jobInput = new JobInputAsset(assetName: inputAssetName);

            client.Assets.CreateOrUpdate(outputAssetName, new Asset());

            Transform transform = EnsureTransformExists(client, config.Region, transformName);

            Job job = SubmitJob(client, transformName, jobName, jobInput, outputAssetName);

            job = WaitForJobToFinish(client, transformName, jobName);

            if (job.State == JobState.Finished)
            {
                Console.WriteLine("Job finished.");
                if (!Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);
                DownloadResults(client, outputAssetName, outputFolder);
            }
        }

        /// <summary>
        /// To program against the Media Services API using .NET, 
        /// you need to create an AzureMediaServicesClient object. 
        /// To create the object, you need to supply credentials needed for the client to connect to Azure using Azure AD. 
        /// You first need to get a token and then create a ClientCredential object from the returned token. 
        /// In this sample, the ArmClientCredential object is used to get the token.  
        /// </summary>
        /// <param name="config">In In the example, we set all the connection parameters in the app.config file. ConfigWrapper gets the values.
        /// </param>
        /// <returns></returns>

        private static IAzureMediaServicesClient CreateMediaServicesClient(ConfigWrapper config)
        {
            ArmClientCredentials credentials = new ArmClientCredentials(config);

            return new AzureMediaServicesClient(config.ArmEndpoint, credentials)
            {
                SubscriptionId = config.SubscriptionId,
                ResourceGroupName = config.ResourceGroup,
                AccountName = config.AccountName
            };
        }

        /// <summary>
        /// Crearte and upload the asset.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="assetName"></param>
        /// <param name="fileToUpload"></param>
        /// <returns></returns>
        private static Asset CreateInputAsset(IAzureMediaServicesClient client, string assetName, string fileToUpload)
        {
            Asset asset = client.Assets.CreateOrUpdate(assetName, new Asset());

            ListContainerSasInput sasInput = new ListContainerSasInput()
            {
                Permissions = AssetContainerPermission.ReadWrite,
                ExpiryTime = DateTimeOffset.Now.AddHours(1)
            };

            var response = client.Assets.ListContainerSasAsync(assetName, sasInput).Result;

            string uploadSasUrl = response.AssetContainerSasUrls.First();

            string filename = Path.GetFileName(fileToUpload);
            var sasUri = new Uri(uploadSasUrl);
            CloudBlobContainer container = new CloudBlobContainer(sasUri);
            var blob = container.GetBlockBlobReference(filename);
            blob.UploadFromFile(fileToUpload);

            return asset;
        }

        /// <summary>
        /// When encoding or processing content in Media Services, it is a common pattern to set up the encoding settings as a recipe. 
        /// You would then submit a job to apply that recipe to a video. By submitting new jobs for each video, you are applying that recipe to all the videos in your library. 
        /// One example of a recipe would be to encode the video in order to stream it to a variety of iOS and Android devices. A recipe in Media Services is called as a Transform.
        /// When creating a Transform, you should first check if one already exists.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="location"></param>
        /// <param name="transformName"></param>
        /// <returns></returns>
        private static Transform EnsureTransformExists(IAzureMediaServicesClient client, string location, string transformName)
        {
            Transform transform = client.Transforms.Get(transformName);

            if (transform == null)
            {
                var output = new[]
                {
                    new TransformOutput
                    {
                        OnError = OnErrorType.ContinueJob,
                        RelativePriority = Priority.Normal,
                        Preset = new BuiltInStandardEncoderPreset()
                        {
                            PresetName = EncoderNamedPreset.AdaptiveStreaming
                        }
                    }
                };

                transform = new Transform(output, location: location);
                transform = client.Transforms.CreateOrUpdate(transformName, transform);
            }

            return transform;
        }

        /// <summary>
        /// A Job is the actual request to Media Services to apply your Transform to a given input video or audio content. 
        /// The Job specifies information like the location of the input video, and the location for the output. 
        /// In this example, the input video is coming from the specified HTTPS URL. You can also specify an Azure Blob SAS URL, or S3 tokenized URL. 
        /// Media Services also allows you to ingest from any existing content in Azure Storage or directly from your machine using the Storage APIs and an Asset.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="transformName"></param>
        /// <param name="jobName"></param>
        /// <returns></returns>
        private static Job SubmitJob(IAzureMediaServicesClient client, string transformName, string jobName, JobInput jobInput, string outputAssetName)
        {
            JobOutput[] jobOutputs =
            {
                new JobOutputAsset(outputAssetName),
            };

            Job job = client.Jobs.CreateOrUpdate(
                transformName,
                jobName,
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs,
                });

            return job;
        }


        /// <summary>
        /// The job takes some time to complete and when it does you want to be notified. 
        /// There are different options to get notified about the job completion. The simplest option (that is shown here) is to use polling. 
        /// Polling is not a recommended best practice for production applications. Developers should instead use Event Grid.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="transformName"></param>
        /// <param name="jobName"></param>
        /// <returns></returns>
        private static Job WaitForJobToFinish(IAzureMediaServicesClient client, string transformName, string jobName)
        {
            const double TimeoutSeconds = 10 * 60;
            const int SleepInterval = 15 * 1000;

            Job job = null;
            bool exit = false;
            DateTime timeout = DateTime.Now.AddSeconds(TimeoutSeconds);

            do
            {
                job = client.Jobs.Get(transformName, jobName);

                if (job.State == JobState.Finished || job.State == JobState.Error || job.State == JobState.Canceled)
                {
                    exit = true;
                }
                else if (DateTime.Now >= timeout)
                {
                    Console.WriteLine($"Job {job.Name} timed out.");
                }
                else
                {
                    System.Threading.Thread.Sleep(SleepInterval);
                }

                Console.WriteLine(job.State);
            }
            while (!exit);

            return job;
        }

        private static void DownloadResults(IAzureMediaServicesClient client, string assetName, string resultsFolder)
        {
            ListContainerSasInput parameters = new ListContainerSasInput(permissions: AssetContainerPermission.Read, expiryTime: DateTimeOffset.UtcNow.AddHours(1));
            AssetContainerSas assetContainerSas = client.Assets.ListContainerSas(assetName, parameters);

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
    }
}
