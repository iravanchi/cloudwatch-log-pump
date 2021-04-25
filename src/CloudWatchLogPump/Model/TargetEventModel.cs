using System;
using System.Text.Json.Serialization;

namespace CloudWatchLogPump.Model
{
    public class TargetEventModel
    {
        [JsonPropertyName("pump_subscription_id")] public string PumpSubscriptionId { get; set; }
        [JsonPropertyName("pump_subscription_data")] public string PumpSubscriptionData { get; set; }
        [JsonPropertyName("pump_in_batch_seq")] public long PumpInputBatchSequence { get; set; }
        [JsonPropertyName("pump_in_event_seq")] public long PumpInputEventSequence { get; set; }
        [JsonPropertyName("pump_out_batch_seq")] public long PumpOutputBatchSequence { get; set; }
        [JsonPropertyName("pump_out_event_seq")] public long PumpOutputEventSequence { get; set; }
        
        [JsonPropertyName("cloudwatch_region")] public string CloudWatchRegion { get; set; }
        [JsonPropertyName("cloudwatch_event_id")] public string CloudWatchEventId { get; set; }
        [JsonPropertyName("cloudwatch_ingestion_time")] public DateTimeOffset CloudWatchIngestionTime { get; set; }
        [JsonPropertyName("cloudwatch_timestamp")] public DateTimeOffset CloudWatchTimestamp { get; set; }
        [JsonPropertyName("cloudwatch_log_group")] public string CloudWatchLogGroup { get; set; }
        [JsonPropertyName("cloudwatch_log_stream")] public string CloudWatchLogStream { get; set; }
        [JsonPropertyName("cloudwatch_message")] public string CloudWatchMessage { get; set; }
    }
}