using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Debugging;
using VM12;

namespace Debugger
{
    public partial class SourceView : UserControl
    {
        [Browsable(true)]
        public event EventHandler TextSelectionChanged
        {
            add { rtbSource.SelectionChanged += value; }
            remove { rtbSource.SelectionChanged -= value; }
        }

        public RichTextBox TextBox { get => rtbSource; }

        string openFile;

        int selectedLine = 0;

        public SourceView()
        {
            InitializeComponent();
        }

        public void Open(string file, int line)
        {
            if (File.Exists(file))
            {
                if (openFile != null)
                {
                    DeSelectLine(selectedLine);
                }

                string path = Path.GetFullPath(file);

                if (openFile != path)
                {
                    rtbSource.Lines = File.ReadAllLines(path);

                    openFile = path;
                }

                SelectLine(line - 1);
            }
        }

        public void DeSelectLine(int line)
        {
            int charIndex = rtbSource.GetFirstCharIndexFromLine(line);
            string str = rtbSource.Lines[line];
            rtbSource.Select(charIndex, str.Length);
            rtbSource.SelectionBackColor = rtbSource.BackColor;
            rtbSource.SelectionColor = rtbSource.ForeColor;
        }

        public void SelectLine(int line)
        {
            int charIndex = rtbSource.GetFirstCharIndexFromLine(line);

            string str = rtbSource.Lines[line];

            int offset = str.IndexOf(c => !char.IsWhiteSpace(c));

            rtbSource.Select(charIndex + offset, str.Length - offset);
            rtbSource.SelectionBackColor = Color.Beige;
            rtbSource.SelectionColor = Color.DarkOliveGreen;
            
            rtbSource.ScrollToCaret();
            
            selectedLine = line;
        }
    }
}
