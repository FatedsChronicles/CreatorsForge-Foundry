using System.Drawing;
using System.Windows.Forms;

namespace CreatorsForge.Samples.VisualWinFormsPanel
{
    public sealed class ControlPanel : Form
    {
        private Button buttonStart;
        private Button buttonStop;
        private Label labelStatus;
        private TextBox textScene;
        private CheckBox checkAutoSwitch;
        private ProgressBar progressAudience;

        public ControlPanel()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Streamer Control Panel";
            this.ClientSize = new Size(760, 520);

            labelStatus = new System.Windows.Forms.Label();
            labelStatus.Location = new System.Drawing.Point(32, 32);
            labelStatus.Size = new System.Drawing.Size(360, 42);
            labelStatus.Text = "Stream status: Ready";

            textScene = new System.Windows.Forms.TextBox();
            textScene.Location = new System.Drawing.Point(32, 104);
            textScene.Size = new System.Drawing.Size(420, 38);
            textScene.Text = "Starting Soon";

            checkAutoSwitch = new System.Windows.Forms.CheckBox();
            checkAutoSwitch.Location = new System.Drawing.Point(32, 170);
            checkAutoSwitch.Size = new System.Drawing.Size(300, 36);
            checkAutoSwitch.Text = "Enable automatic scene switching";

            progressAudience = new System.Windows.Forms.ProgressBar();
            progressAudience.Location = new System.Drawing.Point(32, 236);
            progressAudience.Size = new System.Drawing.Size(620, 34);

            buttonStart = new System.Windows.Forms.Button();
            buttonStart.Location = new System.Drawing.Point(32, 320);
            buttonStart.Size = new System.Drawing.Size(180, 48);
            buttonStart.Text = "Start stream";

            buttonStop = new System.Windows.Forms.Button();
            buttonStop.Location = new System.Drawing.Point(232, 320);
            buttonStop.Size = new System.Drawing.Size(180, 48);
            buttonStop.Text = "Stop stream";

            Controls.Add(labelStatus);
            Controls.Add(textScene);
            Controls.Add(checkAutoSwitch);
            Controls.Add(progressAudience);
            Controls.Add(buttonStart);
            Controls.Add(buttonStop);
        }
    }
}
