﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM12_Opcode;

namespace FastVM12Asm
{
    static class Util
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetMS(this Stopwatch watch)
        {
            return (watch.ElapsedTicks / (double)Stopwatch.Frequency) * 1000d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetSec(this Stopwatch watch)
        {
            return watch.ElapsedTicks / (double)Stopwatch.Frequency;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetMsFromTicks(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHexOrUnderscore(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F') ||
                   c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOctalOrUnderscore(char c)
        {
            return (c >= '0' && c <= '7') || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBinaryOrUnderscore(char c)
        {
            return c == '0' || c == '1' || c == '_';
        }

        internal static bool IsDecimalOrUnderscore(char c)
        {
            return char.IsNumber(c) || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HexToInt(char c)
        {
            c = char.ToUpper(c);  // may not be necessary

            return c < 'A' ? c - '0' : 10 + (c - 'A');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int OctalToInt(char c)
        {
            return c - '0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryToInt(char c)
        {
            return c - '0';
        }

        public static int Log2(int v)
        {
            int r = 0xFFFF - v >> 31 & 0x10;
            v >>= r;
            int shift = 0xFF - v >> 31 & 0x8;
            v >>= shift;
            r |= shift;
            shift = 0xF - v >> 31 & 0x4;
            v >>= shift;
            r |= shift;
            shift = 0x3 - v >> 31 & 0x2;
            v >>= shift;
            r |= shift;
            r |= (v >> 1);
            return r;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Must provide a input file!");
                Console.ReadLine();
                return;
            }

            double totalTime = 0;

            if (File.Exists(args[0]))
            {
                {
                    new Tokenizer(args[0]).Tokenize();
                    new Tokenizer(args[0]).Tokenize();
                    new Tokenizer(args[0]).Tokenize();
                    new Tokenizer(args[0]).Tokenize();
                    new Tokenizer(args[0]).Tokenize();
                    new Tokenizer(args[0]).Tokenize();

                    var tokenizer = new Tokenizer(args[0]);
                    var toks = tokenizer.Tokenize();

                    new Parser(tokenizer.CurrentFile, toks).Parse();
                    new Parser(tokenizer.CurrentFile, toks).Parse();
                    new Parser(tokenizer.CurrentFile, toks).Parse();
                    new Parser(tokenizer.CurrentFile, toks).Parse();
                    new Parser(tokenizer.CurrentFile, toks).Parse();
                    new Parser(tokenizer.CurrentFile, toks).Parse();
                }

                Queue<string> IncludeFiles = new Queue<string>();
                IncludeFiles.Enqueue(args[0]);

                List<ParsedFile> ParsedFiles = new List<ParsedFile>();

                int lineCount = 0;
                int tokenCount = 0;

                Stopwatch watch = new Stopwatch();
                while (IncludeFiles.Count > 0)
                {
                    string file = IncludeFiles.Dequeue();

                    watch.Start();
                    var tokenizer = new Tokenizer(file);
                    var toks = tokenizer.Tokenize();
                    watch.Stop();
                    Console.WriteLine($"Tokenized {tokenizer.GetLines()} lines ({toks.Count} tokens) in {watch.GetMS():#.000}ms");
                    Console.WriteLine($"This is {tokenizer.GetLines() / watch.GetSec():#} lines / sec");
                    totalTime += watch.GetMS();

                    lineCount += tokenizer.GetLines();
                    tokenCount += toks.Count;

                    /*foreach (var tok in toks)
                    {
                        Console.ForegroundColor = GetColor(tok.Type);
                        Console.WriteLine($"{tok,-40} line: {tok.Line:000}, char: {tok.LineCharIndex:000}");
                    }*/

                    watch.Restart();
                    var parser = new Parser(tokenizer.CurrentFile, toks);
                    var res = parser.Parse();
                    ParsedFiles.Add(res);
                    watch.Stop();
                    Console.ResetColor();

                    foreach (var include in res.IncludeFiles)
                    {
                        string includeFile = include.GetContents();
                        if (ParsedFiles.Any(p => p.File.Path == includeFile))
                            continue;

                        IncludeFiles.Enqueue(includeFile);
                    }

                    bool print = false;
                    if (print)
                    {
                        foreach (var proc in res.Procs)
                        {
                            Console.ResetColor();
                            Console.WriteLine(proc.Key.GetContents());

                            foreach (var inst in proc.Value)
                            {
                                if (inst.Flags.HasFlag(InstructionFlags.RawOpcode) && inst.Opcode == Opcode.Nop)
                                {
                                    Console.ForegroundColor = GetColor(TokenType.Identifier);
                                    Console.WriteLine("\tInstruction{Nop}");
                                }
                                else if (inst.Opcode == Opcode.Nop)
                                {
                                    // Here we assume this is not an instruction
                                    if (inst.Flags.HasFlag(InstructionFlags.Label))
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Label);
                                        Console.WriteLine($"\tLabel{{{inst.StrArg.ToString()}}}");
                                    }
                                    else if (inst.Flags.HasFlag(InstructionFlags.RawNumber))
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Number_litteral);
                                        Console.WriteLine($"\tNumeric_litteral{{{inst.Arg}}}");
                                    }
                                    else
                                    {
                                        Console.ResetColor();
                                        Console.WriteLine($"\tFlags: {inst.Flags} Arg: {inst.Arg} StrArg: {inst.StrArg}, {inst.Opcode}");
                                    }
                                }
                                else
                                {
                                    if (inst.Flags.HasFlag(InstructionFlags.RawOpcode))
                                    {
                                        Console.ForegroundColor = ConsoleColor.DarkGray;
                                        Console.WriteLine($"\tInstruction{{{inst.Opcode}}}");
                                    }
                                    else if (inst.Opcode == Opcode.Load_lit || inst.Opcode == Opcode.Load_lit_l)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Identifier);
                                        if (inst.Flags.HasFlag(InstructionFlags.WordArg) || inst.Flags.HasFlag(InstructionFlags.DwordArg))
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.Arg}");
                                        else if (inst.Flags.HasFlag(InstructionFlags.LabelArg) || inst.Flags.HasFlag(InstructionFlags.IdentArg))
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.StrArg.ToString()}");
                                        else
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.Flags}");
                                    }
                                    else if (inst.Opcode == Opcode.Load_local || inst.Opcode == Opcode.Load_local_l)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Identifier);
                                        if (inst.Flags.HasFlag(InstructionFlags.WordArg) || inst.Flags.HasFlag(InstructionFlags.DwordArg))
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.Arg}");
                                        else if (inst.Flags.HasFlag(InstructionFlags.LabelArg) || inst.Flags.HasFlag(InstructionFlags.IdentArg))
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.Arg}");
                                        else
                                            Console.WriteLine($"\tLoad{{{inst.Opcode}}} {inst.Flags}");
                                    }
                                    else if (inst.Opcode == Opcode.Store_local || inst.Opcode == Opcode.Store_local_l)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Identifier);
                                        if (inst.Flags.HasFlag(InstructionFlags.WordArg) || inst.Flags.HasFlag(InstructionFlags.DwordArg))
                                            Console.WriteLine($"\tStore{{{inst.Opcode}}} {inst.Arg}");
                                        else if (inst.Flags.HasFlag(InstructionFlags.LabelArg) || inst.Flags.HasFlag(InstructionFlags.IdentArg))
                                            Console.WriteLine($"\tStore{{{inst.Opcode}}} {inst.Arg}");
                                        else
                                            Console.WriteLine($"\tStore{{{inst.Opcode}}} {inst.Flags}");
                                    }
                                    else if (inst.Opcode == Opcode.Set)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Identifier);
                                        Console.WriteLine($"\tSet{{{(SetMode)inst.Arg}}}");
                                    }
                                    else if (inst.Opcode == Opcode.Jmp)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Identifier);
                                        Console.WriteLine($"\tJmp{{{(JumpMode)inst.Arg}}} {inst.StrArg.ToString()}");
                                    }
                                    else if (inst.Opcode == Opcode.Call)
                                    {
                                        Console.ForegroundColor = GetColor(TokenType.Call);
                                        Console.WriteLine($"\tCall{{{inst.StrArg.ToString()}}}");
                                    }
                                    else
                                    {
                                        Console.ResetColor();
                                        Console.WriteLine($"\tType: {inst.Flags} OP {inst.Opcode}");
                                    }
                                }
                            }

                            Console.WriteLine();
                        }
                    }

                    Console.WriteLine($"Parsed {tokenizer.GetLines()} lines ({toks.Count} tokens) in {watch.GetMS():#.000}ms");
                    Console.WriteLine($"This is {tokenizer.GetLines() / watch.GetSec():#} lines / sec");
                    totalTime += watch.GetMS();
                    Console.ResetColor();
                }

                watch.Restart();
                var emitter = new Emitter(ParsedFiles);
                var bin = emitter.Emit();
                watch.Stop();
                Console.ResetColor();

                Console.WriteLine($"Emitted from {lineCount} lines ({tokenCount} tokens) in {watch.GetMS():#.000}ms");
                Console.WriteLine($"This is {lineCount / watch.GetSec():#} lines / sec");
                totalTime += watch.GetMS();

                Console.WriteLine($"Total {totalTime:#.000}ms");
                Console.WriteLine($"This is {(lineCount / (totalTime / 1000d)):#} lines / sec");

                Console.ReadKey();
            }
            else
            {
                Console.WriteLine($"File '{args[0]}' does not exist!");
                Console.ReadLine();
                return;
            }
        }

        public static ConsoleColor GetColor(TokenType type)
        {
            switch (type)
            {
                case TokenType.StartOfLineTab:
                    return ConsoleColor.DarkGray;
                case TokenType.Call:
                    return ConsoleColor.Yellow;
                case TokenType.Comment:
                    return ConsoleColor.DarkGreen;
                case TokenType.Label:
                    return ConsoleColor.Red;
                case TokenType.Number_litteral:
                    return ConsoleColor.Magenta;
                case TokenType.And:
                    return ConsoleColor.DarkBlue;
                case TokenType.Char_litteral:
                case TokenType.String_litteral:
                    return ConsoleColor.DarkCyan;
                case TokenType.Open_angle:
                case TokenType.Close_angle:
                    return ConsoleColor.Yellow;
                default:
                    Console.ResetColor();
                    return Console.ForegroundColor;
            }
        }
    }
}
