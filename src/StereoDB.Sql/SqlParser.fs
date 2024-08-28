namespace StereoDB.Sql

open FParsec
open System.Collections.Generic
open System

module internal SqlParser =

    let ws = spaces
    let str_ws s = pstring s .>> ws
    let strCI_ws s = pstringCI s .>> ws
    let float_ws = pfloat .>> ws
    let int_ws = pint64 .>> ws
    let int32_ws = pint32 .>> ws

    let private sqlKeywords =
        [   "AND"; "AS";
            "DELETE"; "FROM";
            "INSERT";
            "INTO"; "IS";
            "NOT"; "NULL";
            "OR"; "ORDER";
            "SELECT"; "SET";
            "UPDATE"; "WHERE";
            // Note: we don't include TEMP in this list because it is a schema name.
        ] |> fun kws ->
            HashSet<string>(kws, StringComparer.OrdinalIgnoreCase)
            // Since SQL is case-insensitive, be sure to ignore case
            // in this hash set.

    let identifier =
        let isIdentifierFirstChar c = isLetter c || c = '_'
        let isIdentifierChar c = isLetter c || isDigit c || c = '_'

        many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier" .>> ws 
            // skips trailing whitespace
             >>=? fun ident ->
                    if sqlKeywords.Contains(ident.ToString()) then
                        FParsec.Primitives.fail (sprintf "Cannot use %s as identifier" ident)
                    else
                        preturn ident

    let identifier_ws = identifier .>> ws

    let unwrap_identifier (part1, part2) =
        match part2 with
        | Some part2 -> ((Some part1), part2)
        | None -> (None, part1)

    type SqlPrimitiveExpression = 
        | SqlIntConstant   of int64
        | SqlFloatConstant of float
        | SqlIdentifier    of string option * string
        
    type SqlExpression = 
        | BinaryArithmeticOperator of SqlExpression * string * SqlExpression
        | UnaryArithmeticOperator  of string * SqlExpression
        | Primitive                of SqlPrimitiveExpression

    type SqlLogicalExpression =
        | BinaryLogicalOperator    of SqlLogicalExpression * string * SqlLogicalExpression
        | BinaryComparisonOperator of SqlExpression * string * SqlExpression
        | UnaryLogicalOperator     of string * SqlLogicalExpression
        | IsNull                   of SqlExpression
        | IsNotNull                of SqlExpression
        | Between                  of SqlExpression * SqlExpression * SqlExpression

    type Resultset = 
        | TableResultset of string * string option

    type SelectListItem =
        | AliasedExpression of SqlExpression * string option
        | Star              of string
        
    type SelectClause = int32 option * SelectListItem list
    type SetListItem = string * SqlExpression
    type SetClause = SetListItem list
    
    type FromClause = 
        | Resultset of Resultset
    
    type WhereClause = 
        | WhereCondition of SqlLogicalExpression

    type OrderDirection = Ascending | Descending
    
    type OrderByClause = 
        | SortOrder   of (SqlExpression * OrderDirection option) list

    type Query =
        | SelectQuery of (SelectClause * (FromClause * WhereClause option * OrderByClause option) option)
        | UpdateQuery of (string * SetClause * FromClause option * WhereClause option)
        | DeleteQuery of (string * FromClause option * WhereClause option)

    let SQL_INT_CONSTANT = int_ws |>> SqlIntConstant
    let SQL_FLOAT_CONSTANT = float_ws |>> SqlFloatConstant
    let SQL_IDENTIFIER = identifier .>>. opt (str_ws "." >>. identifier) |>> unwrap_identifier |>> SqlIdentifier
    let SQL_EXPRESSION = SQL_INT_CONSTANT <|> SQL_FLOAT_CONSTANT <|> SQL_IDENTIFIER |>> Primitive

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

    let flatten4 v =
        match v with
        | (((a, b), c), d) -> (a, b, c, d) 
     //   | _ -> failwith "Invalid tuple to flatten"

    let logicOpp = new OperatorPrecedenceParser<SqlLogicalExpression,unit,unit>()
    let SQL_LOGICAL_EXPRESSION = logicOpp.ExpressionParser
    let logicalOperator = str_ws "=" <|> str_ws "<=" <|> str_ws ">=" <|> str_ws "<>" <|> str_ws ">" <|> str_ws "<"
    let primitiveLogicalExpression =
        (SQL_EXPRESSION .>>? strCI_ws "IS" .>>? strCI_ws "NULL" |>> IsNull)
        <|> (SQL_EXPRESSION .>>? strCI_ws "IS" .>>? strCI_ws "NOT" .>> strCI_ws "NULL" |>> IsNotNull)
        <|> (SQL_EXPRESSION .>>? strCI_ws "BETWEEN" .>>. SQL_EXPRESSION .>>? strCI_ws "AND" .>>. SQL_EXPRESSION |>> flatten |>> Between)
        <|> (SQL_EXPRESSION .>>.? logicalOperator .>>. SQL_EXPRESSION |>> flatten |>> BinaryComparisonOperator)
    let logicExpressionTerm = (primitiveLogicalExpression) <|> between (str_ws "(") (str_ws ")") SQL_LOGICAL_EXPRESSION
    logicOpp.TermParser <- logicExpressionTerm

    logicOpp.AddOperator(InfixOperator("AND", ws, 1, Associativity.Left, fun x y -> BinaryLogicalOperator(x, "AND", y)))
    logicOpp.AddOperator(InfixOperator("OR", ws, 1, Associativity.Left, fun x y -> BinaryLogicalOperator(x, "OR", y)))
    logicOpp.AddOperator(PrefixOperator("NOT", ws, 3, false, fun x -> UnaryLogicalOperator("NOT", x)))

    let ALIASED_EXPRESSION    = SQL_EXPRESSION .>>. opt (strCI_ws "AS" >>. identifier) |>> AliasedExpression
    let SELECT_LIST_ITEM      = ALIASED_EXPRESSION <|> (str_ws "*" |>> Star)
    let SELECT_LIST           = sepBy SELECT_LIST_ITEM (str_ws ",")
    let ASSIGNMENT_EXPRESSION = identifier_ws .>> str_ws "=" .>>. arithExpr |>> SetListItem
    let SET_LIST              = strCI_ws "SET" >>. sepBy ASSIGNMENT_EXPRESSION (str_ws ",") //|>> SetClause
    let TABLE_RESULTSET       = identifier .>>. opt (opt (str_ws "AS") >>. identifier) |>> TableResultset
    let FROM_CLAUSE           = strCI_ws "FROM" >>. TABLE_RESULTSET |>> Resultset
    let WHERE_CLAUSE          = strCI_ws "WHERE" >>. SQL_LOGICAL_EXPRESSION |>> WhereCondition

    let SORT_DIRECTION        = (stringCIReturn "ASC" Ascending) <|> (stringCIReturn "DESC" Descending) .>> ws
    let SORT_EXPRESSION       = SQL_EXPRESSION .>>. opt SORT_DIRECTION
    let ORDER_BY_CLAUSE       = strCI_ws "ORDER" >>. strCI_ws "BY" >>. sepBy SORT_EXPRESSION (str_ws ",") |>> SortOrder
    
    let TOP_DIRECTIVE         = strCI_ws "TOP" >>. int32_ws

    let SELECT_STATEMENT = 
        spaces .>> strCI_ws "SELECT" >>. (opt TOP_DIRECTIVE) .>>. SELECT_LIST .>>. 
            (opt (FROM_CLAUSE .>>. (opt WHERE_CLAUSE) .>>. (opt ORDER_BY_CLAUSE) |>> flatten)) |>> SelectQuery

    let UPDATE_STATEMENT =
        spaces .>> strCI_ws "UPDATE" >>. identifier_ws .>>. SET_LIST .>>. 
        (opt FROM_CLAUSE) .>>.
        (opt (WHERE_CLAUSE)) |>> flatten4 |>> UpdateQuery

    let DELETE_STATEMENT =
        spaces .>> strCI_ws "DELETE" >>. opt (strCI_ws "FROM") >>. identifier_ws .>>. 
        (opt FROM_CLAUSE) .>>.
        (opt (WHERE_CLAUSE)) |>> flatten |>> DeleteQuery

    let QUERY = SELECT_STATEMENT <|> UPDATE_STATEMENT <|> DELETE_STATEMENT

    let parseSql str =
        match run QUERY str with
        | Success(result, _, _)   ->
            result
        | Failure(errorMsg, _, _) -> failwithf "Failure: %s" errorMsg