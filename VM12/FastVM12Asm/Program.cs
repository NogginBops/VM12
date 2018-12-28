using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastVM12Asm
{
    class Program
    {
        static void Main(string[] args)
        {
            if (File.Exists(args[0]))
            {
                Stopwatch watch = new Stopwatch();
                watch.Start();
                var tokenizer = new Tokenizer(args[0]);
                var toks = tokenizer.Tokenize();
                watch.Stop();

                Stopwatch watch2 = new Stopwatch();
                watch2.Start();
                string[] lines = File.ReadAllLines(args[0]);
                VM12Asm.VM12Asm.RawFile rawFile = new VM12Asm.VM12Asm.RawFile
                {
                    path = args[0],
                    processedlines = VM12Asm.VM12Asm.PreProcess(lines, Path.GetFileName(args[0])),
                    rawlines = lines,
                };
                //var res = VM12Asm.VM12Asm.Parse(rawFile);
                watch2.Stop();
                
                Console.WriteLine($"Tokenized in {watch.ElapsedMilliseconds}ms");
                Console.WriteLine($"VM12Asm took {watch2.ElapsedMilliseconds}ms");
                /*
                foreach (var tok in toks)
                {
                    Console.ForegroundColor = GetColor(tok.Type);
                    Console.WriteLine(tok);
                }*/

                Console.ReadKey();
            }
        }

        static ConsoleColor GetColor(Tokenizer.TokenType type)
        {
            switch (type)
            {
                case Tokenizer.TokenType.Call:
                    return ConsoleColor.Yellow;
                case Tokenizer.TokenType.Comment:
                    return ConsoleColor.DarkGreen;
                case Tokenizer.TokenType.Label:
                    return ConsoleColor.Red;
                case Tokenizer.TokenType.Number_litteral:
                    return ConsoleColor.Magenta;
                case Tokenizer.TokenType.And:
                    return ConsoleColor.DarkBlue;
                case Tokenizer.TokenType.Char_litteral:
                case Tokenizer.TokenType.String_litteral:
                    return ConsoleColor.DarkCyan;
                case Tokenizer.TokenType.Open_angle:
                case Tokenizer.TokenType.Close_angle:
                    return ConsoleColor.Yellow;
                default:
                    Console.ResetColor();
                    return Console.ForegroundColor;
            }
        }
    }
}
