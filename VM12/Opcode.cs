﻿namespace VM12
{
    enum Opcode : short
    {
        Nop,
        Load_addr,
        Load_lit,
        Load_sp,
        Store_pc,
        Store_sp,
        Call_sp,
        Call_pc,
        Ret,
        Dup,
        Over,
        Swap,
        Drop,
        Reclaim,
        Add,
        Sh_l,
        Sh_r,
        Not,
        Neg,
        Xor,
        And,
        Inc,
        Add_f,
        Neg_f,
        Jmp,
        Jmp_z,
        Jmp_nz,
        Jmp_cz,
        Jmp_fz,
        Hlt = 0xFFF,
    }
}