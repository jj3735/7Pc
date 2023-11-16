#!/bin/bash
for i in $(ls ../tests/nonperfect/*.7c)
do
  program_name=$(basename "${i%%.*}")

  echo "compiling $i..."
  if [ ! -f "$program_name" ]; then
    make PROGRAM="$program_name" DIR=../tests/nonperfect
  fi

  echo "executing $program_name..."
  if [ -f "../tests/nonperfect/$program_name" ]; then
    ../tests/nonperfect/"$program_name"
  fi
done
