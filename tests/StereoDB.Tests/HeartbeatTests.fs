module Tests.HeartbeatTests

open System.Collections.Concurrent
open System.Threading.Tasks

open Swensen.Unquote
open Xunit

open Confluent.Kafka

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper
open System
open System.Threading

[<Fact>]
[<Trait("CI", "disable")>]
let ``Heartbeat is sent on db init`` () = task {
    let settings = StereoDbSettings.OnlyInMemory

    use! db = StereoDb.init(Schema(), settings)
    use kafkaConsumer = ConsumerBuilder<string, string>(
        ConsumerConfig(
            BootstrapServers = settings.HeartbeatConfig.KafkaBootstrapServers,
            AutoOffsetReset = AutoOffsetReset.Latest,
            GroupId = Random.Shared.NextInt64().ToString())).Build()

    use cts = new CancellationTokenSource()
    cts.CancelAfter(TimeSpan.FromSeconds(10)) // wait for heartbeat messages to be sent

    let tcs = TaskCompletionSource()
    let heartbeatMessages = ConcurrentBag<string>()

    task {
        kafkaConsumer.Subscribe(settings.HeartbeatConfig.KafkaHeartbeatTopic(settings.ClusterId))
        try
            while not cts.IsCancellationRequested do
                do! Task.Yield()
                let msg = kafkaConsumer.Consume(cts.Token)
                heartbeatMessages.Add(msg.Message.Value)
        with
            _ ->
                kafkaConsumer.Close()
                tcs.SetResult()                
    } |> ignore

    do! tcs.Task

    test <@ heartbeatMessages.Count > 0 @>
}
