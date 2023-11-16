target triple = "x86_64-pc-linux-gnu" ; no windows allowed!
  declare void @lambda7c_printint(i32)
  declare void @lambda7c_printfloat(double)
  declare void @lambda7c_printstr(i8*)
  declare i32 @lambda7c_cin()
  declare void @lambda7c_newline()
  @r_4 = constant [2 x i8] c"\0a\00"
  @r_8 = constant [2 x i8] c"\0a\00"

define double @transaction_4(double %farg_amt_5, double* %balance_3, double* %init_2){
  funbegin_11:
    %amt_5 = alloca double
    store double %farg_amt_5, double* %amt_5
    %balance_3_2 = load double, double* %balance_3
    %amt_5_3 = load double, double* %amt_5
    %bin_4 = fadd double %balance_3_2, %amt_5_3
    store double %bin_4, double* %balance_3
    ret double %bin_4
} ; fn transaction_4

define double @make_account_1(double %farg_init_2){
  funbegin_10:
    %init_2 = alloca double
    store double %farg_init_2, double* %init_2
    %balance_3 = alloca double
    %init_2_2 = load double, double* %init_2
    store double %init_2_2, double* %balance_3
    ret double %transaction_4_3
} ; fn make_account_1

define i32 @main(){
  beginmain:
    %myaccount_8 = alloca double
    %make_account_1 = call double @make_account_1(double 100.0)
    store double %make_account_1, double* %myaccount_8
    %youraccount_9 = alloca double
    %make_account_2 = call double @make_account_1(double 200.0)
    store double %make_account_2, double* %youraccount_9
    %myaccount_3 = call double @myaccount_8(double 150.0)
    call void @lambda7c_printfloat(double %myaccount_3)
    %r_5 = getelementptr inbounds [2 x i8], [2 x i8]* @r_4, i64 0, i64 0
    call void @lambda7c_printstr(i8* %r_5)
    %r_7 = fneg double 25.0
    %youraccount_6 = call double @youraccount_9(double %r_7)
    call void @lambda7c_printfloat(double %youraccount_6)
    %r_9 = getelementptr inbounds [2 x i8], [2 x i8]* @r_8, i64 0, i64 0
    call void @lambda7c_printstr(i8* %r_9)
    ret i32 0
} ; fn main
