# 7PC Compiler
This is a compiler created using Fussless. It is statically scoped and uses infix-syntax.
This compiler supports basic concepts like variables, while loops and such. It also supports closures and functions.
Functions cannot return functions, nor are arrays available (as of yet).

## File Structure
- The `tests` folder contains various tests.
- `7Pc.fs` is the main file that takes care of the compiling
- `Makefile` automates using the compiler for you.
- `cheats7c.c` is used to embed certain functions in C for 7NPc. These include input and printing functions.
- `llvmir.fs` contains the code to parse LLVM & how to represent it.
- `symbol_table.fs` contains the code on making the symbol table as well as the AST representation for your code.

## Running the Compiler
I made a Makefile to automate the compiling.
Type `make PROGRAM=[filename]` (no file type) and it should work.
You can also optionally put `DIR=[directory]` to specify where the
file is. It will put its compiled file in the same directory.

**In case the Makefile doesn't work, here is how to do the whole process manually:**

#### TO COMPILE THE COMPILER:
`fsharpc symbol_table.fs -r perfect_parser.dll -r perfect__lex.dll`

`fsharpc llvmir.fs -r symbol_table.exe`

`fsharpc 7Pc.fs -r llvmir.exe -r perfect__lex.dll -r perfect_parser.dll -r symbol_table.exe`

#### ****TO RUN YOUR 7C PROGRAM:****
`./7Pc.exe [file].7c`
`clang [file].ll cheats7c.c -o [file]`
`./[file]`
