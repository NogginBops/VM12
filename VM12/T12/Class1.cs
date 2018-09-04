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
    // Now we can have comments?
    word a = 0;
    a = 4 + 4;
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
