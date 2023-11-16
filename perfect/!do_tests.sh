#!/bin/bash
for i in $(ls ../tests/perfect/*.7c)
do
  file=$(basename $i)
  program_name="${file%%.*}"
 
  if [ ! -f "$program_name" ]; then
    echo "compiling $file..."
    make PROGRAM="$program_name" DIR=../tests/perfect
  fi

  if [ -f "../tests/perfect/$program_name" ]; then
    echo -e "executing $program_name...\n"
    ../tests/perfect/"$program_name"
  fi
done
