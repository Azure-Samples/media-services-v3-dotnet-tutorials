---
services: media-services
platforms: dotnet-core
author: Juliako
---

# Azure Media Services v3 tutorials 

The projects in this repository were created using Visual Studio 2017. They target netcoreapp2.0. The code in the projects uses 'async main', which is avaiable starting with C# 7.1. See [this blog](https://blogs.msdn.microsoft.com/benwilli/2017/12/08/async-main-is-available-but-hidden/) for more details.

The projects in this repository support the Azure Media Services v3 articles:


|Project name|Article|
|---|---|
|UploadEncodeAndStreamFiles/UploadEncodeAndStreamFiles.csproj|[Tutorial: Upload, encode, download, and stream videos](https://docs.microsoft.com/azure/media-services/latest/stream-files-tutorial-with-api)|
|AnalyzeVideos/AnalyzeVideos.csproj|[Tutorial: Analyze videos with Media Services](https://docs.microsoft.com/azure/media-services/latest/analyze-videos-tutorial-with-api)|
|EncryptWithAES/EncryptWithAES.csproj|[Use AES-128 dynamic encryption and the key delivery service](https://docs.microsoft.com/azure/media-services/latest/protect-with-aes128)|
## Prerequisites

To run samples in this repository, you need:

* Visual Studio 2017.  
* An Azure Media Services account. See the steps described in [Create a Media Services account](https://docs.microsoft.com/azure/media-services/latest/create-account-cli-quickstart).

## NuGet packages 

The following NuGet packages were added to the project: 

|Package|Description|
|---|---|
|Microsoft.Azure.Management.Media|Azure Media Services SDK|
|Microsoft.Rest.ClientRuntime.Azure.Authentication|ADAL authentication library for Azure SDK for NET|
|Microsoft.Extensions.Configuration.EnvironmentVariables|Read configuration values from environment variables and local JSON files|
|Microsoft.Extensions.Configuration.Json|Read configuration values from environment variables and local JSON files
|WindowsAzure.Storage|Storage SDK|

## To run each project in the solution

* Clean and rebuild the solution.
* Set the desired project as the **Set as Startup project**.
* Add appropriate values to the appsettings.json configuration file. For more information, see [Access APIs](https://docs.microsoft.com/azure/media-services/latest/access-api-cli-how-to).
