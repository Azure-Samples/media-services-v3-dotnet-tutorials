---
services: media-services
platforms: dotnet
author: Juliako
---

# Azure Media Services v3 tutorials 

The projects in this repository support the Azure Media Services v3 tutorials topics:


|Project name|Article|
|---|---|
|UploadEncodeAndStreamFiles/UploadEncodeAndStreamFiles.csproj|[Tutorial: Upload, encode, download, and stream videos](https://docs.microsoft.com/azure/media-services/latest/stream-files-tutorial-with-api)|
|AnalyzeVideos/AnalyzeVideos.csproj|[Tutorial: Analyze videos with Media Services](https://docs.microsoft.com/azure/media-services/latest/analyze-videos-tutorial-with-api)|

## Prerequisites

To run samples in this repository, you need:

* Visual Studio. If you do not have Visual Studio installed, you can get [Visual Studio Community 2017, Visual Studio Professional 2017, or Visual Studio Enterprise 2017](https://www.visualstudio.com/downloads/).
* An Azure Media Services account. See the steps described in [Create a Media Services account](https://docs.microsoft.com/azure/media-services/latest/create-account-cli-quickstart).

## Required assemblies

The following NuGet packages were added to the project: 

* Microsoft.Azure.Management.Media -Version 1.0.0
* Microsoft.IdentityModel.Clients.ActiveDirectory -Version 3.19.4
* WindowsAzure.Storage  -Version 9.1.1

## To run each project in the solution

* Clean and rebuild the solution.
* Set the desired project as the **Set as Startup project**.
* Add appropriate values to App.config. For more information, see [Access APIs](https://docs.microsoft.com/azure/media-services/latest/access-api-cli-how-to).
