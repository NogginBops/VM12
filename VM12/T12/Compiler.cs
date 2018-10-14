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
            
            //string result = Compile(inFile);

            Console.WriteLine();
            //Console.WriteLine(result);
            
            Console.ReadKey();
        }

        public static void Compile(FileInfo inFile, FileInfo outFile = null)
        {
            if (outFile == null)
            {
                outFile = new FileInfo(Path.ChangeExtension(inFile.FullName, "12asm"));
            }
            
            /*string result = */Compile(inFile);
            
            //File.WriteAllText(outFile.FullName, result);
        }

        public static void Compile(FileInfo infile)
        {
            string fileData = File.ReadAllText(infile.FullName);

            var tokens = Tokenizer.Tokenize(fileData, infile.FullName);

            AST ast = AST.Parse(infile);

            // TODO: Do validaton on the AST

            // TODO: We can generate a debug file with the name of all locals!

            foreach (var file in ast.Files)
            {
                string result = Emitter.EmitAsem(file.Value.File, ast);

                // FIXME: Write to file
                File.WriteAllText(Path.ChangeExtension(file.Value.FileInfo.FullName, ".12asm"), result);
            }
            
            return;
        }
    }
}
