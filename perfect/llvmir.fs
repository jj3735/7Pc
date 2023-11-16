module Perfect_
open System
open Option

type Vec<'T> = ResizeArray<'T>
type HashMap<'K,'V> = System.Collections.Generic.Dictionary<'K,'V>
type Conslist<'K> = 'K list

(*
  //// TYPE "LLVMtype" ////
  Basic(s)          : "[s]"
  Pointer(type)     : "[type]*"
  Array_t(len,type) : "\[[len] x [type]\]"
  Userstruct(s)     : "[s]"
  Ellipsis          : "..."
  Void_t            : ""
*)

type LLVMtype =
  | Basic of string     // i32: Basic("i32")
  | Pointer of LLVMtype //i32*:  Pointer(Basic("i32"))
  | Array_t of int*LLVMtype  // [5 x i8], Array_t(5,Basic("i8"))
  | Userstruct of string
  | Ellipsis  // special case for optional args, like printf(i8*,...)
  | Void_t

(*
  //// TYPE "LLVMexpr" ////
  Iconst(i)   : i
  Sconst(s)   : "s"
  Fconst(f)   : f
  I1const(b)  : b    // booleans of type i1 are true and false
  Register(r) : %[r]
  Global(g)   : @[g]
  Novalue            // default

  //Label(s) : "s"   // not used
*)

type LLVMexpr =
  | Iconst of int
  | Sconst of string
  | Fconst of float
  | I1const of bool  
  | Register of string
  | Global of string
  | Novalue

(*
  //// TYPE "Instruction" ////

  // TERMINATOR INSTRUCTIONS //
  // ends a basic block //
  Ret(expr_type, expr) :
    "ret i32 4"
  Ret_noval :
    "ret"
  Br_uc(label_name) : // unconditional jump
    "br label %[label_name]"
  Bri1(cond_reg,true_label,false_label) : // always on i1
    "br i1 [cond_reg], label %[true_label], label %[false_label]"
  
  // MEMORY OPERATIONS //
  // store i32 3, i32* %a, optional "align 4", type i32* is omitted.
  // potential metadata could be align 8 or whatever
  Load(to_reg,t,from_reg,optional_metadata) :
   "%[to_reg] = load [t], [t]* [from_reg]{, [optional_metadata]}"

  // pointer type is omitted
  Store(t,value,dest,optional_metadata) :
    "store [t] [value], [t]* [dest]{, [optional_metadata]}"

  // NOTE: changed Option<int> to Option<string> since the
  // last argument is also for align...
  Alloca(reg_str,t,optional_metadata) :
    "%[reg_str] = alloca [t]{, [optional_metadata]}"

  // ALU OPERATIONS //
  Binaryop(reg_str,op,t,x,y) :
    "%[reg_str] = [op] [t] [x], [y]"

  // only for fneg float %r1
  Unaryop(reg_str,op,optional_metadata,t,value) :
    "%[reg_str] = [op] {[optional_metadata]} [t] [value]"

  // CASTING OPERATIONS //
  Cast(reg_str,cast_op,from_type,value,to_type) :
    "%[reg_str] = [cast_op] [from_type] [value] to [to_type]"

  // COMPARISON OPERATIONS //
  Icmp(reg_str,op,t,x,y) :
    "%[reg_str] = icmp [op] [t] [x], [y]"
  Fcmp(reg_str,op,t,x,y) :
    "%[reg_str] = fcmp [op] [t] [x], [y]"

  // SELECTION OPERATIONS //
  // true is on the left. "why would you select false?"
  SelectTrue(reg_str,t,true_expr,false_expr) :
    "%[reg_str] = select i1 true, [t] [true_expr], [t] [false_expr]"

  Phi2(reg_str,t,phi_1,from_1,phi_2,from_2) :
    "%[reg_str] = phi [t] \[[phi_1], [from_1]\], \[[phi_2], [from_2]\]"

  // phi2 is preferred...
  Phi(reg_str,t,phi_list) :
    "%[reg_str] = phi [t] \[[phi_1], [from_1]\], ..."

  // FUNCTION CALL //
  Call(optional_reg_str,t,other_types,func,args) :
    "{%[optional_reg_str] = } call [t]{(other_types)} @[func]{(args)}"

  // GETELEMENTPTR INSTRUCTION (aka the ritual) //
  // simplified for array access
  Arrayindex(reg_str,len,t,x,i) :
    "%[reg_str] = getelementptr inbounds \[[len] x [t]\], \[[len] x [t]\]* [x], i64 0, i64 [i]"

  // simplified for struct field
  Structfield(reg_str,struc,x,i) :
   "%[reg_str] = getelementptr inbounds %[struc], %[struc]* [x] i32 0, i32 [i]"

  // OTHER LLVM INSTRUCTIONS //
  // generic "other" instruction, default case, comments
  Verbatim(s) :
    "[s]"
*)

