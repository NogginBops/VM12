namespace VM12_Opcode
{
    public enum IOMode
    {
        Compact,
        Fit,
        SFit,
        Cast,
        SCast,
    }

    public enum BlitMode : int
    {
        Black,
        And,
        AAndNotB,    // A And !B
        Src,
        NotAAndB,   // !A And B
        Dest,
        Xor,
        Or,
        Nor,
        Xnor,
        NotB,
        AOrNotB,     // A Or !B
        NotA,
        NotAOrB,    // !A Or B
        Nand,
        White
    }

    public enum JumpMode
    {
        Jmp,
        Z, Nz,
        C, Cz,
        Gz, Lz,
        Ge, Le,
        Eq, Neq,
        Ro,
        
        Z_l = 128, Nz_l,
        Gz_l, Lz_l,
        Ge_l, Le_l,
        Eq_l, Neq_l,
        Ro_l
    }

    public enum Opcode
    {
        Nop,
        Pop,
        Fp, Pc,
        Sp, Set_sp,
        Load_lit, Load_lit_l,
        Load_sp, Load_sp_l,
        Load_local, Load_local_l,
        Store_sp, Store_sp_l,
        Store_local, Store_local_l,
        Swap, Swap_l, Swap_s_l,
        Dup, Dup_l,
        Over, Over_l_l, Over_l_s,
        Add, Add_l, Add_c,
        Sub, Sub_l,
        Neg, Neg_l,
        Inc, Inc_l,
        Dec, Dec_l,
        Or, Xor, And, Not,
        C_ss, C_se, C_cl, C_flp,
        Rot_l_c, Rot_r_c,
        Mul, Div,
        Eni, Dsi, Hlt,
        Jmp,
        Call, Call_v,
        Ret, Ret_1, Ret_2, Ret_v,
        Memc,
        
        Inc_local, Inc_local_l,
        Dec_local, Dec_local_l,
        Mul_2,
        Fc, Fc_b,
        Mul_l, Mul_2_l,
        Div_l, Mod, Mod_l,
        Blit, Blit_mask,
        Read, Write,
    }

    public enum Opcode_old : short
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
        Ret_cz,
        Dup_l,
        Over_l_l,
        Over_l_s,
        Swap_l,
        Drop_v,
        Reclaim_v,
    }
}