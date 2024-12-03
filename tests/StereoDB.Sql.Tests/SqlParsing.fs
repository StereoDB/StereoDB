module Tests.SqlParsing

open StereoDB
open Xunit
open Tests.TestHelper
open StereoDB.FSharp

let sqlCompilationFailure (db: IStereoDb<Schema>) (sql: string) expectedError = 
    try
        db.ExecuteNonQuery (sql)
        Assert.True(false, "Should not happens")
    with ex ->
        Assert.Equal (expectedError, ex.Message)

[<Fact>]
let ``Fails on not existing table`` () = task {
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)
    
    sqlCompilationFailure db "UPDATE NonExisting SET Quantity = 2" "Table NonExisting is not defined"
}

[<Fact>]
let ``Fails on not existing column`` () = task {
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)
    
    sqlCompilationFailure db "UPDATE Books SET NonExisting = 2" "Column NonExisting does not exist in table Books"
}