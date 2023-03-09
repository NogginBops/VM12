using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM12Opcode;
using VM12Util;

namespace FastVM12Asm
{
    static class Fast12AsmUtil
    {
        public static SizedNumber ParseNumber(this string data)
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

                size /= 4;
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

                size /= 12;
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

            return new SizedNumber(result, size);
        }

        public static SizedNumber ParseNumber(this StringRef data)
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

                size /= 4;
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

                size /= 12;
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

            return new SizedNumber(result, size);
        }

        public static short[] ParseLargeNumber(Trace trace, string litteral)
        {
            litteral = litteral.Replace("_", "");
            litteral = litteral.Replace("\\", "");
            litteral = litteral.Replace("\r", "");
            litteral = litteral.Replace("\n", "");
            litteral = litteral.Replace("\t", "");

            short[] ret = null;

            if (litteral.StartsWith("0x"))
            {
                litteral = litteral.Substring(2);
                if (litteral.Length % 3 != 0)
                {
                    litteral = new string('0', 3 - (litteral.Length % 3)) + litteral;
                }
                ret = Enumerable.Range(0, litteral.Length)
                    .GroupBy(x => x / 3)
                    .Select(g => g.Select(i => litteral[i]))
                    .Select(s => String.Concat(s))
                    .Select(s => Convert.ToInt16(s, 16))
                    .Reverse()
                    .ToArray();
            }
            else if (litteral.StartsWith("8x"))
            {
                litteral = litteral.Substring(2);
                if (litteral.Length % 4 != 0)
                {
                    litteral = new string('0', 4 - (litteral.Length % 4)) + litteral;
                }
                ret = Enumerable.Range(0, litteral.Length)
                    .GroupBy(x => x / 4)
                    .Select(g => g.Select(i => litteral[i]))
                    .Select(s => String.Concat(s))
                    .Select(s => Convert.ToInt16(s, 8))
                    .Reverse()
                    .ToArray();
            }
            else if (litteral.StartsWith("0b"))
            {
                litteral = litteral.Substring(2);
                if (litteral.Length % 12 != 0)
                {
                    litteral = new string('0', 12 - (litteral.Length % 12)) + litteral;
                }
                ret = Enumerable.Range(0, litteral.Length)
                    .GroupBy(x => x / 12)
                    .Select(g => g.Select(i => litteral[i]))
                    .Select(s => String.Concat(s))
                    .Select(s => { var a = litteral; return Convert.ToInt16(s, 2); })
                    .Reverse()
                    .ToArray();
            }
            else
            {
                List<short> values = new List<short>();
                int value = Convert.ToInt32(litteral, 10);
                int itt = 0;
                do
                {
                    values.Add((short)((value >> (12 * itt)) & 0x0FFF));
                } while ((value >> (12 * itt++)) >= 4096);

                ret = values.ToArray();
            }

            return ret;
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

    public class Fast12Asm
    {
        const string compiler_generated_warning = "; This is a compiler generated file! Changes to this file will be lost.\n\n";

        public static void Main(params string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Must provide a input file!");
                Console.ReadLine();
                return;
            }

            //Console.WriteLine($"Size of Token: {Marshal.SizeOf(typeof(Token))}");
            //Console.WriteLine($"Size of StringRef: {Marshal.SizeOf(typeof(StringRef))}");

            double totalTime = 0;

            if (File.Exists(args[0]))
            {
                Stopwatch totalWatch = new Stopwatch();
                totalWatch.Start();

                Stopwatch watch = new Stopwatch();

                /**
                watch.Start();
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
                watch.Stop();
                Console.WriteLine($"Prewarm in {watch.GetMS():#.000}ms");
                */

                FileInfo fileInf = new FileInfo(args[0]);
                DirectoryInfo dirInf = fileInf.Directory;

                string outputName = Path.GetFileNameWithoutExtension(args[0]);
                if (args.Length >= 2) outputName = args[1];

                // FIXME: Handle t12 files!
                FileInfo[] dirFiles = dirInf.GetFilesByExtensions(".12asm").ToArray();

                Stack<FileInfo> IncludeFiles = new Stack<FileInfo>();
                IncludeFiles.Push(new FileInfo(args[0]));

                List<ParsedFile> ParsedFiles = new List<ParsedFile>();

                int lineCount = 0;
                int tokenCount = 0;

                
                while (IncludeFiles.Count > 0)
                {
                    FileInfo file = IncludeFiles.Pop();

                    // If this file was already parsed we don't parse it again
                    // We do this here instead of when adding IncludeFiles to retain the same import order as the old assembler
                    if (ParsedFiles.Any(p => Path.GetFileName(p.File.Path) == file.Name))
                        continue;

                    // Skip including this file as it will be created later!
                    if (file.Name == "ProcMapData.12asm") continue;
                    if (file.Name == "TypeMapData.12asm") continue;

                    // Tokenize
                    watch.Start();
                    var tokenizer = new Tokenizer(file.FullName);
                    var toks = tokenizer.Tokenize();
                    watch.Stop();
                    //Console.WriteLine($"Tokenized {tokenizer.GetLines()} lines ({toks.Count} tokens) in {watch.GetMS():#.000}ms");
                    //Console.WriteLine($"This is {tokenizer.GetLines() / watch.GetSec():#} lines / sec");
                    totalTime += watch.GetMS();

                    lineCount += tokenizer.GetLines();
                    tokenCount += toks.Count;

                    // Parse
                    watch.Restart();
                    var parser = new Parser(tokenizer.CurrentFile, toks.FlipToQueue());
                    var res = parser.Parse();
                    ParsedFiles.Add(res);
                    watch.Stop();
                    Console.ResetColor();

                    foreach (var include in res.IncludeFiles.Reverse<Token>())
                    {
                        string includeFile = include.GetContents();
                        //if (ParsedFiles.Any(p => Path.GetFileName(p.File.Path) == includeFile))
                        //    continue;

                        FileInfo fi = null;
                        for (int i = 0; i < dirFiles.Length; i++)
                        {
                            if (dirFiles[i].Name == includeFile) fi = dirFiles[i];
                        }

                        if (fi == null) throw new InvalidOperationException($"Could not find file called: '{includeFile}'");

                        //if (IncludeFiles.Contains(fi) == false)
                        if (ParsedFiles.Any(p => Path.GetFileName(p.File.Path) == includeFile) == false)
                            IncludeFiles.Push(fi);
                    }

                    //Console.WriteLine($"Parsed {tokenizer.GetLines()} lines ({toks.Count} tokens) in {watch.GetMS():#.000}ms");
                    //Console.WriteLine($"This is {tokenizer.GetLines() / watch.GetSec():#} lines / sec");
                    totalTime += watch.GetMS();
                    Console.ResetColor();
                }

                // Generate AutoStrings file!!
                Dictionary<StringRef, string> AutoStrings = new Dictionary<StringRef, string>();
                {
                    StringBuilder AutoStringsFile = new StringBuilder();

                    AutoStringsFile.Append(compiler_generated_warning);
                    AutoStringsFile.AppendLine("; This is a compiler generated file! Changes to this file will be lost.");
                    AutoStringsFile.AppendLine();
                    AutoStringsFile.AppendLine("!noprintouts");
                    AutoStringsFile.AppendLine("!global");
                    AutoStringsFile.AppendLine("!no_map");
                    AutoStringsFile.AppendLine();

                    int index = 0;
                    foreach (var file in ParsedFiles)
                    {
                        foreach (var autoString in file.AutoStrings)
                        {
                            if (AutoStrings.TryGetValue(autoString, out _) == false)
                            {
                                string labelName = $":__str_{index++}__";
                                AutoStringsFile.AppendLine(labelName);
                                AutoStringsFile.Append('\t').AppendLine(autoString.ToString());
                                AutoStrings.Add(autoString, labelName);
                            }
                        }
                    }

                    AutoStringsFile.AppendLine();

                    // NOTE: We might want to not retokenize and parse this file!
                    // If so we would have to generate all of the procs by hand here
                    // but then you would have to make the traces ourselves and that is a pain

                    // Here we output the file
                    string autoStringsPath = Path.Combine(dirInf.FullName, "AutoStrings.12asm");
                    File.WriteAllText(autoStringsPath, AutoStringsFile.ToString());

                    // Tokenize
                    watch.Start();
                    var tokenizer = new Tokenizer(autoStringsPath);
                    var toks = tokenizer.Tokenize();
                    watch.Stop();
                    //Console.WriteLine($"Tokenized {tokenizer.GetLines()} lines ({toks.Count} tokens) in {watch.GetMS():#.000}ms");
                    //Console.WriteLine($"This is {tokenizer.GetLines() / watch.GetSec():#} lines / sec");
                    totalTime += watch.GetMS();

                    lineCount += tokenizer.GetLines();
                    tokenCount += toks.Count;

                    // Parse
                    watch.Restart();
                    var parser = new Parser(tokenizer.CurrentFile, toks.FlipToQueue());
                    var res = parser.Parse();
                    ParsedFiles.Add(res);
                    watch.Stop();
                    Console.ResetColor();
                }

                // Generate ProcMapData file!
                {
                    StringBuilder ProcMapDataFile = new StringBuilder();
                    StringBuilder ProcNameBuilder = new StringBuilder();

                    ProcMapDataFile.Append(compiler_generated_warning);
                    ProcMapDataFile.AppendLine("!noprintouts");
                    ProcMapDataFile.AppendLine("!no_map");
                    ProcMapDataFile.AppendLine("!global");
                    ProcMapDataFile.AppendLine("");

                    int insertIndex = ProcMapDataFile.Length;

                    ProcMapDataFile.AppendLine(":__proc_map__");

                    int procsMapped = 0;
                    foreach (var file in ParsedFiles)
                    {
                        if (file.Flags.Contains((StringRef)"!no_map") == false)
                        {
                            foreach (var procName in file.Procs.Keys)
                            {
                                if (procName.StartsWith(":__")) continue;

                                ProcMapDataFile.AppendLine($"\t{procName.Data}* #(sizeof({procName.Data})) 0x{ProcNameBuilder.Length:X6} 0x{procName.Data.Length:X6}");
                                ProcNameBuilder.Append(procName.Data);
                                procsMapped++;
                            }
                        }
                    }

                    ProcMapDataFile.AppendLine();

                    ProcMapDataFile.AppendLine($":__proc_map_strings__");
                    ProcMapDataFile.AppendLine($"\t@\"{ProcNameBuilder}\"");

                    ProcMapDataFile.Insert(insertIndex, $"<proc_map_entries = {procsMapped}>\n" +
                                                    $"<proc_map_strings_length = {ProcNameBuilder.Length}>\n" +
                                                    $"<proc_map_length = #({procsMapped} 8 *)>\n\n");

                    // NOTE: We might want to not retokenize and parse this file!
                    // If so we would have to generate all of the procs by hand here
                    // but then you would have to make the traces ourselves and that is a pain

                    // Here we output the file
                    string procMapDataPath = Path.Combine(dirInf.FullName, "ProcMapData.12asm");
                    File.WriteAllText(procMapDataPath, ProcMapDataFile.ToString());

                    // Tokenize
                    watch.Start();
                    var tokenizer = new Tokenizer(procMapDataPath);
                    var toks = tokenizer.Tokenize();
                    watch.Stop();
                    totalTime += watch.GetMS();

                    lineCount += tokenizer.GetLines();
                    tokenCount += toks.Count;

                    // Parse
                    watch.Restart();
                    var parser = new Parser(tokenizer.CurrentFile, toks.FlipToQueue());
                    var res = parser.Parse();
                    ParsedFiles.Add(res);
                    watch.Stop();
                    Console.ResetColor();
                }

                // Emit TypeMapData file!
                {
                    // FIXME: Remove dependency on T12!!!
                    // Let someone else emit this file!!
                    string typeData = T12.Compiler.GetTypeMapData();

                    // Here we output the file
                    string typeMapDataPath = Path.Combine(dirInf.FullName, "TypeMapData.12asm");
                    File.WriteAllText(typeMapDataPath, typeData);

                    // Tokenize
                    watch.Start();
                    var tokenizer = new Tokenizer(typeMapDataPath);
                    var toks = tokenizer.Tokenize();
                    watch.Stop();
                    totalTime += watch.GetMS();

                    lineCount += tokenizer.GetLines();
                    tokenCount += toks.Count;

                    // Parse
                    watch.Restart();
                    var parser = new Parser(tokenizer.CurrentFile, toks.FlipToQueue());
                    var res = parser.Parse();
                    ParsedFiles.Add(res);
                    watch.Stop();
                    Console.ResetColor();
                }

                watch.Restart();
                var emitter = new Emitter(ParsedFiles, AutoStrings);
                var bin = emitter.Emit();
                watch.Stop();
                Console.ResetColor();

                //Console.WriteLine($"Emitted from {lineCount} lines ({tokenCount} tokens) in {watch.GetMS():#.000}ms");
                //Console.WriteLine($"This is {lineCount / watch.GetSec():#} lines / sec");
                totalTime += watch.GetMS();

                Console.WriteLine($"Fast12Asm Total {totalTime:#.000}ms");
                Console.WriteLine($"This is {(lineCount / (totalTime / 1000d)):#} lines/sec for {lineCount} lines");

                FileInfo resFile = new FileInfo(Path.Combine(dirInf.FullName, $"{outputName}.12exe"));
                FileInfo metaFile = new FileInfo(Path.Combine(dirInf.FullName, $"{outputName}.12meta"));

                Console.WriteLine($"Result ({bin.UsedInstructions} used words ({((double)bin.UsedInstructions / Constants.ROM_SIZE):P5}))");

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
                    foreach (var constant in emitter.AutoConstants)
                    {
                        writer.WriteLine($"[constant:{{{constant.Name},{constant.Size},0x{Convert.ToString(constant.Location >> 12, 16)}_{Convert.ToString(constant.Location & 0xFFF, 16)}}}]");
                    }

                    writer.WriteLine();

                    foreach (var proc in bin.Metadata)
                    {
                        writer.WriteLine(proc.ProcName);
                        // FIXME
                        writer.WriteLine($"[file:{new FileInfo(proc.Trace.FilePath).Name}]");
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

                totalWatch.Stop();
                Console.WriteLine($"Grand total {totalWatch.GetMS():#.000}ms");
                Console.WriteLine($"This is {(lineCount / (totalWatch.GetMS() / 1000d)):#} lines / sec");

                //Console.ReadKey();
            }
            else
            {
                Console.WriteLine($"File '{args[0]}' does not exist!");
                Console.ReadLine();
                return;
            }
        }
    }
}
