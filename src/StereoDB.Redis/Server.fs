module StereoDB.Redis.Server

open System.Net
open System.Net.Sockets
open System.Threading.Tasks
open System.IO
open System.Threading
open StereoDB
open StereoDB.FSharp
open StereoDB.Redis.Schema

let mutable clientTaskList : (int * CancellationTokenSource * Task * NetworkStream) list = []

let addClientTask cl = clientTaskList <- cl :: clientTaskList

let writeToClient cl msg =
    let streamWriter = new StreamWriter(stream= cl)
    async{
            do! streamWriter.WriteLineAsync(msg.ToString()) |> Async.AwaitIAsyncResult |> Async.Ignore
            do! streamWriter.FlushAsync() |> Async.AwaitIAsyncResult |> Async.Ignore
    }

let removeFirst pred list = 
    let rec removeFirstTailRec p l acc =
        match l with
        | [] -> acc |> List.rev
        | h::t when p h -> (acc |> List.rev) @ t
        | h::t -> removeFirstTailRec p t (h::acc)
    removeFirstTailRec pred list []

type RedisValue = 
| Nil
| String of string
| Error of string
| Ok

let serializeValue value =
    match value with
    | Nil -> "$-1"
    | String v -> v.Length.ToString() + "\r\n" + v
    | Error v -> "-ERR " + v
    | Ok -> "+OK"

let db = StereoDb.create(Schema(), StereoDbSettings.Default)
let listenForMessages clientId endpoint cl = 
    let listenWorkflow = 
        async { 
            use reader = new System.IO.StreamReader(stream = cl)
            try 
                while true do
                    let! line = reader.ReadLineAsync() |> Async.AwaitTask
                    let response = 
                        match line.Split(' ') with
                        | [|"GET"|] ->  Error "wrong number of arguments for 'get' command"
                        | [|"GET"; key |] ->
                            let result = db.ReadTransaction (fun ctx ->
                                let records = ctx.UseTable(ctx.Schema.Records.Table)
                                records.Get key
                            )
                            match result with
                            | ValueNone -> Nil
                            | ValueSome value -> String value.Value
                        | [|"SET"|] -> Error "wrong number of arguments for 'set' command"
                        | [|"SET"; _ |] -> Error "wrong number of arguments for 'set' command"
                        | [|"SET"; key; value |] -> 
                            let result = db.WriteTransaction (fun ctx ->
                                let records = ctx.UseTable(ctx.Schema.Records.Table)
                                records.Set { Id=key; Value = value}
                            )
                            Ok
                        | _ -> Error "unknown command '1', with args beginning with:"
                    do! writeToClient cl response
            with _ -> let client = clientTaskList |> List.find (fun (id, _, _, _) -> id = clientId)
                      let  _, cts, task, stream = client
                      cts.Cancel()
                      task.Dispose()
                      stream.Dispose()
                      clientTaskList <- clientTaskList |> removeFirst (fun (id, _, _, _) -> id = clientId)
                      printfn "%s disconnected" endpoint
        }
    listenWorkflow

let listen port = 
    let mutable id = 0
    let listenWorkflow =  
        async { 
            let listener = new TcpListener(IPAddress.Any, port)
            listener.Start()
            printfn "Start listening on port %i" port
            while true do
                let! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
                let endpoint = (client.Client.RemoteEndPoint :?> IPEndPoint).Address.ToString()
                id <- id + 1
                printfn "Client connected: %A, id: %d" endpoint id
                let cts = new CancellationTokenSource();
                let clientListenTask = Async.StartAsTask (listenForMessages id endpoint (client.GetStream()), cancellationToken = cts.Token)
               
                addClientTask (id, cts, clientListenTask, client.GetStream())
        }
    Async.StartAsTask listenWorkflow
