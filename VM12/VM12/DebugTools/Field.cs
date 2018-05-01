using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Debugging
{
    public partial class Field : UserControl
    {
        [Browsable(true)]
        public string LableText { get => label1.Text; set => label1.Text = value; }
        
        [Browsable(true)]
        public string ValueText { get => textBox1.Text; set => textBox1.Text = value; }

        [Browsable(true)]
        public event EventHandler ValueTextChanged
        {
            add { textBox1.TextChanged += value; }
            remove { textBox1.TextChanged -= value; }
        }

        public Field()
        {
            InitializeComponent();
        }
    }
}
