using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Common_Utils;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EncryptWithAES
{
    class Program
    {
        private const string AdaptiveStreamingTransformName = "MyTransformWithAdaptiveStreamingPreset";
        private const string OutputFolderName = @"Output";

        // Set this variable to true if you want to authenticate Interactively through the browser using your Azure user account
        private const bool UseInteractiveAuth = false;
        private const string SourceUri = "https://nimbuscdn-nimbuspm.streaming.mediaservices.windows.net/2b533311-b215-4409-80af-529c3e853622/Ignite-short.mp4";
        private static readonly string Issuer = "myIssuer";
        private static readonly string Audience = "myAudience";
        private static byte[] TokenSigningKey = new byte[40];
        private static readonly string ContentKeyPolicyName = "SharedContentKeyPolicyUsedByAllAssets";

        public static async Task Main(string[] args)
        {
            // If Visual Studio is used, let's read the .env file which should be in the root folder (same folder than the solution .sln file).
            // Same code will work in VS Code, but VS Code uses also launch.json to get the .env file.
            // You can create this ".env" file by saving the "sample.env" file as ".env" file and fill it with the right values.
            try
            {
                DotEnv.Load(".env");
            }
            catch
            {

            }

            ConfigWrapper config = new(new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables() // parses the values from the optional .env file at the solution root
                .Build());


            try
            {
                await RunAsync(config);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"{exception.Message}");

                if (exception.GetBaseException() is ErrorResponseException apiException)
                {
                    Console.Error.WriteLine(
                        $"ERROR: API call failed with error code '{apiException.Body.Error.Code}' and message '{apiException.Body.Error.Message}'.");
                }
            }

            Console.WriteLine("Press Enter to continue.");
            Console.ReadLine();
        }

        /// <summary>
        /// Run the sample async.
        /// </summary>
        /// <param name="config">The parm is of type ConfigWrapper. This class reads values from local configuration file.</param>
        /// <returns></returns>
        // <RunAsync>
        private static async Task RunAsync(ConfigWrapper config)
        {
            IAzureMediaServicesClient client;
            try
            {
                client = await Authentication.CreateMediaServicesClientAsync(config, UseInteractiveAuth);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("TIP: Make sure that you have filled out the appsettings.json file before running this sample.");
                Console.Error.WriteLine($"{e.Message}");
                return;
            }

            // Set the polling interval for long running operations to 2 seconds.
            // The default value is 30 seconds for the .NET client SDK
            client.LongRunningOperationRetryTimeout = 2;

            // Creating a unique suffix so that we don't have name collisions if you run the sample
            // multiple times without cleaning up.
            string uniqueness = Guid.NewGuid().ToString("N");
            string jobName = $"job-{uniqueness}";
            string locatorName = $"locator-{uniqueness}";
            string outputAssetName = $"output-{uniqueness}";

            // Ensure that you have the desired encoding Transform. This is really a one time setup operation.
            _ = await GetOrCreateTransformAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName);

            // Output from the encoding Job must be written to an Asset, so let's create one
            Asset outputAsset = await CreateOutputAssetAsync(client, config.ResourceGroup, config.AccountName, outputAssetName);

            _ = await SubmitJobAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, outputAsset.Name, jobName);

            // In this demo code, we will poll for Job status
            // Polling is not a recommended best practice for production applications because of the latency it introduces.
            // Overuse of this API may trigger throttling. Developers should instead use Event Grid.
            Job job = await WaitForJobToFinishAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, jobName);

            if (job.State == JobState.Finished)
            {
                Console.WriteLine("Job finished.");
                if (!Directory.Exists(OutputFolderName))
                    Directory.CreateDirectory(OutputFolderName);

                // Set a token signing key that you want to use
                TokenSigningKey = Convert.FromBase64String(config.SymmetricKey);

                // Create the content key policy that configures how the content key is delivered to end clients
                // via the Key Delivery component of Azure Media Services.
                _ = await GetOrCreateContentKeyPolicyAsync(client, config.ResourceGroup, config.AccountName, ContentKeyPolicyName);

                StreamingLocator locator = await CreateStreamingLocatorAsync(client, config.ResourceGroup, config.AccountName, outputAsset.Name, locatorName, ContentKeyPolicyName);

                // We are using the ContentKeyIdentifierClaim in the ContentKeyPolicy which means that the token presented
                // to the Key Delivery Component must have the identifier of the content key in it.  Since we didn't specify
                // a content key when creating the StreamingLocator, the system created a random one for us.  In order to 
                // generate our test token we must get the ContentKeyId to put in the ContentKeyIdentifierClaim claim.
                string keyIdentifier = locator.ContentKeys.First().Id.ToString();

                string token = GetTokenAsync(Issuer, Audience, keyIdentifier, TokenSigningKey);

                string dashPath = await GetDASHStreamingUrlAsync(client, config.ResourceGroup, config.AccountName, locator.Name);

                Console.WriteLine("Copy and paste the following URL in your browser to play back the file in the Azure Media Player.");
                Console.WriteLine("Note, the player is set to use the AES token and the Bearer token is specified. ");
                Console.WriteLine();
                Console.WriteLine($"https://ampdemo.azureedge.net/?url={dashPath}&aes=true&aestoken=Bearer%3D{token}");
                Console.WriteLine();

            }

            Console.WriteLine("When finished press enter to cleanup.");
            Console.Out.Flush();
            Console.ReadLine();

            Console.WriteLine("Cleaning up...");
            await CleanUpAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, job.Name, new List<string> { outputAsset.Name }, ContentKeyPolicyName);
        }
        // </RunAsync>


        /// <summary>
        /// Create the content key policy that configures how the content key is delivered to end clients 
        /// via the Key Delivery component of Azure Media Services.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="contentKeyPolicyName">The name of the content key policy resource.</param>
        /// <returns></returns>
        // <GetOrCreateContentKeyPolicy>
        private static async Task<ContentKeyPolicy> GetOrCreateContentKeyPolicyAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string contentKeyPolicyName)
        {
            bool createPolicy = false;
            ContentKeyPolicy policy = null;
            try
            {
                policy = await client.ContentKeyPolicies.GetAsync(resourceGroupName, accountName, contentKeyPolicyName);
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                createPolicy = true;
            }

            if (createPolicy)
            {
                ContentKeyPolicySymmetricTokenKey primaryKey = new ContentKeyPolicySymmetricTokenKey(TokenSigningKey);
                List<ContentKeyPolicyRestrictionTokenKey> alternateKeys = null;
                List<ContentKeyPolicyTokenClaim> requiredClaims = new List<ContentKeyPolicyTokenClaim>()
                {
                    ContentKeyPolicyTokenClaim.ContentKeyIdentifierClaim
                };

                List<ContentKeyPolicyOption> options = new List<ContentKeyPolicyOption>()
                {
                    new ContentKeyPolicyOption(
                        new ContentKeyPolicyClearKeyConfiguration(),
                        new ContentKeyPolicyTokenRestriction(Issuer, Audience, primaryKey,
                            ContentKeyPolicyRestrictionTokenType.Jwt, alternateKeys, requiredClaims))
                };

                // Since we are randomly generating the signing key each time, make sure to create or update the policy each time.
                // Normally you would use a long lived key so you would just check for the policies existence with Get instead of
                // ensuring to create or update it each time.
                policy = await client.ContentKeyPolicies.CreateOrUpdateAsync(resourceGroupName, accountName, contentKeyPolicyName, options);

            }

            return policy;
        }
        // </GetOrCreateContentKeyPolicy>

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
        private static async Task<Transform> GetOrCreateTransformAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {
            bool createTransform = false;
            Transform transform = null;
            try
            {
                // Does a transform already exist with the desired name? Assume that an existing Transform with the desired name
                // also uses the same recipe or Preset for processing content.
                transform = client.Transforms.Get(resourceGroupName, accountName, transformName);
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                createTransform = true;
            }

            if (createTransform)
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
                transform = await client.Transforms.CreateOrUpdateAsync(resourceGroupName, accountName, transformName, output);
            }

            return transform;
        }
        // </EnsureTransformExists>


        /// <summary>
        /// Creates an ouput asset. The output from the encoding Job must be written to an Asset.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The output asset name.</param>
        /// <returns></returns>
        // <CreateOutputAsset>
        private static async Task<Asset> CreateOutputAssetAsync(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            bool existingAsset = true;
            Asset outputAsset;
            try
            {
                // Check if an Asset already exists
                outputAsset = await client.Assets.GetAsync(resourceGroupName, accountName, assetName);
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                existingAsset = false;
            }

            Asset asset = new Asset();
            string outputAssetName = assetName;

            if (existingAsset)
            {
                // Name collision! In order to get the sample to work, let's just go ahead and create a unique asset name
                // Note that the returned Asset can have a different name than the one specified as an input parameter.
                // You may want to update this part to throw an Exception instead, and handle name collisions differently.
                string uniqueness = $"-{Guid.NewGuid():N}";
                outputAssetName += uniqueness;

                Console.WriteLine("Warning – found an existing Asset with name = " + assetName);
                Console.WriteLine("Creating an Asset with this name instead: " + outputAssetName);
            }

            return await client.Assets.CreateOrUpdateAsync(resourceGroupName, accountName, outputAssetName, asset);
        }
        // </CreateOutputAsset>

        /// <summary>
        /// Submits a request to Media Services to apply the specified Transform to a given input video.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <param name="outputAssetName">The (unique) name of the  output asset that will store the result of the encoding job. </param>
        /// <param name="jobName">The (unique) name of the job.</param>
        /// <returns></returns>
        // <SubmitJob>
        private static async Task<Job> SubmitJobAsync(IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string transformName,
            string outputAssetName,
            string jobName)
        {
            // This example shows how to encode from any HTTPs source URL - a new feature of the v3 API.  
            // Change the URL to any accessible HTTPs URL or SAS URL from Azure.
            JobInputHttp jobInput =
                new JobInputHttp(files: new[] { SourceUri });

            JobOutput[] jobOutputs =
            {
                new JobOutputAsset(outputAssetName),
            };

            // In this example, we are assuming that the job name is unique.
            //
            // If you already have a job with the desired name, use the Jobs.Get method
            // to get the existing job. In Media Services v3, the Get method on entities returns null 
            // if the entity doesn't exist (a case-insensitive check on the name).
            Job job = await client.Jobs.CreateAsync(
                resourceGroup,
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
        private static async Task<Job> WaitForJobToFinishAsync(IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName,
            string jobName)
        {
            const int SleepIntervalMs = 20 * 1000;

            Job job;
            do
            {
                job = await client.Jobs.GetAsync(resourceGroupName, accountName, transformName, jobName);

                Console.WriteLine($"Job is '{job.State}'.");
                for (int i = 0; i < job.Outputs.Count; i++)
                {
                    JobOutput output = job.Outputs[i];
                    Console.Write($"\tJobOutput[{i}] is '{output.State}'.");
                    if (output.State == JobState.Processing)
                    {
                        Console.Write($"  Progress (%): '{output.Progress}'.");
                    }

                    Console.WriteLine();
                }

                if (job.State != JobState.Finished && job.State != JobState.Error && job.State != JobState.Canceled)
                {
                    await Task.Delay(SleepIntervalMs);
                }
            }
            while (job.State != JobState.Finished && job.State != JobState.Error && job.State != JobState.Canceled);

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
        private static async Task<StreamingLocator> CreateStreamingLocatorAsync(
            IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string assetName,
            string locatorName,
            string contentPolicyName)
        {
            StreamingLocator locator = await client.StreamingLocators.CreateAsync(
                resourceGroup,
                accountName,
                locatorName,
                new StreamingLocator
                {
                    AssetName = assetName,
                    StreamingPolicyName = PredefinedStreamingPolicy.ClearKey,
                    DefaultContentKeyPolicyName = contentPolicyName
                });

            return locator;
        }
        // </CreateStreamingLocator>

        /// <summary>
        /// Create a token that will be used to protect your stream.
        /// Only authorized clients would be able to play the video.  
        /// </summary>
        /// <param name="issuer">The issuer is the secure token service that issues the token. </param>
        /// <param name="audience">The audience, sometimes called scope, describes the intent of the token or the resource the token authorizes access to. </param>
        /// <param name="keyIdentifier">The content key ID.</param>
        /// <param name="tokenVerificationKey">Contains the key that the token was signed with. </param>
        /// <returns></returns>
        // <GetToken>
        private static string GetTokenAsync(string issuer, string audience, string keyIdentifier, byte[] tokenVerificationKey)
        {
            var tokenSigningKey = new SymmetricSecurityKey(tokenVerificationKey);

            SigningCredentials cred = new SigningCredentials(
                tokenSigningKey,
                // Use the  HmacSha256 and not the HmacSha256Signature option, or the token will not work!
                SecurityAlgorithms.HmacSha256,
                SecurityAlgorithms.Sha256Digest);

            Claim[] claims = new Claim[]
            {
                new Claim(ContentKeyPolicyTokenClaim.ContentKeyIdentifierClaim.ClaimType, keyIdentifier)
            };

            JwtSecurityToken token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.Now.AddMinutes(-5),
                expires: DateTime.Now.AddMinutes(60),
                signingCredentials: cred);

            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();

            return handler.WriteToken(token);
        }
        // </GetToken>

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
        // <GetMPEGStreamingUrl>
        private static async Task<string> GetDASHStreamingUrlAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string locatorName)
        {
            const string DefaultStreamingEndpointName = "default";
            string dashPath = "";

            StreamingEndpoint streamingEndpoint = await client.StreamingEndpoints.GetAsync(resourceGroupName, accountName, DefaultStreamingEndpointName);

            if (streamingEndpoint.ResourceState != StreamingEndpointResourceState.Running)
            {
                await client.StreamingEndpoints.StartAsync(resourceGroupName, accountName, DefaultStreamingEndpointName);
            }

            ListPathsResponse paths = await client.StreamingLocators.ListPathsAsync(resourceGroupName, accountName, locatorName);

            foreach (StreamingPath path in paths.StreamingPaths)
            {
                UriBuilder uriBuilder = new UriBuilder
                {
                    Scheme = "https",
                    Host = streamingEndpoint.HostName
                };

                // Look for just the DASH path and generate a URL for the Azure Media Player to playback the content with the AES token to decrypt.
                // Note that the JWT token is set to expire in 1 hour. 
                if (path.StreamingProtocol == StreamingPolicyStreamingProtocol.Dash)
                {
                    uriBuilder.Path = path.Paths[0];

                    dashPath = uriBuilder.ToString();

                }
            }
            return dashPath;
        }
        // </GetMPEGStreamingUrl>


        /// <summary>
        ///  Downloads the results from the specified output asset, so you can see what you got.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The output asset.</param>
        /// <param name="outputFolderName">The name of the folder into which to download the results.</param>
        // <DownloadResults>
        private static async Task DownloadOutputAssetAsync(
            IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string assetName,
            string outputFolderName)
        {
            if (!Directory.Exists(outputFolderName))
            {
                Directory.CreateDirectory(outputFolderName);
            }

            AssetContainerSas assetContainerSas = await client.Assets.ListContainerSasAsync(
                resourceGroup,
                accountName,
                assetName,
                permissions: AssetContainerPermission.Read,
                expiryTime: DateTime.UtcNow.AddHours(1).ToUniversalTime());

            Uri containerSasUrl = new Uri(assetContainerSas.AssetContainerSasUrls.FirstOrDefault());
            BlobContainerClient container = new BlobContainerClient(containerSasUrl);

            string directory = Path.Combine(outputFolderName, assetName);
            Directory.CreateDirectory(directory);

            Console.WriteLine($"Downloading output results to '{directory}'...");

            string continuationToken = null;
            IList<Task> downloadTasks = new List<Task>();

            do
            {
                var resultSegment = container.GetBlobs().AsPages(continuationToken);

                foreach (Azure.Page<BlobItem> blobPage in resultSegment)
                {
                    foreach (BlobItem blobItem in blobPage.Values)
                    {
                        var blobClient = container.GetBlobClient(blobItem.Name);
                        string filename = Path.Combine(directory, blobItem.Name);

                        downloadTasks.Add(blobClient.DownloadToAsync(filename));
                    }
                    // Get the continuation token and loop until it is empty.
                    continuationToken = blobPage.ContinuationToken;
                }

            } while (continuationToken != "");

            await Task.WhenAll(downloadTasks);

            Console.WriteLine("Download complete.");
        }
        // </DownloadResults>


        /// <summary>
        /// Deletes the jobs, assets and potentially the content key policy that were created.
        /// Generally, you should clean up everything except objects 
        /// that you are planning to reuse (typically, you will reuse Transforms, and you will persist output assets and StreamingLocators).
        /// </summary>
        /// <param name="client"></param>
        /// <param name="resourceGroupName"></param>
        /// <param name="accountName"></param>
        /// <param name="transformName"></param>
        /// <param name="jobName"></param>
        /// <param name="assetNames"></param>
        /// <param name="contentKeyPolicyName"></param>
        /// <returns></returns>
        // <CleanUp>
        private static async Task CleanUpAsync(
           IAzureMediaServicesClient client,
           string resourceGroupName,
           string accountName,
           string transformName,
           string jobName,
           List<string> assetNames,
           string contentKeyPolicyName = null
           )
        {
            await client.Jobs.DeleteAsync(resourceGroupName, accountName, transformName, jobName);

            foreach (var assetName in assetNames)
            {
                await client.Assets.DeleteAsync(resourceGroupName, accountName, assetName);
            }

            if (contentKeyPolicyName != null)
            {
                client.ContentKeyPolicies.Delete(resourceGroupName, accountName, contentKeyPolicyName);
            }
        }
        // </CleanUp>
    }
}
