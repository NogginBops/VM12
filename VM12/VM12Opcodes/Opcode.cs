namespace VM12_Opcode
{
    public static class Constants
    {
        public const int RAM_SIZE = 10_483_712; // before gram: 10_485_760
        public const int GRAM_SIZE = 2048;
        public const int VRAM_SIZE = 307_200;
        public const int ROM_SIZE = 5_984_256;

        public const int RAM_START = 0;
        public const int GRAM_START = RAM_SIZE;
        public const int VRAM_START = RAM_SIZE + GRAM_SIZE;
        public const int ROM_START = RAM_SIZE + GRAM_SIZE + VRAM_SIZE;

        public const int RAM_END = RAM_START + RAM_SIZE - 1;
        public const int GRAM_END = GRAM_START + GRAM_SIZE - 1;
        public const int VRAM_END = VRAM_START + VRAM_SIZE - 1;
        public const int ROM_END = ROM_START + ROM_SIZE - 1;

        public const int MEM_SIZE = RAM_SIZE + GRAM_SIZE + VRAM_SIZE + ROM_SIZE;

        public const int SCREEN_WIDTH = 640;
        public const int SCREEN_HEIGHT = 480;

        public const int STORAGE_START_ADDR = 0;
        public const int STORAGE_SIZE = 357_913_941 / 2;

        public const int STACK_MAX_ADDRESS = 0x100_000;
    }
    
    public enum InterruptType : int
    {
        stop,
        h_Timer = 0xFFF_FF0,
        v_Blank = 0xFFF_FE0,
        keyboard = 0xFFF_FD0,
        mouse = 0xFFF_FC0,
    }
    
    public enum JumpMode : int
    {
        Jmp,
        Z, Nz,
        C, Cz,
        Gz, Lz,     // signed
        Ge, Le,     // signed
        Eq, Neq,
        Ro,
        
        Z_l = 128, Nz_l,
        Gz_l, Lz_l,     // signed
        Ge_l, Le_l,     // signed
        Eq_l, Neq_l,
        Ro_l
    }
    
    public enum GrapicOps : int
    {
        Nop = 0,
        // Halts execution until next frame.
        Hlt = 1,
        Hlt_reset = 2,
        Jmp = 3,

        Line = 10,
        Rectangle = 11,
        Ellipse = 12,
        Fontchar = 13,
        TrueColorSprite = 14,
        PalettedSprite = 15,
        Fontchar_Mask = 16,
        TrueColorSprite_Mask = 17,
        PalettedSprite_Mask = 18,
        FontcharBuffer = 19,
    }

    public enum Opcode : int
    {
        Nop,
        Pop,
        Fp, Pc, Pt,
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
        Read, Write,

        Clz, Ctz,
        Selz, Selgz, Selge, Selc,

        Start_coproc, Hlt_coproc, Int_coproc,
        Graf_clear, Graf_fill,
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