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
            if (args.Length < 1)
            {
                Console.WriteLine("Must provide a input file!");
                return;
            }

            if (File.Exists(args[0]))
            {
                Stopwatch watch = new Stopwatch();
                watch.Start();
                var tokenizer = new Tokenizer(args[0]);
                var toks = tokenizer.Tokenize();
                watch.Stop();
                
                Console.WriteLine($"Tokenized in {watch.ElapsedMilliseconds}ms");

                Console.WriteLine($"This is {(long)(tokenizer.GetLines() / watch.Elapsed.TotalSeconds)} lines / sec");

                /*
                foreach (var tok in toks)
                {
                    Console.ForegroundColor = GetColor(tok.Type);
                    Console.WriteLine(tok);
                }*/

                Console.ReadKey();
            }
        }

        static ConsoleColor GetColor(TokenType type)
        {
            switch (type)
            {
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
