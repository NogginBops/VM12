using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;

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
        
        public static bool Compiling { get; private set; }
        static StringBuilder FuncDebug = new StringBuilder();
        static DirectoryInfo BaseDirectory;

        public static void StartCompiling(DirectoryInfo baseDirectory)
        {
            Compiling = true;
            FuncDebug = new StringBuilder();
            BaseDirectory = baseDirectory;
        }

        public static void StopCompiling()
        {
            Compiling = false;
            File.WriteAllText(Path.Combine(BaseDirectory.FullName, "Data", "debug_t12.df"), FuncDebug.ToString());
        }

        public static void Compile(FileInfo infile)
        {
            string fileData = File.ReadAllText(infile.FullName);

            var tokens = Tokenizer.Tokenize(fileData, infile.FullName);

            AST ast = AST.Parse(infile, BaseDirectory);

            // TODO: Do validaton on the AST

            // TODO: We can generate a debug file with the name of all locals!
            
            foreach (var file in ast.Files)
            {
                var result = Emitter.EmitAsem(file.Value.File, ast);

                // FIXME: Write to file
                File.WriteAllText(Path.ChangeExtension(file.Value.FileInfo.FullName, ".12asm"), result.assembly);

                FuncDebug.Append(result.funcDebug);
            }
            
            return;
        }
    }
}
