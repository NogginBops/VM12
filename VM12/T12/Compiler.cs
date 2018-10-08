using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace T12
{
    public class Compiler
    {
        public static void Main(params string[] args)
        {
            string inFile;
            string outFile;

            if (args.Length <= 0)
            {
                Console.Write("InputFile: ");
                inFile = Console.ReadLine();
                outFile = Path.ChangeExtension(inFile, "12asm");
            }
            else
            {
                // The fist argument is the input file
                inFile = args[0];
                outFile = Path.ChangeExtension(inFile, "12asm");

                if (args.Length > 1)
                {
                    // Then there could be more arguments to parse
                    // But at the moment we wont do that
                }
            }
            
            string result = Compile(inFile);

            Console.WriteLine();
            Console.WriteLine(result);
            
            Console.ReadKey();
        }

        public static void Compile(FileInfo inFile, FileInfo outFile = null)
        {
            if (outFile == null)
            {
                outFile = new FileInfo(Path.ChangeExtension(inFile.FullName, "12asm"));
            }
            
            string result = Compile(inFile.FullName);
            
            File.WriteAllText(outFile.FullName, result);
        }

        public static string Compile(string infile)
        {
            string fileData = File.ReadAllText(infile);

            var tokens = Tokenizer.Tokenize(fileData);

            AST ast = AST.Parse(tokens);
            
            // TODO: Do validaton on the AST

            // TODO: We can generate a debug file with the name of all locals!

            string result = Emitter.EmitAsem(ast);
            
            return result;
        }

        string TestCode = @"
use Test.12asm;
extern word* alloc_w(word words);

use Testing.12asm;

extern void run_tests();

void start() {
	run_tests();
    return;
}

word main()
{
    bool bo = true;
    // Now we can have comments?
    alloc_w(10);
    word a = 0;
    a = 4 + 4;
    word b = clamp(a, 40, pow(40, 2));
    return 4 % 2; // Test
}

word clamp(word val, word min, word max)
{
    return val < min ? min : val > max ? max : val;
}

word clamp2(word val, word min, word max)
{
    if (val < min) return min;
    if (val > max) return max;
    return val;
}

word pow(word base, word exp) {
    word result = 1;
    for (word i = 0; i < exp; i += 1)
    {
        result *= base;
    }
    return result;
}

word breakcontinuetest(word asdf) {
    for (word a = 0; a < 1000; a += 1)
    {
        if (a % 2 == 0) continue;
        if (a == 59) break;
        asdf += 1;
    }
    
    return 2;
}

word whiletest(word a) {
    word b = a;
    do
    {
        a += a;
        
        b += 1;
    } while (b > 0);
    
    return a;
}
            ";
    }
}
