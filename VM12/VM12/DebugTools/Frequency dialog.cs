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
        public enum DisplayMode
        {
            Numbers,
            Fractions,
            AveragePerSecond
        }

        public delegate int EnumToInt(T e);
        
        Dictionary<T, long> internalFreq;
        long[] freqs;

        long[] currentData;

        DisplayMode mode = DisplayMode.Numbers;
        
        EnumToInt etoi;

        static long[] intArrToLong(int[] ints)
        {
            long[] longs = new long[ints.Length];
            for (int i = 0; i < longs.Length; i++)
            {
                longs[i] = ints[i];
            }
            return longs;
        }

        internal Frequency_dialog(int[] frequencies, string title, string column_name, EnumToInt etoi) : this(intArrToLong(frequencies), title, column_name, etoi)
        {

        }

        internal Frequency_dialog(long[] frequencies, string title, string column_name, EnumToInt etoi)
        {
            if (typeof(T).IsEnum == false)
            {
                throw new ArgumentException("T must be an enumerated type!");
            }
            
            InitializeComponent();

            displayModeComboBox.ComboBox.Items.AddRange(Enum.GetValues(typeof(DisplayMode)).Cast<DisplayMode>().Select(e => e.ToString()).ToArray());
            
            freqs = frequencies;
            currentData = new long[freqs.Length];
            Array.Copy(freqs, currentData, freqs.Length);

            internalFreq = new Dictionary<T, long>(Enum.GetValues(typeof(T)).Length);

            this.etoi = etoi;

            Text = title;

            instructionFrequencyListView.Columns.Add(column_name);
            var header = instructionFrequencyListView.Columns.Add("x Times");
            header.Width *= 2;

            instructionFrequencyListView.Items.Add("Totals", "Totals", 0).SubItems.Add("0");

            displayModeComboBox.SelectedIndex = displayModeComboBox.FindString(mode.ToString());
        }

        private void Instruction_frequency_Load(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            refreshInstructionFreqTimer.Enabled = refreshToolStripMenuItem.Checked;
        }
        
        private void displayModeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateMode();
            UpdateList();
        }

        private void UpdateMode()
        {
            if (Enum.TryParse(displayModeComboBox.SelectedItem.ToString(), out DisplayMode mode))
            {
                this.mode = mode;
            }
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

            long total = internalFreq.Sum(kvp => kvp.Value);

            long delta = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds() - VM12Form.StartTime;

            string getValueString(long value)
            {
                switch (mode)
                {
                    case DisplayMode.Numbers:
                        return value.ToString();
                    case DisplayMode.Fractions:
                        return string.Format("{0:P6}", (float)value / total);
                    case DisplayMode.AveragePerSecond:
                        return $"{(value / delta).ToString()}/s";
                    default:
                        return "Unkown mode!";
                }
            }

            string getTotalString(long value)
            {
                switch (mode)
                {
                    case DisplayMode.Numbers:
                    case DisplayMode.Fractions:
                        return value.ToString();
                    case DisplayMode.AveragePerSecond:
                        return $"{value / delta}/s";
                    default:
                        return "Unkown mode!";
                }
            }

            instructionFrequencyListView.Items["Totals"].SubItems[1].Text = getTotalString(total);

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

                // FIXME!! This is not robust!!!
                if (xs[xs.Length - 1] == 's')
                {
                    xs = xs.Substring(0, xs.Length - 2);
                    xp = true;
                }

                if (ys[ys.Length - 1] == 's')
                {
                    ys = ys.Substring(0, ys.Length - 2);
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

                if (double.TryParse(xs, out double xr) && double.TryParse(ys, out double yr))
                {
                    returnVal = Math.Sign(xr - yr);
                }
                else
                {
                    returnVal = string.Compare(xs, ys);
                }
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
