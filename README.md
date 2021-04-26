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
|`progressDbPath`|Path, Optional|`null`|Specifies a path in which progress of each subscription is stored to resume after restart. If left empty, progress will not be stored and all subscriptions will start based on their individual settings as if new.|
|`logFolderPath`|Path, Optional|`null`|Specifies a path in which log files are created. If left empty, no log files are created and only console is used for logging.|
|`enableDebugConsoleLog`|Boolean, Optional|`false`|If set to `true`, verbose debug log will be included in the console.|
|`enableDebugFileLog`|Boolean, Optional|`false`|If set to `true`, verbose debug log will be included in a separate log file in the path specified by `logFolderPath`, provided that the path is also included in the config.|
||


# Subscription configuration reference

## Main fields

|Field name|Type|Default|Description|
|----|----|----|----|
|`id`|String, Required, Max 100 chars|`null`|Identifier of the specific log subscription. The `id` is used as a file name to keep progress, and also included in the log output for any message regarding the particular subscription. When used in nested child subscriptions, will be concatenated with parents' `id` using "." as separator.|
|`comments`|String, Optional|`null`|Not used by the application. Can be used to include human-readable comments or documentation about each individual subscription entry.|
|`awsRegion`|String, Required|`null`|AWS region from which CloudWatch Log entries should be queried for this subscription. Should be standard AWS region codes (eg. `us-west-2` or `ca-central-1`).|
|`children`|Array, Optional|Empty|Can be used to re-use configuration settings for any number of subscriptions without the need to repeat them. If specified, current subscription entry will not be processed, rather all children of this entry will be treated as subscriptions. Elements of the array are exactly similar to subscription configuration including this very field, which can be used to nest subscriptions to any number of levels.|
||



## Specifying log groups

Either of these two fields (not both) can be used to specify which log group(s)
on AWS CloudWatch Logs should be queried for events. This is a required part
of the configuration, and exactly one of these two fields should be specified.

If a pattern is specified here, list of all of the log groups in the AWS region
is enumerated and matched against the regular expression, and the subscription
entry is copied and expanded to cover all matching log groups. The log group
name is appended to the `id` field when expanding to make it unique. If no
log groups match the pattern, the subscription entry will be ignored without
any errors.

|Field name|Type|Default|Description|
|----|----|----|----|
|`logGroupName`|String|`null`|Specifies the exact name of the log group in CloudWatch Logs to query and fetch events from.|
|`logGroupPattern`|Regex|`null`|Specifies a pattern, in the form of a regular expression, to match against all log groups of the AWS region to determine which log groups need to be queried for events. Can match any number of log groups. Matching is case insensitive, and is not limited to full name, so you may want to consider using ^ / $ to match beginning / end of log group name.|
||



## Specifying time range

These fields can be used to limit the time range of events queried from
CloudWatch. For each start / end time, either absolute or relative time
can be specified, not both. All of these fields are optional.

Please note:

* If start time is not specified, current system time is used as start time 
  and events created prior to application start up will be ignored.
* If `progressDbPath` is specified in the root configuration, and this
particular `id` has an entry (has been active in previous runs of the 
application), start time specified here is ignored and the application will
resume previous progress.
* If you want the subscription to start from the configured start time again, 
regardless of previous progress, remove the file named after specific
subscription's `id` from `progressDbPath`.
* If end time is not specified, application will keep querying for new events
as long as it's running, and will retrieve any newly added events to the
CloudWatch Log stream.
* If end time is specified, no more events will be fetched once the
progress of fetch reaches the specified time.


|Field name|Type|Default|Description|
|----|----|----|----|
|`startTimeIso`|ISO-8601 Date and Time, Optional|`null`|Absolute time instant in UTC to start fetching events from. Example: `'2021-01-15T15:30:00Z'`|
|`startTimeSecondsAgo`|Integer, Optional|`null`|Relative time instant to start fetching events from, specified as the number of seconds before application startup. Negative values can be used to denote a time instant in the future.|
|`endTimeIso`|ISO-8601 Date and Time, Optional|`null`|Absolute time instant in UTC to stop fetching events when reached. Example: `'2021-04-30T23:59:59Z'`|
|`endTimeSecondsAgo`|Integer, Optional|`null`|Relative time instant to stop fetching events when reached, specified as the number of seconds before application startup. Negative values can be used to denote a time instant in the future.|
||


## Filtering events / log streams

You can filter-in which log streams or events within the log group 
you want to query and fetch, using AWS CloudWatch API features. If none of
the following fields are used, all of the events will be included from all
log streams in the log group.

All fields are passed to AWS CloudWatch Logs `FilterLogEvents` API
([documentation](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/CloudWatchLogs/MICloudWatchLogsFilterLogEventsFilterLogEventsRequest.html))
 as a
`FilterLogEventsRequest` object, for which you can see the documentation
[here](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/CloudWatchLogs/TFilterLogEventsRequest.html).

