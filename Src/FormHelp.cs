using System;
using System.Windows.Forms;

namespace _8085
{
    public partial class FormHelp : Form
    {
        #region Members

        System.Drawing.Font newFont1 = new System.Drawing.Font("Georgia", 14f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 178, false);
        System.Drawing.Font newFont2 = new System.Drawing.Font("Georgia", 12f, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, 178, false);

        #endregion

        #region Constructor

        public FormHelp()
        {
            InitializeComponent();
            Text = "PKW-3000 Help";
            var manualLink = new LinkLabel {
                Text = "PKW-3000 User Manual (PDF)", AutoSize = true,
                Location = new System.Drawing.Point(12, 536),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                TabIndex = 0
            };
            manualLink.LinkClicked += (s, e) => {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = "https://github.com/sirtet/pkw-3000/blob/main/PKW-3000_User_Manual_with_notes_OCR.pdf",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { MessageBox.Show(this, "Could not open the manual:\n" + ex.Message, "PKW-3000 Manual", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Controls.Add(manualLink);
            Disposed += (s, e) => { newFont1.Dispose(); newFont2.Dispose(); };
        }

        #endregion

        #region EventHandlers

        /// <summary>
        /// Exit
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// First shown
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormHelp_Shown(object sender, EventArgs e)
        {
            // Load help text
            rtbInfo.Text = Properties.Resources.manual;

            // Deselect
            rtbInfo.DeselectAll();

            // Make everything between < and > bold
            int start = 0, end = 0;
            while ((start >= 0) && (end >= 0) && (start < rtbInfo.Text.Length) && (rtbInfo.Text.IndexOf('<', start) >= 0))
            {
                start = rtbInfo.Text.IndexOf('<', start);
                end   = rtbInfo.Text.IndexOf('>', start);

                if (end > 0)
                {
                    rtbInfo.Select(start, end - start + 1);
                    rtbInfo.SelectionFont = newFont1;
                }

                start = end;
            }

            // Make everything between ' and ' in italics
            start = end = 0;
            while ((start >= 0) && (end >= 0) && (start < rtbInfo.Text.Length) && (rtbInfo.Text.IndexOf('`', start) >= 0))
            {
                start = rtbInfo.Text.IndexOf('`', start);
                end = rtbInfo.Text.IndexOf('`', start + 1);

                if (end > 0)
                {
                    rtbInfo.Select(start, end - start + 1);
                    rtbInfo.SelectionFont = newFont2;
                }

                start = end + 1;
            }
            rtbInfo.Select(0, 0);
            rtbInfo.ScrollToCaret();
        }

        #endregion
    }
}
