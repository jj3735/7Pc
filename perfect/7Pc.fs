// THE 7PC COMPILER (aka 7c perfect compiler)
module Perfect_
open System
open Fussless
open Fussless.RuntimeParser
open Option
open System.Collections.Generic
open Perfect_

// HashMap<var_name,(symbol_table.gindex*LLVMtype)>
type closure_vars = HashMap<string,(int*LLVMtype)>

type LLVMCompiler = 
  { 
    symbol_table   : SymbolTable;
    program        : LLVMprogram;
    gindex         : int;
    mutable lindex : int;
    closure_hashes : HashMap<string, (table_frame*closure_vars)>
  }

  // gives new unique ID from this.lindex.
  member this.new_lID(lID:string) =
    this.lindex <- this.lindex + 1
    lID + "_" + (string (this.lindex))

  // gives new unique ID from the symbol table's gindex.
  member this.new_gID(gID:string) =
    this.symbol_table.global_index <-
      this.symbol_table.global_index + 1
    gID + "_" + (string (this.symbol_table.global_index))

  // creates a new BB record based on a given name.
  member this.new_BB(name:string) =
    { //BasicBlock
      label        = name;
      body         = new Vec<Instruction>();
      predecessors = new Vec<string>();
      ssamap       = new HashMap<string,string>();
    }

  // fixes string for Strlit() exprs by:
  //   * replacing all `\n` characters with `\0a`
  //   * adding the null terminator `\00` at the end.
  // it also gives back the corrected length.
  member this.fix_string(s:string) =
    // i can't think of a good analogy, but...
    // think of a jenga tower w/ blue and red pieces.
    // the amount of red pieces would be:
    // ((tower).Length-(blue tower).Length) = (red tower).Length
    // (red tower).Length / (red piece).Length = red pieces
    let all_newlines_length = s.Length - s.Replace("\\n","").Length
    let newline_amount = all_newlines_length / ("\\n".Length)

    // +1 for '\\00' and -2 for double quotes gives -1.
    let fixed_s_length = s.Length - newline_amount - 1

    // getting rid of double quotes & putting \\00
    let mutable fixed_s = s.Replace("\\n","\0a")
    fixed_s <- fixed_s.[1..fixed_s.Length-2] + "\\00"

    (fixed_s,fixed_s_length)

  // translates an lrtype to an LLVMtype
  member this.translate_lrtype(t:lrtype) =
    match t with
    | LRint -> Basic("i32")
    | LRfloat -> Basic("double")
    | LRvar(x) ->
      let potential_entry = this.symbol_table.get_entry(x)
      let mutable ll_type = LRuntypable

      if (isSome potential_entry) then
        ll_type <- potential_entry.Value.typeof
        
      // points to an int address
      Pointer(this.translate_lrtype(ll_type))
    | LRunknown -> Basic("i32") // TODO: this is temp
    | LRclosure((args,return_type),_) ->
      this.translate_lrtype(return_type)
    | LRunit -> Void_t
    // NOTE: LRstring, LRlist, LRunknown and LRtuple may have to be
    // implemented... LRtuple may be translated first type??
    // this is for LRuntypable...
    | _ ->
      printfn "translate_lrtype: tried to translate type %A which isn't supported.." t
      Void_t

  // gets the LLVMtype of an expr
  member this.expr_LLVMtype(expression:expr) =
    match expression with
    | Integer(_) -> Basic("i32")
    | Floatpt(_) -> Basic("double")
    | Strlit(s) ->
      let (fixed_s,fixed_s_length) = this.fix_string(s)
      Array_t(fixed_s_length,Basic("i8"))
    | Binop(_) | Uniop(_) | Let(_) | TypedLet(_) as expr ->
      let expr_lrtype = this.symbol_table.type_hash.[expr]
      this.translate_lrtype(expr_lrtype)
    | Var(x) | Apply(Var(x),_) ->
      let potential_entry = this.symbol_table.get_entry(x)
      if (isSome potential_entry) then
        let x_entry = potential_entry.Value
        this.translate_lrtype(x_entry.typeof)
      else Void_t
    | Beginseq(list) | Sequence(list) ->
      let last_element = list.[list.Length-1].value
      let last_element_lrtype =
        this.symbol_table.infer_type(last_element)
      this.translate_lrtype(last_element_lrtype)
    | TypedLambda(_) as lam ->
      let lam_type = this.symbol_table.type_hash.[lam]
      let mutable return_lrtype = LRunknown

      match lam_type with
      | LRclosure((_,r),_) -> (return_lrtype <- r)
      | _ -> ()

      this.translate_lrtype(return_lrtype)
    | Setq(_, e) | Define(_,e) | TypedDefine(_,e) ->
      this.expr_LLVMtype(e)
    | _ ->
      printfn "expr_LLVMtype: could not get LLVMtype from expr_op %A" expression
      Void_t

  // gets the LLVMop from a specific binop/uniop operation
  member this.op_to_LLVMop(operation:expr) =
    match operation with
    | Binop(op,_,_) | Uniop(op,_) as expr ->
      let expr_type = this.symbol_table.type_hash.[expr]
      let is_float = (expr_type = LRfloat)

      match op with
      // Binop //
      | "+" -> if is_float then "fadd" else "add"
      | "-" -> if is_float then "fsub" else "sub"
      | "*" -> if is_float then "fmul" else "mul"
      | "/" -> if is_float then "fdiv" else "udiv"
      | "%" -> if is_float then "frem" else "urem"
      | "<" -> "slt"
      | ">" -> "sgt"
      | "<=" -> "sle"
      | ">=" -> "sge"
      | "==" -> "eq"
      | "!=" -> "ne"
      | "&&" -> "and"
      | "||" -> "or"
      | "^" -> "xor"
      // Uniop //
      | "!" -> "eq" // not is eq w/ 0.
      | "~" -> "fneg"
      // TODO: car & cdr for arrays
      | _ ->
        printfn "op_to_LLVMop (binop/uniop): could not get LLVMop from op %A" op
        ""
    | (_ as op) ->
      printfn "op_to_LLVMop: cannot get LLVMop from operator %A" op
      ""

  member this.compile_expr(expression:expr, func:LLVMFunction) =
    match expression with
    | Integer(i) -> Iconst(i)
    | Floatpt(f) -> Fconst(f)
    | (Strlit(str) as strlit_expr) ->
      // fixing string for calculating length
      let (fixed_str,fixed_str_length) = this.fix_string(str)

      // declaring that string as a global variable
      let str_id = this.new_lID("r")
      let str_type = this.expr_LLVMtype(strlit_expr)
      let global_str_var =
        Globalconst(str_id,str_type,Sconst(fixed_str),None)

      // adding that global variable
      this.program.global_declarations.Add(global_str_var)

      // getting that global variable into a local register
      let local_str_id = this.new_lID("r")
      let get_inst =
        Arrayindex(local_str_id,fixed_str_length,
                   Basic("i8"),Global(str_id),Iconst(0))
      func.add_inst(get_inst)

      // returning the local string register
      Register(local_str_id) 
    | Binop(expr_op,a,b) as binop_expr ->
      let LLVMop = this.op_to_LLVMop(binop_expr)
      let mutable LLVMop_type = this.expr_LLVMtype(binop_expr)

      let a_dest = this.compile_expr(a,func)
      let mutable binop_id = ""

      // comparison operations: <, >, <=, >=, eq, neq
      if (LLVMop = "slt" || LLVMop = "sgt"
       || LLVMop = "sle" || LLVMop = "sge"
       || LLVMop = "eq"  || LLVMop = "ne") then
        let b_dest = this.compile_expr(b,func)
        let ptr_lID = this.new_lID("cmp") + "_ptr"

        let alloca_inst = Alloca(ptr_lID,Basic("i1"),None)
        func.add_inst(alloca_inst)
          
        binop_id <- "cmp_" + (string this.lindex)
        if (LLVMop_type = Basic("double")) then
          let fcmp_inst =
            Fcmp(binop_id,LLVMop,LLVMop_type,a_dest,b_dest)
          func.add_inst(fcmp_inst)
        else
          let icmp_inst =
            Icmp(binop_id,LLVMop,LLVMop_type,a_dest,b_dest)
          func.add_inst(icmp_inst)

        let store_inst =
         Store(Basic("i1"),Register(binop_id),Register(ptr_lID),None)
        func.add_inst(store_inst)

      // boolean operations: and, or, ^ (xor)
      else if (LLVMop = "xor") then
        let b_dest = this.compile_expr(b,func)
        binop_id <- this.new_lID("bool")

        // bruh i dont have a xor inst
        let xor_inst =
          Verbatim("%" + binop_id + " = xor i1 "
                   + this.program.expr_str(a_dest) + ", "
                     + this.program.expr_str(b_dest))
        func.add_inst(xor_inst)
      else if (LLVMop = "and" || LLVMop = "or") then
        let pred_label = func.current_BB_label()

        let b_bin_id = this.new_lID("b_bin")
        let end_bin_id = this.new_lID("e_bin")

        if (LLVMop = "and") then
          let bri1_inst = Bri1(a_dest,b_bin_id,end_bin_id)
          func.add_inst(bri1_inst)
        else
          let bri1_inst = Bri1(a_dest,end_bin_id,b_bin_id)
          func.add_inst(bri1_inst)

        // b_bin basic block //
        let mutable b_bin_BB = this.new_BB(b_bin_id)
        b_bin_BB.predecessors.Add(pred_label)
        func.add_BB(b_bin_BB)

        let b_bin_label = func.current_BB_label()
        let b_dest = this.compile_expr(b,func)

        binop_id <- this.new_lID("bin")
        let binop_lID = this.new_lID(binop_id)
          
        let mutable a_dest_id = this.program.expr_str(a_dest)

        match a_dest with
        | Global(_) | Register(_) ->
          // getting the pointer instead + getting rid of the %/@
          // at the beginning
          a_dest_id <- a_dest_id.[1..a_dest_id.Length-1] + "_ptr"
        | _ -> ()

        let a_dest_lID = this.new_lID(a_dest_id)

        // binary operations don't have a representation lol
        let bin_inst =
          Verbatim("%" + a_dest_lID + " = " + LLVMop + " i1 "
                   + this.program.expr_str(a_dest) + ", "
                   + this.program.expr_str(b_dest))
        func.add_inst(bin_inst)

        let store_bin_inst =
          Store(Basic("i1"),Register(a_dest_lID),
                Register(a_dest_id),None)
        func.add_inst(store_bin_inst)

        let br_inst = Br_uc(end_bin_id)
        func.add_inst(br_inst)

        // end_bin basic block //
        let mutable end_bin_BB = this.new_BB(end_bin_id)
        end_bin_BB.predecessors.Add(pred_label)
        end_bin_BB.predecessors.Add(b_bin_label)
        func.add_BB(end_bin_BB)

        // loading it
        let load_inst =
          Load(binop_id,Basic("i1"),Register(a_dest_id),None)
        func.add_inst(load_inst)
      // other binary operations: +, -, *, /, %
      else
        let b_dest = this.compile_expr(b,func)
        binop_id <- this.new_lID("bin")

        let binop_inst =
          Binaryop(binop_id,LLVMop,LLVMop_type,a_dest,b_dest)
        func.add_inst(binop_inst)

      // returning the binop register  
      Register(binop_id)  
    | Uniop("print",s) ->
      if (s = Strlit("\n")) then
        // that means that you're just printing a newline.
        // note that if you're printing a string that has
        // other stuff but contains `\n`, it'll call
        // "lambda7c_printstr" instead (else case).
        let newline_inst =
          Call(None,Void_t,[],"lambda7c_newline",[])
        func.add_inst(newline_inst)

        Novalue  // display doesn't return anything
      else if (this.symbol_table.value_exists(s)) then
        let mutable s_type = this.expr_LLVMtype(s)
        let s_dest = this.compile_expr(s,func)

        // getting specific print function from s_type
        let mutable print_func = ""
        match s_type with
        | Basic("i32") ->
          print_func <- "lambda7c_printint"
        | Basic("double")->
          print_func <- "lambda7c_printfloat"
        | Array_t(_,_) ->
          s_type <- Pointer(Basic("i8"))
          print_func <- "lambda7c_printstr"
        | _ -> ()

        // adding print instruction
        let print_inst =
          Call(None,Void_t,[],print_func,[(s_type,s_dest)])
        func.add_inst(print_inst)

        Novalue  // display doesn't return anything
      else Novalue // the value doesn't exist, don't do anything
    | Uniop(expr_op,a) as uniop_expr ->
      let LLVMop = this.op_to_LLVMop(uniop_expr)
      let LLVMop_type = this.expr_LLVMtype(uniop_expr)

      let a_dest = this.compile_expr(a,func)
      let uniop_id = this.new_lID("r")

      // this is the not case, which is the same as comparing
      // your boolean w/ 0 (0 gives 1 and 1 gives 0).
      if (LLVMop = "eq") then
        let icmp_inst =
          Icmp(uniop_id,LLVMop,Basic("i1"),a_dest,Iconst(0))

        func.add_inst(icmp_inst)
      // this is ~, i.e., taking the negative of something
      else if (LLVMop = "fneg") then
        let fneg_inst =
          Unaryop(uniop_id,LLVMop,None,LLVMop_type,a_dest)

        func.add_inst(fneg_inst)
      //else // TODO: car and cdr for arrays

      // returning the uniop register
      Register(uniop_id)  
    | Whileloop(cond, d) ->
      let cond_dest = this.compile_expr(cond,func)
      let pred_label = func.current_BB_label()

      // branching to `loop` or `endloop` depending on
      // the condition
      let loop_id = this.new_lID("loop")
      let endloop_id = this.new_lID("endloop")
      let bri1_inst = Bri1(cond_dest,loop_id,endloop_id)
      func.add_inst(bri1_inst)

      // loop basic block
      let mutable loop_BB = this.new_BB(loop_id)
      loop_BB.predecessors.Add(pred_label)
      func.add_BB(loop_BB)

      let loop_label = func.current_BB_label()
      let loop_dest = this.compile_expr(d,func)
      let check_cond_dest = this.compile_expr(cond,func)

      // branching to current loop label or `endloop`
      // depending on the condition
      let bri1_inst_2 = Bri1(check_cond_dest,loop_label,endloop_id)
      func.add_inst(bri1_inst_2)

      // endloop basic block
      let mutable endloop_BB = this.new_BB(endloop_id)
      endloop_BB.predecessors.Add(pred_label)
      endloop_BB.predecessors.Add(loop_label)
      func.add_BB(endloop_BB)

      Novalue  // while loops don't return anything 
    | Ifelse(cond,tC,fC) -> 
      let cond_dest = this.compile_expr(cond,func)
      let pred_label = func.current_BB_label()

      // branching to `iftrue` or `iffalse` depending on
      // the condition
      let iftrue_id = this.new_lID("iftrue")
      let iffalse_id = this.new_lID("iffalse")
      let br_inst = Bri1(cond_dest,iftrue_id,iffalse_id)
      func.add_inst(br_inst)

      // (for later) instruction to branch to `endif`
      let endif_id = this.new_lID("endif")
      let br_inst = Br_uc(endif_id)

      // COMPILING TRUE CASE //
      // true case basic block
      let mutable iftrue_BB = this.new_BB(iftrue_id)
      iftrue_BB.predecessors.Add(pred_label)
      func.add_BB(iftrue_BB)

      let tC_dest = this.compile_expr(tC,func)
      let iftrue_label = func.current_BB_label()

      // (for now) adding instruction to branch to `endif`
      func.add_inst(br_inst)

      // COMPILING FALSE CASE //
      // false case basic block
      let mutable iffalse_BB = this.new_BB(iffalse_id)
      iffalse_BB.predecessors.Add(pred_label)
      func.add_BB(iffalse_BB)

      let fC_dest = this.compile_expr(fC,func)
      let iffalse_label = func.current_BB_label()

      // (for now) adding instruction to branch to `endif`
      func.add_inst(br_inst)

      // endif basic block
      let mutable endif_BB = this.new_BB(endif_id)
      endif_BB.predecessors.Add(iftrue_label)
      endif_BB.predecessors.Add(iffalse_label)
      func.add_BB(endif_BB)

      // COMPILING ENDIF //
      // check type of a case (both cases return the same thing)
      let case_type = this.expr_LLVMtype(tC)

      // what do we return?
      if (case_type = Void_t) then Novalue
      else
        let phi_dest = this.new_lID("r")
        let phi_inst =
          Phi2(phi_dest,case_type,
               tC_dest,iftrue_label,fC_dest,iffalse_label)
        func.add_inst(phi_inst)
 
        // returning the phi register
        Register(phi_dest) 
    | Setq(Lbox(x),e) -> 
      let potential_entry = this.symbol_table.get_entry(x)
      if (isSome potential_entry) then
        let x_entry = potential_entry.Value

        // this is the name of x's original register
        let x_id = x + "_" + (string x_entry.gindex)
        let x_LLVMtype = this.translate_lrtype(x_entry.typeof)

        let e_dest = this.compile_expr(e,func)

        let store_inst =
          Store(x_LLVMtype,e_dest,Register(x_id),None)
        func.add_inst(store_inst)

        // returning the updated value
        e_dest
      else Novalue
    | Beginseq(list) | Sequence(list) ->
      // all we do is compile each expression in the sequence.
      for e in list.[0..list.Length-2] do
        this.compile_expr(e.value,func) |> ignore

      // we return the last thing compiled...
      if (list.Length > 0) then 
        this.compile_expr(list.[list.Length-1].value,func)
      else Novalue  // ...or nothing if it's an empty seq
    | Apply(Var("getint"),[]) ->
      let local_int_id = this.new_lID("in")

      let call_inst =
        Call(Some(local_int_id),Basic("i32"),[],"lambda7c_cin",[])
      func.add_inst(call_inst)

      Register(local_int_id)
    | Apply(Var("free"),[Var(x)]) ->
      printfn "free %A moment." x
      Novalue
    | Apply(Var(func_name) as v,func_args) ->
      // PSEUDOCODE //
      // if `func_name` exists & is a lambda, then we get
      //   * the substiutions for the formal arguments,
      //      i.e., the function arguments
      //   * the free variables required
      // we then call the function given all these parameters.
      if (this.symbol_table.value_exists(v)) then
        let func_entry =
          this.symbol_table.get_entry(func_name).Value

        // checking if reg is needed for call inst later //
        let mutable potential_reg = None
        match func_entry.typeof with
        | LRint | LRfloat | LRstring | LRvar(_) ->
          potential_reg <- Some(this.new_lID(func_name))
        | LRclosure((_,return_type),_) ->
          if (return_type<>LRunit) then
            potential_reg <- Some(this.new_lID(func_name))
        | _ -> ()

        match func_entry.typeof with
        | LRclosure(_) ->
          // getting function arguments //
          let mutable compiled_func_args = []

          for arg in func_args do
            let i_dest_type = this.expr_LLVMtype(arg)

            let mutable i_dest = Novalue
            match arg with
            | Var(x) ->
              let mutable x_id = ""
              let potential_entry = this.symbol_table.get_entry(x)
              if (isSome potential_entry) then
                let x_gindex = potential_entry.Value.gindex
                x_id <- x + "_" + (string x_gindex)
              else x_id <- this.new_gID(x)

              let local_x_id = this.new_lID(x_id)
              i_dest <- Register(local_x_id)

              let load_inst =
                Load(local_x_id,i_dest_type,Register(x_id),None)
              func.add_inst(load_inst)
            | _ ->
              i_dest <- this.compile_expr(arg,func)

            compiled_func_args <-
              (i_dest_type,i_dest)::compiled_func_args
          // end for

          compiled_func_args <- List.rev compiled_func_args

          // getting free vars //
          let mutable free_vars = new List<LLVMtype*string>()

          let mutable called_func = func
          if (this.program.functions.Count > 0) then
            let last_index = this.program.functions.Count-1
            called_func <- this.program.functions.[last_index]
          if (called_func.formal_args.Count > 0) then
            free_vars <-
              called_func.formal_args.GetRange(
                func_args.Length,
                called_func.formal_args.Count-1
              )

          // putting them in (LLVMtype*LLVMexpr) format
          let mutable fixed_free_vars = []
          for var in free_vars do
            let (v_type, v_name) = var
            let mutable reg_name = v_name

            let v_separator = v_name.IndexOf("_")
            let only_v_name = v_name.[0..v_separator-1]
                      
            // the register will use the current function's
            // argument instead of the called function
            // if it's being used (only happens in let)
            for arg in func.formal_args do
              let (arg_type, arg_name) = arg
              let arg_separator = arg_name.IndexOf("_")
              let only_arg_name = arg_name.[0..arg_separator-1]
              if (only_v_name = only_arg_name) then
                reg_name <- arg_name

            fixed_free_vars <-
              (v_type, Register(reg_name))::fixed_free_vars
          // end for

          fixed_free_vars <- List.rev fixed_free_vars

          // all the function arguments ordered are the
          // func arguments followed by the free vars...
          let all_args =
            List.append compiled_func_args fixed_free_vars

          let func_LLVMtype =
            this.translate_lrtype(func_entry.typeof)
          let r_func_name =
            func_name + "_" + (string func_entry.gindex)

          let call_inst =
            Call(potential_reg,func_LLVMtype,[],r_func_name,all_args)
          func.add_inst(call_inst)

          // if there's a register that stores the
          // call inst, it returns that. else Novalue
          if (isSome potential_reg) then
            Register(potential_reg.Value)
          else Novalue
        | _ ->
          // this means that you're trying to do function
          // application for a variable that isn't a
          // function, which is an error...
          printfn "attempting to call %A as a function when it is type %A..." func_name func_entry.typeof
          Novalue
        // func_entry.ast_rep match end
      else
        printfn "function %A doesn't exist!" func_name
        Novalue
    | Var(x) | TypedVar(_,x) ->
      let potential_entry = this.symbol_table.get_entry(x)

      if (isNone potential_entry) then Novalue
      else
        let x_entry = potential_entry.Value
        let x_type = x_entry.typeof
        let LLVM_x_type = this.translate_lrtype(x_type)

        let x_id = x + "_" + (string x_entry.gindex)
        let new_x_id = this.new_lID(x_id)
        match x_type with
        | LRclosure(_) -> Register(new_x_id)
        | _ ->
          let load_inst =
            Load(new_x_id,LLVM_x_type,Register(x_id),None)
          func.add_inst(load_inst)

          // returning a new x register
          Register(new_x_id)  
    | (Define(Lbox(x),(TypedLambda(_) as lam))) as def ->
      this.compile_function(def,func) 
    | Define(Lbox(x),value) | TypedDefine(Lbox(_,x),value) ->
      let value_LLVMtype = this.expr_LLVMtype(value)

      let mutable free_var_set = this.symbol_table.collect_freevars()

      let mutable potential_free_var = None
      for free_var in free_var_set do
        let (var_name, var_index) = free_var.Key
        if (var_name = x) then 
          potential_free_var <- Some(free_var)

      if (isNone potential_free_var) then
        // getting local entry if it's already there
        let mutable x_id = ""
        let potential_entry = this.symbol_table.get_entry(x)
        if (isSome potential_entry) then
          let x_entry = potential_entry.Value
          x_id <- x + "_" + (string x_entry.gindex)
        else
          x_id <- this.new_lID(x)

        let alloca_inst = Alloca(x_id,value_LLVMtype,None)
        func.add_inst(alloca_inst)
 
        let value_dest = this.compile_expr(value, func)
        let store_inst =
          Store(value_LLVMtype,value_dest,Register(x_id),None)
        func.add_inst(store_inst)           

        value_dest  // returning the compiled value
      else 
        // this means that you are trying to define & use a local
        // variable that has the same name as a free variable...
        // otherwise known as dynamic scoping!!!!!
        // no dynamic scoping allowed!
        Novalue
    | Let((Lbox(s) as lbS),value,(Lbox(_) as lb_expr)) ->
      // restarting lindex
      let original_lindex = this.lindex
      this.lindex <- 0
        
      // locate & set frame from lb_expr lbox.
      let original_frame = this.symbol_table.current_frame
      this.symbol_table.current_frame <-
       this.symbol_table.frame_hash.[(lb_expr.line,lb_expr.column)]

      // var should already exist in this frame due to typechecking
      let var_entry = this.symbol_table.get_entry(s).Value

      // doing something similar to define
      let var_id = s + "_" + (string var_entry.gindex)
      let var_LLVMtype = this.translate_lrtype(var_entry.typeof)

      let alloca_inst = Alloca(var_id,var_LLVMtype,None)
      func.add_inst(alloca_inst)

      let value_dest = this.compile_expr(value, func)
      let store_inst =
        Store(var_LLVMtype,value_dest,Register(var_id),None)
      func.add_inst(store_inst)

      // now compile the lb_expr body
      let lb_dest = this.compile_expr(lb_expr.value, func)

      // point the symbol table back
      this.symbol_table.current_frame <- original_frame

      // putting lindex back
      this.lindex <- original_lindex

      lb_dest // i guess
    | _ -> Novalue 

  //////// ASSIGNMENT 7 PART III ////////
  // this is when we use a define for a lambda...
  member this.compile_function(expression:expr, func:LLVMFunction) =
    match expression with
    | Define(Lbox(def_name), (TypedLambda(lambda_vars,_,(Lbox(lbexpr) as lb)) as lam)) ->
      // restarting lindex
      let original_lindex = this.lindex
      this.lindex <- 0
        
      // locate & set frame from lambda lbox.
      let original_frame = this.symbol_table.current_frame
      this.symbol_table.current_frame <-
        this.symbol_table.frame_hash.[(lb.line,lb.column)]

      // getting return_lrtype //
      // the lambda return_lrtype won't be updated if it's not
      // explicitly stated by the user. but, the inferred type is
      // stored in the type_hash, so we'll be getting it from there
      let lam_type = this.symbol_table.type_hash.[lam]
      let mutable return_lrtype = LRunknown

      match lam_type with
      | LRclosure((_,r),_) -> (return_lrtype <- r)
      | _ -> ()

      // initializing //
      let pointer_insts = new Vec<Instruction>();
      // for new func free vars
      let all_vars = new Vec<(LLVMtype*string)>(); 

      for (lvar_lrtype,lvar_name) in lambda_vars do
        // getting the LLVMtype of var
        let var_LLVMtype = this.translate_lrtype(lvar_lrtype)

        let potential_entry =
          this.symbol_table.get_entry(lvar_name)
        let mutable var_id = ""

        if (isSome potential_entry) then
          // getting var ID name from entry

          let var_entry = potential_entry.Value
          var_id <- lvar_name + "_" + (string var_entry.gindex)
        else
          // getting unique var ID name (from symbol table gindex)
          var_id <- this.new_gID(lvar_name)

        // getting local "real" var ID name (from this.lindex)
        let local_var_id = this.new_lID(var_id)

        // adding local var to all_vars:Vec<(LLVMtype*string)>
        all_vars.Add((var_LLVMtype,"farg_" + var_id)) 

        // add alloca & store instructions to nsts
        let alloca_inst = Alloca(var_id,var_LLVMtype,None)
        pointer_insts.Add(alloca_inst)

        // Store(t,value,to_reg,potential_metadata)
        let store_inst =
           Store(var_LLVMtype,Register("farg_" + var_id),
                 Register(var_id),None)
        pointer_insts.Add(store_inst)

        //// ACTUAL IMPORTANT SYMBOL TABLE STUFF
        // what this is supposed to do is add all the previous
        // free vars into the lambda table frame so that they are
        // there when needed... but only if they haven't been added
        if (isNone (this.symbol_table.get_entry(lvar_name))) then
          // add_entry func adds 1...
          this.symbol_table.global_index <-
            this.symbol_table.global_index - 1
          this.symbol_table.add_entry(lvar_name,lvar_lrtype,None)
      //// END FOR

      // adding free variables to current frame
      let mutable free_var_set = this.symbol_table.collect_freevars()
      this.symbol_table.current_frame.closure <- free_var_set

      // adding free_var:<(string*int),lrtype> to
      // all_vars:Vec<LLVMtype*string>
      for var in free_var_set do
        let (var_name,var_index) = var.Key
        let var_id = var_name + "_" + (string var_index)

        let var_LLVMtype = this.translate_lrtype(var.Value)
        // all free vars are pointers ig
        all_vars.Add(Pointer(var_LLVMtype),var_id)

      // make some_new_func w/ all vars as formal args
      let mutable def_name_id = ""
      let potential_entry = this.symbol_table.get_entry(def_name)

      if (isSome potential_entry) then
        let def_name_entry = potential_entry.Value
        def_name_id <-
          def_name + "_" + (string def_name_entry.gindex)
      else
        def_name_id <- this.new_gID(def_name)

      let return_LLVMtype = this.translate_lrtype(return_lrtype)
      let some_new_func =
        { // LLVMfunction
          name        = def_name_id;
          formal_args = all_vars;
          return_type = return_LLVMtype; 
          bblocks     = new Vec<BasicBlock>();
          attributes  = new Vec<string>();
          bblocator   = new HashMap<string,int>();
        }

      // add BB (dont name regs funbegin)
      let funbegin_id = this.new_gID("funbegin")
      let some_new_BB = this.new_BB(funbegin_id)
      some_new_BB.predecessors.Add(func.name)
      some_new_func.add_BB(some_new_BB)

      // add former arg insts
      for inst in pointer_insts do some_new_func.add_inst(inst)

      // compile expr w/ pointer to newfunc
      let some_dest = this.compile_expr(lbexpr,some_new_func)

      // point the symbol table back
      this.symbol_table.current_frame <- original_frame

      // check if last function
      let is_length_0 = (this.program.functions.Count = 0)
      let mutable is_last_function = false
      let last_index = this.program.functions.Count-1
      if (not(is_length_0) &&
          this.program.functions.[last_index].name = func.name) then
        is_last_function <- true

      if (not(is_last_function)) then
        if (some_dest <> Novalue) then
          some_new_func.add_inst(Ret(return_LLVMtype,some_dest))
        else
          // returns nothing. Void_t, Novalue.
          some_new_func.add_inst(Ret_noval)

        // add func in program
        this.program.functions.Add(some_new_func)

      // putting lindex back
      this.lindex <- original_lindex
      
      // returning Novalue
      Novalue
    | _ ->
      printfn "using compile_function for something other than define..."
      Novalue

  member this.compile_program(mainseq:expr) =
    let program_type = this.symbol_table.infer_type(mainseq);
    printfn "DA AST NOW BE LIKE: %A" mainseq
    printfn "DA PROGRAM TYPE IS %A" program_type
    //printfn "DA TYPE HASH BE LIKE: %A" (Seq.toList this.symbol_table.type_hash)
    //printfn "DA EXPORT HASH BE LIKE: %A" (Seq.toList this.symbol_table.export_hash)
    if program_type = LRuntypable then
      printfn "Program failed to type-check. No output produced."
      ""
    else
      // "define i32 @main()"
      let main_func =
        { // LLVMfunction
          name        = "main";
          formal_args = new Vec<LLVMtype*string>();
          return_type = Basic("i32"); 
          bblocks     = new Vec<BasicBlock>();
          attributes  = new Vec<string>();
          bblocator   = new HashMap<string,int>();
        }

      let begin_BB = this.new_BB("beginmain")
      main_func.add_BB(begin_BB)

      // compile programAST
      let LLVM_AST = this.compile_expr(mainseq, main_func)

      // "return 0;" at end of main
      main_func.add_inst(Ret(Basic("i32"),Iconst(0)));

      this.program.functions.Add(main_func)

      // assignment 6 part II
      this.program.to_string()

