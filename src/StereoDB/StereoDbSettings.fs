namespace StereoDB

open System.Runtime.InteropServices
open MessagePack

type StereoDbSettings = {    
    LocalPersistenceEnabled: bool
    EntitySerializer: IEntitySerializer
    DbFolderPath: string
}
with
    static member OnlyInMemory = {        
        LocalPersistenceEnabled = false
        EntitySerializer = Unchecked.defaultof<_>
        DbFolderPath = ""        
    }
    
    static member FileStorageMsgPack(
        [<Optional; DefaultParameterValue(null:MessagePackSerializerOptions)>] options: MessagePackSerializerOptions,
        [<Optional; DefaultParameterValue("":string)>] dbFolderPath: string) = {
        
        LocalPersistenceEnabled = true
        DbFolderPath = dbFolderPath
        EntitySerializer = {
            new IEntitySerializer with
                member this.Serialize(writer, value) = MessagePackSerializer.Serialize(writer, value, options)
                member this.Deserialize(data) = MessagePackSerializer.Deserialize(data, options)
        }        
    }