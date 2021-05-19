# Live Events and Live Outputs
This example shows how to create and use Live Events and Live Outputs in the v3 Media Services API.

Run the sample to create a new LiveEvent, configure your live encoder with the ingest RTMP feed, and preview the playback using Azure Media Player at http://ampdemo.azureedge.net

### Recommended encoder setup
It is recommended to use **OBS Studio** for this sample, as it has been tested to work with the service.  This sample assumes that you will use OBS Studio to broadcast RTMP to the ingest endpoint. Please install OBS Studio first. 

Use the following settings in OBS:
- Encoder: NVIDIA NVENC (if avail) or x264
- Rate Control: CBR
- Bitrate: 2500 Kbps (or something reasonable for your laptop)
- Keyframe Interval : 2s, or 1s for low latency  
- Preset : Low-latency Quality or Performance (NVENC) or "veryfast" using x264
- Profile: high
- GPU: 0 (Auto)
- Max B-frames: 2

    
### Overview of the code workflow

The workflow for the sample and for the recommended use of the Live API:
1. Create the client for AMS using AAD service principal or managed ID
1. Set up your IP restriction allow objects for ingest and preview
1. Configure the Live Event object with your settings. Choose pass-through or encoding channel type and size (720p or 1080p)
1. Create the Live Event without starting it
1. Create an Asset to be used for recording the live stream into
1. Create a Live Output, which acts as the "recorder" to record into the Asset (which is like the tape in the recorder).
1. Start the Live Event - this can take a little bit.
1. Get the preview endpoint to monitor in a player for DASH or HLS.
1. Get the ingest RTMP endpoint URL for use in OBS Studio. Set up OBS studio and start the broadcast.  Monitor the stream in your DASH or HLS player of choice. 
1. Create a new Streaming Locator on the recording Asset object from step 5.
1. Get the URLs for the HLS and DASH manifest to share with your audience or CMS system. This can also be created earlier after step 5 if desired.

### Creating a pass-through or live encoder type Live Event
To create a "pass-through" LiveEvent - set the encoding type on create of the LiveEvent to None

    encodingType:LiveEventEncodingType.None,


To create an encoding LiveEvent - set the encoding type on create of the LiveEvent to Standard for 720P or Premium for 1080P encoding

    encodingType:LiveEventEncodingType.Standard,   


## Update the appsettings.json

To use this project, you must first update the appsettings.json with your account settings. The settings for your account can be retrieved using the following Azure CLI command in the Media Services module.

It is **recommended** to use the Azure portal for your Media Services account and navigate to the API Access menu to easily create the JSON settings needed for the appsettings.json file. In the API Access menu, first create an AAD Service Principal, and then select the v3 API version and select the JSON tab to easily copy and paste a complete appsetting.json file for this sample. 

In addition, the following bash shell script creates a service principal for the account and returns the json settings using the Azure CLI. 

```bash
    #!/bin/bash

    resourceGroup= <your resource group>
    amsAccountName= <your ams account name>
    amsSPName= <your AAD application>

    #Create a service principal with password and configure its access to an Azure Media Services account.
    az ams account sp create
    --account-name $amsAccountName` \\
    --name $amsSPName` \\
    --resource-group $resourceGroup` \\
    --role Owner` \\
    --years 2`
```

### Optional - Use Event Grid instead of polling (recommended for production code)

* The following steps should be used if you want to test Event Grid for job monitoring. Please note, there are costs for using Event Hub. For more details, refer to [Event Hubs overview](https://azure.microsoft.com/en-in/pricing/details/event-hubs/) and [Event Hubs pricing](https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-faq#pricing).

#### Enable Event Grid resource provider

  `az provider register --namespace Microsoft.EventGrid`

#### To check if registered, run the next command. You should see "Registered".

  `az provider show --namespace Microsoft.EventGrid --query "registrationState"`

#### Create an Event Hub

```bash
  namespace=<unique-namespace-name>
  hubname=<event-hub-name>
  az eventhubs namespace create --name $namespace --resource-group <resource-group>
  az eventhubs eventhub create --name $hubname --namespace-name $namespace --resource-group <resource-group>
```

#### Subscribe to Media Services events

```bash
  hubid=$(az eventhubs eventhub show --name $hubname --namespace-name $namespace --resource-group <resource-group> --query id --output tsv)\
  
  amsResourceId=$(az ams account show --name <ams-account> --resource-group <resource-group> --query id --output tsv)\
  
  az eventgrid event-subscription create --source-resource-id $amsResourceId --name &lt;event-subscription-name&gt; --endpoint-type eventhub --endpoint $hubid
```


- Create a storage account and container for Event Processor Host if you don't have one - see [Create a Storage account for event processor host](https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-dotnet-standard-getstarted-send#create-a-storage-account-for-event-processor-host)

- Update *appsettings.json* or *.env* (at root of solution) with your Event Hub and Storage information
  - **StorageAccountName**: The name of your storage account.
  - **StorageAccountKey**: The access key for your storage account. In Azure portal "All resources", search your storage account, then click "Access keys", copy key1.
  - **StorageContainerName**: The name of your container. Click Blobs in your storage account, find you container and copy the name.
  - **EventHubConnectionString**: The Event Hub connection string. Search for your Event Hub namespace you just created. &lt;your namespace&gt; -&gt; Shared access policies -&gt; RootManageSharedAccessKey -&gt; Connection string-primary key. You can optionally create a SAS policy for the Event Hub instance with Manage and Listen policies and use the connection string for the Event Hub instance.
  - **EventHubName**: The Event Hub instance name.  &lt;your namespace&gt; -&gt; Event Hubs.