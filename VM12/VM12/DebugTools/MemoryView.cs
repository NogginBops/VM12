using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ComponentModel.Design;
using Be.Windows.Forms;

namespace Debugging
{
    public partial class MemoryView : UserControl
    {
        private DynamicByteProvider provider;

        public MemoryView()
        {
            InitializeComponent();
            
            byte[] data = new byte[10000];

            new Random().NextBytes(data);
            
            hexBox.ByteProvider = new DynamicByteProvider(data);
        }

        public void setData(byte[] data)
        {
            hexBox.ByteProvider = new DynamicByteProvider(data);
        }
    }
}
