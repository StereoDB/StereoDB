namespace StereoDB.Sql

open FParsec

module internal SqlParser =

    let ws = spaces
    let str_ws s = pstring s .>> ws
    let strCI_ws s = pstringCI s .>> ws
    let float_ws = pfloat .>> ws

    let identifier =
        let isIdentifierFirstChar c = isLetter c || c = '_'
        let isIdentifierChar c = isLetter c || isDigit c || c = '_'

        many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier" .>> ws // skips trailing whitespace

    let identifier_ws = identifier .>> ws

    type SqlPrimitiveExpression = 
        | SqlFloatConstant of float
        | SqlIdentifier of string
    type SqlExpression = 
        | BinaryArithmeticOperator of SqlExpression * string * SqlExpression
        | UnaryArithmeticOperator of string * SqlExpression
        | Primitive of SqlPrimitiveExpression

    type SqlLogicalExpression =
        | BinaryLogicalOperator of SqlLogicalExpression * string * SqlLogicalExpression
        | BinaryComparisonOperator of SqlExpression * string * SqlExpression
        | UnaryLogicalOperator of string * SqlLogicalExpression
        | IsNull of SqlExpression
        | IsNotNull of SqlExpression

    type Resultset = 
        | TableResultset of string

    type SelectListItem =
        | AliasedExpression of SqlExpression * string option
    type SelectClause = SelectListItem list
    type SetListItem = string * SqlExpression
    type SetClause = SetListItem list
    type FromClause = 
        | Resultset of Resultset
    type WhereClause = 
        | WhereCondition of SqlLogicalExpression

    type Query =
    | SelectQuery of (SelectClause * (FromClause * WhereClause option) option)
    | UpdateQuery of (string * SetClause * WhereClause option)

    let SQL_CONSTANT = float_ws |>> SqlFloatConstant
    let SQL_IDENTIFIER = identifier |>> SqlIdentifier
    let SQL_EXPRESSION = SQL_CONSTANT <|> SQL_IDENTIFIER |>> Primitive

    let arithOpp = new OperatorPrecedenceParser<SqlExpression,unit,unit>()
    let arithExpr = arithOpp.ExpressionParser
    let arithExpressionTerm = (SQL_EXPRESSION) <|> between (str_ws "(") (str_ws ")") arithExpr
    arithOpp.TermParser <- arithExpressionTerm

    arithOpp.AddOperator(InfixOperator("+", ws, 1, Associativity.Left, fun x y -> BinaryArithmeticOperator(x, "+", y)))
    arithOpp.AddOperator(InfixOperator("-", ws, 1, Associativity.Left, fun x y -> BinaryArithmeticOperator(x, "-", y)))
    arithOpp.AddOperator(InfixOperator("*", ws, 2, Associativity.Left, fun x y -> BinaryArithmeticOperator(x, "*", y)))
    arithOpp.AddOperator(InfixOperator("/", ws, 2, Associativity.Left, fun x y -> BinaryArithmeticOperator(x, "/", y)))
    arithOpp.AddOperator(PrefixOperator("-", ws, 3, false, fun x -> UnaryArithmeticOperator("-", x)))

    let flatten v =
        match v with
        | ((a, b), c) -> (a, b, c) 
     //   | _ -> failwith "Invalid tuple to flatten"

    let logicOpp = new OperatorPrecedenceParser<SqlLogicalExpression,unit,unit>()
    let SQL_LOGICAL_EXPRESSION = logicOpp.ExpressionParser
    let primitiveLogicalExpression =
        (SQL_EXPRESSION .>>? strCI_ws "IS" .>>? strCI_ws "NULL" |>> IsNull)
        <|> (SQL_EXPRESSION .>>? strCI_ws "IS" .>>? strCI_ws "NOT" .>> strCI_ws "NULL" |>> IsNotNull)
        <|> (SQL_EXPRESSION .>>.? strCI_ws "=" .>>. SQL_EXPRESSION |>> flatten |>> BinaryComparisonOperator)
        <|> (SQL_EXPRESSION .>>.? strCI_ws "<>" .>>. SQL_EXPRESSION |>> flatten |>> BinaryComparisonOperator)
    let logicExpressionTerm = (primitiveLogicalExpression) <|> between (str_ws "(") (str_ws ")") SQL_LOGICAL_EXPRESSION
    logicOpp.TermParser <- logicExpressionTerm

    logicOpp.AddOperator(InfixOperator("AND", ws, 1, Associativity.Left, fun x y -> BinaryLogicalOperator(x, "+", y)))
    logicOpp.AddOperator(InfixOperator("OR", ws, 1, Associativity.Left, fun x y -> BinaryLogicalOperator(x, "-", y)))
    logicOpp.AddOperator(PrefixOperator("NOT", ws, 3, false, fun x -> UnaryLogicalOperator("NOT", x)))

    let ALIASED_EXPRESSION = SQL_EXPRESSION .>>. opt (strCI_ws "AS" >>. identifier) |>> AliasedExpression
    let SELECT_LIST = sepBy ALIASED_EXPRESSION (str_ws ",")
    let ASSIGNMENT_EXPRESSION = identifier_ws .>> str_ws "=" .>>. arithExpr |>> SetListItem
    let SET_LIST = strCI_ws "SET" >>. sepBy ASSIGNMENT_EXPRESSION (str_ws ",") //|>> SetClause
    let TABLE_RESULTSET = identifier |>> TableResultset
    let FROM_CLAUSE = strCI_ws "FROM" >>. TABLE_RESULTSET |>> Resultset
    let WHERE_CLAUSE = strCI_ws "WHERE" >>. SQL_LOGICAL_EXPRESSION |>> WhereCondition
    let SELECT_STATEMENT = 
        spaces .>> strCI_ws "SELECT" >>. SELECT_LIST .>>. 
            (opt (FROM_CLAUSE .>>. (opt WHERE_CLAUSE))) |>> SelectQuery

    let UPDATE_STATEMENT =
        spaces .>> strCI_ws "UPDATE" >>. identifier_ws .>>. SET_LIST .>>. 
        (opt (WHERE_CLAUSE)) |>> flatten |>> UpdateQuery

    let QUERY = SELECT_STATEMENT <|> UPDATE_STATEMENT

    let parseSql str =
        match run QUERY str with
        | Success(result, _, _)   ->
            result
        | Failure(errorMsg, _, _) -> failwithf "Failure: %s" errorMsg

module internal QueryBuilder =
    open SqlParser

    type SchemaMetadata(schema) =
        let schemaType = schema.GetType()
        member this.TryGetTable tableName = 
            let schemaProperty = schemaType.GetProperty(tableName)
            if schemaProperty <> null then
                Some schemaProperty
            else None
        member this.TryGetTableColumn tableName columnName = 
            this.TryGetTable tableName
                |> Option.bind (fun table -> 
                        let columnProperty = table.PropertyType.GetProperty(columnName)
                        if columnProperty <> null then
                            Some columnProperty
                        else None)

    let buildQuery (query: Query) schema =
        let metadata = SchemaMetadata(schema)
        match query with
        | SelectQuery (sel, body) ->
            let querySource = ()
            ()
        | UpdateQuery (tableName, set, filter) ->
            let table = 
                match metadata.TryGetTable tableName with
                | Some t -> t
                | None -> failwith $"Table {tableName} is not defined"
            let findColumn column = 
                let column = 
                    match metadata.TryGetTableColumn tableName column with
                    | Some c -> c
                    | None -> failwith $"Column {column} does not exist in table {tableName}"
                column
            let setExpressions = set |> List.map (fun (column, expression) -> findColumn column) |> Seq.toArray
            let querySource = ()
            ()