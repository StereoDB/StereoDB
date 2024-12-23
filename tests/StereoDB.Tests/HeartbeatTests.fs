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

[<Fact(Skip = "requires local kafka")>]
// [<Fact>]
let ``Heartbeat is sent on db init`` () = task {
    let settings = StereoDbSettings.OnlyInMemory

    use! db = StereoDb.init(Schema(), settings)
    use kafkaConsumer = ConsumerBuilder<string, string>(
        ConsumerConfig(
            BootstrapServers = settings.HeartbeatConfig.KafkaBootstrapServers,
            EnableAutoCommit = true,
            GroupId = Random.Shared.NextInt64().ToString())).Build()

    use cts = new CancellationTokenSource()
    cts.CancelAfter(TimeSpan.FromSeconds(10)) // wait for heartbeat messages to be sent

    let tcs = TaskCompletionSource()
    let heartbeatMessages = ConcurrentBag<string>()

    task {
        kafkaConsumer.Subscribe(settings.HeartbeatConfig.KafkaHeartbeatTopic(settings.ClusterId))
        while cts.IsCancellationRequested do
            let msg = kafkaConsumer.Consume(cts.Token)
            heartbeatMessages.Add(msg.Message.Value)
        tcs.SetResult()
    } |> ignore

    do! tcs.Task
    kafkaConsumer.Close()

    test <@ heartbeatMessages.Count > 0 @>
}