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



## Required Assemblies in the project
- Microsoft.Azure.Management.Media -Version 3.0.3
- Microsoft.Rest.ClientRuntime.Azure.Authentication -Version 2.4.1

## Update the appsettings.json

To use this project, you must first update the appsettings.json with your account settings. The settings for your account can be retrieved using the following Azure CLI command in the Media Services module.

It is **recommended** to use the Azure portal for your Media Services account and navigate to the API Access menu to easily create the JSON settings needed for the appsettings.json file. In the API Access menu, first create an AAD Service Principal, and then select the v3 API version and select the JSON tab to easily copy and paste a complete appsetting.json file for this sample. 

In addition, the following bash shell script creates a service principal for the account and returns the json settings using the Azure CLI. 

    #!/bin/bash

    resourceGroup=build2018
    amsAccountName=build2018
    amsSPName=build2018AADapplication

    # Create a service principal with password and configure its access to an Azure Media Services account.
    az ams account sp create \
    --account-name $amsAccountName \
    --name $amsSPName \
    --resource-group $resourceGroup \
    --role Owner \
    --years 2 \