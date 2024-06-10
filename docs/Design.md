# Orcanode Monitor Design

![Orcanode Monitor Architecture](OrcanodeMonitorArchitecture.png "Orcanode Monitor Architecture")

## Backend Monitoring Logic

The web service will be deployed as an azurewebsites.net service.  Periodically, at a frequency that can be
configured by an administrator, the service will do the following:

1. Enumerate the orcanodes listed at
   [https://apps.dataplicity.com/devices](https://docs.dataplicity.com/reference/devicessearch) and update the
   internal list of nodes, tracking the "name" and "online" and various other attributes, for each orcanode.

2. For each orcanode found in step 1:

   a. Update the state of the orcanode in stable storage.  Other internal modules can register for notifications

3. Enumerate the orcanodes listed at https://live.orcasound.net/api/json/feeds and update the internal list
   of nodes, tracking the “node_name” and “bucket”, for each orcanode.

4. For each orcanode found in step 3:

   a. Query the latest timestamp by fetching “https://{bucket}.s3.amazonaws.com/{node_name}/latest.txt”
      (e.g., https://streaming-orcasound-net.s3.amazonaws.com/rpi_orcasound_lab/latest.txt for the Orcasound
      Lab node).  This could possibly be optimized by storing the Last-Modified header value and using
      If-Modified-Since in subsequent queries, but since the content is so small the optimization does not
      seem worth it.

   b. If the timestamp is new, query the manifest file by fetching
      “https://{bucket}.s3.amazonaws.com/{node_name}/hls/{timestamp}/live.m3u8”
      (e.g., https://streaming-orcasound-net.s3.amazonaws.com/rpi_port_townsend/hls/1717439421/live.m3u8
      for the Port Townsend node).

   c. Update the state of the orcanode in stable storage.  Other internal modules can register for notifications
      of changes to this state.

   d. Query aifororcas to find the last detection by fetching
      "https://aifororcasdetections.azurewebsites.net/api/detections?Page=1&SortBy=timestamp&SortOrder=desc&Timeframe=all&Location={name}&RecordsPerPage=1", where "{name}" is the display name of the node. Note: AIforOrcas
      still uses "Haro Strait" instead of "Orcasound Lab" so this exception will be manually coded until the
      node name is corrected.

The following state will be stored per orcanode:

  * **DisplayName**: The human-readable name to display on the web page.  This name is derived from the names
    obtained from Dataplicity and live.orcasound.net.

  * **OrcasoundName**: The human-readable name used by live.orcasound.net.

  * **S3NodeName**: The URI path component from the “node_name” obtained from live.orcasound.net.

  * **S3Bucket**: The hostname component from the “bucket” obtained from live.orcasound.net.

  * **OrcasoundSlug**: The URI path component from the “slug” obtained from live.orcasound.net.

  * **LatestRecordedUtc**: The timestamp recorded in the latest.txt file on S3.

  * **LatestUploadedUtc**: The Last-Modified timestamp on the latest.txt file as recorded on S3.

  * **ManifestUpdatedUtc**: The Last-Modified timestamp on the manifest file as recorded by S3.

  * **LastCheckedUtc**: The last time the S3 instance was queried, as recorded by Orcanode Monitor.

  * **DataplicityName**: The value of the "name" field obtained from Dataplicity.

  * **DataplicityDescription**: The value of the "description" field obtained from Dataplicity.

  * **AgentVersion**: The version of the agent software running on the node, as obtained from Dataplicity.

  * **DiskCapacity**: The disk capacity of the node, as obtained from Dataplicity.

  * **DiskUsed**: The amount of disk used on the node, as obtained from Dataplicity.

  * **DataplicityUpgradeAvailable**: The value of the "upgrade_available" field obtained from Dataplicity.

  * **DataplicityOnline**: The value of the "online" field obtained from Dataplicity.

  * **LastOrcaHelloDetectionTimestamp**: The time at which OrcaHello last generated a detection for this node.

  * **LastOrcaHelloDetectionConfidence**: The confidence of the last OrcaHello detection.

  * **LastOrcaHelloDetectionComments**: The comments on the last OrcaHello detection.

  * **LastOrcaHelloDetectionFound**: The value of the "found" field obtained from OrcaHello.

### Configured parameters

**ORCASOUND_POLL_FREQUENCY_IN_MINUTES**: Service will poll each orcanode at the configured frequency. Default: 5

**ORCASOUND_MAX_UPLOAD_DELAY_MINUTES**: If the manifest file is older than this, the node will be considered offline. Default: 2

## Web page front end

The web service exposes a web page that displays, for each node, the current state of the nodes, and a list of
recent events (i.e., state changes in nodes).  In the future, it could potentially also show the % uptime of each
node over some time period.

## If-This-Then-That (IFTTT) Integration

The service will act as a "Shim App" in the [IFTTT Architecture](https://ifttt.com/docs/process_overview):

![IFTTT Architecture](https://web-assets.ifttt.com/packs/media/docs/architecture_diagram-731615e48160fd6438d2.png "IFTTT Architecture")

and expose endpoints for an [IFTTT Service API](https://ifttt.com/docs/api_reference).  For the
present, no authentication will be required since all state is read-only and public information.

An IFTTT-compatible service can implement:

 * **Triggers**: These are HTTPS API endpoints the service exposes that IFTTT can poll to fetch events
   that can be used to trigger IFTTT Applets. IFTTT docs explain that “IFTTT will fire an Applet’s
   action for each new item returned by the trigger.  Events should remain on the timeline indefinitely
   and should not expire, although they may roll off the bottom of the list once the timeline exceeds 50 items.”

 * **Actions**: These are HTTPS API endpoints the service exposes that IFTTT can call to cause actions
   to occur as directed by an IFTTT Applet.  Currently, this capability will not be used.

 * **Realtime notifications**: These are HTTP API endpoints that IFTTT exposes that the service can call
   to notify IFTTT that new information is available at a trigger API the service exposes.  In other words,
   when something critical changes, the [IFTTT Realtime API](https://ifttt.com/docs/api_reference#realtime-api)
   can be used to notify IFTTT that new information is available.  This will cause IFTTT to call the
   service’s trigger API to fetch the latest data.

### Configured parameters

**api_url_prefix**: The URI prefix to use for exposing the IFTTT Service API

## Azure web service

### Production server

The production server is online at: https://orcanodemonitor.azurewebsites.net/

The production server is automatically updated if a Git tag with a [SemVer](semver.org) format version
(e.g., "v0.1.0") is pushed to GitHub.

### Staging server

The staging server is online at: https://orcanodemonitorstaging.azurewebsites.net/

The staging server is automatically updated every time a GitHub pull request is merged.
