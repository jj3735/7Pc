# run with make PROGRAM=[file name] (no file type)
# you can also optionally put DIR=[direcotry]

PROGRAM = NULL_PROGRAM
CHEATS = cheats7c.c
DIR = .

COMPILER = 7Pc
LLVMIR = llvmir
SYMBOL_TABLE = symbol_table

PARSER = perfect_parser.dll
LEXER = perfect__lex.dll

EXECUTABLE = $(DIR)/$(PROGRAM)

# running the program
default: $(PROGRAM)
	@test -s $(EXECUTABLE) || { $(EXECUTABLE); }

# making the program
$(PROGRAM): $(PROGRAM).ll $(CHEATS)
	@clang $(DIR)/$(PROGRAM).ll $(CHEATS) -o $(DIR)/$(PROGRAM)
	@rm -f $(DIR)/$(PROGRAM).ll
$(PROGRAM).ll: $(COMPILER).exe $(DIR)/$(PROGRAM).7c
	@./$(COMPILER).exe $(DIR)/$(PROGRAM).7c

# compiling the compiler
$(COMPILER).exe: $(COMPILER).fs $(LLVMIR).exe $(LEXER) $(PARSER) $(SYMBOL_TABLE).exe
	fsharpc $(COMPILER).fs -r $(LLVMIR).exe -r $(LEXER) -r $(PARSER) -r $(SYMBOL_TABLE).exe
$(SYMBOL_TABLE).exe: $(SYMBOL_TABLE).fs $(PARSER) $(LEXER)
	fsharpc $(SYMBOL_TABLE).fs -r $(PARSER) -r $(LEXER)
$(LLVMIR).exe: $(LLVMIR).fs
	fsharpc $(LLVMIR).fs
