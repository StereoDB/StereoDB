module StereoDB.Infra.Heartbeat

open System

type HeartbeatConfig = {
    KafkaBootstrapServers: string
    KafkaHeartbeatTopicPrefix: string
    KafkaHeartbeatTopicRetention: TimeSpan
    HeartbeatInterval: TimeSpan
}
with
    static member Default = {
        KafkaBootstrapServers = "localhost:19092"
        KafkaHeartbeatTopicPrefix = "stereodb_cluster"
        KafkaHeartbeatTopicRetention = TimeSpan.FromSeconds 10
        HeartbeatInterval = TimeSpan.FromSeconds 5
    }

    member this.KafkaHeartbeatTopic(clusterId: string) = $"{this.KafkaHeartbeatTopicPrefix}{clusterId}"
