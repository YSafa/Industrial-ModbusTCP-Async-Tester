using System;
using System.Windows.Forms;

namespace ModbusTester
{
    static class Program
    {
        /// <summary>
        /// Uygulamanın ana giriş noktası.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // İşte sihirli satır burası; Windows'a MainForm'u ekrana çizmesini söylüyoruz.
            Application.Run(new MainForm());
        }
    }
}