type Instruction =  
  | Ret of LLVMtype*LLVMexpr
  | Ret_noval 
  | Br_uc of string   
  | Bri1 of LLVMexpr*string*string 
  | Load of string*LLVMtype*LLVMexpr*Option<string>
  | Store of LLVMtype*LLVMexpr*LLVMexpr*Option<string>
  | Alloca of string*LLVMtype*Option<string> 
  | Binaryop of string*string*LLVMtype*LLVMexpr*LLVMexpr
  | Unaryop of string*string*Option<string>*LLVMtype*LLVMexpr 
  | Cast of string*string*LLVMtype*LLVMexpr*LLVMtype
  | Icmp of string*string*LLVMtype*LLVMexpr*LLVMexpr 
  | Fcmp of string*string*LLVMtype*LLVMexpr*LLVMexpr
  | SelectTrue of string*LLVMtype*LLVMexpr*LLVMexpr
  | Phi2 of string*LLVMtype*LLVMexpr*string*LLVMexpr*string
  | Phi of string*LLVMtype*Conslist<(LLVMexpr*string)>
  | Call of Option<string>*LLVMtype*Conslist<LLVMtype>*string*Conslist<(LLVMtype*LLVMexpr)>
  | Arrayindex of string*int*LLVMtype*LLVMexpr*LLVMexpr
  | Structfield of string*LLVMtype*LLVMexpr*LLVMexpr
  | Verbatim of string 

(*
// extracts the destination register from an instruction, returns string option
let destination = function
  | Load(s,_,_,_) | Alloca(s,_,_) | Unaryop(s,_,_,_,_) -> Some(s)
  | Binaryop(s,_,_,_,_) | Call(Some(s),_,_,_,_) -> Some(s)
  | Cast(s,_,_,_,_) | Icmp(s,_,_,_,_) | Fcmp(s,_,_,_,_) -> Some(s)
  | SelectTrue(s,_,_,_) | Phi2(s,_,_,_,_,_) | Phi(s,_,_) -> Some(s)
  | Arrayindex(s,_,_,_,_) | Structfield(s,_,_,_) -> Some(s) 
  | _ -> None  // includes case for Verbatim
*)

(*
  //// TYPE LLVMDECLARATION ////
  Globalconst(reg_str,t,c_str,optional_metadata) :
    "@[reg_str] = constant [t] c\[c_str]{, optional_metadata}"
  Externfunc(return_type,name,arg_types) :
    "declare [return_type] @[name]{arg_types}"
  Structdec(name,types) :
    "%struct.[name] = type \{types\}"
  Verbatim_dec(s):
    "[s]"
*)

type LLVMdeclaration =
  | Globalconst of string*LLVMtype*LLVMexpr*Option<string>
  | Externfunc of LLVMtype*string*Vec<LLVMtype>
  | Structdec of string*Vec<LLVMtype>
  | Verbatim_dec of string // cases not covered by above

type BasicBlock =
  {
     label: string;
     body: Vec<Instruction>; // last instruction must be a terminator
     predecessors : Vec<string>; //control-flow graph, not used for now
     ssamap : HashMap<string,string>; //current manifestation of a variable
  }

