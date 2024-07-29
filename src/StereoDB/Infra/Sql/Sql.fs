namespace StereoDB.Sql

open System.Linq.Expressions
open StereoDB
open StereoDB.FSharp

module Expr = 
    open FSharp.Quotations.DerivedPatterns
    open FSharp.Quotations.Patterns
    let onlyVar = function Var v -> Some v | _ -> None

    let (|Property|_|) = function
        | PropertyGet(_, info, _) -> Some info
        | Lambda(arg, PropertyGet(Some(Var var), info, _))
        | Let(arg, _, PropertyGet(Some(Var var), info, _))
            when arg = var -> Some info
        | _ -> None

    let private (|LetInCall|_|) expr =
        let rec loop e collectedArgs =
            match e with
            | Let(arg, _, exp2) ->  
                let newArgs = Set.add arg collectedArgs
                loop exp2 newArgs
            | Call(_instance, mi, args) ->
                let setOfCallArgs =
                    args
                    |> List.choose onlyVar
                    |> Set.ofList
                if Set.isSubset setOfCallArgs collectedArgs then
                    Some mi
                else None
            | _ -> None
        loop expr Set.empty

    let private (|Func|_|) = function
        // function values without arguments
        | Lambda (arg, Call (target, info, []))
            when arg.Type = typeof<unit> -> Some (target, info)
        // function values with one argument
        | Lambda (arg, Call (target, info, [Var var]))
            when arg = var -> Some (target, info)
        // function values with a set of curried or tuple arguments
        | Lambdas (args, Call (target, info, exprs)) ->
            let justArgs = List.choose onlyVar exprs
            let allArgs = List.concat args
            if justArgs = allArgs then
                Some (target, info)
            else None
        | Lambdas(_args, _body) ->
            None
        | _ -> None

    let (|Method|_|) = function
        // any ordinary calls: foo.Bar ()
        | Call (_, info, _) -> Some info
        // calls and function values via a lambda argument:
        // fun (x: string) -> x.Substring (1, 2)
        // fun (x: string) -> x.StartsWith
        | Lambda (arg, Call (Some (Var var), info, _))
        | Lambda (arg, Func (Some (Var var), info))
            when arg = var -> Some info
        // any function values:someString.StartsWith
        | Func (_, info) -> Some info
        // calls and function values ​​via instances:
        // "abc" .StartsWith ("a")
        // "abc" .Substring
        | Let (arg, _, Call (Some (Var var), info, _))
        | Let (arg, _, Func (Some (Var var), info))
            when arg = var -> Some info
        | LetInCall(info) -> Some info
        | _ -> None
        
    /// Get a MethodInfo from an expression that is a method call or a function type value
    let methodof = function Method mi -> mi | _ -> failwith "Not a method expression"

