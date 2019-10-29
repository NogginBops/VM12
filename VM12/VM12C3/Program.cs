using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VM12C3
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new VM12.VM12Form());
        }

        // NOTE: This code has nothing to do with this codebase...
        public static float EvalPolynomial(float[] coefficients, float x)
        {
            if (coefficients.Length == 1) return coefficients[0];

            float result = coefficients[0];
            int itterations = coefficients.Length;
            for (int i = 1; i < itterations; i++)
            {
                result = MathF.FusedMultiplyAdd(x, result, coefficients[i]);
            }

            return result;
        }
    }
}