type LLVMFunction =
  {
     name: string;
     formal_args : Vec<(LLVMtype*string)>;
     return_type : LLVMtype;  // only basic and pointer types can be returned
     bblocks: Vec<BasicBlock>; 
     attributes : Vec<string>; // like "dso_local", "#1", or ["","#1"]
     bblocator : HashMap<string,int>; // vector index of basic block
  }

  //////// ASSIGNMENT 7 PART I ////////
  // gets last basic block label
  member this.current_BB_label() =
    let mutable bblocks_length = this.bblocks.Count
    if (bblocks_length=0) then bblocks_length <- 1

    this.bblocks.[bblocks_length-1].label

  // adds instruction to the last basic block of the function
  // currently being built.
  // you can only add to the very last basic block since you
  // can only add sequentially.
  member this.add_inst(instruction:Instruction) = 
    let mutable bblocks_length = this.bblocks.Count
    if (bblocks_length=0) then bblocks_length <- 1

    this.bblocks.[bblocks_length-1].body.Add(instruction)

  member this.add_BB(BB:BasicBlock) =
    let list_of_instructions = BB.body
    
    let bblocks_length = this.bblocks.Count
    let length_is_0 = (bblocks_length=0)
    let mutable is_last_block = false
    if (not(length_is_0) && (this.bblocks.[bblocks_length-1] = BB))
       || length_is_0 then
      is_last_block <- true

    for instruction in list_of_instructions do
      printfn "LIST OF INSTRUCRIONS %A" list_of_instructions
      let potential_new_BB =
        {
          label = "newBB";  // TODO: change label?
          body = list_of_instructions;
          predecessors = new Vec<string>();
          ssamap = new HashMap<string,string>();
        }

      if not(is_last_block) then
        // a basic block is terminated if it ends in a branch or
        // return. all instructions are added sequentially to the
        // last basic block until a terminating instruction is
        // added. at that point, a new basic block must be created
        // and added to the function.
        match instruction with
        | Bri1(_,_,_) | Br_uc(_) | Ret(_,_) | Ret_noval ->
          this.add_BB(potential_new_BB)
        | _ ->
          this.add_inst(instruction)
          
          // assuming the instruction is the first element...
          list_of_instructions.Remove(instruction) |> ignore
      else
        // only the last basic block can be non-terminated.
        this.add_inst(instruction)
        list_of_instructions.Remove(instruction) |> ignore

    this.bblocks.Add(BB)

