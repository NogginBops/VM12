namespace VM12Opcode
{
    public static class Constants
    {
        public const int RAM_SIZE = 10_416_128; // before gram: 10_485_760 before sndram: 10_483_712 before gram extension: 10_479_616
        public const int SNDRAM_SIZE = 4096;
        public const int GRAM_SIZE = 65_536;
        public const int VRAM_SIZE = 307_200;
        public const int ROM_SIZE = 5_984_256;

        public const int RAM_START = 0;
        public const int SNDRAM_START = RAM_START + RAM_SIZE;
        public const int GRAM_START = SNDRAM_START + SNDRAM_SIZE;
        public const int VRAM_START = GRAM_START + GRAM_SIZE;
        public const int ROM_START = VRAM_START + VRAM_SIZE;

        public const int RAM_END = RAM_START + RAM_SIZE - 1;
        public const int SNDRAM_END = SNDRAM_START + SNDRAM_SIZE - 1;
        public const int GRAM_END = GRAM_START + GRAM_SIZE - 1;
        public const int VRAM_END = VRAM_START + VRAM_SIZE - 1;
        public const int ROM_END = ROM_START + ROM_SIZE - 1;

        // This should always be this!
        public const int MEM_SIZE = 0x1_000_000;

        public const bool VALID_MEM_PARTITIONS = MEM_SIZE == (RAM_SIZE + SNDRAM_SIZE + GRAM_SIZE + VRAM_SIZE + ROM_SIZE);

        public const int SCREEN_WIDTH = 640;
        public const int SCREEN_HEIGHT = 480;

        public const int STORAGE_START_ADDR = 0;
        public const int STORAGE_SIZE = 357_913_941 / 2;

        public const int STACK_MAX_ADDRESS = 0x100_000;

        public const int MAX_WORD = 4096;
        public const int MAX_DWORD = 16777216;
    }
    
    public enum InterruptType : int
    {
        stop,
        h_timer = 0xFFF_FF0,
        v_blank = 0xFFF_FE0,
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
    
    public enum SetMode : int
    {
        Z, Nz,
        C, Cz,
        Gz, Lz,
        Ge, Le,

        Z_l = 128, Nz_l,
        C_l, Cz_l,
        Gz_l, Lz_l,
        Ge_l, Le_l,
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

        Fontchar = 20,
        FontcharBg = 21,
        FontcharMask = 22,
        FontcharBuffer = 23,
        FontcharBufferColor = 24,
        FontcharBufferColorBg = 25,

        TrueColorSprite = 30,
        PalettedSprite = 31,
        TrueColorSprite_Mask = 32,
        PalettedSprite_Mask = 33,
    }

    public enum Opcode : int
    {
        Nop,
        Pop,
        Fp, Pc, Pt,
        Sp, Set_sp, Set_fp,
        Add_sp_lit_l,
        Load_lit, Load_lit_l,
        Load_sp, Load_sp_l,
        Load_local, Load_local_l,
        Store_sp, Store_sp_l,
        Store_local, Store_local_l,
        Swap, Swap_l, Swap_s_l,
        Dup, Dup_l,
        Over, Over_l_l, Over_l_s, Over_s_l,
        Add, Add_l, Add_c, Add_c_l,
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
        Mul_Add, Mul_Add_l,
        
        Inc_local, Inc_local_l,
        Dec_local, Dec_local_l,
        Mul_2,
        [System.Obsolete]
        Fc,
        [System.Obsolete]
        Fc_b,
        Mul_l, Mul_2_l,
        Div_l, Mod, Mod_l,
        Read, Write,

        Clz, Ctz,
        Selz, Selgz, Selge, Selc,
        Set,
        Brk,

        Start_coproc, Hlt_coproc, Int_coproc,
        [System.Obsolete]
        Graf_clear,
        [System.Obsolete]
        Graf_fill,
        Int_snd_chip,
    }
}