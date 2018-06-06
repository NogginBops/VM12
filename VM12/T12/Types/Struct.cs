using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace T12.Types
{
    internal abstract class Struct
    {
        public string Name { get; protected set; }
        
        public virtual int Size { get; }

        public Struct(string name)
        {
            Name = name;
        }
    }

    internal class Complex : Struct
    {
        private List<Struct> Members;

        public override int Size => Members.Select(member => member.Size).Sum();

        public Complex(string name, List<Struct> members) : base(name)
        {
            Members = members;
        }
    }

    internal class Void : Struct
    {
        public override int Size => 0;

        public Void() : base("void") { }
    }

    internal class Word : Struct
    {
        public override int Size => 1;

        public Word() : base("word") { }
    }

    internal class DoubleWord : Struct
    {
        public override int Size => 2;

        public DoubleWord() : base("double_word") { }
    }
}
