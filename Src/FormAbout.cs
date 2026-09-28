using System;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace _8085
{
    public partial class FormAbout : Form
    {
        public FormAbout()
        {
            InitializeComponent();

            Version version = typeof(MainForm).Assembly.GetName().Version;
            DateTime built = new DateTime(2000, 1, 1).AddDays(version.Build).AddSeconds(version.Revision * 2);
            tbAbout.Text = "8085 Simulator — PKW-3000 Edition\r\n" +
                "PKW-Sim by toro / Codex\r\n\r\n" +
                "Original 8085 Simulator by Dirk Prins.\r\n" +
                "Copyright © 2022 D. Prins\r\n\r\n" +
                "This fork adds PKW-3000 emulation and modifies parts of the\r\n" +
                "original simulator. These changes are maintained\r\n" +
                "independently of Dirk Prins.\r\n\r\n" +
                "Version " + version.Major + "." + version.Minor + " — PKW-Sim\r\n" +
                "Build " + version.Build + "." + version.Revision + " · " + built.ToString("yyyy-MM-dd HH:mm");

            tbAbout.DeselectAll();
        }

        private void button_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void FormAbout_Shown(object sender, EventArgs e)
        {
            this.tbAbout.DeselectAll();
            this.btnOK.Focus();
        }
    }
}
