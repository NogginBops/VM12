using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Linq;
using Util;

namespace T12
{
    using SpecializationList = List<(ASTFunction Specialization, List<ASTType> GenericTypes)>;
    using SpecializationMap = Dictionary<ASTGenericFunction, List<(ASTFunction Specialization, List<ASTType> GenericTypes)>>;
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
        public static int AppendageLines = 0;
        public static int ResultLines = 0;
        internal static AST CurrentAST;
        internal static AST WorkingAST;
        internal static ErrorHandler CurrentErrorHandler;

        // FIXME: For now we will use the filename as a key here but we should really use ASTFile
        internal static Dictionary<string, (StringBuilder File, StringBuilder Debug)> Appendages;

        internal static SpecializationMap GenericSpecializations;

        private static List<ASTType> ReferencedTypes;

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
            AppendageLines = 0;
            ResultLines = 0;
            CurrentAST = new AST(new Dictionary<string, (ASTFile File, FileInfo FileInfo)>());

            CurrentErrorHandler = errorHandler;

            Appendages = new Dictionary<string, (StringBuilder File, StringBuilder Debug)>();

            GenericSpecializations = new SpecializationMap();

            ReferencedTypes = new List<ASTType>();
            foreach (var btype in Emitter.GenerateDefaultTypeMap())
            {
                ReferencedTypes.Add(btype.Value);
            }
        }

        public static int AddReferencedType(ASTType type)
        {
            int index = ReferencedTypes.IndexOf(type);

            if (index == -1)
            {
                index = ReferencedTypes.Count;
                ReferencedTypes.Add(type);
            }

            return index;
        }

        public static string GetTypeMapData()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("!noprintout");
            sb.AppendLine("!global");
            sb.AppendLine();
            int lengthDataIndex = sb.Length;
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($":__types__");

            /*
            struct Type
            {
	            dword ID;
	            dword Size;
	            dword Name_offset;
	            dword Name_length;
	            []Member Members;
            }
            */

            int typeID = 1;

            StringBuilder nameString = new StringBuilder(100);

            List<StringBuilder> MemberProcs = new List<StringBuilder>();

            /*
            struct Member
            {
	            *Type Type;
	            dword Name_offset;
                dword Name_length;
            }
            */

            // FIXME!! We need to be able to get the index of a specific type!
            
            var indexList = new List<ASTType>(ReferencedTypes);

            Dictionary<string, ASTType> TypeDict = ReferencedTypes.ToDictionary(type => type.TypeName);

            List<(string Name, ASTType Type)> AdditionalTypes = new List<(string, ASTType)>();

            foreach (var type in ReferencedTypes)
            {
                // FIXME: We could format some of these in decimal and pad with zeroes
                sb.AppendLine($"\t0x{typeID:X6} 0x{Emitter.SizeOfType(type, TypeDict):X6} 0x{nameString.Length:X6} 0x{type.TypeName.Length:X6} {(type is ASTStructType sType ? $":__{type.TypeName}_members* 0x{sType.Members.Count:X6}" : "0x000000 0x000000")}");

                nameString.Append(type.TypeName);

                if (type is ASTStructType structType)
                {
                    StringBuilder members = new StringBuilder();
                    members.AppendLine($":__{structType.TypeName}_members");
                    foreach (var member in structType.Members)
                    {
                        var membType = member.Type;

                        int index = indexList.IndexOf(membType);
                        if (index == -1)
                        {
                            AdditionalTypes.Add((membType.TypeName, membType));
                            index = indexList.Count;
                            indexList.Add(membType);
                        }

                        const int SizeOfTypeStruct = 12;
                        members.AppendLine($"\t#(:__types__ 0x{index:X6} {SizeOfTypeStruct} * +) 0x{nameString.Length:X6} 0x{member.Name.Length:X6}");
                        
                        nameString.Append(member.Name);
                    }

                    MemberProcs.Add(members);
                }

                typeID++;
            }

            foreach (var additionalType in AdditionalTypes)
            {
                sb.AppendLine($"\t0x{typeID:X6} 0x{Emitter.SizeOfType(additionalType.Type, TypeDict):X6} 0x{nameString.Length:X6} 0x{additionalType.Type.TypeName.Length:X6} 0x000000 0x000000");

                nameString.Append(additionalType.Type.TypeName);

                typeID++;
            }

            sb.AppendLine();

            foreach (var memberProc in MemberProcs)
            {
                sb.Append(memberProc).AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine($":__type_names__");

            sb.Append("\t@\"").Append(nameString).Append('"').AppendLine();

            sb.Insert(lengthDataIndex, $"<type_map_entries = {typeID - 1}>\n<type_map_strings_length = {nameString.Length}>\n<type_map_length = {typeID * 12}>");


            return sb.ToString();
        }

        public static void StopCompiling()
        {
            Compiling = false;
            File.WriteAllText(Path.Combine(BaseDirectory.FullName, "Data", "debug_t12.df"), FuncDebug.ToString());
            CurrentAST = null;
            BaseDirectory = null;
            DirectoryFiles = null;
            CurrentErrorHandler = null;

            Appendages = null;
            GenericSpecializations = null;
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

            WorkingAST = ast;

            // TODO: Do validaton on the AST

            foreach (var file in ast.Files)
            {
                if (CurrentAST.Files.ContainsKey(file.Key))
                {
                    // This means we have already emitted this file!
                    continue;
                }
                
                var result = Emitter.EmitAsem(file.Value.File, ast);

                string fileAssembly = result.assembly.ToString();
                // FIXME: Write to file
                File.WriteAllText(Path.ChangeExtension(file.Value.FileInfo.FullName, ".12asm"), fileAssembly);

                ResultLines += fileAssembly.CountLines();

                // Add the emitted file to the AST
                CurrentAST.Files.Add(file.Key, file.Value);
                Console.WriteLine($"'{file.Key}' compiled!");

                FuncDebug.Append(result.funcDebug);
            }

            foreach (var appendage in Appendages)
            {
                var str = appendage.Value.File.ToString();
                int lines = str.CountLines();
                ResultLines += lines;
                AppendageLines += lines;
                File.AppendAllText(Path.ChangeExtension(appendage.Key, ".12asm"), str);

                FuncDebug.Append(appendage.Value.Debug);
            }

            return;
        }

        internal static (StringBuilder File, StringBuilder Debug) AppendToFile(TraceData trace, string file)
        {
            if (WorkingAST.Files.ContainsKey(Path.GetFileName(file)) == false)
                Emitter.Fail(trace, $"Tried to add appendage to unknown file '{file}'");

            if (Appendages.TryGetValue(file, out var sbs) == false)
            {
                sbs = (new StringBuilder(), new StringBuilder());
                Appendages[file] = sbs;
            }

            // FIXME: We want to document all files that caused specializations to happen so we need some other way to do this!
            sbs.File.AppendLine($"; Appendage generated from file '{Path.GetFileName(trace.File)}'");
            sbs.File.AppendLine();

            return sbs;
        }
    }
}
