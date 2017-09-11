using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM12_Opcode;

namespace VM12
{
    public partial class Frequency_dialog<T> : Form where T : struct, IComparable
    {
        public delegate int EnumToInt(T e);
        
        Dictionary<T, int> internalFreq;
        int[] freqs;

        int[] currentData;

        bool showFractions = false;

        EnumToInt etoi;

        internal Frequency_dialog(int[] frequencies, string title, string column_name, EnumToInt etoi)
        {
            if (typeof(T).IsEnum == false)
            {
                throw new ArgumentException("T must be an enumerated type!");
            }
            
            InitializeComponent();
            freqs = frequencies;
            currentData = new int[freqs.Length];
            Array.Copy(freqs, currentData, freqs.Length);

            internalFreq = new Dictionary<T, int>(Enum.GetValues(typeof(T)).Length);

            this.etoi = etoi;

            Text = title;

            instructionFrequencyListView.Columns.Add(column_name);
            instructionFrequencyListView.Columns.Add("x Times");

            instructionFrequencyListView.Items.Add("Totals", "Totals", 0).SubItems.Add("0");
        }

        private void Instruction_frequency_Load(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            refreshInstructionFreqTimer.Enabled = refreshToolStripMenuItem.Checked;
        }

        private void fractionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showFractions = fractionsToolStripMenuItem.Checked;
            UpdateList();
        }

        private void refreshInstructionFreqTimer_Tick(object sender, EventArgs e)
        {
            UpdateData();
            UpdateList();
        }

        private void UpdateData()
        {
            Array.Copy(freqs, currentData, freqs.Length);
        }

        private void UpdateList()
        {
            foreach (T eval in Enum.GetValues(typeof(T)))
            {
                internalFreq[eval] = currentData[etoi(eval)];
            }

            int total = internalFreq.Sum(kvp => kvp.Value);

            string getValueString(int value)
            {
                return showFractions ? string.Format("{0:P6}", (float)value / total) : value.ToString();
            }
            
            instructionFrequencyListView.Items["Totals"].SubItems[1].Text = total.ToString();

            foreach (var kvp in internalFreq.Where(kvp => kvp.Value > 0).OrderByDescending(kvp => kvp.Value))
            {
                string key = kvp.Key.ToString();
                ListViewItem item = instructionFrequencyListView.Items[key];
                if (item == null)
                {
                    instructionFrequencyListView.Items.Add(key, key, 0).SubItems.Add(getValueString(kvp.Value));
                }
                else
                {
                    item.SubItems[1].Text = getValueString(kvp.Value);
                }
            }

            instructionFrequencyListView.ListViewItemSorter = new ListViewItemComparer(1, SortOrder.Descending, true);

            instructionFrequencyListView.Sort();
        }
    }

    internal class ListViewItemComparer : IComparer
    {
        ListViewItemComparerImpl comp;

        public ListViewItemComparer(int col)
        {
            comp = new ListViewItemComparerImpl(col, SortOrder.Ascending, false);
        }

        public ListViewItemComparer(int col, SortOrder sort, bool number)
        {
            comp = new ListViewItemComparerImpl(col, sort, number);
        }

        public int Compare(object x, object y)
        {
            return comp.Compare((ListViewItem) x, (ListViewItem) y);
        }
    }

    internal class ListViewItemComparerImpl : IComparer<ListViewItem>
    {
        private int col;
        private SortOrder order;
        private bool numbers;

        public ListViewItemComparerImpl()
        {
            col = 0;
            order = SortOrder.Ascending;
        }

        public ListViewItemComparerImpl(int column, SortOrder order, bool numbers)
        {
            col = column;
            this.order = order;
            this.numbers = numbers;
        }

        public int Compare(ListViewItem x, ListViewItem y)
        {
            int returnVal = -1;
            if (col >= x.SubItems.Count || col >= y.SubItems.Count)
            {
                return 0;
            }

            if (numbers)
            {
                string xs = x.SubItems[col].Text;
                string ys = y.SubItems[col].Text;

                bool xp = false;
                bool yp = false;

                if (xs[xs.Length - 1] == '%')
                {
                    xs = xs.Substring(0, xs.Length - 1);
                    xp = true;
                }

                if (ys[ys.Length - 1] == '%')
                {
                    ys = ys.Substring(0, ys.Length - 1);
                    yp = true;
                }

                if (xp == true && yp == false)
                {
                    return 1;
                }
                else if (xp == false && yp == true)
                {
                    return -1;
                }

                returnVal = Math.Sign(double.Parse(xs) - double.Parse(ys));
            }
            else
            {
                returnVal = String.Compare(x.SubItems[col].Text, y.SubItems[col].Text);
            }
            // Determine whether the sort order is descending.
            if (order == SortOrder.Descending)
            { 
                // Invert the value returned by String.Compare.
                returnVal *= -1;
            }
            return returnVal;
        }
    }
}
