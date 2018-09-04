using System;
using System.Collections.Generic;

namespace T12
{
    public class Class1
    {

        public static void Main(params string[] args)
        {
            string TestCode = @"
word main()
{
    word a = 0;
    a = 4 + 4;
    return 4 % 2;
}

word test()
{
    word a = 0;
    word b = 10 + a;
    b = b * 2 + a;
    return ~!(1 ^ ~b + ~-a);
}

word add(word a, word b)
{
    word c = a * 2;
    return a + b + c;
}

word set_bit(word data, word bit)
{
    return ~data & 1 ^ bit;
}
            ";

            var tokens = Tokenizer.Tokenize(TestCode);
            
            AST ast = AST.Parse(tokens);

            // TODO: Do validaton on the AST

            string result = Emitter.EmitAsem(ast);

            Console.WriteLine();
            Console.WriteLine(result);
            
            Console.ReadKey();
        }
    }
}