//////// COMPILING ////////
// getting program from input
let mutable program_name = ""
try program_name <- (Environment.GetCommandLineArgs() |> Array.tail |> Array.head)
with | :? System.ArgumentException -> printfn "You must input a file to compile!"
let program = System.IO.File.ReadAllText(program_name)

// generating program AST
let parser = make_parser();
let lexer = perfect_lexer<unit>(program)
let program_AST = parse_with(parser,lexer);

// for typechecking
let global_table_frame =
  { // table_frame
    name         = "global_table_frame"
    entries      = new HashMap<string, table_entry>()
    parent_scope = None
    closure      = new SortedDictionary<(string*int),lrtype>()
  };
let ST =
  { // SymbolTable
    current_frame = global_table_frame;
    global_index  = 0;
    frame_hash    = new HashMap<(int*int), table_frame>();
    type_hash     = new HashMap<expr,lrtype>();
    export_hash   = new HashMap<string,lrtype>();
  };

// for compiling
let llprogram =
  { // LLVMprogram
    preamble = "target triple = \"x86_64-pc-linux-gnu\""
             + " ; no windows allowed!"
             + "\n  declare void @lambda7c_printint(i32)"
             + "\n  declare void @lambda7c_printfloat(double)"
             + "\n  declare void @lambda7c_printstr(i8*)"
             + "\n  declare i32 @lambda7c_cin()"
             + "\n  declare void @lambda7c_newline()"
             + "\n";
    global_declarations = new Vec<LLVMdeclaration>();
    functions           = new Vec<LLVMFunction>();
    postamble           = "";
  };
let compiler =
  { // LLVMCompiler
    symbol_table   = ST;
    program        = llprogram;
    gindex         = 0;
    lindex         = 0;
    closure_hashes = HashMap<string,(table_frame*closure_vars)>();
  };

// compiling the program
if (isSome program_AST) then
  let LLVM_program = compiler.compile_program(program_AST.Value)
  if (LLVM_program<>"") then
    printfn "\n\n\nCODE:\n%s" LLVM_program

    System.IO.File.WriteAllText(
      program_name.[0..program_name.Length-4] + ".ll",
      LLVM_program
    )
else
  printfn "how the hell do you expect me to do code for an empty program"
