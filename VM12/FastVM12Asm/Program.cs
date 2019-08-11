using System;
using System.Collections;
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

        public static (int Value, int Size) ParseNumber(this string data)
        {
            int result = 0;
            int size = 0;
            if (data.StartsWith("0x"))
            {
                // Parse hex
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;

                    size++;

                    result *= 16;
                    result += Util.HexToInt(c);
                }

                size /= 3;
                // Count digits!
            }
            else if (data.StartsWith("8x"))
            {
                // Parse octal
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    size++;
                    result *= 8;
                    result += Util.OctalToInt(c);
                }

                size %= 4;
            }
            else if (data.StartsWith("0b"))
            {
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    size++;
                    result *= 2;
                    result += Util.BinaryToInt(c);
                }

                size %= 12;
            }
            else
            {
                for (int i = 0; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    result *= 10;
                    result += (int)char.GetNumericValue(c);
                }

                size = Util.Log2(result);
                size /= 12;
                //size = result / (1 << 12);
                size++;
            }

            return (result, size);
        }

        public static (int Value, int Size) ParseNumber(this StringRef data)
        {
            int result = 0;
            int size = 0;
            if (data.StartsWith("0x"))
            {
                // Parse hex
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;

                    size++;

                    result *= 16;
                    result += Util.HexToInt(c);
                }

                size /= 3;
                // Count digits!
            }
            else if (data.StartsWith("8x"))
            {
                // Parse octal
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    size++;
                    result *= 8;
                    result += Util.OctalToInt(c);
                }

                size %= 4;
            }
            else if (data.StartsWith("0b"))
            {
                for (int i = 2; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    size++;
                    result *= 2;
                    result += Util.BinaryToInt(c);
                }

                size %= 12;
            }
            else
            {
                for (int i = 0; i < data.Length; i++)
                {
                    char c = data[i];
                    if (c == '_') continue;
                    result *= 10;
                    result += (int)char.GetNumericValue(c);
                }

                size = Util.Log2(result);
                size /= 12;
                //size = result / (1 << 12);
                size++;
            }

            return (result, size);
        }

        public static IEnumerable<FileInfo> GetFilesByExtensions(this DirectoryInfo dir, params string[] extensions)
        {
            if (extensions == null)
                throw new ArgumentNullException("extensions");
            IEnumerable<FileInfo> files = dir.EnumerateFiles("*", SearchOption.AllDirectories);
            return files.Where(f => extensions.Contains(f.Extension));
        }
    }

    public class DynArray<T> : ICollection<T>
    {
        public T[] Elements;
        public int Count = 0;

        public int Capacity => Elements.Length;

        int ICollection<T>.Count => Count;

        public bool IsReadOnly => false;

        public DynArray(int initSize) => Elements = new T[initSize];

        private void Resize(int minCapacity)
        {
            int newSize = Elements.Length + (Elements.Length / 2);
            if (newSize < minCapacity) newSize = minCapacity;
            T[] newArray = new T[newSize];
            Elements.CopyTo(newArray, 0);
            Elements = newArray;
        }

        public void Add(T element)
        {
            if (Count + 1 >= Elements.Length) Resize(Count + 1);
            Elements[Count++] = element;
        }

        public void Clear() => Count = 0;

        public ref T IndexByRef(int i) => ref Elements[i];

        public bool Contains(T item) => Elements.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => Array.Copy(Elements, 0, array, arrayIndex, Elements.Length);

        public bool Remove(T item) => throw new InvalidOperationException();

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return Elements[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public T this[int i]
        {
            get => Elements[i];
            set => Elements[i] = value;
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

                FileInfo fileInf = new FileInfo(args[0]);
                DirectoryInfo dirInf = fileInf.Directory;

                // FIXME: Handle t12 files!
                FileInfo[] dirFiles = dirInf.GetFilesByExtensions(".12asm").ToArray();

                Queue<FileInfo> IncludeFiles = new Queue<FileInfo>();
                IncludeFiles.Enqueue(new FileInfo(args[0]));

                List<ParsedFile> ParsedFiles = new List<ParsedFile>();

                int lineCount = 0;
                int tokenCount = 0;

                Stopwatch watch = new Stopwatch();
                while (IncludeFiles.Count > 0)
                {
                    FileInfo file = IncludeFiles.Dequeue();

                    watch.Start();
                    var tokenizer = new Tokenizer(file.FullName);
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

                        FileInfo fi = null;
                        for (int i = 0; i < dirFiles.Length; i++)
                        {
                            if (dirFiles[i].Name == includeFile) fi = dirFiles[i];
                        }

                        if (fi == null) throw new InvalidOperationException($"Could not find file called: '{file}'");

                        IncludeFiles.Enqueue(fi);
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

                FileInfo resFile = new FileInfo(Path.Combine(dirInf.FullName, Path.ChangeExtension(fileInf.Name, "12exe")));
                FileInfo metaFile = new FileInfo(Path.Combine(dirInf.FullName, Path.ChangeExtension(fileInf.Name, "12meta")));

                bool dump_mem = true;
                if (dump_mem)
                {
                    bool toFile = false;
                    TextWriter output = Console.Out;
                    if (toFile)
                    {
                        output = new StreamWriter(File.Create(Path.Combine(dirInf.FullName, "output.txt")), Encoding.ASCII, 2048);
                    }

                    output.WriteLine($"Result ({bin.UsedInstructions} used words ({((double)bin.UsedInstructions / Constants.ROM_SIZE):P5})): ");
                    output.WriteLine();

                    Console.ForegroundColor = ConsoleColor.White;

                    const int instPerLine = 3;
                    for (int i = 0; i < bin.Instructions.Length; i++)
                    {
                        if ((bin.Instructions[i] & 0xF000) != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            output.Write("¤");
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        output.Write("{0:X6}: ", i + Constants.ROM_START);
                        Console.ForegroundColor = ConsoleColor.White;
                        output.Write("{0:X3}{1,-14}", bin.Instructions[i] & 0xFFF, "(" + (Opcode)(bin.Instructions[i] & 0xFFF) + ")");

                        if ((i + 1) % instPerLine == 0)
                        {
                            output.WriteLine();

                            int sum = 0;
                            for (int j = 0; j < instPerLine; j++)
                            {
                                if (i + j < bin.Instructions.Length)
                                {
                                    sum += bin.Instructions[i + j];
                                }
                            }

                            if (sum == 0)
                            {
                                int instructions = 0;
                                while (i + instructions < bin.Instructions.Length && bin.Instructions[i + instructions] == 0)
                                {
                                    instructions++;
                                }

                                i += instructions;
                                i -= i % instPerLine;
                                i--;

                                output.WriteLine($"... ({instructions} instructions)");
                                continue;
                            }
                        }
                    }

                    Console.WriteLine();
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"Result ({bin.UsedInstructions} used words ({((double)bin.UsedInstructions / Constants.ROM_SIZE):P5}))");
                }

                resFile.Delete();

                using (FileStream stream = resFile.Create())
                {
                    using (BinaryWriter bw = new BinaryWriter(stream))
                    {
                        for (int pos = 0; pos < bin.Instructions.Length;)
                        {
                            int skipped = 0;
                            while (pos < bin.Instructions.Length && bin.Instructions[pos] == 0)
                            {
                                pos++;
                                skipped++;
                            }

#if DEBUG_BINFORMAT
                        if (skipped > 0)
                        {
                            Console.WriteLine($"Skipped {skipped} instructions");
                        }
#endif

                            int length = 0;
                            int zeroes = 0;

                            while (pos + length < bin.Instructions.Length)
                            {
                                length++;
                                if (bin.Instructions[pos + length] == 0)
                                {
                                    zeroes++;
                                    if (zeroes >= 3)
                                    {
                                        length -= 2;
                                        break;
                                    }
                                }
                                else
                                {
                                    zeroes = 0;
                                }
                            }

                            if (length > 0)
                            {
                                // Write block

#if DEBUG_BINFORMAT
                            Console.WriteLine($"Writing block at pos {pos} and length {length} with fist value {libFile.Instructions[pos]} and last value {libFile.Instructions[pos + length - 1]}");
#endif

                                bw.Write(pos);
                                bw.Write(length);

                                for (int i = 0; i < length; i++)
                                {
                                    bw.Write(bin.Instructions[pos + i]);
                                }

                                pos += length;
                            }
                        }
                    }
                }

                using (FileStream stream = metaFile.Create())
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    // FIXME: Get constants data!
                    /*foreach (var constant in autoConstants)
                    {
                        writer.WriteLine($"[constant:{{{constant.Key},{constant.Value.Length},{constant.Value.Value}}}]");
                    }*/

                    writer.WriteLine();

                    foreach (var proc in bin.Metadata)
                    {
                        writer.WriteLine(proc.ProcName);
                        // FIXME
                        writer.WriteLine($"[file:{new FileInfo(proc.Trace.File.Path).Name}]");
                        writer.WriteLine($"[location:{proc.Address - Constants.ROM_START}]");
                        //writer.WriteLine($"[link?]");
                        writer.WriteLine($"[proc-line:{proc.Line}]");
                        // We are no longer doing breakpoints this way!
                        // if (proc.breaks?.Count > 0) writer.WriteLine($"[break:{{{string.Join(",", proc?.breaks)}}}]");
                        writer.WriteLine($"[link-lines:{{{string.Join(",", proc.LinkedLines.Select(kvp => $"{kvp.Instruction}:{kvp.Line}"))}}}]");
                        writer.WriteLine($"[size:{proc.Size}]");
                        writer.WriteLine();
                    }
                }

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
