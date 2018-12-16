using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Linq;
using Util;

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

        public enum MessageLevel
        {
            Error,
            Warning,
        }

        public struct MessageData
        {
            public MessageLevel Level;
            public string File;
            public int StartLine;
            public int EndLine;
            public string Message;

            public MessageData(MessageLevel level, string file, int startLine, int endLine, string errorMessage)
            {
                Level = level;
                File = file;
                StartLine = startLine;
                EndLine = endLine;
                Message = errorMessage;
            }

            public static MessageData FromError(Token tok, string message)
            {
                return new MessageData(MessageLevel.Error, tok.File, tok.Line, tok.Line, message);
            }

            public static MessageData FromError(TraceData trace, string message)
            {
                return new MessageData(MessageLevel.Error, trace.File, trace.StartLine, trace.EndLine, message);
            }

            public static MessageData FromWarning(Token tok, string message)
            {
                return new MessageData(MessageLevel.Warning, tok.File, tok.Line, tok.Line, message);
            }

            public static MessageData FromWarning(TraceData trace, string message)
            {
                return new MessageData(MessageLevel.Warning, trace.File, trace.StartLine, trace.EndLine, message);
            }
        }

        public delegate void ErrorHandler(MessageData message);

        public static bool Compiling { get; private set; }
        static StringBuilder FuncDebug = new StringBuilder();
        static DirectoryInfo BaseDirectory;
        static Dictionary<string, FileInfo> DirectoryFiles;
        public static int CompiledFiles = 0;
        public static int CompiledLines = 0;
        public static int ResultLines = 0;
        internal static AST CurrentAST;
        internal static ErrorHandler CurrentErrorHandler;
        
        public static void StartCompiling(DirectoryInfo baseDirectory, ErrorHandler errorHandler)
        {
            if (Compiling)
            {
                Console.WriteLine("Trying to start compiling when we are already compiling!!!");
                return;
            }

            Compiling = true;
            FuncDebug = new StringBuilder();
            BaseDirectory = baseDirectory;
            DirectoryFiles = baseDirectory.GetFilesByExtensions(".t12").ToDictionary(f => f.Name);

            CompiledFiles = 0;
            CompiledLines = 0;
            ResultLines = 0;
            CurrentAST = new AST(new Dictionary<string, (ASTFile File, FileInfo FileInfo)>());

            CurrentErrorHandler = errorHandler;
        }

        public static void StopCompiling()
        {
            Compiling = false;
            File.WriteAllText(Path.Combine(BaseDirectory.FullName, "Data", "debug_t12.df"), FuncDebug.ToString());
            CurrentAST = null;
            BaseDirectory = null;
            DirectoryFiles = null;
            CurrentErrorHandler = null;
        }

        public static void Compile(FileInfo infile)
        {
            if (CurrentAST.Files.ContainsKey(infile.Name))
            {
                // We don't need to worry about this file as it is already compiled
                return;
            }

            string fileData = File.ReadAllText(infile.FullName);

            CompiledFiles += 1;
            CompiledLines += fileData.CountLines();

            var tokens = Tokenizer.Tokenize(fileData, infile.FullName);

            AST ast = AST.Parse(infile, DirectoryFiles);
            
            // TODO: Do validaton on the AST
            
            foreach (var file in ast.Files)
            {
                if (CurrentAST.Files.ContainsKey(file.Key))
                {
                    // This means we have already emitted this file!
                    continue;
                }

                var result = Emitter.EmitAsem(file.Value.File, ast);

                // FIXME: Write to file
                File.WriteAllText(Path.ChangeExtension(file.Value.FileInfo.FullName, ".12asm"), result.assembly);

                ResultLines += result.assembly.CountLines();

                // Add the emitted file to the AST
                CurrentAST.Files.Add(file.Key, file.Value);
                Console.WriteLine($"'{file.Key}' compiled!");

                FuncDebug.Append(result.funcDebug);
            }
            
            return;
        }
    }
}
