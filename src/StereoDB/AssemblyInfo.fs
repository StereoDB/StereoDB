namespace StereoDB

open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo("StereoDB.Tests")>]
[<assembly: InternalsVisibleTo("StereoDB.Benchmarks")>]
[<assembly: InternalsVisibleTo("StereoDB.Sql")>]
[<assembly: InternalsVisibleTo("StereoDB.Sql.Tests")>]

do()

