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
let ``Heartbeat is sent on db init`` () = task {
    let settings = StereoDbSettings.OnlyInMemory
    use! db = StereoDb.init(Schema(), settings)
    do! Task.Delay(TimeSpan.FromSeconds(20)) // wait for heartbeat messages to be sent

    use kafkaConsumer = ConsumerBuilder(
        ConsumerConfig(
            BootstrapServers = settings.HeartbeatConfig.KafkaBootstrapServers)).Build()

    use cts = new CancellationTokenSource()
    let tcs = TaskCompletionSource()
    let heartbeatMessages = ConcurrentBag<string>()

    task {
        kafkaConsumer.Subscribe(settings.HeartbeatConfig.KafkaHeartbeatTopic(settings.ClusterId))
        while true do
            let msg = kafkaConsumer.Consume(cts.Token)
            heartbeatMessages.Add(msg.Message.Value)
    } |> ignore

    do! tcs.Task
    kafkaConsumer.Close()

    test <@ heartbeatMessages.Count > 0 @>
}