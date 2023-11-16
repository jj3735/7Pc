module Nonperfect_
open Fussless
open Fussless.RuntimeParser
open Option
open System.Collections.Generic
open Nonperfect_

type var_set = SortedDictionary<(string*int),lltype>

type table_entry =
  { // table_entry
    // mutable for declaring & recursion..
    mutable typeof  : lltype; 
    gindex          : int;
    mutable ast_rep : expr option;
  }
and table_frame =
  { // table_frame
    name            : string;
    entries         : HashMap<string, table_entry>;
    parent_scope    : table_frame option;
    mutable closure : var_set; 
  }

// wrapping structure for symbol table frames
type SymbolTable = 
  { // SymbolTable
    mutable current_frame : table_frame;
    mutable global_index  : int;
    frame_hash            : HashMap<(int*int),table_frame>;
  }

  // adds a table entry.
  member this.add_entry(s:string,t:lltype,a:expr option) =
    this.global_index <- this.global_index + 1
  
    let new_entry =
      { //table_entry
        typeof  = t;
        gindex  = this.global_index;
        ast_rep = a;
      }

    this.current_frame.entries.Add(s,new_entry)


  // adds a new table frame.
  member this.push_frame(n,line,column) =
    let new_frame =
      { //table_frame
        name         = n;
        entries      = new HashMap<string,table_entry>();
        parent_scope = Some(this.current_frame);
        closure      = this.find_closure();
      }

    this.frame_hash.[(line,column)] <- new_frame
    this.current_frame <- new_frame

  // pops the last table frame.
  member this.pop_frame() =
    this.current_frame.parent_scope
      |> Option.map (fun p -> (this.current_frame <- p))

  // gets a table entry given a string.
  member this.get_entry (s:string) =
    let starting_frame = this.current_frame
    let mutable entry_found = false
    let mutable in_global_frame = false
    let mutable entry = None

    while (not(entry_found) && not(in_global_frame)) do
      let potential_entry =
        try
          Some(this.current_frame.entries.[s])
        with | :? System.Collections.Generic.KeyNotFoundException -> None
      if (isSome potential_entry) then
        entry <- potential_entry
        entry_found <- true
      else if (isNone this.current_frame.parent_scope) then
        in_global_frame <- true
      else
        this.pop_frame() |> ignore
          
    this.current_frame <- starting_frame
    if (not(entry_found) && s<>"getint") then
      printfn "Your variable %s does not exist!" s

    entry

  // if a variable is given, this function checks if it exists.
  // if given anything else (non-vars), we can say it does exist.
  member this.value_exists (potential_var:expr) =
    match potential_var with
    | Var(s) -> isSome (this.get_entry(s))
    | TypedVar(t,s) -> isSome (this.get_entry(s))
    | Sequence(seq) ->
      List.forall this.value_exists [for lb in seq -> lb.value]
    | _ -> true  // non-vars 

  // gets the type of a lambda
  member this.get_lambda_type (expression:byref<expr>) =
    match expression with
    | TypedLambda(args,given_return_type,(Lbox(lbexpr) as lb)) ->
      this.push_frame("temp_lambda_frame",lb.line,lb.column)

      // put all the variable types in a list
      let arg_types = [for (arg_type,arg) in args -> arg_type]

      // add entries for this frame 
      for (arg_type,arg) in args do
        this.add_entry(arg,arg_type,Some(Var(arg)))

      // would now get information from entries
      let mutable mlbexpr = lbexpr
      let return_type = this.infer_type(&mlbexpr)

      let mlb = lbox(mlbexpr,lb.line,lb.column)
      expression <- TypedLambda(args,given_return_type,mlb)

      // we got the actual expr_type so we don't need the frame now
      this.pop_frame() |> ignore

      let mutable lambda_type = LLuntypable
      if (mlbexpr=Nil) then 
         // this is only for declare... you could use define
         // for this, but errors will occur.
         lambda_type <- LLfun(arg_types, given_return_type)
      else if (return_type=LLuntypable) then 
        printfn "[%d, %d] Your lambda is untypable!" lb.line lb.column
      else if (given_return_type<>LLunknown
            && given_return_type<>return_type) then
        printfn "[%d, %d] Your given return type and the actual return type aren't the same!" lb.line lb.column
      else
        // this means that either:
        // * there was no given return type, but the return type is
        //   typable. therefore, we can just return LLfun(I,O)
        //  
        // * there was a given return type and it's the same as the
        //   actual return type. so again, we can return LLfun.
        expression <- TypedLambda(args,return_type,mlb)
        lambda_type <- LLfun(arg_types, return_type)

      lambda_type
    | _ -> LLuntypable

  // gets the type of an assumed entry from its name.
  member this.get_entry_type(s:string) =
    let potential_entry = this.get_entry(s)
    if (isSome potential_entry) then
      let entry_type = potential_entry.Value.typeof
      match entry_type with
      | LLfun(_,return_type) -> return_type
      | _ -> entry_type
    else LLuntypable

  // converts lltype to string
  member this.lltype_to_string(t:lltype) =
    match t with
    | LLint -> "LLint"
    | LLfloat -> "LLfloat"
    | LLstring -> "LLstring"
    | LLunit -> "LLunit"
    | LLunknown -> "LLunknown"
    | LLuntypable -> "LLuntypable"
    // NOTE: these might need to be changed in the future
    | LLvar(s) -> "LLvar"
    | LList(list_type) -> "LList"
    | LLtuple(type_list) -> "LLtuple"
    | LLfun(args,return_type) -> "LLfun"

  // infers the type of an expression.
  // a ByRef.InOut is needed for assignment 7 as we will manipulate
  // the expression itself to include its type.
  member this.infer_type (expression:byref<expr>) = 
    match expression with 
    | Integer(_) -> LLint
    | Floatpt(_) -> LLfloat
    | Strlit(_) -> LLstring
    // atomic binary operations + assigning
    | Binop("+" as op,a,b) | Binop("-" as op,a,b)
    | Binop("*" as op,a,b) | Binop("/" as op,a,b) 
    | Binop("%" as op,a,b)
    | Binop("=" as op,a,b) ->
      let mutable inferred_type = LLuntypable

      if (this.value_exists a && this.value_exists b) then
        // f# is lovely in the sense that you can't mutate a
        // variable unless it's mutable, even if you have its
        // specific memory address (i.e., a ByRef.In is passed
        // instead of a ByRef.InOut).
        // this means that i have to make a mutable version of all
        // arguments given & infer all their types just to really
        // make sure that the type inference is in the name!
        // wonderful.
        let mutable (ma,mb) = (a,b)
        let mutable a_type = this.infer_type(&ma)
        let mutable b_type = this.infer_type(&mb)

        // for my lovely lambdas
        match a_type with
        | LLfun(_,return_type) -> (a_type <- return_type)
        | _ -> ()

        match b_type with
        | LLfun(_,return_type) -> (b_type <- return_type)
        | _ -> ()

        if (a_type=LLfloat || b_type=LLfloat) then
          expression <- Binop(op + "_LLfloat",ma,mb)
          inferred_type <- LLfloat
        else if (a_type=LLint || b_type=LLint) then
          expression <- Binop(op + "_LLint",ma,mb)
          inferred_type <- LLint
        else if (a_type=LLunknown || b_type=LLunknown) then
          expression <- Binop(op + "_LLunknown",ma,mb)
          inferred_type <- LLunknown
        else
          printfn "You can't do a binary operation with these two variables!"

      inferred_type
    // relational binary operations. returns boolean as LLint
    // NOTE: it's not my job to figure out how to do
    // LLint < LLfloat!!! it returns boolean regardless.
    | Binop("<" as op,a,b) | Binop(">" as op,a,b)
    | Binop("<=" as op,a,b) | Binop(">=" as op,a,b)
    | Binop("eq" as op,a,b) | Binop("neq" as op,a,b) 
    | Binop("and" as op,a,b) | Binop("or" as op,a,b)
    | Binop("^" as op,a,b) ->
      let mutable inferred_type = LLuntypable
      if (this.value_exists a && this.value_exists b) then 
        let mutable (ma,mb) = (a,b)
        let mutable a_type = this.infer_type(&ma)
        let mutable b_type = this.infer_type(&mb)

        // for my lovely lambdas
        match a_type with
        | LLfun(_,return_type) -> a_type <- return_type
        | _ -> ()

        match b_type with
        | LLfun(_,return_type) -> b_type <- return_type
        | _ -> ()
  
        if (a_type=LLunknown || b_type=LLunknown) then 
          expression <- Binop(op + "_LLunknown",ma,mb)
          inferred_type <- LLunknown
        else if ((a_type=LLint || a_type=LLint)
              || (b_type=LLfloat || b_type=LLfloat)) then
          expression <- Binop(op + "_LLint",ma,mb)
          inferred_type <- LLint // again. boolean
        else
          printfn "You can't compare these variables!"

      inferred_type
    | Binop("cons",a,b) ->
      let mutable inferred_type = LLuntypable

      if (this.value_exists a && this.value_exists b) then 
        let mutable (ma,mb) = (a,b)
        let mutable a_type = this.infer_type(&ma)
        let mutable b_type = this.infer_type(&mb)

        // for my lovely lambdas
        match a_type with
        | LLfun(_,return_type) ->
          a_type <- return_type
        | _ -> ()

        match b_type with
        | LLfun(_,return_type) ->
          b_type <- return_type
        | _ -> ()

        if (a_type=b_type) then
          let a_type_string = this.lltype_to_string(a_type)
          expression <- Binop("cons_" + a_type_string,ma,mb)
          inferred_type <- a_type
        else printfn "You must cons the same type."

      inferred_type
    | Uniop("car",a) | Uniop("cdr",a) ->
      if (this.value_exists a) then
        let mutable ma = a
        this.infer_type(&ma)
      else
        printfn "Your variable is untypable!"
        LLuntypable
    // `not` is only for booleans, which are LLint...
    | Uniop("not",a) ->
      let mutable inferred_type = LLuntypable
      if (this.value_exists a) then
        let mutable ma = a
        let a_type = this.infer_type(&ma)
        if (a_type = LLint) then
          expression <- Uniop("not_LLint",a)
          inferred_type <- a_type
        else
          printfn "You can only use the not operator for booleans (integers)."

      inferred_type
    // negative of a
    | Uniop("~",a) ->
      let mutable inferred_type = LLuntypable
      if (this.value_exists a) then
        let mutable ma = a 
        let a_type = this.infer_type(&ma)

        if (a_type=LLint || a_type=LLfloat) then
          let a_type_string = this.lltype_to_string(a_type)
          expression <- Uniop("~_" + a_type_string,ma)
          inferred_type <- a_type
        else printfn "You can only take the negative of a float or an integer."

      inferred_type
    | Uniop("display",a) ->
      let mutable ma = a
      this.infer_type(&ma) |> ignore
      expression <- Uniop("display",ma)
  
      LLunit // the only* expression w/ type unit.
    | Whileloop(cond,d) ->
      let mutable (mcond, md) = (cond,d)
      this.infer_type(&mcond) |> ignore
      this.infer_type(&md) |> ignore
      expression <- Whileloop(mcond,md)

      LLunit  //        *thanks todd
    | Ifelse(cond,i,e) ->
      let mutable inferred_type = LLuntypable

      if (this.value_exists i && this.value_exists e) then 
        let mutable (mcond, mi, me) = (cond,i,e)
        let cond_type = this.infer_type(&mcond)
        let if_type = this.infer_type(&mi)
        let else_type = this.infer_type(&me)

        if (if_type=else_type) then
          expression <- Ifelse(mcond,mi,me)
          inferred_type <- if_type
        else printfn "If and else statements must return the same value at the end."

      inferred_type
    | Setq(Lbox(s) as lb,e) ->
      let mutable inferred_type = LLuntypable

      if (this.value_exists e) then
        let mutable me = e
        let s_type = this.get_entry_type(s)
        let e_type = this.infer_type(&me)
         
        expression <- Setq(lb,me)
        inferred_type <- e_type

      inferred_type
    | Beginseq(list) ->
      let mutable mlist = []
      let mutable last_element_type = LLuntypable
      let mutable i = 0
      while i <= list.Length-1 do
        let mutable mi_value = list.[i].value
        if (i = list.Length-1) then
          last_element_type <- this.infer_type(&mi_value)
        else this.infer_type(&mi_value) |> ignore
        let mutable mi = lbox(mi_value,list.[i].line,list.[i].column)
        mlist <- mi::mlist
        i <- i+1

      mlist <- List.rev mlist
      expression <- Beginseq(mlist)

      last_element_type
    | Sequence(list) ->
      let mutable mlist = []
      let mutable last_element_type = LLuntypable
      let mutable i = 0
        
      let mutable first_val_var = false
        
      match list.[0].value with
      | Var(s) as v ->
        if (this.value_exists(v)) then
          (first_val_var <- true)
          (last_element_type <- this.get_entry_type(s))
      | _ -> ()

      if (first_val_var = true && last_element_type <> LLuntypable)
      || (first_val_var = false) then
        while i <= list.Length-1 do
          let mutable mi_value = list.[i].value

          if (i = list.Length-1 && not(first_val_var)) then
            last_element_type <- this.infer_type(&mi_value)
          else this.infer_type(&mi_value) |> ignore
 
          let mutable mi =
            lbox(mi_value,list.[i].line,list.[i].column)
          mlist <- mi::mlist
          i <- i+1

        mlist <- List.rev mlist
        expression <- Sequence(mlist)

      last_element_type
    | Var(s) -> this.get_entry_type(s)
    | TypedVar(x_given_type, s) ->
      let entry_type = this.get_entry_type(s)

      if (entry_type=x_given_type) then entry_type
      else
        printfn "Your given type isn't the same as the variable's."
        LLuntypable
    | Define((Lbox(s) as lb),value) as def ->
      let mutable mvalue = value
      let value_type = this.infer_type(&mvalue)

      expression <- Define(lbox(s,lb.line,lb.column),mvalue)
      if (value_type <> LLuntypable) then
        try this.add_entry(s,value_type,Some(value))
        with | :? System.ArgumentException ->
          let actual_entry = this.get_entry(s).Value

          // this means that it was just declared
          if (isNone actual_entry.ast_rep) then
            actual_entry.ast_rep <- Some(def)
          // this means they're trying to do dynamic scoping. not allowed!
          else
            printfn "Dynamic scoping isn't allowed, so your inner variable %s won't be referred to!" s
            this.add_entry(s + "_" + (string this.global_index),value_type,Some(value))
            
        value_type
      else LLuntypable
    | TypedDefine((Lbox(s_type,s) as lb),value) as def ->
      let mutable inferred_type = LLuntypable
      let mutable mvalue = value
      let mutable value_type = this.infer_type(&mvalue)
      let mutable s_type_element = s_type

      match s_type with
      | LLtuple(list) -> s_type_element <- list.[0]
      | _ -> ()

      match value_type with
      | LLtuple(list) ->
        let mutable mlist_type = list.[0]
        value_type <- mlist_type
      | _ -> ()

      if (value_type=s_type_element) then 
        try this.add_entry(s,s_type,Some(value))
        with | :? System.ArgumentException ->
          let actual_entry = this.get_entry(s).Value

          // this means that it was just declared
          if (isNone actual_entry.ast_rep) then
            actual_entry.ast_rep <- Some(def)
          // this means they're trying to do dynamic scoping. not allowed!
          else
            printfn "Dynamic scoping isn't allowed, so your inner variable %s won't be referred to!" s
            this.add_entry(s + "_" + (string this.global_index),s_type,Some(value))

        inferred_type <- this.infer_type(&mvalue) 
      else printfn "[%d, %d] Your variable's type isn't the same as the value's type." lb.line lb.column

      inferred_type
    | Let((Lbox(s) as lbS),value,(Lbox(e) as lbE)) ->
      this.push_frame("let_frame",lbE.line,lbE.column)
        
      let mutable mvalue = value
      let value_type = this.infer_type(&mvalue)
      this.add_entry(s,value_type,Some(value))

      // the expression has to be inferred after the entry's added.
      let mutable me = e
      let expr_type = this.infer_type(&me)

      expression <- Let(lbS,mvalue,lbox(me,lbE.line,lbE.column))

      // we got the actual expr_type so we don't need the frame now
      this.pop_frame() |> ignore

      expr_type
    | TypedLet(Lbox((var_type,var)),value,(Lbox(expr) as lbE)) ->
      this.push_frame("typed_let_frame",lbE.line,lbE.column)

      let mutable inferred_type = LLuntypable
      let mutable mvalue = value
      let value_type = this.infer_type(&mvalue)
      let mutable inferred_type = LLuntypable

      if (var_type<>value_type) then
        printfn "[%d, %d] Your variable type and value type aren't the same." lbE.line lbE.column
      else
        this.add_entry(var,var_type,Some(value))
        if (value_type<>this.infer_type(ref expr)) then
          printfn "[%d, %d] Your variable type and the return type of the `let` block isn't the same." lbE.line lbE.column
        else inferred_type <- var_type
        
      inferred_type
    // sorry for the horrible code in the following sections in advance
    | TypedLambda(args,given_return_type,Lbox(expr)) as lam ->
      let mutable mlam = lam
      let mutable lam_type = this.get_lambda_type(&mlam)
      expression <- mlam
      let mutable (arg_types,return_type) = ([LLuntypable],LLuntypable) // good programming

      match lam_type with
      | LLfun(a,r) ->
        arg_types <- a
        if (expr=Nil) then
          // for declaring...
          return_type <- given_return_type
        else return_type <- r
      | _ -> ()

      if (List.contains LLuntypable arg_types) || (return_type=LLuntypable) then LLuntypable
      else lam_type
    | Vector(list) ->
      let mutable inferred_type = LLuntypable
  
      if (list.Length = 0) then inferred_type <- LLunit
      else
        let mutable mfirst_element_value = list.[0].value
        let first_type = this.infer_type(&mfirst_element_value)
        let element_is_first_type i =
          (this.infer_type(&i.value) = first_type)

        let mutable mlist = list // TODO: i don't think this works
        if (List.forall element_is_first_type mlist) then
          // the LLtype is a tuple of whatever the first type is,
          // where it's length is however long the vector is.
          inferred_type <- LLtuple([for i in 1 .. list.Length -> first_type])
        else
          printfn "All of the elements in your list must contain the same type!" 

      inferred_type
    | VectorGet(s, index) ->
      let mutable inferred_type = LLuntypable
      let entry_type = this.get_entry_type(s)

      if (entry_type<>LLuntypable) then
        match entry_type with
        | LLtuple(vector_types) ->
          if (vector_types.Length < index) then
            printfn "The index precedes this vector's size!"
          else inferred_type <- vector_types.[0]
        | _ -> 
          printfn "You can only access the index of a vector!"

      inferred_type
    | VectorSet(s, index, value) ->
      let mutable inferred_type = LLuntypable
      let entry_type = this.get_entry_type(s)

      if (entry_type<>LLuntypable) then
        match entry_type with
        | LLtuple(vector_types) ->
          let mutable mvalue = value
          if (vector_types.Length < index) then
            printfn "The index precedes this vector's size!"
          else if (this.infer_type(&mvalue) <> vector_types.[0]) then
            printfn "Your value must be the same type as the vector's elements!"
          else inferred_type <- vector_types.[0]
        | _ -> printfn "You can only access the index of a vector!"

      inferred_type
    | Declare(Lbox(s),x) ->
      let mutable mx = x
      let x_type = this.infer_type(&mx)
      this.add_entry(s,x_type,None)

      x_type
    | _ -> LLuntypable

  //////// ASSIGNMENT 7 PART II ////////
  member this.find_closure() =
    let mutable fvs = new var_set();
    this.collect_freevars(&fvs)
    fvs

  member this.collect_freevars(VS:byref<var_set>) =
    if (isSome this.current_frame.parent_scope) then
      let mutable parent_frame =
        this.current_frame.parent_scope.Value

      for var in parent_frame.closure do
        VS.Add(var.Key,var.Value)

      for entry in parent_frame.entries do
        let table_entry = entry.Value

        match table_entry.typeof with
        | LLfun(_,_) -> ()
        | _ ->
          if (isSome (this.get_entry(entry.Key))) then
            VS.Add((entry.Key,table_entry.gindex),table_entry.typeof)