module internal QueryBuilder =
    open SqlParser

    type QueryExecution<'TSchema> =
        | Write of System.Action<ReadWriteTsContext<'TSchema>>
        | Read of System.Func<ReadOnlyTsContext<'TSchema>, obj>

    type SchemaMetadata(schema) =
        let schemaType = schema.GetType()
        
        member this.TryGetTable tableName = 
            let schemaProperty = schemaType.GetProperty(tableName)
            if not (isNull schemaProperty) then
                let tableEntityType = schemaProperty.PropertyType.GenericTypeArguments[0].GenericTypeArguments[1]
                let keyType = tableEntityType.GetInterfaces() |> Seq.find (fun ifType -> ifType.Name = "IEntity`1")
                Some (schemaProperty, tableEntityType, keyType.GenericTypeArguments[0])
            else None
        
        member this.GetTable tableName = 
            match this.TryGetTable tableName with
            | Some (schemaProperty, tableEntityType, keyType) -> (schemaProperty, tableEntityType, keyType) 
            | None -> failwith $"Table {tableName} was not found"
        
        member this.TryGetTableColumn(tableName, columnName) = 
            this.TryGetTable tableName
                |> Option.bind (fun (tableProperty, entityType, keyType) -> 
                        let columnProperty = entityType.GetProperty(columnName)
                        if not (isNull columnProperty) then
                            Some columnProperty
                        else None)
        
        member this.GetTableColumn(tableName, columnName) = 
            let column = 
                match this.TryGetTableColumn(tableName, columnName) with
                | Some c -> c
                | None -> failwith $"Column {columnName} does not exist in table {tableName}"
            column

    let iter<'T> (action: System.Action<'T>) (seq: 'T voption seq) =
        Seq.iter (fun x ->
            match x with 
            | ValueSome x -> action.Invoke(x)
            | _ -> ()) seq

    let iterMaterialize<'T> (action: System.Action<'T>) (seq: 'T voption seq) =
        Seq.iter (fun x ->
            match x with 
            | ValueSome x -> action.Invoke(x)
            | _ -> ()) (seq |> Seq.toList)

    let map (action: System.Func<'T, 'TResult>) (seq: 'T voption seq) =
        Seq.map (fun x ->
            match x with 
            | ValueSome x -> action.Invoke(x)
            | _ -> failwith "Cannot apply SELECT on non-row") seq

    let filter (action: System.Func<'T, bool>) (seq: 'T voption seq) =
        Seq.filter (fun x ->
            match x with 
            | ValueSome x -> action.Invoke(x)
            | _ -> false) seq

    let take count (seq: 'T seq) =
        Seq.take count seq

    let sortWith (comparer: System.Comparison<'T>) (seq: 'T voption seq) =
        Seq.sortWith (fun x y -> match(x,y) with | ValueSome(x), ValueSome(y) -> comparer.Invoke(x,y) | _ -> 0) seq    

    let buildTableScan tableType tableEntityType keyType =
        let rot = typeof<IReadOnlyTable<_, _>>.GetGenericTypeDefinition().MakeGenericType(keyType, tableEntityType)

        // Build Table scan
        // let tableScan entity = 
        //     let ids = entity.GetIds()
        //     ids
        //         |> System.Linq.Enumerable.Select (fun id -> entity.Get id)
        let entity = Expression.Parameter(tableType, "entity")
        let getIdsCall = Expression.Call(entity, rot.GetMethod("GetIds"))
            
        let x = Expression.Parameter(keyType, "x")
        let enumerableType = typeof<System.Linq.Enumerable>
            
        let getMethod = rot.GetMethod("Get")
        let getIdLambda = Expression.Lambda (Expression.Call(entity, getMethod, x), x)
        let selectMethod = enumerableType.GetMethods() |> Seq.filter (fun c -> c.Name = "Select") |> Seq.head
        let result = Expression.Call(selectMethod.MakeGenericMethod(keyType, getMethod.ReturnType), getIdsCall, getIdLambda)
        let tableScanExpresion = Expression.Lambda (result, entity)
        tableScanExpresion

    let normalizeSortExpressions expressions = 
        expressions |> List.map (fun (expr, dir) -> (expr, dir |> Option.defaultValue Ascending))
        
    type QueryScope = 
        {
            aliases: System.Collections.Generic.Dictionary<string, string>
        }

    let addTableAlias scope alias tableName =
        scope.aliases.Add(alias, tableName)

    let resolveAlias scope alias =
        let mutable resolved = Unchecked.defaultof<string>
        if scope.aliases.TryGetValue(alias, &resolved) then
            resolved
        else
            alias

    type ExpressionBuilder(tableName:string, queryScope: QueryScope, metadata:SchemaMetadata) = class
        let (table, tableEntityType, keyType) = metadata.GetTable tableName

        member this.GetExpressionType(expr) =
            match expr with
            | Primitive primitive ->
                match primitive with
                | SqlIdentifier (tableAlias, ident) ->
                    match tableAlias with
                    | Some tableAlias ->
                        let resolvedTableName = resolveAlias queryScope tableAlias
                        let (_, tableEntityType, _) = metadata.GetTable resolvedTableName
                        tableEntityType.GetProperty(ident).PropertyType
                    | None ->
                        tableEntityType.GetProperty(ident).PropertyType // failwithf "Cannot get type for expression %s" ident
                | SqlFloatConstant _ -> typeof<float>
                | SqlIntConstant _ -> typeof<int>
            | UnaryArithmeticOperator (op, expr) -> this.GetExpressionType(expr)
            | BinaryArithmeticOperator (left, op, right) -> this.GetExpressionType(left)

        member this.buildExpression row expression :Expression =
            match expression with
            | BinaryArithmeticOperator (left, op, right) -> failwith "Not implemented"
            | UnaryArithmeticOperator (op, expr) -> failwith "Not implemented"
            | Primitive primitive ->
                match primitive with
                | SqlFloatConstant c -> Expression.Constant(c, typeof<float>)
                | SqlIntConstant c -> if keyType = typeof<int> then Expression.Constant(c |> int, typeof<int>) else Expression.Constant(c, typeof<int64>)
                | SqlIdentifier (tableAlias, identifier) ->
                    match tableAlias with
                    | Some tableAlias ->
                        let resolvedTableName = resolveAlias queryScope tableAlias
                        Expression.Property(row, metadata.GetTableColumn(resolvedTableName, identifier))
                    | None ->
                        Expression.Property(row, metadata.GetTableColumn(tableName, identifier))

        member this.buildLogicExpression row expression :Expression =
            match expression with
            | BinaryLogicalOperator (left, op, right) -> failwith "Not implemented"
            | BinaryComparisonOperator (left, op, right) ->
                let leftExpression = this.buildExpression row left
                let rightExpression = this.buildExpression row right
                match op with
                | "<=" -> Expression.LessThanOrEqual(leftExpression, rightExpression)
                | "<" -> Expression.LessThan(leftExpression, rightExpression)
                | ">=" -> Expression.GreaterThanOrEqual(leftExpression, rightExpression)
                | ">" -> Expression.GreaterThan(leftExpression, rightExpression)
                | "<>" -> Expression.NotEqual(leftExpression, rightExpression)
                | "=" -> Expression.Equal(leftExpression, rightExpression)
                | _ -> failwith $"Operator {op} is not implemented"
            | UnaryLogicalOperator (op, expr) -> failwith "Not implemented"
            | IsNull (expr) -> failwith "Not implemented"
            | IsNotNull (expr) -> failwith "Not implemented"
            | Between (expr: SqlExpression, left, right) ->
                let leftExpression = this.buildExpression row left
                let rightExpression = this.buildExpression row right
                let exprToCheck = this.buildExpression row expr
                Expression.AndAlso(
                    Expression.GreaterThanOrEqual(exprToCheck, leftExpression),
                    Expression.LessThanOrEqual(exprToCheck, rightExpression)
                )
                

        member this.buildFilterProjection tableEntityType whereExpression =
            // Build Update projection
            // let whereProjection = fun row -> 
            //   row.Quantity <= 100
            //   ()
            let row = Expression.Parameter(tableEntityType, "row");

            let updateBlock: Expression = this.buildLogicExpression row whereExpression

            let updateLambdaType = (typeof<System.Func<_, _>>).GetGenericTypeDefinition().MakeGenericType(tableEntityType, typeof<bool>)
            let updateProjectionExpresion = Expression.Lambda (updateLambdaType, updateBlock, row)
            updateProjectionExpresion
        end       

    let buildQuery<'TSchema, 'TResult> (query: Query) executionContext schema =
        let metadata = SchemaMetadata(schema)
        let schemaType = typeof<'TSchema>
        let queryScope = { aliases = System.Collections.Generic.Dictionary<string, string>() }
        match query with
        | SelectQuery (sel, body) ->
            let resultsetType = typeof<System.Collections.Generic.List<'TResult>>
            // let resutlt = List<'TResult>()
            // 
            // let selectProjection = fun row -> (row.Title)
            // let whereFilter = fun row -> row.Quantity > 100
            // let tableScan entity = 
            //     let ids = entity.GetIds()
            //     ids
            //         |> Seq.map (fun id -> entity.Get id)
            //    
            // let actualTable = ctx.UseTable(ctx.Schema.Books.Table)
            // tableScan actualTable
            //     |> Seq.filter whereFilter
            //     |> Seq.orderBy orderByFunction
            //     |> Seq.map selectProjection
            let contextType = typeof<ReadOnlyTsContext<'TSchema>>
            let context = Expression.Parameter(contextType, "context")
            let schemaAccess = Expression.Property(context, "Schema")

            match body with
            | Some (fromClause, whereClause, orderClause) ->
                let tableName =
                    match fromClause with
                    | Resultset(TableResultset(tableName, tableAlias)) ->
                        match tableAlias with
                        | Some tableAlias -> addTableAlias queryScope tableAlias tableName
                        | None -> ()
                        tableName
                let expressionBuilder = ExpressionBuilder(tableName, queryScope, metadata)
                let (table, tableEntityType, keyType) = 
                    match metadata.TryGetTable tableName with
                    | Some t -> t
                    | None -> failwith $"Table {tableName} is not defined"

                let buildSelectProjection () =
                    // Build Update projection
                    // let updateProjection = fun row -> 
                    //   row.Quantity = 100
                    //   ()
                    let row = Expression.Parameter(tableEntityType, "row");
                    let namedSelect = 
                        sel |> snd
                            |> List.map (fun x ->
                            match x with
                            | AliasedExpression (expr, Some(alias)) -> [(alias, expr)]
                            | AliasedExpression (expr, None) ->
                                match expr with
                                | Primitive prim -> match prim with
                                                    | SqlIdentifier (tableAlias, ident) -> [(ident, expr)]
                                                    | _ -> failwith "Column name cannot be determined"
                                | _ -> failwith "Column name cannot be determined"
                            | Star(_) -> 
                                tableEntityType.GetProperties() |> Array.map(fun p -> (p.Name, Primitive(SqlIdentifier(Some tableName, p.Name)))) |> Array.toList)
                            |> List.concat

                    let selectProjectionType = typeof<'TResult>
                    let updateBlock: Expression = 
                        let constructorParameters =
                            selectProjectionType.GetProperties() |> Array.map (fun prop -> 
                                let shouldBeUpdated = namedSelect |> Seq.tryFind (fun (alias, expr) -> alias = prop.Name)
                                match shouldBeUpdated with
                                | Some (column, expression) -> 
                                    let newValueExpression = expressionBuilder.buildExpression row expression
                                    newValueExpression
                                | None -> Expression.Property(row, prop)
                                )
                        let constructor = selectProjectionType.GetConstructors() |> Seq.head
                        let createNew = Expression.New(constructor, constructorParameters)
                        createNew

                    let updateLambdaType = (typeof<System.Func<_, _>>).GetGenericTypeDefinition().MakeGenericType(tableEntityType, selectProjectionType)
                    let updateProjectionExpresion = Expression.Lambda (updateLambdaType, updateBlock, row)
                    updateProjectionExpresion

                let buildSortFunction (sortExpressions: (SqlExpression * OrderDirection) list) =
                    // Build Sort function
                    // let sortFunction = fun (a, b) -> 
                    //   let mutable result;
                    //   result = a.Quantity.CompareTo(b.Quantity)
                    //   if result <> 0 then result
                    //   else
                    //       result = a.Title.CompareTo(b.Title)
                    //       if result <> 0 then result
                    //       else 0
                    let a = Expression.Parameter(tableEntityType, "a");
                    let b = Expression.Parameter(tableEntityType, "b");

                    let result = Expression.Variable(typeof<int>, "result")
                    let initResult: Expression = Expression.Assign(result, Expression.Constant(0))
                    let exitTarget = Expression.Label()
                    let buildSingleComparison ((expr:SqlExpression), (dir: OrderDirection)) =
                        let compableType = (typeof<System.IComparable<_>>).GetGenericTypeDefinition().MakeGenericType(expressionBuilder.GetExpressionType(expr))
                        let compareToMethod = compableType.GetMethod("CompareTo")
                        let aExpression = expressionBuilder.buildExpression a expr
                        let bExpression = expressionBuilder.buildExpression b expr                        
                        let comparisonValue = Expression.Call(aExpression, compareToMethod, bExpression)
                        let comparisonWithDir: Expression =
                            match dir with
                            | Descending -> Expression.Negate(comparisonValue)
                            | Ascending -> comparisonValue
                        let assignComparison: Expression = Expression.Assign(result, comparisonWithDir)
                        let compare = Expression.IfThen(
                            Expression.NotEqual(result, Expression.Constant(0)),
                            Expression.Return(exitTarget)
                        )
                        [assignComparison; compare]
                    let comparison = sortExpressions |> List.collect buildSingleComparison
                    let body = seq {
                        initResult
                        yield! comparison
                        Expression.Label(exitTarget)
                        result
                    }

                    let sortBlock: Expression = Expression.Block([| result |], body)

                    let comparisonLambdaType = (typeof<System.Comparison<_>>).GetGenericTypeDefinition().MakeGenericType(tableEntityType)
                    let sortFunctionExpresion = Expression.Lambda (comparisonLambdaType, sortBlock, a, b)
                    sortFunctionExpresion

                let tablePropertyAccess = Expression.Property(schemaAccess, schemaType.GetProperty(tableName))
                let tet = schemaType.GetProperty(tableName).PropertyType.GetProperty("Table")
                let tableExpression: Expression = Expression.Property(tablePropertyAccess, tet)
                let useTable = Expr.methodof <@ ReadOnlyTsContextExt.UseTable @>
                let useTableMethod = useTable.GetGenericMethodDefinition().MakeGenericMethod(schemaType, keyType, tableEntityType)
                let tableAccessor = Expression.Call(useTableMethod, context, tableExpression)
                let rot = typeof<IReadOnlyTable<_, _>>.GetGenericTypeDefinition().MakeGenericType(keyType, tableEntityType)
                let actualTable = Expression.Variable(rot, "actualTable");
                let actualTableAssignment = Expression.Assign(actualTable, tableAccessor)

                let tableScanExpresion = buildTableScan rot tableEntityType keyType
                let selectProjection = buildSelectProjection ()

                // Create table scan
                let tableScanResultType = typeof<seq<_>>.GetGenericTypeDefinition().MakeGenericType(typeof<obj voption>.GetGenericTypeDefinition().MakeGenericType(tableEntityType))
                let tableScan = Expression.Variable(typeof<System.Func<_, _>>.GetGenericTypeDefinition().MakeGenericType(rot, tableScanResultType), "tableScan");
                let tableScanAssignment = Expression.Assign(tableScan, tableScanExpresion)
                let createTableScanExpression = Expression.Invoke(tableScan, actualTable)

                let createNew = Expression.New(resultsetType)
                let result = Expression.Variable(resultsetType, "result");
                let resultAssignment = Expression.Assign(result, createNew)

                let filteredResult : Expression =
                    match whereClause with
                    | Some(WhereCondition(whereExpression)) -> 
                        let condition = expressionBuilder.buildFilterProjection tableEntityType whereExpression
                        let seqFilter = Expr.methodof <@ filter @>
                        let filterExpression = Expression.Call(seqFilter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), condition, createTableScanExpression)
                        filterExpression
                    | _ -> createTableScanExpression

                let sorderResult : Expression =
                    match orderClause with
                    | Some(SortOrder(sortExpressions)) ->
                        let sortExpressions = normalizeSortExpressions sortExpressions
                        let sortFunction = buildSortFunction sortExpressions
                        let seqSort = Expr.methodof <@ sortWith @>
                        let orderedExpression = Expression.Call(seqSort.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), sortFunction, filteredResult)
                        orderedExpression
                    | _ -> filteredResult

                // Apply select to each object
                let seqIter = Expr.methodof <@ map @>
                let selectedExpression = Expression.Call(seqIter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType, typeof<'TResult>), selectProjection, sorderResult)

                let resultExpression = 
                    match sel |> fst with
                    | Some top ->
                        let take = Expr.methodof <@ take @>
                        Expression.Call(take.GetGenericMethodDefinition().MakeGenericMethod(typeof<'TResult>), Expression.Constant(top), selectedExpression)
                    | None -> selectedExpression
                let addRangeCall = Expression.Call(result, resultsetType.GetMethod("AddRange"), resultExpression);
                let letFunction = Expression.Block([| result; actualTable; tableScan |], actualTableAssignment, tableScanAssignment, resultAssignment, addRangeCall, result)
                let resultLambdaType = typeof<System.Func<ReadOnlyTsContext<'TSchema>, obj>>
                let readTransaction = Expression.Lambda (resultLambdaType, letFunction, context)
                Read (readTransaction.Compile() :?> System.Func<ReadOnlyTsContext<'TSchema>, obj>)
            | None -> failwith "SELECT without FROM not yet supported"
        | UpdateQuery (tableNameOrAlias, set, fromClause, whereClause) ->
            let tableName =
                match fromClause with
                | Some (resultset) -> 
                    match resultset with
                    | Resultset(TableResultset(tableName, tableAlias)) ->
                        match tableAlias with
                        | Some tableAlias -> addTableAlias queryScope tableAlias tableName
                        | None -> ()
                        resolveAlias queryScope tableNameOrAlias
                | None -> tableNameOrAlias
            let (table, tableEntityType, keyType) = 
                match metadata.TryGetTable tableName with
                | Some t -> t
                | None -> failwith $"Table {tableName} is not defined"
            let expressionBuilder = ExpressionBuilder(tableName, queryScope, metadata)
            let contextType = typeof<ReadWriteTsContext<'TSchema>>
            let rwt = typeof<IReadWriteTable<_, _>>.GetGenericTypeDefinition().MakeGenericType(keyType, tableEntityType)

            let buildUpdateProjection actualTable =
                // Build Update projection
                // let updateProjection = fun row -> 
                //   row.Quantity = 100
                //   ()
                // OR
                // let updateProjection = fun row -> 
                //   activeTable.Set({ row with Quantity = 100 })
                //   ()
                let row = Expression.Parameter(tableEntityType, "row");

                let allPropertiesWriteable = set |> List.forall (fun (column,_) -> (metadata.GetTableColumn(tableName, column)).CanWrite)
                let updateBlock: Expression = 
                    if allPropertiesWriteable then
                        // directly update property
                        let setExpressions = set |> List.map (fun (column, expression) -> 
                            let newValueExpression = expressionBuilder.buildExpression row expression
                            let targetProperty = metadata.GetTableColumn(tableName, column)
                            let columnExpression = Expression.Property(row, targetProperty)
                            let assignment = Expression.Assign(columnExpression, newValueExpression)
                            assignment :> Expression)
                        Expression.Block(setExpressions)
                    else
                        let setMethod = rwt.GetMethod("Set")
                        let constructorParameters =
                            tableEntityType.GetProperties() |> Array.map (fun prop -> 
                                let shouldBeUpdated = set |> Seq.tryFind (fun (c, e) -> c = prop.Name)
                                match shouldBeUpdated with
                                | Some (column, expression) -> 
                                    let newValueExpression = expressionBuilder.buildExpression row expression
                                    newValueExpression
                                | None -> Expression.Property(row, prop)
                                )
                        let constructor = tableEntityType.GetConstructors() |> Seq.head
                        let createNew = Expression.New(constructor, constructorParameters)
                        Expression.Call(actualTable, setMethod, createNew)

                let updateLambdaType = (typeof<System.Action<_>>).GetGenericTypeDefinition().MakeGenericType(tableEntityType)
                let updateProjectionExpresion = Expression.Lambda (updateLambdaType, updateBlock, row)
                updateProjectionExpresion

            // Composing things
            //
            // let actualTable = ctx.UseTable(ctx.Schema.Books.Table)
            // tableScan actualTable
            //     |> Seq.filter whereFilter
            //     |> Seq.iter updateProjection
            let context = Expression.Parameter(contextType, "context")
            let schemaAccess = Expression.Property(context, "Schema")
            let tablePropertyAccess = Expression.Property(schemaAccess, schemaType.GetProperty(tableName))
            let tet = schemaType.GetProperty(tableName).PropertyType.GetProperty("Table")
            let tableExpression: Expression = Expression.Property(tablePropertyAccess, tet)
            let useTable = Expr.methodof <@ ReadWriteTsContextExt.UseTable<_,_,_> @>
            let useTableMethod = useTable.GetGenericMethodDefinition().MakeGenericMethod(schemaType, keyType, tableEntityType)
            let tableAccessor = Expression.Call(useTableMethod, context, tableExpression)
            let actualTable = Expression.Variable(rwt, "actualTable");
            let actualTableAssignment = Expression.Assign(actualTable, tableAccessor)

            let updateProjectionExpresion = buildUpdateProjection actualTable
            let tableScanExpresion = buildTableScan rwt tableEntityType keyType

            // Create table scan
            let tableScanResultType = typeof<seq<_>>.GetGenericTypeDefinition().MakeGenericType(typeof<obj voption>.GetGenericTypeDefinition().MakeGenericType(tableEntityType))
            let tableScan = Expression.Variable(typeof<System.Func<_, _>>.GetGenericTypeDefinition().MakeGenericType(rwt, tableScanResultType), "tableScan");
            let tableScanAssignment = Expression.Assign(tableScan, tableScanExpresion)
            let createTableScanExpression = Expression.Invoke(tableScan, actualTable)

            let filteredResult : Expression =
                match whereClause with
                | Some(WhereCondition(whereExpression)) -> 
                    let condition = expressionBuilder.buildFilterProjection tableEntityType whereExpression
                    let seqFilter = Expr.methodof <@ filter @>
                    let filterExpression = Expression.Call(seqFilter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), condition, createTableScanExpression)
                    filterExpression
                | _ -> createTableScanExpression

            // Apply update to each object
            let seqIter = Expr.methodof <@ iter @>
            let resultExpression = Expression.Call(seqIter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), updateProjectionExpresion, filteredResult)
            let letFunction = Expression.Block([| actualTable; tableScan |], actualTableAssignment, tableScanAssignment, resultExpression)
            let resultLambdaType = typeof<System.Action<ReadWriteTsContext<'TSchema>>>
            let writeTransaction = Expression.Lambda (resultLambdaType, letFunction, context)
            
            Write (writeTransaction.Compile() :?> System.Action<ReadWriteTsContext<'TSchema>>)
        | DeleteQuery (tableNameOrAlias, fromClause, whereClause) ->
            let tableName =
                match fromClause with
                | Some (resultset) -> 
                    match resultset with
                    | Resultset(TableResultset(tableName, tableAlias)) ->
                        match tableAlias with
                        | Some tableAlias -> addTableAlias queryScope tableAlias tableName
                        | None -> ()
                        resolveAlias queryScope tableNameOrAlias
                | None -> tableNameOrAlias
            let (table, tableEntityType, keyType) = 
                match metadata.TryGetTable tableName with
                | Some t -> t
                | None -> failwith $"Table {tableName} is not defined"
            let expressionBuilder = ExpressionBuilder(tableName, queryScope, metadata)
            let contextType = typeof<ReadWriteTsContext<'TSchema>>
            let rwt = typeof<IReadWriteTable<_, _>>.GetGenericTypeDefinition().MakeGenericType(keyType, tableEntityType)

            let buildDeleteProjection actualTable =
                // Build Delete projection
                // let updateProjection = fun row -> 
                //   activeTable.Delete(row.Id)
                //   ()
                let row = Expression.Parameter(tableEntityType, "row");

                let deleteBlock: Expression = 
                    let deleteMethod = rwt.GetMethod("Delete")
                    let id = Expression.Property(row, "Id")
                    Expression.Block(Expression.Call(actualTable, deleteMethod, id))

                let deleteLambdaType = (typeof<System.Action<_>>).GetGenericTypeDefinition().MakeGenericType(tableEntityType)
                let deleteProjectionExpresion = Expression.Lambda (deleteLambdaType, deleteBlock, row)
                deleteProjectionExpresion

            // Composing things
            //
            // let actualTable = ctx.UseTable(ctx.Schema.Books.Table)
            // tableScan actualTable
            //     |> Seq.filter whereFilter
            //     |> Seq.toList
            //     |> Seq.iter deleteProjection
            let context = Expression.Parameter(contextType, "context")
            let schemaAccess = Expression.Property(context, "Schema")
            let tablePropertyAccess = Expression.Property(schemaAccess, schemaType.GetProperty(tableName))
            let tet = schemaType.GetProperty(tableName).PropertyType.GetProperty("Table")
            let tableExpression: Expression = Expression.Property(tablePropertyAccess, tet)
            let useTable = Expr.methodof <@ ReadWriteTsContextExt.UseTable<_,_,_> @>
            let useTableMethod = useTable.GetGenericMethodDefinition().MakeGenericMethod(schemaType, keyType, tableEntityType)
            let tableAccessor = Expression.Call(useTableMethod, context, tableExpression)
            let actualTable = Expression.Variable(rwt, "actualTable");
            let actualTableAssignment = Expression.Assign(actualTable, tableAccessor)

            let deleteProjectionExpresion = buildDeleteProjection actualTable
            let tableScanExpresion = buildTableScan rwt tableEntityType keyType

            // Create table scan
            let tableScanResultType = typeof<seq<_>>.GetGenericTypeDefinition().MakeGenericType(typeof<obj voption>.GetGenericTypeDefinition().MakeGenericType(tableEntityType))
            let tableScan = Expression.Variable(typeof<System.Func<_, _>>.GetGenericTypeDefinition().MakeGenericType(rwt, tableScanResultType), "tableScan");
            let tableScanAssignment = Expression.Assign(tableScan, tableScanExpresion)
            let createTableScanExpression = Expression.Invoke(tableScan, actualTable)

            let filteredResult : Expression =
                match whereClause with
                | Some(WhereCondition(whereExpression)) -> 
                    let condition = expressionBuilder.buildFilterProjection tableEntityType whereExpression
                    let seqFilter = Expr.methodof <@ filter @>
                    let filterExpression = Expression.Call(seqFilter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), condition, createTableScanExpression)
                    filterExpression
                | _ -> createTableScanExpression

            // Apply update to each object
            let seqIter = Expr.methodof <@ iterMaterialize @>
            let resultExpression = Expression.Call(seqIter.GetGenericMethodDefinition().MakeGenericMethod(tableEntityType), deleteProjectionExpresion, filteredResult)
            let letFunction = Expression.Block([| actualTable; tableScan |], actualTableAssignment, tableScanAssignment, resultExpression)
            let resultLambdaType = typeof<System.Action<ReadWriteTsContext<'TSchema>>>
            let writeTransaction = Expression.Lambda (resultLambdaType, letFunction, context)
            
            Write (writeTransaction.Compile() :?> System.Action<ReadWriteTsContext<'TSchema>>)
