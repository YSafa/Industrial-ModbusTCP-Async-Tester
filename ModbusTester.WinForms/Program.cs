using System;
using System.Windows.Forms;

namespace ModbusTester
{
    static class Program
    {
        /// <summary>
        /// The application's main entry point.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // This is the key line — it tells Windows to render MainForm on screen.
            Application.Run(new MainForm());
        }
    }
}