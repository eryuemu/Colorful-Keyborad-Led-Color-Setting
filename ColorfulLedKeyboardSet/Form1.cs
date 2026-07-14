using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ColorfulLedKeyboardSet
{
    public partial class Form1 : Form
    {
        // PInvoke declaration for Clevo/Hasee/Colorful hardware driver API
        [DllImport("InsydeDCHU.dll", EntryPoint = "SetDCHU_Data", CallingConvention = CallingConvention.StdCall)]
        private static extern int SetDCHU_Data_Raw(int command, byte[] buffer, int length);

        private static bool isDllAvailable = true;
        private static bool isSafeModeForced = false;

        private AppConfig currentConfig;
        private CancellationTokenSource cts;
        private Task animationTask;

        private bool initialMinimized = false;
        private bool isForceShown = false;
        private bool isExiting = false;

        // Custom Slider State
        private bool isSpeedDragging = false;
        private bool isBrightnessDragging = false;
        private Color currentKeyboardColor = Color.Green;

        // Preset Colors
        private readonly Color[] PresetColors = new Color[]
        {
            Color.FromArgb(255, 0, 0),      // Red
            Color.FromArgb(255, 127, 0),    // Orange
            Color.FromArgb(255, 255, 0),    // Yellow
            Color.FromArgb(0, 255, 0),      // Green
            Color.FromArgb(0, 255, 255),    // Cyan
            Color.FromArgb(0, 0, 255),      // Blue
            Color.FromArgb(139, 0, 255),    // Purple
            Color.FromArgb(255, 255, 255)   // White
        };

        public Form1(bool startMinimized)
        {
            this.initialMinimized = startMinimized;
            InitializeComponent();
            
            // Enable double buffering to avoid flicker on custom painting
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
        }

        // Safe wrapper for DLL API to prevent crash
        public static int SetDCHU_Data(int command, byte[] buffer, int length)
        {
            if (!isDllAvailable || isSafeModeForced) return -1;

            try
            {
                return SetDCHU_Data_Raw(command, buffer, length);
            }
            catch (DllNotFoundException)
            {
                isDllAvailable = false;
                return -2;
            }
            catch (Exception)
            {
                return -3;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Load application icon from executable
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notifyIcon1.Icon = this.Icon;
            }
            catch { }

            // 1. Check DLL availability
            string dllPath = Path.Combine(Application.StartupPath, "InsydeDCHU.dll");
            if (!File.Exists(dllPath))
            {
                isDllAvailable = false;
                chkSafeMode.Checked = true;
                chkSafeMode.Enabled = false;
                lblStatus.Text = "安全仿真模式 / 未检测到驱动组件";
                lblStatus.ForeColor = Color.Tomato;
            }
            else
            {
                lblStatus.Text = "硬件驱动已连接 / 物理写入模式已启用";
                lblStatus.ForeColor = Color.FromArgb(0, 173, 181);
            }

            // 2. Load Config
            currentConfig = ConfigManager.Load();

            // Override minimized if config says so and we didn't specify via args
            if (!initialMinimized && currentConfig.StartMinimized)
            {
                initialMinimized = true;
            }

            // 3. Setup UI Controls values from config
            chkAutoStart.Checked = currentConfig.AutoStart;
            chkStartMinimized.Checked = currentConfig.StartMinimized;
            lblSpeed.Text = string.Format("动画速度 / 频率值 ({0})", currentConfig.Speed);
            lblBrightness.Text = string.Format("背光亮度 / 调整值 ({0}%)", currentConfig.Brightness);

            // Initialize visual keyboard color state
            currentKeyboardColor = ColorFromHex(currentConfig.Color);

            // Set active mode button style
            UpdateModeButtonStyles(currentConfig.Mode);

            // Populate preset colors
            SetupPresetButtons();

            // 4. Start animation loop
            StartAnimation();

            // If started minimized, hide form immediately
            if (initialMinimized)
            {
                // Task.Delay to avoid window flash during Load event
                Task.Run(async () =>
                {
                    await Task.Delay(50);
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        this.WindowState = FormWindowState.Minimized;
                        this.Hide();
                        notifyIcon1.ShowBalloonTip(2000, "键盘灯光控制中心", "已在后台启动并最小化到系统托盘", ToolTipIcon.Info);
                    });
                });
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            // If starting minimized, prevent the window from becoming visible initially
            if (initialMinimized && !isForceShown)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            // Draw a gorgeous futuristic mirror/glass gradient background
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(18, 18, 22),
                Color.FromArgb(8, 8, 10),
                90F))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
            
            // Draw a high-tech glowing border around the window
            using (var pen = new Pen(Color.FromArgb(40, 0, 173, 181), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
            }
        }

        private void SetupPresetButtons()
        {
            flowPresets.Controls.Clear();
            foreach (var color in PresetColors)
            {
                var pnl = new Panel
                {
                    Size = new Size(26, 26),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(8, 6, 8, 0)
                };
                
                pnl.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    
                    // Draw outer glowing shadow
                    using (var glowBrush = new SolidBrush(Color.FromArgb(45, color)))
                    {
                        e.Graphics.FillEllipse(glowBrush, 0, 0, 25, 25);
                    }
                    
                    // Draw main solid sphere
                    using (var brush = new SolidBrush(color))
                    {
                        e.Graphics.FillEllipse(brush, 3, 3, 19, 19);
                    }

                    // Draw inner glass reflection highlight (crescent)
                    using (var highlightPen = new Pen(Color.FromArgb(140, Color.White), 1))
                    {
                        e.Graphics.DrawArc(highlightPen, 5, 5, 15, 15, -160, 100);
                    }
                };

                pnl.Click += (s, ev) =>
                {
                    ApplyColorToZones(color);
                };

                flowPresets.Controls.Add(pnl);
            }
        }

        private void ApplyColorToZones(Color color)
        {
            currentConfig.Color = ColorToHex(color);
            currentKeyboardColor = color;

            if (currentConfig.Mode == 0)
            {
                Task.Run(() => ApplyStaticColors());
            }
            pnlKeyboardColor.Refresh();
            pnlBrightnessSlider.Refresh();
            pnlSpeedSlider.Refresh();
            SaveConfig();
        }

        private void UpdateModeButtonStyles(int activeMode)
        {
            Button[] modeButtons = new Button[] { btnModeStatic, btnModeLoop, btnModeBreath, btnModeStrobe };

            for (int i = 0; i < modeButtons.Length; i++)
            {
                if (i == activeMode)
                {
                    modeButtons[i].BackColor = Color.FromArgb(30, 30, 35);
                    modeButtons[i].ForeColor = Color.FromArgb(0, 173, 181);
                }
                else
                {
                    modeButtons[i].BackColor = Color.FromArgb(20, 20, 24);
                    modeButtons[i].ForeColor = Color.FromArgb(140, 140, 145);
                }
            }
        }

        private void btnMode_Click(object sender, EventArgs e)
        {
            Button clicked = (Button)sender;
            int mode = 0;

            if (clicked == btnModeStatic) mode = 0;
            else if (clicked == btnModeLoop) mode = 1;
            else if (clicked == btnModeBreath) mode = 2;
            else if (clicked == btnModeStrobe) mode = 3;

            currentConfig.Mode = mode;
            UpdateModeButtonStyles(mode);
            StartAnimation();
            SaveConfig();
        }

        // Setup the specific hardware color command packet
        private void SendHardwareColor(int partIndex, Color color)
        {
            int num = 0;
            switch (partIndex)
            {
                case 1: num = 240; break; // Left
                case 2: num = 241; break; // Middle
                case 3: num = 242; break; // Right
                case 7: num = 246; break;
                case 8: num = 243; break;
            }

            byte r = color.R;
            byte g = color.G;
            byte b = color.B;

            // Maintain the specific driver workaround from the original source code
            if (r == 0 && g == 255 && b == 127)
            {
                b = 0x46; // 70
            }

            // Packet format: [G, R, B, PartID]
            byte[] bytes = new byte[] { g, r, b, (byte)num };
            SetDCHU_Data(103, bytes, 4);
        }

        private void ApplyStaticColors()
        {
            Color color = ScaleBrightness(ColorFromHex(currentConfig.Color), currentConfig.Brightness);

            // In single zone mode, send the same color to all physical zones
            SendHardwareColor(1, color);
            SendHardwareColor(2, color);
            SendHardwareColor(3, color);
        }

        private void StartAnimation()
        {
            StopAnimation();

            cts = new CancellationTokenSource();
            var token = cts.Token;
            int mode = currentConfig.Mode;

            if (mode == 0)
            {
                // Static mode does not need an active loop, saving CPU
                Task.Run(() => ApplyStaticColors());
                return;
            }

            animationTask = Task.Run(async () =>
            {
                double hue = 0;
                bool breathIn = true;
                double breathVal = 1.0;
                bool strobeState = true;

                while (!token.IsCancellationRequested)
                {
                    int speed = currentConfig.Speed;
                    int sleepMs = 50;

                    Color midOut = Color.Black;

                    // Calculate color frames based on animation modes
                    if (mode == 1) // Rainbow cycle
                    {
                        sleepMs = Math.Max(15, 180 - speed * 16);
                        midOut = ColorFromHSV(hue, 1.0, 1.0);
                        hue = (hue + 2) % 360;
                    }
                    else if (mode == 2) // Breath mode
                    {
                        sleepMs = Math.Max(15, 80 - speed * 6);

                        if (breathIn)
                        {
                            breathVal += 0.02;
                            if (breathVal >= 1.0) { breathVal = 1.0; breathIn = false; }
                        }
                        else
                        {
                            breathVal -= 0.02;
                            if (breathVal <= 0.05) { breathVal = 0.05; breathIn = true; }
                        }

                        Color baseColor = ColorFromHex(currentConfig.Color);
                        midOut = Color.FromArgb((int)(baseColor.R * breathVal), (int)(baseColor.G * breathVal), (int)(baseColor.B * breathVal));
                    }
                    else if (mode == 3) // Strobe mode
                    {
                        sleepMs = Math.Max(50, 600 - speed * 55);

                        if (strobeState)
                        {
                            midOut = ColorFromHex(currentConfig.Color);
                        }
                        else
                        {
                            midOut = Color.Black;
                        }
                        strobeState = !strobeState;
                    }

                    // Apply global brightness scaling
                    midOut = ScaleBrightness(midOut, currentConfig.Brightness);

                    // Send hardware write commands to all physical zones
                    SendHardwareColor(1, midOut);
                    SendHardwareColor(2, midOut);
                    SendHardwareColor(3, midOut);

                    // Safely update the on-screen simulated keyboard
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            currentKeyboardColor = midOut;
                            pnlKeyboardColor.Refresh();
                        });
                    }

                    try
                    {
                        await Task.Delay(sleepMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private void StopAnimation()
        {
            if (cts != null)
            {
                cts.Cancel();
                try
                {
                    if (animationTask != null)
                    {
                        animationTask.Wait(200);
                    }
                }
                catch { }
                cts.Dispose();
                cts = null;
            }
        }

        private void SaveConfig()
        {
            if (currentConfig != null)
            {
                ConfigManager.Save(currentConfig);
            }
        }

        // Helper: scale color by brightness ratio
        private Color ScaleBrightness(Color color, int brightness)
        {
            double factor = brightness / 100.0;
            return Color.FromArgb(
                (int)(color.R * factor),
                (int)(color.G * factor),
                (int)(color.B * factor)
            );
        }

        // Helper: Convert HSV to RGB Color
        private Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0) return Color.FromArgb(v, t, p);
            else if (hi == 1) return Color.FromArgb(q, v, p);
            else if (hi == 2) return Color.FromArgb(p, v, t);
            else if (hi == 3) return Color.FromArgb(p, q, v);
            else if (hi == 4) return Color.FromArgb(t, p, v);
            else return Color.FromArgb(v, p, q);
        }

        private string ColorToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private Color ColorFromHex(string hex)
        {
            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return Color.Red;
            }
        }

        // --- Custom Paint Event Handlers (Sci-Fi GlassHUD & Neon Glow) ---

        private void grpPanel_Paint(object sender, PaintEventArgs e)
        {
            var panel = (Panel)sender;
            int w = panel.Width;
            int h = panel.Height;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Semi-translucent glass background
            using (var bgBrush = new SolidBrush(Color.FromArgb(8, 255, 255, 255)))
            {
                e.Graphics.FillRectangle(bgBrush, 0, 0, w, h);
            }

            // High-tech glowing cyan border outline
            using (var borderPen = new Pen(Color.FromArgb(22, 0, 173, 181), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }
        }

        private void pnlKeyboardColor_Paint(object sender, PaintEventArgs e)
        {
            Color baseColor = currentKeyboardColor;
            int w = pnlKeyboardColor.Width;
            int h = pnlKeyboardColor.Height;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background of neon glow container
            using (var bgBrush = new SolidBrush(Color.FromArgb(12, 12, 14)))
            {
                e.Graphics.FillRectangle(bgBrush, 0, 0, w, h);
            }

            // Draw glowing neon tube bloom layer (Outer glow)
            for (int i = 5; i > 0; i--)
            {
                int alpha = 45 - i * 8;
                if (alpha < 0) alpha = 0;
                int thickness = i * 4;
                using (var pen = new Pen(Color.FromArgb(alpha, baseColor), thickness))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawLine(pen, 15, h / 2, w - 15, h / 2);
                }
            }

            // Draw bright lasersaber core
            Color coreColor = Color.FromArgb(255, (baseColor.R + 255) / 2, (baseColor.G + 255) / 2, (baseColor.B + 255) / 2);
            using (var corePen = new Pen(coreColor, 3))
            {
                corePen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                corePen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                e.Graphics.DrawLine(corePen, 15, h / 2, w - 15, h / 2);
            }
        }

        private void DrawEnergyBar(Graphics g, int width, int height, double percentage, Color activeColor)
        {
            int segmentCount = 15;
            int gap = 5;
            int segWidth = (width - (segmentCount - 1) * gap) / segmentCount;
            int activeSegments = (int)Math.Round(percentage * segmentCount);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            for (int i = 0; i < segmentCount; i++)
            {
                int x = i * (segWidth + gap);
                var rect = new Rectangle(x, 2, segWidth, height - 4);

                if (i < activeSegments)
                {
                    // Active glowing segment with outer bloom
                    using (var glowBrush = new SolidBrush(Color.FromArgb(40, activeColor)))
                    {
                        g.FillRectangle(glowBrush, Rectangle.Inflate(rect, 1, 1));
                    }
                    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                        rect,
                        activeColor,
                        Color.FromArgb(Math.Min(255, activeColor.R + 40), Math.Min(255, activeColor.G + 40), Math.Min(255, activeColor.B + 40)),
                        90F))
                    {
                        g.FillRectangle(brush, rect);
                    }
                }
                else
                {
                    // Inactive dark segment
                    using (var brush = new SolidBrush(Color.FromArgb(30, 30, 36)))
                    {
                        g.FillRectangle(brush, rect);
                    }
                }
            }
        }

        private void pnlSpeedSlider_Paint(object sender, PaintEventArgs e)
        {
            double pct = (currentConfig.Speed - 1) / 9.0;
            DrawEnergyBar(e.Graphics, pnlSpeedSlider.Width, pnlSpeedSlider.Height, pct, Color.FromArgb(0, 173, 181));
        }

        private void pnlBrightnessSlider_Paint(object sender, PaintEventArgs e)
        {
            double pct = (currentConfig.Brightness - 10) / 90.0;
            DrawEnergyBar(e.Graphics, pnlBrightnessSlider.Width, pnlBrightnessSlider.Height, pct, Color.FromArgb(0, 173, 181));
        }

        // --- Custom Slider Dragging Mouse Event Handlers ---

        private void pnlSpeedSlider_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSpeedDragging = true;
                UpdateSpeedFromMouse(e.X);
            }
        }

        private void pnlSpeedSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSpeedDragging)
            {
                UpdateSpeedFromMouse(e.X);
            }
        }

        private void pnlSpeedSlider_MouseUp(object sender, MouseEventArgs e)
        {
            isSpeedDragging = false;
        }

        private void UpdateSpeedFromMouse(int mouseX)
        {
            double pct = (double)mouseX / pnlSpeedSlider.Width;
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;
            int speed = (int)Math.Round(pct * 9) + 1; // 1 to 10
            currentConfig.Speed = speed;
            lblSpeed.Text = string.Format("动画速度 / 频率值 ({0})", speed);
            pnlSpeedSlider.Refresh();
            SaveConfig();
        }

        private void pnlBrightnessSlider_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isBrightnessDragging = true;
                UpdateBrightnessFromMouse(e.X);
            }
        }

        private void pnlBrightnessSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (isBrightnessDragging)
            {
                UpdateBrightnessFromMouse(e.X);
            }
        }

        private void pnlBrightnessSlider_MouseUp(object sender, MouseEventArgs e)
        {
            isBrightnessDragging = false;
        }

        private void UpdateBrightnessFromMouse(int mouseX)
        {
            double pct = (double)mouseX / pnlBrightnessSlider.Width;
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;
            int brightness = (int)Math.Round(pct * 90) + 10;
            brightness = (brightness / 10) * 10; // snap to multiples of 10
            if (brightness < 10) brightness = 10;
            if (brightness > 100) brightness = 100;
            currentConfig.Brightness = brightness;
            lblBrightness.Text = string.Format("背光亮度 / 调整值 ({0}%)", brightness);
            pnlBrightnessSlider.Refresh();
            if (currentConfig.Mode == 0)
            {
                Task.Run(() => ApplyStaticColors());
            }
            SaveConfig();
        }

        // --- System Checkboxes and Footers ---

        private void chkSafeMode_CheckedChanged(object sender, EventArgs e)
        {
            isSafeModeForced = chkSafeMode.Checked;
            if (!isSafeModeForced && !isDllAvailable)
            {
                // Prevent unchecking safe mode if driver DLL is missing
                MessageBox.Show("错误：由于未检测到 [InsydeDCHU.dll] 驱动组件，无法关闭安全测试模式！", "操作受限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                chkSafeMode.Checked = true;
                isSafeModeForced = true;
            }

            if (isSafeModeForced)
            {
                lblStatus.Text = "安全仿真模式 / 未检测到驱动组件";
                lblStatus.ForeColor = Color.Tomato;
            }
            else
            {
                lblStatus.Text = "硬件驱动已连接 / 物理写入模式已启用";
                lblStatus.ForeColor = Color.FromArgb(0, 173, 181);
            }
        }

        private void chkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            currentConfig.AutoStart = chkAutoStart.Checked;
            StartupManager.SetEnabled(chkAutoStart.Checked);
            SaveConfig();
        }

        private void chkStartMinimized_CheckedChanged(object sender, EventArgs e)
        {
            currentConfig.StartMinimized = chkStartMinimized.Checked;
            SaveConfig();
        }

        private void speedBar_Scroll(object sender, EventArgs e) { }
        private void brightnessBar_Scroll(object sender, EventArgs e) { }

        private void pnlKeyboardColor_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                ApplyColorToZones(colorDialog1.Color);
            }
        }

        private void btnCustomColor_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                ApplyColorToZones(colorDialog1.Color);
            }
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show("七彩虹笔记本键盘灯光控制中心\n" +
                            "版本：v2.5 Acrylic HUD Edition\n\n" +
                            "改进说明：\n" +
                            "1. 优化后台渲染线程，采用微秒/毫秒级 Task 调度，CPU 占用率降至近 0%。\n" +
                            "2. 增加【安全测试模式】，可在非兼容设备上无害化测试 UI 和效果。\n" +
                            "3. 引入极简科技感【亚克力 HUD 镜面背景】和 3D LED 圆球发光预设。\n" +
                            "4. 重构了传统的 TrackBar，替换为发光【数字能量条】滑块，支持平滑鼠标拖拽调整。\n" +
                            "5. 对中央模拟光效条采用了【双层霓虹激光核心算法】绘制，动态渐变效果拉满。\n" +
                            "6. 引入亮度平滑模拟、系统托盘常驻和 Windows 开机自启支持，支持大屏拉伸自适应布局。\n\n" +
                            "声明：本程序利用逆向硬件接口开发，开发者不对任何可能引起的硬件或驱动异常承担责任。",
                            "关于软件", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Minimize to Tray Logic
        private void Form1_SizeChanged(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon1.Visible = true;
            }
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            ShowMainForm();
        }

        private void menuShow_Click(object sender, EventArgs e)
        {
            ShowMainForm();
        }

        private void menuExit_Click(object sender, EventArgs e)
        {
            isExiting = true;
            Application.Exit();
        }

        private void ShowMainForm()
        {
            isForceShown = true;
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // If user clicks the X button, minimize to tray instead of closing (unless exiting from context menu)
            if (e.CloseReason == CloseReason.UserClosing && !isExiting)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                notifyIcon1.ShowBalloonTip(1500, "键盘灯光控制中心", "程序已最小化到系统托盘后台运行", ToolTipIcon.Info);
            }
            else
            {
                StopAnimation();
                SaveConfig();
            }
        }
    }

    // Registry Boot Helper class
    public static class StartupManager
    {
        private const string RegistryKeyName = "ColorfulLedKeyboardSet";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key == null) return false;
                    return key.GetValue(RegistryKeyName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string path = Application.ExecutablePath;
                        key.SetValue(RegistryKeyName, string.Format("\"{0}\" -minimized", path));
                    }
                    else
                    {
                        key.DeleteValue(RegistryKeyName, false);
                    }
                }
            }
            catch
            {
                // Fail silently (e.g. if permissions restrict registry writing)
            }
        }
    }
}
