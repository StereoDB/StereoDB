module StereoDB.Infra.Heartbeat
open System

type HeartbeatConfig = {
    KafkaBootstrapServers: string
    KafkaHeartbeatTopicPrefix: string
    HeartbeatInterval: TimeSpan
}
with
    static member Default = {
        KafkaBootstrapServers = "localhost:9092"
        KafkaHeartbeatTopicPrefix = "stereodb_cluster"
        HeartbeatInterval = TimeSpan.FromSeconds(5)
    }

    member this.KafkaHeartbeatTopic(clusterId: string) = $"{this.KafkaHeartbeatTopic}_{clusterId}"