type LLVMprogram =
  {
     preamble : string; // arbitrary stuff like target triple
     global_declarations : Vec<LLVMdeclaration>;
     functions: Vec<LLVMFunction>;
     postamble : string;  // stuff you don't want to know about
     //strconsts:HashMap<string,string>;
  }

  //////// ASSIGNMENT 6 PART II ////////
  member this.type_str(t:LLVMtype) =
    match t with
    | Basic(s) | Userstruct(s) -> s
    | Pointer(x) -> this.type_str(x) + "*"
    | Array_t(len,array_type) ->
      "[" + string len + " x " + this.type_str(array_type) + "]"
    | Ellipsis -> "..."
    | Void_t -> "void"

  member this.expr_str(expr:LLVMexpr) =
    match expr with
    | Sconst(s) -> s
    | Iconst(i) -> string i
    | Fconst(f) ->
      if ((int (f*10.0))%10=0) then (string f) + ".0"
      else string f 
    | I1const(b) -> if (b) then "true" else "false"
    | Register(s) -> "%" + s
    | Global(s) -> "@" + s
    | Novalue -> ""

  // i'm sorry to whoever has to read this
  member this.instruction_str(instruction:Instruction) =
    let mutable i_str = ""
    match instruction with
    // TERMINATOR INSTRUCTIONS //
    | Ret(expr_type,expr) ->
      i_str <-
        "ret " + this.type_str(expr_type) + " " + this.expr_str(expr)
    | Ret_noval ->
      i_str <- "ret void"
    | Br_uc(label_str) ->
      i_str <- "br label %" + label_str
    | Bri1(cond_reg,true_label,false_label) ->
      i_str <-
        "br i1 " + this.expr_str(cond_reg)
             + ", label %" + true_label
             + ", label %" + false_label
    // MEMORY OPERATIONS //
    | Load(to_reg,t,from_reg,optional_metadata) ->
      i_str <-
        "%" + to_reg + " = load " + this.type_str(t) + ", "
        + this.type_str(t) + "* " + this.expr_str(from_reg)

      if (isSome optional_metadata) then
        i_str <- i_str + ", " + optional_metadata.Value
    | Store(t,value,dest,optional_metadata) ->
      i_str <-
        "store " + this.type_str(t) + " " + this.expr_str(value)
        + ", " + this.type_str(t) + "* " + this.expr_str(dest)

      if (isSome optional_metadata) then
        i_str <- i_str + ", " + optional_metadata.Value
    | Alloca(reg_str,t,optional_metadata) ->
      i_str <- "%" + reg_str + " = alloca " + this.type_str(t)

      if (isSome optional_metadata) then
        i_str <- i_str + ", " + optional_metadata.Value
    // ALU OPERATIONS //
    | Binaryop(reg_str,op,t,x,y) ->
      i_str <-
        "%" + reg_str + " = " + op + " " + this.type_str(t)
        + " " + this.expr_str(x) + ", " + this.expr_str(y)
    | Unaryop(reg_str,op,optional_metadata,t,value) ->
      i_str <- "%" + reg_str + " = " + op 
          
      if (isSome optional_metadata) then
        i_str <- i_str + " " + optional_metadata.Value

      i_str <-
        i_str + " " + this.type_str(t) + " "
        + this.expr_str(value)
    // CASTING OPERATIONS //
    | Cast(reg_str,cast_op,from_type,value,to_type) ->
      i_str <-
        "%" + reg_str + " = " + cast_op + " "
        + this.type_str(from_type) + " " + this.expr_str(value)
        + " to " + this.type_str(to_type)
    // COMPARISON OPERATIONS //
    | Icmp(reg_str,op,t,x,y) ->
      i_str <-
        "%" + reg_str + " = icmp " + op + " " + this.type_str(t)
        + " " + this.expr_str(x) + ", " + this.expr_str(y)
    | Fcmp(reg_str,op,t,x,y) ->
      i_str <-
        "%" + reg_str + " = fcmp " + op + " " + this.type_str(t)
        + " " + this.expr_str(x) + ", " + this.expr_str(y)
    // SELECTION OPERATIONS //
    | SelectTrue(reg_str,t,true_expr,false_expr) -> 
      i_str <-
        "%" + reg_str + " = select i1 true, "
        + this.type_str(t) + " " + this.expr_str(true_expr) + ", "
        + this.type_str(t) + " " + this.expr_str(false_expr)
    | Phi2(reg_str,t,phi_1,from_1,phi_2,from_2) ->
      i_str <-
        "%" + reg_str + " = phi " + this.type_str(t)
        + " [" + this.expr_str(phi_1) + ", %" + from_1 + "],"
        + " [" + this.expr_str(phi_2) + ", %" + from_2 + "]"
    | Phi(reg_str,t,phi_list) ->
      i_str <-
        "%" + reg_str + " = phi " + this.type_str(t) + " " 

      for (phi_i,from_i) in phi_list do
        i_str <- i_str + "[" + this.expr_str(phi_i)
                 + ", %" + from_i + "], "

      // no trailing commas allowed!
      // -1 b/c array, -1 b/c extra space, -1 b/c trailing comma
      i_str <- i_str.[0..i_str.Length-3]
    // FUNCTION CALL //
    | Call(optional_reg_str,t,other_types,value,args) ->
      if (isSome optional_reg_str) then
        i_str <- "%" + optional_reg_str.Value + " = "

      i_str <- i_str + "call " + this.type_str(t) + "("
      for ot in other_types do
        i_str <- i_str + this.type_str(ot) + ","

      // no trailing commas allowed!!
      i_str <- i_str.[0..i_str.Length-2]

      // no random right parentheses allowed!
      if (other_types.Length<>0) then i_str <- i_str + ")"

      // assuming global here.
      i_str <- i_str + " @" + value + "("

      for (arg_type, arg_expr) in args do
        i_str <- i_str + this.type_str(arg_type)
                 + " " + this.expr_str(arg_expr) + ", "

      // no trailing commas allowed!!!!
      if (args.Length>0) then
        i_str <- i_str.[0..i_str.Length-3] + ")"
      else
        i_str <- i_str.[0..i_str.Length-1] + ")"
    // GETELEMENTPTR INSTRUCTION (aka the ritual) //
    | Arrayindex(reg_str,len,t,x,i) ->
      i_str <-
        "%" + reg_str + " = getelementptr inbounds ["
        + (string len) + " x " + this.type_str(t) + "], ["
        + (string len) + " x " + this.type_str(t) + "]* "
        + this.expr_str(x) + ", i64 0, i64 " + this.expr_str(i)
    | Structfield(reg_str,struc,x,i) ->
      i_str <-
        "%" + reg_str + " = getelementptr inbounds %"
        + this.type_str(struc) + ", %" + this.type_str(struc) + "* "
        + this.expr_str(x) + " i32 0, i32 " + this.expr_str(i)
    // OTHER LLVM INSTRUCTIONS //
    | Verbatim(s) ->
      i_str <- s
    
    i_str

  member this.declaration_str(declaration:LLVMdeclaration) =
    let mutable d_str = ""
    match declaration with
    | Globalconst(reg_str,t,c_str,optional_metadata) ->
      d_str <-
        "@" + reg_str + " = constant " + this.type_str(t)
        + " c\"" + this.expr_str(c_str) + "\""

      if (isSome optional_metadata) then
        d_str <- d_str + ", " + optional_metadata.Value 
    | Externfunc(return_type,name,arg_types) ->
      d_str <-
        "declare " + this.type_str(return_type) + " @" + name + "("

      for t in arg_types do
        d_str <- d_str + this.type_str(t) + ", "

      // NO TRAILING COMMAS!!!!!!!!
      d_str <- d_str.[0..d_str.Length-3] + ")"
    | Structdec(name, types) ->
      d_str <- "%struct." + name + " = type { "

      for t in types do
        d_str <- d_str + this.type_str(t) + ", "

      // !!!!!!!!!!!!!!!!
      d_str <- d_str.[0..d_str.Length-3] + " }"
    | Verbatim_dec(s) ->
      d_str <- s

    d_str

  member this.to_string_define(fn:LLVMFunction) =
    let mutable def_str =
      "\ndefine " + this.type_str(fn.return_type)
      + " @" + fn.name + "(" 

    for (arg_type,arg_name) in fn.formal_args do
      def_str <- def_str + this.type_str(arg_type)
                 + " %" + arg_name + ", "

    // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    if (fn.formal_args.Count>0) then
      def_str <- def_str.[0..def_str.Length-3]
    def_str <- def_str + ")"

    def_str

  member this.to_string() =
    let mutable LLVM_code = this.preamble

    for d in this.global_declarations do
      let d_str = this.declaration_str(d)
      LLVM_code <- LLVM_code + "  " + d_str + "\n" 
   
    for fn in this.functions do
      LLVM_code <- LLVM_code + this.to_string_define(fn) + "{\n"

      // printing bblocks
      for bb in fn.bblocks do
        // bblock label 
        LLVM_code <- LLVM_code + "  " + bb.label + ":\n"
       
        for i in bb.body do
          let i_str = this.instruction_str(i)
          LLVM_code <- LLVM_code + "    " + i_str + "\n"
      
        LLVM_code <- LLVM_code + "\n"

      // getting rid of last extra newline
      LLVM_code <- LLVM_code.[0..LLVM_code.Length-2]
      LLVM_code <- LLVM_code + "} ; fn " + fn.name + "\n"

    LLVM_code

  // for testing
  member this.print_instructions(instructions:Vec<Instruction>) =
    for i in instructions do
      let i_str = this.instruction_str(i)
      printfn "    %s" i_str

  // for testing
  member this.print_declarations(declarations:Vec<LLVMdeclaration>) =
    for d in declarations do
      let d_str = this.declaration_str(d)
      printfn "%s" d_str

