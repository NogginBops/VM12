using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM12_Opcode;

namespace FastVM12Asm
{
    public enum StatementType
    {
        Include,
        Constant,
        Proc,
        Flag,
    }
    
    public enum ExpressionType
    {
        NumberLitteral,
        CharLitteral,
        StringLitteral,

        SimpleInstruction,

        LoadConst,
        LoadLocal,
        LoadRegister,
        LoadString,
        LoadProcPointer,
        
        StoreLocal,
        StorePointer,
        StoreRegister,

        IncLocal,
        DecLocal,

        FunctionCall,

        // #(...) type constants that need to be evaluated.
        ComplexConstant,
    }

    public struct Trace
    {
        public string FilePath;
        public int Line;
        public StringRef TraceString;
    }

    public struct StringRef
    {
        public string Data;
        public int Index;
        public int Length;

        public StringRef(string data, int index, int length)
        {
            Data = data;
            Index = index;
            Length = length;
        }

        public override string ToString()
        {
            return Data.Substring(Index, Length);
        }
    }

    public struct Statement
    {
        public StatementType Type;
        public Trace Trace;
        public StringRef IncludeFile;
        public StringRef ConstantName;
        public Expression ConstantValue;
        public StringRef ProcName;
        public List<Expression> ProcData;
        public StringRef Flag;
    }

    public class Expression
    {
        public ExpressionType Type;
        public Trace Trace;

        public StringRef NumberString;
        public char CharValue;
        public StringRef StringValue;

        public Opcode Opcode;

        
        public int LocaLindex;

        // FunctionCall, Label
        public StringRef LabelName;

        // For instructions that deal with 
        public int LocalIndex;

    }

    public class Parser
    {
        private List<Token> Tokens;
        
        public Parser(List<Token> tokens)
        {
            Tokens = tokens;
        }

        public List<Statement> Parse()
        {
            return default;
        }
    }
}
