module Perfect_
open System
open Fussless
open Fussless.RuntimeParser
open Option
open System.Collections.Generic
open Perfect_

// fsharpc symbol_table.fs -r perfect_parser.dll -r perfect__lex.dll

type var_set = SortedDictionary<(string*int),lrtype>

// for as5
type table_entry =
  { // table_entry
    mutable typeof  : lrtype;
    gindex          : int;
    mutable ast_rep : expr option;
  }
and table_frame =
  { // table_frame
    name         : string;
    entries      : HashMap<string, table_entry>;
    parent_scope : table_frame option;
    mutable closure : var_set;
  }

// wrapping structure for symbol table frames
type SymbolTable =  
  { // SymbolTable
    mutable current_frame : table_frame;
    mutable global_index  : int;
    frame_hash            : HashMap<(int*int),table_frame>;
    type_hash             : HashMap<expr, lrtype>;
    export_hash           : HashMap<string, lrtype>;
  }

  // adds a table entry.
  member this.add_entry(s:string,t:lrtype,a:expr option) =
    this.global_index <- this.global_index + 1

    let new_entry =
      { // table_entry
        typeof  = t;
        gindex  = this.global_index;
        ast_rep = a;
      }
  
    this.current_frame.entries.Add(s,new_entry)

  // adds a new table frame.
  member this.push_frame(n,line,column) =
    let newframe =
      { // table_frame
        name         = n;
        entries      = new HashMap<string,table_entry>();
        parent_scope = Some(this.current_frame);
        closure      = this.collect_freevars();
      }

    this.frame_hash.[(line,column)] <- newframe
    this.current_frame <- newframe

  // pops the last table frame.
  member this.pop_frame() =
    this.current_frame.parent_scope
      |> Option.map (fun p -> (this.current_frame <- p))

  member this.push_type(t:lrtype,e:expr) =
    try this.type_hash.[e] <- t
    with | :? System.ArgumentException -> ()

  // gets a table entry given a string
  member this.get_entry (s:string) =
    let starting_frame = this.current_frame
    let mutable entry_found = false
    let mutable in_global_frame = false
    let mutable entry = None

    while (not(entry_found) && not(in_global_frame)) do
      let potential_entry =
        try
          Some(this.current_frame.entries.[s])
        with
          | :? System.Collections.Generic.KeyNotFoundException ->
            None
      if (isSome potential_entry) then
        entry <- potential_entry
        entry_found <- true
      else if (isNone this.current_frame.parent_scope) then
        in_global_frame <- true
      else
        this.pop_frame() |> ignore
          
    this.current_frame <- starting_frame
    if (not entry_found && s<>"getint" && s<>"free") then
      printfn "Your variable %s does not exist!" s

    entry

  // if a variable is given, this function checks if it exists.
  // if given anything else (non-vars), we can say it does exist.
  member this.value_exists (potential_var:expr) =
    match potential_var with
    | Var(s) ->
      if (s<>"getint" && s<>"free") then isSome (this.get_entry(s))
      else true
    | TypedVar(t,s) ->
      if (s<>"getint" && s<>"free") then isSome (this.get_entry(s))
      else true
    | Sequence(seq) ->
      List.forall this.value_exists [for lb in seq -> lb.value]
    | _ -> true  // non-vars

  // gets the type of a lambda
  member this.get_lambda_type (expression:expr,lam_name:string) =
    match expression with
    | TypedLambda(args,given_return_type,(Lbox(expr) as lb)) as lam 
      ->
      // put all the variable types in a list
      let mutable arg_types = []
      for (arg_type,arg_name) in args do
        match arg_type with
        | LRclosure((_,_),f_type_name) ->
          let mutable updated_arg_type = arg_type
          try (updated_arg_type <- this.export_hash.[f_type_name])
          with
            | :? System.Collections.Generic.KeyNotFoundException ->
              ()
          arg_types <- updated_arg_type::arg_types 
        | _ ->
          arg_types <- arg_type::arg_types
      arg_types <- List.rev arg_types
 
      // adding lambda itself as an entry for outside frame
      // in case of recursion
      let base_lam_type =
        LRclosure((arg_types, given_return_type),lam_name)
      this.add_entry(lam_name,base_lam_type,None)

      // pushing lambda frame
      this.push_frame(lam_name + "_frame",lb.line,lb.column)

      // add arg entries for this frame
      //for (outdated_arg_type,arg) in args do
       // let real_arg_type = 
        //this.add_entry(arg,arg_type,Some(Var(arg)))
      for i in 0..arg_types.Length-1 do
        let (outdated_arg_type,arg_name) = args.[i]
        this.add_entry(arg_name,arg_types.[i],Some(Var(arg_name)))

      // would now get information from entries
      let return_type = this.infer_type(expr)

      // we got the actual expr_type so we don't need the frame now
      this.pop_frame() |> ignore

      let mutable lambda_type = LRuntypable

      if (return_type=LRuntypable) then 
        printfn "[%d, %d] Lambda %A is untypable!" lb.line lb.column lam_name
      else if (given_return_type<>LRunknown
            && given_return_type<>return_type) then
        printfn "[%d, %d] Your given return type and the actual return type for %A isn't the same!" lb.line lb.column lam_name
      else
        // this means that either:
        // * there was no given return type, but the return type is
        //   typable. so, we can just return LRclosure((I,O),name).
        // 
        // * there was a given return type and it's the same as the
        //   actual return type. so again, we can return LRclosure.
        lambda_type <-
          LRclosure((arg_types, return_type), lam_name)

      // lambdas in the AST are unknown if the user doesn't
      // explicitly express what the types are. i can't be arsed
      // to make everything mutable again so i'll put this "updated"
      // lambda_type in the type_hash for lookup in later stages.
      this.push_type(lambda_type,expression)

      // editing the entry's `typeof` too...
      this.get_entry(lam_name).Value.typeof <- lambda_type

      lambda_type
    | _ -> LRuntypable

  // gets the type of an assumed entry from its name.
  member this.get_entry_type(s:string) =
    let potential_entry = this.get_entry(s)
    if (isSome potential_entry) then
      let entry_type = potential_entry.Value.typeof
      match entry_type with
      | LRclosure((_,return_type),_) ->
        try this.export_hash.[s]
        with
          | :? System.Collections.Generic.KeyNotFoundException ->
            entry_type
      | _ -> entry_type
    else LRuntypable

  // infers the type of an expression.
  member this.infer_type (expression:expr) = 
    match expression with 
    | Integer(_) -> LRint
    | Floatpt(_) -> LRfloat
    | Strlit(_) -> LRstring
    // atomic binary operations
    | Binop("+" as op,a,b) | Binop("-" as op,a,b)
    | Binop("*" as op,a,b) | Binop("/" as op,a,b)
    | Binop("%" as op,a,b) ->
      let mutable inferred_type = LRuntypable

      if (this.value_exists a && this.value_exists b) then
        let mutable a_type = this.infer_type(a)
        let mutable b_type = this.infer_type(b)

        // for my lovely lambdas
        match a_type with
        | LRclosure((_,return_type),_) -> (a_type <- return_type)
        | _ -> ()

        match b_type with
        | LRclosure((_,return_type),_) -> (b_type <- return_type)
        | _ -> ()

        if (a_type=LRfloat || b_type=LRfloat) then
          //expression <- Binop(op + "_LRfloat",ma,mb)
          this.push_type(LRfloat,expression)
          inferred_type <- LRfloat
        else if (a_type=LRint || b_type=LRint) then
          //expression <- Binop(op + "_LRint",ma,mb)
          this.push_type(LRint,expression)
          inferred_type <- LRint
        else if (a_type=LRunknown || b_type=LRunknown) then
          //expression <- Binop(op + "_LRunknown",ma,mb)
          this.push_type(LRunknown,expression)
          inferred_type <- LRunknown
        else
          printfn "You can't do a binary operation with these two variables!"

      inferred_type
    // relational binary operations. returns boolean as LRint
    // NOTE: it's not my job to figure out how to do
    // LRint < LRfloat!!! it returns boolean regardless.
    | Binop("<" as op,a,b) | Binop(">" as op,a,b)
    | Binop("<=" as op,a,b) | Binop(">=" as op,a,b)
    | Binop("==" as op,a,b) | Binop("!=" as op,a,b)
    | Binop("&&" as op,a,b) | Binop("||" as op,a,b)
    | Binop("^" as op,a,b) ->
      let mutable inferred_type = LRuntypable

      if (this.value_exists a && this.value_exists b) then 
        let mutable a_type = this.infer_type(a)
        let mutable b_type = this.infer_type(b)

        // for my lovely lambdas
        match a_type with
        | LRclosure((_,return_type),_) -> (a_type <- return_type)
        | _ -> ()

        match b_type with
        | LRclosure((_,return_type),_) -> (b_type <- return_type)
        | _ -> ()

        if (a_type=LRunknown || b_type=LRunknown) then
          this.push_type(LRunknown,expression)
          inferred_type <- LRunknown
        else if ((a_type=LRint || b_type=LRint)
              || (a_type=LRfloat || b_type=LRfloat)) then

          this.push_type(LRint,expression)
          inferred_type <- LRint // again. boolean
        else
          printfn "You can't compare these variables!"

      inferred_type
    | Uniop("car",a) | Uniop("cdr",a) ->
      if (this.value_exists a) then
        this.infer_type(a)
      else
        printfn "Your variable is untypable!"
        LRuntypable
    // `not` is only for booleans, which are LRint...
    | Uniop("not",a) ->
      let mutable inferred_type = LRuntypable
      if (this.value_exists a) then
        let a_type = this.infer_type(a)
        if (a_type = LRint) then
          this.push_type(a_type,expression)
          inferred_type <- a_type
        else
          printfn "You can only use the not operator for booleans (integers)."

      inferred_type
    // negative of a
    | Uniop("~",a) ->
      let mutable inferred_type = LRuntypable
      if (this.value_exists a) then 
        let a_type = this.infer_type(a)

        if (a_type=LRint || a_type=LRfloat) then
          this.push_type(LRfloat,expression)
          inferred_type <- a_type
        else printfn "You can only take the negative of a float or an integer."

      inferred_type
    | Uniop("print",a) ->
      let a_type = this.infer_type(a)
      this.push_type(a_type,a)

      LRunit // the only* expression w/ type unit.
    | Whileloop(cond,d) -> 
      this.infer_type(cond) |> ignore
      this.infer_type(d) |> ignore

      LRunit  //        *thanks todd
    | Ifelse(cond,i,e) ->
      let mutable inferred_type = LRuntypable

      if (this.value_exists i && this.value_exists e) then 
        this.infer_type(cond) |> ignore
        let if_type = this.infer_type(i)
        let else_type = this.infer_type(e)

        if (if_type=else_type) then inferred_type <- if_type
        else printfn "If and else statements must return the same value at the end."

      inferred_type
    | Setq(Lbox(s) as lb,e) -> 
      if (this.value_exists e) then this.infer_type(e)
      else LRuntypable
    | Beginseq(list) | Sequence(list) ->
      for i in 0 .. list.Length-2 do
        this.infer_type(list.[i].value) |> ignore
      this.infer_type(list.[list.Length-1].value)
    | Apply(f,args) ->
      let mutable inferred_type = LRuntypable
      let arg_types = [for arg in args do this.infer_type(arg)]

      // TODO: also account for value existing in LRclosure???
      match f with
      | Var(s) as v ->
        if (this.value_exists(v)) then
          let v_entry = this.get_entry(s).Value

          let f_type = v_entry.typeof
          match f_type with
          | LRclosure((lam_arg_types,return_type),_) ->
            if (arg_types.Length=lam_arg_types.Length) then
              // replace LRunknown w/ whatever user put, we assume
              // it's fine 
              // TODO: you probably shouldn't. double("hi")
              let mutable acceptable_types = []
              for i in 0..arg_types.Length-1 do
                if lam_arg_types.[i] = LRunknown then
                  acceptable_types <- arg_types.[i]::acceptable_types
                else
                  acceptable_types <-
                    lam_arg_types.[i]::acceptable_types

              acceptable_types <- List.rev acceptable_types
              if (arg_types=acceptable_types) then
                inferred_type <- return_type
              else
                printfn "You cannot apply arguments %A to %s(%A)." arg_types s lam_arg_types
          | _ -> printfn "var %A is not a closure..." s
      | TypedLambda(_) as lam ->
        let lam_type = this.infer_type(lam)
        match lam_type with
        | LRclosure((lam_arg_types,return_type),_) ->
          if (arg_types=lam_arg_types) then
            inferred_type <- return_type
          else
            printfn "You cannot apply arguments %A to the nameless function with arguments (%A)." arg_types lam_arg_types
        | _ -> printfn "lambda %A is not a closure..." lam
      | _ -> printfn "You can only apply arguments to a function!"

      inferred_type
    | Var(s) -> this.get_entry_type(s)
    | TypedVar(x_given_type, s) ->
      let entry_type = this.get_entry_type(s)

      if (entry_type=x_given_type) then entry_type
      else
        printfn "Your given type isn't the same as the variable's."
        LRuntypable
    // TODO: combine define and typeddefine maybe
    | Define(Lbox(s),value) as def ->
      let mutable value_type = LRuntypable
      match value with
      | TypedLambda(_) ->
        value_type <- this.get_lambda_type(value, s)
      | _ ->
        value_type <- this.infer_type(value)

      let mutable s_ast_rep = Some(value)
      match value_type with
      | LRclosure(_) -> (s_ast_rep <- Some(Var(s)))
      | _ -> ()

      if (value_type <> LRuntypable) then
        try this.add_entry(s,value_type,s_ast_rep)
        with | :? System.ArgumentException ->
          let actual_entry = this.get_entry(s).Value

          // this means that it was just declared
          if (isNone actual_entry.ast_rep) then
            actual_entry.ast_rep <- s_ast_rep
          // this means they're trying to do dynamic scoping.
          // not allowed!
          else
            printfn "Dynamic scoping isn't allowed, so your inner variable %s won't be referred to!" s
            this.add_entry(s + "_" + (string this.global_index),value_type,s_ast_rep)
      else
        printfn "Your definition for %s is untypable!" s

      value_type    
    | TypedDefine((Lbox(given_value_type,s) as lb),value) as def->
      let mutable inferred_type = LRuntypable

      let mutable value_type = LRuntypable
      match value with
      | TypedLambda(_) ->
        value_type <- this.get_lambda_type(value, s)
      | _ ->
        value_type <- this.infer_type(value)

      let mutable s_ast_rep = Some(value)
      match value_type with
      | LRclosure(_) -> (s_ast_rep <- Some(Var(s)))
      | _ -> ()

      if (value_type=given_value_type) then
        try this.add_entry(s,given_value_type,s_ast_rep)
        with | :? System.ArgumentException ->
          let actual_entry = this.get_entry(s).Value

          // this means that it was just declared
          if (isNone actual_entry.ast_rep) then
            actual_entry.ast_rep <- s_ast_rep
          // this means they're trying to do dynamic scoping.
          // not allowed!
          else
            printfn "Dynamic scoping isn't allowed, so your inner variable %s won't be referred to!" s
            this.add_entry(s + "_" + (string this.global_index),value_type,s_ast_rep)

        inferred_type <- value_type // TODO: should be done for dynamic scoping too?
      else
        printfn "[%d, %d] Your variable's type isn't the same as the value's type." lb.line lb.column

      inferred_type
    | Let(Lbox(s),value,(Lbox(e) as lbE)) ->
      this.push_frame("let_frame",lbE.line,lbE.column)
        
      let value_type = this.infer_type(value)
      this.add_entry(s,value_type,Some(value))

      // inferring the expression has to be after pushing the variable & value...
      let expr_type = this.infer_type(e)

      // we got the actual expr_type so we don't need the frame now
      this.pop_frame() |> ignore

      // the expressions inside lets can have different types
      // despite having the same expression, e.g.,
      // `let z:float=4.5 in (z+z)` and `let z=3 in (z+z)`.
      // so i'm adding the type_hash to eliminate ambiguity.
      // TODO: could i get rid of duplicates?
      this.push_type(expr_type,expression)

      expr_type 
    | TypedLet(Lbox((var_type,var)),value,(Lbox(expr) as lbE)) ->
      this.push_frame("typed_let_frame",lbE.line,lbE.column)

      let mutable inferred_type = LRuntypable
      let value_type = this.infer_type(value)

      if (var_type<>value_type) then
        printfn "[%d, %d] Your variable type and value type aren't the same." lbE.line lbE.column
      else
        this.add_entry(var,var_type,Some(value))

        let expr_type = this.infer_type(expr)
        this.pop_frame() |> ignore

        if (value_type<>expr_type) then
          printfn "[%d, %d] Your variable type and the return type of the `let` block isn't the same." lbE.line lbE.column
        else
          this.push_type(expr_type,expression)
          inferred_type <- expr_type
        
      inferred_type
    // sorry for the following horrible code in advance
    | TypedLambda(args,given_return_type,Lbox(expr)) as lam ->
      let mutable lam_type = this.get_lambda_type(lam, "0unnamed")
      let mutable (arg_types,return_type) =
        ([LRuntypable],LRuntypable) // good programming

      printfn "%A LAM TYPE: %A" lam lam_type
      match lam_type with
      | LRclosure((a,r),_) ->
        arg_types <- a
        return_type <- r
      | _ -> ()

      if (List.contains LRuntypable arg_types)
          || (return_type=LRuntypable) then
        LRuntypable
      else lam_type
    | Vector(list) ->
      let mutable inferred_type = LRuntypable
  
      if (list.Length = 0) then
        inferred_type <- LRunit
      else
        let first_type = this.infer_type(list.[0].value)
        let element_is_first_type i =
          (this.infer_type(i.value) = first_type)

        if (List.forall element_is_first_type list) then
          inferred_type <- LRlist(first_type)
        else
          printfn "All of the elements in your list must contain the same type!" 

      inferred_type
    | VectorGet(s, index_expr) ->
      let mutable inferred_type = LRuntypable

      let potential_entry = this.get_entry(s)
      if (isSome potential_entry) then
        let s_entry = potential_entry.Value
        if (isSome s_entry.ast_rep) then
          match s_entry.ast_rep.Value with
          | Vector(list) ->
            let index_type = this.infer_type(index_expr)
            if (index_type=LRint) then
              match s_entry.typeof with
              | LRlist(list_type) -> (inferred_type <- list_type)
              | _ -> (inferred_type <- s_entry.typeof) // ???
            else
              printfn "The index's type is not an integer!"
          | _ ->
            printfn "You can only access the index of a vector!"

      inferred_type
    | VectorSet(s, index_expr, value) ->
      let mutable inferred_type = LRuntypable

      let potential_entry = this.get_entry(s)
      if (isSome potential_entry) then
        let s_entry = potential_entry.Value
        if (isSome s_entry.ast_rep) then
          match s_entry.ast_rep.Value with
          | Vector(list) ->
            let index_type = this.infer_type(index_expr)
            if (index_type=LRint) then
              match s_entry.typeof with
              | LRlist(list_type) ->
                let value_type = this.infer_type(value)
                if (value_type=list_type) then
                  inferred_type <- value_type
                else 
                  printfn "Your value must be the same type as the vector's elements!"
              | _ -> () // ???
            else 
              printfn "The index precedes this vector's size!"
          | _ ->
            printfn "You can only access the index of a vector!"

      inferred_type
    | Export(s) ->
      let mutable inferred_type = LRunit

      let potential_entry = this.get_entry(s)
      if (isSome potential_entry) then
        let s_entry = potential_entry.Value
        this.export_hash.Add((s,s_entry.typeof))
        this.type_hash.Add((expression,s_entry.typeof))

        inferred_type <- s_entry.typeof

      inferred_type
    | _ -> LRuntypable

  //////// ASSIGNMENT 8.2 ////////
  member this.collect_freevars() =
    let VS = new var_set();
    if (isSome this.current_frame.parent_scope) then
      let mutable parent_frame =
        this.current_frame.parent_scope.Value

      for var in parent_frame.closure do
        VS.Add(var.Key,var.Value)

      for entry in parent_frame.entries do
        let table_entry = entry.Value

        match table_entry.typeof with
        | LRclosure(_) -> ()
        | _ ->
          if (isSome (this.get_entry(entry.Key))) then
            VS.Add((entry.Key,table_entry.gindex),table_entry.typeof)

    VS
