# cloudwatch-log-pump

Lightweight application that can be used to read AWS CloudWatch Log stream continuously (or for a specified time range) and
send them in HTTP payload to any endpoint over the network.

## Features

* Cross-platform (written in C# / .Net Core)
* Packed as a public Linux-based Docker image, easy to use and update
* Light weight with minimal memory / CPU footprint
* Can be used in continuous mode, or for a specific time range for events
* Stored progress allows resume after restart
* In-built resiliency - monitors and restarts worker threads if failed
* Back-pressure sensitive for target HTTP endpoint, with exponential backoff
* Understands AWS rate limit, with exponential backoff to slow down requests
* Configurable batch size, independently for reading from AWS and posting to HTTP
* Configurable timings and clock-skew protection
* Logging to console / file allows easier diagnosis
* Unlimited number of streams / log groups processing concurrently
* Hierarchical configuration allows more concise, manageable configuration files
* Supports Regex-based log group selection
* Fully open-source, MIT license

# Installation

Up-to-date Docker image is publicly available under "iravanchi/cloudwatch-log-pump"
in official Docker Hub repository. 
Configuration needs to be provided in (mounted to) `/app/appsettings.json` path.
Rest of the paths can be controlled by configuration file contents.

```
# Create configuration file
touch ~/appconfig.json

# Edit configuration file - see samples below
vim ~/appconfig.json

# Pull Docker image
docker pull iravanchi/cloudwatch-log-pump

# Run
docker run -it -v ~/appsettings.json:/app/appsettings.json iravanchi/cloudwatch-log-pump
```

If there are configuration issues, errors will be written to console.


# Configuration

Sample minimal configuration:

```
{
    "subscriptions": [
        {
            "id": "stream-1",
            "awsRegion": "ca-central-1",
            "logGroupName": "my-log-group",
            "startTimeIso": "2021-01-12T00:00:00Z",
            "minIntervalSeconds": 30,
            "maxIntervalSeconds": 7200,
            "clockSkewProtectionSeconds": 15,
            "targetUrl": "http://localhost:8080/",
            "targetTimeoutSeconds": 120,
            "targetMaxBatchSize": 200
        },
        {
            ...
        }
    ],
    "progressDbPath": "/app/progress-db",
    "logFolderPath": "/app/log",
    "enableDebugConsoleLog": false,
    "enableDebugFileLog": true
}
```

The above configuration file starts reading events from `my-log-group`
log group in AWS CloudWatch Logs in `ca-central-1` region beginning from Jan. 12 2021, and posting them to `http://localhost:8080/` address. Once it reaches the end of the stream, it will wait for new events and continuously posts them. You can have as many log groups configured as needed, under the `subscriptions` element.

To keep the progress and resume after restart, you should also map the path
specified in `progressDbPath` when running the docker image. Eg:

```
mkdir ~/progress-db
docker run ... -v ~/progress-db:/app/progress-db ...
```

Also mapping log path specified in `logFolderPath` allows you to access and keep
log files across Docker image restarts.

## Hierarchical configuration

Configuration can be specified in a hierarchy to allow re-use and more concise 
and managable configuration files when there are many streams. Here's a more complex configuration file sample:

```
{
    "subscriptions": [
        {
            "awsRegion": "ca-central-1",
            "startTimeIso": "2021-01-12T00:00:00Z",
            "minIntervalSeconds": 30,
            "maxIntervalSeconds": 7200,
            "clockSkewProtectionSeconds": 15,
            "targetUrl": "http://localhost:8080/",
            "targetTimeoutSeconds": 120,
            "targetMaxBatchSize": 200,
            "children": [
                {
                    "id": "stream-1",
                    "logGroupName": "my-log-group-1"
                }
                {
                    "id": "stream-2",
                    "logGroupName": "my-log-group-2"
                }
                {
                    "id": "stream-3",
                    "logGroupName": "my-log-group-3",
                    "startTimeIso": "2021-01-30T00:00:00Z"
                }
            ]
        }
    ],
    ...
}
```

This configuration file specifies three independent and concurrent streams,
but instead of repeating all details they just inherit them from the parent.
The parent itself will not create a stream/subscription. Any setting specified
in the child will override parents' configuration. In the above example,
`stream-1` and `stream-2` use the same `startTimeIso` as their parent, but
that setting is overriden for `stream-3`.

There can be *any number of nesting levels* in specifying subscriptions.



# Root configuration reference

|Field name|Value|Default|Description|
|----|----|----|----|
|`subscriptions`|Array, Required|Empty|Lists the log groups to watch and read, along with configuration data for each of them. See following section for more details.|
|`progressDbPath`|Path, Optional|Empty|Specifies a path in which progress of each subscription is stored to resume after restart. If left empty, progress will not be stored and all subscriptions will start based on their individual settings as if new.|
|`logFolderPath`|Path, Optional|Empty|Specifies a path in which log files are created. If left empty, no log files are created and only console is used for logging.|
|`enableDebugConsoleLog`|Boolean, Optional|`false`|If set to `true`, verbose debug log will be included in the console.|
|`enableDebugFileLog`|Boolean, Optional|`false`|If set to `true`, verbose debug log will be included in a separate log file in the path specified by `logFolderPath`, provided that the path is also included in the config.|


# Subscription configuration reference

## Main fields

|Field name|Type|Default|Description|
|----|----|----|----|
|`id`|String, Required, Max 100 chars|Empty|Identifier of the specific log subscription. The `id` is used as a file name to keep progress, and also included in the log output for any message regarding the particular subscription|
|`comments`||||
|`awsRegion`|String, Required|Empty||
|`children`||||



## Specifying log groups

|Field name|Type|Default|Description|
|----|----|----|----|
|`logGroupName`||||
|`logGroupPattern`||||



## Specifying time range

|Field name|Type|Default|Description|
|----|----|----|----|
|`startTimeIso`||||
|`endTimeIso`||||
|`startTimeSecondsAgo`||||
|`endTimeSecindsAgo`||||
|``||||



## Filtering events / log streams

You can filter-out which log streams or events within the log group 
you want to query and fetch, using AWS CloudWatch API features.

|Field name|Type|Default|Description|
|----|----|----|----|
|`logStreamNamePrefix`||||
|`logStreamNames`||||
|`eventFilterPattern`||||



## Fetch batch size and intervals

|Field name|Type|Default|Description|
|----|----|----|----|
|`readMaxBatchSize`||||
|`minIntervalSeconds`||||
|`maxIntervalSeconds`||||
|`clockSkewProtectionSeconds`||||




## Target specification

|Field name|Type|Default|Description|
|----|----|----|----|
|`targetUrl`||||
|`targetTimeoutSeconds`||||
|`targetMaxBatchSize`||||
|`targetSubscriptionData`||||



# Target HTTP Calls

Target URL specified in the configuration for each subscription is
called using `POST` as the HTTP verb, and the request body is used to
pass event data.

Event data is encoded in JSON format, and the media type header of HTTP
requests is set to `application/json` to reflect that. UTF8 encoding is
used for all string encodings.

If `targetMaxBatchSize` is specified for any number larger than 1, 
more than one JSON objects will be included in each POST request.
Each JSON object will appear on a single line.



## Target JSON schema

Each JSON object sent to target URL includes the following fields:

|Field name|Type|Description|
|----|----|----|
|`pump_subscription_id`|String||
|`pump_subscription_data`|String||
|`pump_in_batch_seq`|Integer||
|`pump_in_event_seq`|Integer||
|`pump_out_batch_seq`|Integer||
|`pump_out_event_seq`|Integer||
|`cloudwatch_region`|String||
|`cloudwatch_event_id`|String||
|`cloudwatch_ingestion_time`|DateTimeOffset||
|`cloudwatch_timestamp`|DateTimeOffset||
|`cloudwatch_log_group`|String||
|`cloudwatch_log_stream`|String||
|`cloudwatch_message`|String||




## Retry and back-pressure

If the target HTTP call returns a retryable status code, application will
wait for a random number of milliseconds with exponential back-off time, and
retry for up to 4 times. This prevents temporary outages or server restarts to
affect the event flow.

The following HTTP response statuses are considered retryable:

* 409 Conflict
* 423 Locked
* 429 Too many requests
* 500 Internal server error
* 502 Bad gateway
* 503 Service unavailable
* 504 Gateway timeout

A common pattern to implement back-pressure and control the rate of event
flow, is for the web server to return HTTP status code 429 (too many requests)
as a response when it is overwhelmed. The exponential back-off retry algorithm
responds to that, allowing the web server to digest and process ongoing
requests by slowing down exponentially until the target server can catch up.

The exponential back-off retry is applied to each individual batch of
events sent to target web server. If you find yourself encountering the 
back-pressure situation too often, it can be worth looking into batching 
configuration. Tune output batch size according to the processing power of 
the target web server.



# Using in Docker Compose

This image can easily be used in a docker-compose configuration file 
to work in conjunction with other targets that need to receive log events
(eg. logstash or elasticsearch).

Here's a docker-compose snippet you can use for easier configuration:

    cloudwatch-pump:
      image: "iravanchi/cloudwatch-log-pump"
      stdin_open: true
      tty: true
      volumes:
        - ./appsettings.json:/app/appsettings.json:ro
        - ./progress-db:/app/progress-db
        - ./logs:/app/log

