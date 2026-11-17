using System;
using System.Windows.Forms;

namespace ServerApp
{
    public partial class ServerMainForm : Form
    {
        private readonly Server server;

        public ServerMainForm()
        {
            InitializeComponent();
            server = new Server();

            txtLog.ReadOnly = true;

            // Khi server gửi log, hiển thị ra textbox
            Server.OnServerLog += AddLog;
            this.FormClosing += ServerMainForm_FormClosing;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            server.Start(5000);
            if (server.IsRunning)
            {
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            server.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
        }

        private void ServerMainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Server.OnServerLog -= AddLog;
            server.Stop();
        }

        private void AddLog(string msg)
        {
            if (IsDisposed) return;
            if (txtLog.InvokeRequired)
            {
                try
                {
                    txtLog.Invoke(new Action(() => AddLog(msg)));
                }
                catch { }
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        }
    }
}