(*
// create parser
let parser = make_parser();

// for file
let program = System.IO.File.ReadAllText("./perfect_test.7c")
let lexer = perfect_lexer<unit>(program);
let program_AST = parse_with(parser,lexer);
printfn "Program AST = %A" program_AST;;

// for typechecking
let global_table_frame =
  {
    name         = "global_table_frame";
    entries      = new HashMap<string, table_entry>();
    parent_scope = None;
    closure      = new var_set();
  };
let ST =
  {
    current_frame = global_table_frame;
    global_index  = 0;
    frame_hash    = new HashMap<(int*int), table_frame>();
    type_hash     = new HashMap<expr,lrtype>();
    export_hash   = new HashMap<string,lrtype>();
  };

let program_TI = ST.infer_type(program_AST.Value)
printfn "Program Type Inference = %A" program_TI
printfn "Symbol Table Type Hash = \n%A" (Seq.toList ST.type_hash)
printfn "Symbol Table Export Hash = \n%A" (Seq.toList ST.export_hash)

// for lexer
while true do
  Console.Write("Enter Expression: ");
  let lexpr = perfect_lexer<unit>(Console.ReadLine());
  let expr_AST = parse_with(parser,lexpr);
  printfn "Result = %A" expr_AST;

  if (isSome expr_AST) then
    let global_table_frame =
      {
        name         = "global_table_frame";
        entries      = new HashMap<string, table_entry>();
        parent_scope = None;
        closure      = new var_set();
      };
    let ST =
      {
        current_frame = global_table_frame;
        global_index  = 0;
        frame_hash    = new HashMap<(int*int), table_frame>();
        type_hash     = new HashMap<expr,lrtype>();
        export_hash   = new HashMap<string,lrtype>();
      };

    let program_TI = ST.infer_type(expr_AST.Value);
    printfn "Program Type Inference = %A" program_TI;
    printfn "Symbol Table Type Hash = \n%A" (Seq.toList ST.type_hash)
    printfn "Symbol Table Export Hash = \n%A" (Seq.toList ST.export_hash)
*)