|Field name|Type|Default|Description|
|----|----|----|----|
|`logStreamNamePrefix`|String, Optional|`null`|Passed as `LogStreamNamePrefix` property when calling AWS API. If specified, only those log streams whose name start with the specified string will be queried for events. Cannot be set in conjunction with `logStreamNames` setting.|
|`logStreamNames`|Array, Optional|Empty|Array of strings passed as `LogStreamNames` property when calling AWS API. If set, only events from specified log streams will be queried. Cannot be set in conjunction with `logStreamNamePrefix` setting.|
|`eventFilterPattern`|String, Optional|`null`|Passed to `FilterPattern` property for AWS API call. If not set, all events are retrieved. For pattern language, see [Filter and Pattern Syntax](https://docs.aws.amazon.com/AmazonCloudWatch/latest/logs/FilterAndPatternSyntax.html) in AWS documentation.|
||


## Fetch batch size and intervals

These settings control how often a call to AWS CloudWatch Log API is performed
to fetch logs, and how large can the fetched data size be.

You can use these settings to control liveliness of event fetch and streaming,
and also control how frequent CloudWatch APIs are called to ensure you don't
hit API Call quota limits frequently. It's recommended to review [CloudWatch Logs quotas](https://docs.aws.amazon.com/AmazonCloudWatch/latest/logs/cloudwatch_limits_cwl.html) to understand the quotas.

Note that all subscriptions specified in the application work independently
and in parallel, so you have to take all different log groups when counting
toward your allowed quota.

If AWS API call quota is reached, the application will detect it and slow down API calls using exponential back-off retry algorithm.


|Field name|Type|Default|Description|
|----|----|----|----|
|`readMaxBatchSize`|Integer, Optional|10000|Maximum number of events fetched in one single API call. Acceptable range is 1 to 10000. Note that reaching the maximum is not guaranteed even if there is enough events to fetch, since AWS also enforces payload size limits.|
|`minIntervalSeconds`|Integer, Optional|15|Minimum time window to query events for, in seconds, if the end time is not limited. For example when set to 15 seconds, and the previous call retrieved events up to 10 seconds ago, the application will wait at least another 5 seconds to query for the new events. Increasing this number for live streaming subscriptions will increase latency but decrease number of API calls to AWS CloudWatch.|
|`maxIntervalSeconds`|Integer, Optional|300|Maximum time window to query at a time for events, specified in number of seconds. Each interval is queried repeatedly until all events are fetched, before moving to the next interval. Order of events fetched and passed to target HTTP endpoint is not guaranteed in each interval.|
|`clockSkewProtectionSeconds`|Integer, Optional|15|Allows a difference of up to specified number of seconds between AWS Clock and system clock on the machine running the application, without loosing any events. Increasing this number will also increase latency in live event streaming, but makes system more resilient to clock skews.|
||



## Target specification

These settings specify where, and how, should the events retrieved from
CloudWatch Logs to be sent.

|Field name|Type|Default|Description|
|----|----|----|----|
|`targetUrl`|String, Required|`null`|Specifies an HTTP or HTTPS address to which requests will be sent, each time a batch of events is ready.|
|`targetTimeoutSeconds`|Integer, Optional|60|HTTP call timeout for `targetUrl`|
|`targetMaxBatchSize`|Integer, Optional|1|Number of events to be included in each HTTP request. If set to 1 (or left unset), one HTTP request will be made for each fetched event, which can be sub-optimal. If the target system can handle multiple events in a batch, consider increasing this number to improve performance.|
|`targetSubscriptionData`|String, Optional|`null`|Additional information to be passed to the target system. Any string specified here, will be included in each event JSON sent to target, in the `pump_subscription_data` field. See "Target JSON Schema" below for more information.|
||


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
|`pump_subscription_id`|String|The `id` field of the subscription configuration, which ended up sending this event to target.|
|`pump_subscription_data`|String|Custom data specified in `targetSubscriptionData` field in subscription configuration.|
|`pump_in_batch_seq`|Integer|Sequence number of input batch of events read from CloudWatch. Starts with 1 everytime the application is started, and increases each time a call to CloudWatch API is made and successfully  retrieved a batch of events.|
|`pump_in_event_seq`|Integer|Sequence number of events in each batch of events read from CloudWatch. Each time an API call is made to read events from CloudWatch API, the sequence is reset to 1 and all messages in that input batch is numbered according to order messages returned from CloudWatch.|
|`pump_out_batch_seq`|Integer|Sequence number of output batch for a given input batch. If the target batch size is smaller than the number of events retrieved in an API call, output batches will be number from 1 onwards and the number resets for every input batch.|
|`pump_out_event_seq`|Integer|Sequence number of each event in one single output batch. For each output batch, sent to target as an HTTP request, all events are numbered from 1 onwards, and the number resets to one for each output batch.|
|`cloudwatch_region`|String|The AWS region from which the event is read.|
|`cloudwatch_event_id`|String|Event ID assigned by CloudWatch to each single event, and returned when fetching event information. Can be used to identify each message in CloudWatch uniquely.|
|`cloudwatch_ingestion_time`|DateTimeOffset|Date and time that the event is ingested by CloudWatch Logs.|
|`cloudwatch_timestamp`|DateTimeOffset|Date and time specified on the event, as the timestamp of event creation.|
|`cloudwatch_log_group`|String|Name of the log group in CloudWatch Logs that the event is queried and read from|
|`cloudwatch_log_stream`|String|Name of the log stream in CloudWatch Logs that the event is queried and read from|
|`cloudwatch_message`|String|Full payload of the event exactly as it is read from CloudWatch|
||



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



# AWS Authentication and Authorization

The application will use the credentials set into the context to connect
to AWS and authenticate. Preferred and most secure method is to deploy the 
application in the AWS infrastructure (eg. on EC2 or ECS) and use Roles to 
grant access to CloudWatch Logs to the system running the application.

Alternatively, you can use current user profile to authenticate, which
can be a bit tricky given the authentication should happen inside the Docker
container. Easiest way is to use Docker volume mappings 
(`-v` in `docker run` command) to map you current authenticated credentials 
and profile (typically in `~/.aws`) into the container's `~/.aws` to use 
the same credentials as yourself.



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