//////// TESTING ////////
// you can see expected outputs & additional comments in `expected_outputs.txt`.
// skipping Call, Phi and LLVMdeclarations for now...
let instructions = new Vec<Instruction>()
instructions.AddRange(
  [Instruction.Binaryop("r2","add",Basic("i8"),Register("r1"),Iconst(1));
  Instruction.Bri1(LLVMexpr.Register("r1"), "label1", "label2");
  Instruction.Load("r2",LLVMtype.Basic("i32"),LLVMexpr.Register("r1"),None);
  Instruction.Load("r2",LLVMtype.Basic("i32"),LLVMexpr.Register("r1"),Some("align 4"));
  Instruction.Binaryop("r3","add",Basic("i32"),Register("r1"),Iconst(1));
  Instruction.Call(Some("r2"),Basic("i32"),[],"func1",[(Pointer(Basic("i8")),Register("r1")); (Basic("double"),Fconst(1.1))]);
  Instruction.Call(None,Basic("i32"),[Pointer(Basic("i8")); Ellipsis],"printf",[(Pointer(Basic("i8")),Register("r2")); (Basic("i32"),Register("r1"))]);
  Instruction.Arrayindex("r2",9,Basic("i8"),Global("str1"),Iconst(0));
  Instruction.Structfield("r2",Userstruct("bigstruct"),Register("r1"),Iconst(1));
  Instruction.Ret(LLVMtype.Basic("i32"),LLVMexpr.Register("r1"));
  Instruction.Ret_noval; 
  Instruction.Br_uc("loopstart");
  Instruction.Bri1(Register("r2"),"label1","label2");
  Instruction.Store(Basic("i8"),Iconst(1),Register("r1"),Some("align 1"));
  Instruction.Alloca("r1",Basic("i32"),Some("align 4"));
  Instruction.Unaryop("r2","fneg",None,Basic("float"),Register("r1"));
  Instruction.Cast("r2","bitcast",Pointer(Basic("i8")),Register("r1"),Pointer(Basic("i32")));
  Instruction.Icmp("r2","sle",Basic("i32"),Register("r1"),Iconst(1));
  Instruction.Fcmp("r2","oeq",Basic("float"),Fconst(4.0),Register("r1"));
  Instruction.SelectTrue("r3",Basic("i32"),Register("r1"),Register("r2"));
  Instruction.Phi2("r2",Basic("i32"),Iconst(1),"block1",Register("r1"),"block2");
  Instruction.Phi("3",Basic("i8"),[(Iconst(1),"bb1"); (Iconst(2),"bb2"); (Iconst(3),"bb3")]);
  Instruction.Call(Some("r1"),Basic("i64"),[],"factorial",[(Basic("i8"),Iconst(8))]);
  Instruction.Verbatim("; comments start with ; so you can add ; to end of line");
  Instruction.Call(None,Void_t,[],"lambda7c_newline",[])]
)

let declarations = new Vec<LLVMdeclaration>()
declarations.Add(Globalconst("str1",Array_t(6,Basic("i8")),Sconst("hello\00"),Some("align 1")))

let types = new Vec<LLVMtype>()
types.AddRange([Pointer(Basic("i8")); Ellipsis])
declarations.Add(Externfunc(Basic("i32"),"printf",types))

let types2 = new Vec<LLVMtype>()
types2.AddRange([Basic("i32"); Basic("i8"); Basic("double")])
declarations.Add(Structdec("bigstruct",types2))

let llvm_program =
  { //LLVMProgram
    preamble            = "";
    global_declarations = new Vec<LLVMdeclaration>();
    functions           = new Vec<LLVMFunction>();
    postamble           = "";
  }

llvm_program.print_instructions(instructions)
llvm_program.print_declarations(declarations)
