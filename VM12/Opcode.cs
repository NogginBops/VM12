﻿namespace VM12
{
    internal enum Opcode : short
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
        Not,
        Neg,
        Or,
        Xor,
        And,
        Inc,
        C_ss,
        Rot_l_c,
        Rot_r_c,
        Jmp,
        Jmp_z,
        Jmp_nz,
        Jmp_cz,
        Jmp_c,
        Eni,
        Dsi,
        Hlt,
        C_se,
        C_cl,
        C_flp,
        Jmp_gz,
        Jmp_lz,
        Jmp_z_l,
        Jmp_nz_l,
        Nand,
        Xnor,
        Dec,
        Add_c,
        Inc_l,
        Dec_l,
        Add_l,
        Not_l,
        Neg_l,
        Load_lit_l,
        Memc,
        Read,
        Write,
        Call_pc_nz,
        Call_pc_cz,
        Call_sp_nz,
        Call_sp_cz,
        Ret_z,
        Ret_nz,
        Ret_cz,
        Dup_l,
        Over_l_l,
        Over_l_s,
        Swap_l
    }
}