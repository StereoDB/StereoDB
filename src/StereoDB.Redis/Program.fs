open StereoDB.Redis

[<EntryPoint>]
let main argv = 
    
    let listenTask = Server.listen(6379)

    System.Console.ReadLine() |> ignore
    0