using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace ColorCop
{
    // ========== NativeMethods ==========
    internal static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xFFFF;
        public const int WM_SHOW_COLORCOP = 0x8000 + 0x100;
        public const int WM_HOTKEY = 0x0312;
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern int GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        public static extern int GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }

    // ========== ColorHelpers ==========
    public static class ColorHelpers
    {
        public static string FormatHtml(Color color, bool uppercase, bool omitSymbol)
        {
            string format = uppercase ? "{0:X2}{1:X2}{2:X2}" : "{0:x2}{1:x2}{2:x2}";
            string hex = string.Format(format, color.R, color.G, color.B);
            return omitSymbol ? hex : "#" + hex;
        }
    }

    // ========== ScreenSampler ==========
    public static class ScreenSampler
    {
        public static Color GetPixel(int x, int y)
        {
            IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return Color.Black;
            try
            {
                int pixel = NativeMethods.GetPixel(hdc, x, y);
                return Color.FromArgb((pixel >> 0) & 0xFF, (pixel >> 8) & 0xFF, (pixel >> 16) & 0xFF);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        public static Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Color SampleArea(int x, int y, int width, int height)
        {
            using (Bitmap bmp = CaptureRegion(x, y, width, height))
            {
                long totalR = 0, totalG = 0, totalB = 0;
                int count = 0;
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bd.Stride;
                    byte[] pixels = new byte[stride * height];
                    Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
                    for (int py = 0; py < height; py++)
                    {
                        int row = py * stride;
                        for (int px = 0; px < width; px++)
                        {
                            int col = row + px * 4;
                            totalB += pixels[col];
                            totalG += pixels[col + 1];
                            totalR += pixels[col + 2];
                            count++;
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
                return (count == 0) ? Color.Black : Color.FromArgb(
                    (int)(totalR / count), (int)(totalG / count), (int)(totalB / count));
            }
        }
    }

    // ========== SettingsManager ==========
    internal static class SettingsManager
    {
        public static bool AlwaysOnTop { get; set; }
        public static bool AutoCopyToClipboard { get; set; }
        public static int WindowX { get; set; }
        public static int WindowY { get; set; }
        private static string _settingsPath;

        public static void Initialize()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = System.IO.Path.Combine(appData, "ColorCop");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            _settingsPath = System.IO.Path.Combine(dir, "Color_Cop.dat");
            LoadDefaults();
            Load();
        }

        private static void LoadDefaults()
        {
            AlwaysOnTop = false;
            AutoCopyToClipboard = false;
            WindowX = 200;
            WindowY = 200;
        }

        public static void Save()
        {
            try
            {
                using (XmlWriter writer = XmlWriter.Create(_settingsPath, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("ColorCopSettings");
                    writer.WriteElementString("WindowX", WindowX.ToString());
                    writer.WriteElementString("WindowY", WindowY.ToString());
                    writer.WriteElementString("AlwaysOnTop", AlwaysOnTop ? "true" : "false");
                    writer.WriteElementString("AutoCopyToClipboard", AutoCopyToClipboard ? "true" : "false");
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error saving settings: " + ex.Message);
            }
        }

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(_settingsPath))
                    return;
                string xml = System.IO.File.ReadAllText(_settingsPath);
                using (XmlReader reader = XmlReader.Create(new System.IO.StringReader(xml)))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                            ReadSetting(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading settings: " + ex.Message);
                LoadDefaults();
            }
        }

        private static void ReadSetting(XmlReader reader)
        {
            string name = reader.Name;
            string content = reader.ReadElementContentAsString();
            int intVal;
            bool boolVal;
            if (name == "WindowX" && int.TryParse(content, out intVal)) { WindowX = intVal; return; }
            if (name == "WindowY" && int.TryParse(content, out intVal)) { WindowY = intVal; return; }
            if (name == "AlwaysOnTop" && bool.TryParse(content, out boolVal)) { AlwaysOnTop = boolVal; return; }
            if (name == "AutoCopyToClipboard" && bool.TryParse(content, out boolVal)) { AutoCopyToClipboard = boolVal; return; }
        }
    }

    // ========== TrayManager ==========
    public class TrayManager : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private MainForm _mainForm;

        public TrayManager(MainForm mainForm)
        {
            _mainForm = mainForm;
            Initialize();
        }

        private void Initialize()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("恢复窗口", null, (s, e) => { _mainForm.ShowWindow(); });
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("退出", null, (s, e) => { _mainForm.ExitApplication(); });
            _contextMenu.Items[0].Font = new System.Drawing.Font(_contextMenu.Items[0].Font, System.Drawing.FontStyle.Bold);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "Color Cop 取色器";
            try
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.Visible = false;
            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _mainForm.ShowWindow();
            };
        }

        public void Show() { _notifyIcon.Visible = true; }
        public void Hide() { _notifyIcon.Visible = false; }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }
    }

    // ========== MainForm ==========
    public class MainForm : Form
    {
        private const int HOTKEY_ID = 1;
        private const int EYE_DROPPER_INTERVAL = 50;

        private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
        private static readonly Color FormBgColor = Color.FromArgb(248, 248, 248);
        private static readonly Color StatusBarColor = Color.FromArgb(235, 235, 238);
        private static readonly Color SubtleTextColor = Color.FromArgb(96, 96, 96);

        private Panel _colorPreview;
        private Button _btnPick;
        private Button _btnCopy;
        private TextBox _txtHex;
        private CheckBox _chkAlwaysOnTop;
        private CheckBox _chkAutoCopy;
        private TextBox _txtR;
        private TextBox _txtG;
        private TextBox _txtB;
        private Label _lblStatus;
        private GroupBox _grpRgb;

        private Color _currentColor;
        private bool _eyeDropping;
        private Timer _eyeDropperTimer;
        private TrayManager _trayManager;

        public MainForm()
        {
            _currentColor = Color.FromArgb(0, 51, 34);
            _eyeDropping = false;
            _eyeDropperTimer = new Timer();
            _eyeDropperTimer.Interval = EYE_DROPPER_INTERVAL;
            _eyeDropperTimer.Tick += EyeDropperTimer_Tick;

            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9F);
            this.BackColor = FormBgColor;

            InitializeComponent();
            ApplySettings();
            UpdateDisplay();
            RegisterHotKey();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _trayManager = new TrayManager(this);
        }

        private void InitializeComponent()
        {
            this.Text = "Color Cop 取色器";
            this.ClientSize = new Size(380, 240);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            if (SettingsManager.WindowX > 0 && SettingsManager.WindowY > 0)
                this.Location = new Point(SettingsManager.WindowX, SettingsManager.WindowY);

            // ---- Color Preview ----
            _colorPreview = new Panel();
            _colorPreview.BorderStyle = BorderStyle.FixedSingle;
            _colorPreview.Location = new Point(16, 16);
            _colorPreview.Size = new Size(100, 100);
            _colorPreview.BackColor = _currentColor;
            _colorPreview.Paint += ColorPreview_Paint;

            // ---- Pick Button (primary) ----
            _btnPick = new Button();
            _btnPick.Text = "取色";
            _btnPick.Location = new Point(132, 16);
            _btnPick.Size = new Size(80, 34);
            _btnPick.FlatStyle = FlatStyle.Flat;
            _btnPick.BackColor = AccentColor;
            _btnPick.ForeColor = Color.White;
            _btnPick.FlatAppearance.BorderSize = 0;
            _btnPick.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _btnPick.Cursor = Cursors.Hand;
            _btnPick.Click += BtnPick_Click;

            // ---- Copy Button (secondary) ----
            _btnCopy = new Button();
            _btnCopy.Text = "复制";
            _btnCopy.Location = new Point(224, 16);
            _btnCopy.Size = new Size(80, 34);
            _btnCopy.FlatStyle = FlatStyle.Flat;
            _btnCopy.BackColor = Color.White;
            _btnCopy.ForeColor = Color.FromArgb(51, 51, 51);
            _btnCopy.FlatAppearance.BorderColor = Color.FromArgb(204, 204, 204);
            _btnCopy.Cursor = Cursors.Hand;
            _btnCopy.Click += BtnCopy_Click;

            // ---- HEX Label + TextBox ----
            Label lblHex = new Label();
            lblHex.Text = "HEX";
            lblHex.Location = new Point(132, 58);
            lblHex.Size = new Size(36, 24);
            lblHex.TextAlign = ContentAlignment.MiddleLeft;
            lblHex.ForeColor = SubtleTextColor;
            lblHex.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);

            _txtHex = new TextBox();
            _txtHex.Location = new Point(170, 56);
            _txtHex.Size = new Size(194, 24);
            _txtHex.Font = new Font("Consolas", 11F);
            _txtHex.ReadOnly = true;
            _txtHex.BackColor = Color.White;
            _txtHex.Cursor = Cursors.IBeam;
            _txtHex.Click += TxtHex_Click;

            // ---- Checkboxes ----
            _chkAlwaysOnTop = new CheckBox();
            _chkAlwaysOnTop.Text = "总在最前";
            _chkAlwaysOnTop.Location = new Point(132, 92);
            _chkAlwaysOnTop.Size = new Size(100, 24);
            _chkAlwaysOnTop.CheckedChanged += ChkAlwaysOnTop_CheckedChanged;

            _chkAutoCopy = new CheckBox();
            _chkAutoCopy.Text = "自动复制";
            _chkAutoCopy.Location = new Point(240, 92);
            _chkAutoCopy.Size = new Size(100, 24);
            _chkAutoCopy.CheckedChanged += ChkAutoCopy_CheckedChanged;

            // ---- RGB GroupBox ----
            _grpRgb = new GroupBox();
            _grpRgb.Text = "RGB";
            _grpRgb.Location = new Point(16, 128);
            _grpRgb.Size = new Size(348, 56);
            _grpRgb.ForeColor = SubtleTextColor;

            Font rgbLabelFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            Font rgbValueFont = new Font("Consolas", 10F);

            Label lblR = new Label();
            lblR.Text = "R";
            lblR.Location = new Point(40, 22);
            lblR.Size = new Size(20, 22);
            lblR.Font = rgbLabelFont;
            lblR.TextAlign = ContentAlignment.MiddleCenter;
            lblR.ForeColor = Color.FromArgb(200, 40, 40);

            _txtR = new TextBox();
            _txtR.Location = new Point(62, 20);
            _txtR.Size = new Size(56, 24);
            _txtR.ReadOnly = true;
            _txtR.BackColor = Color.White;
            _txtR.TextAlign = HorizontalAlignment.Center;
            _txtR.Font = rgbValueFont;

            Label lblG = new Label();
            lblG.Text = "G";
            lblG.Location = new Point(136, 22);
            lblG.Size = new Size(20, 22);
            lblG.Font = rgbLabelFont;
            lblG.TextAlign = ContentAlignment.MiddleCenter;
            lblG.ForeColor = Color.FromArgb(40, 160, 40);

            _txtG = new TextBox();
            _txtG.Location = new Point(158, 20);
            _txtG.Size = new Size(56, 24);
            _txtG.ReadOnly = true;
            _txtG.BackColor = Color.White;
            _txtG.TextAlign = HorizontalAlignment.Center;
            _txtG.Font = rgbValueFont;

            Label lblB = new Label();
            lblB.Text = "B";
            lblB.Location = new Point(232, 22);
            lblB.Size = new Size(20, 22);
            lblB.Font = rgbLabelFont;
            lblB.TextAlign = ContentAlignment.MiddleCenter;
            lblB.ForeColor = Color.FromArgb(40, 80, 200);

            _txtB = new TextBox();
            _txtB.Location = new Point(254, 20);
            _txtB.Size = new Size(56, 24);
            _txtB.ReadOnly = true;
            _txtB.BackColor = Color.White;
            _txtB.TextAlign = HorizontalAlignment.Center;
            _txtB.Font = rgbValueFont;

            _grpRgb.Controls.AddRange(new Control[] { lblR, _txtR, lblG, _txtG, lblB, _txtB });

            // ---- Status Label ----
            _lblStatus = new Label();
            _lblStatus.Text = "就绪";
            _lblStatus.Location = new Point(16, 198);
            _lblStatus.Size = new Size(348, 26);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.ForeColor = SubtleTextColor;
            _lblStatus.BackColor = StatusBarColor;
            _lblStatus.Padding = new Padding(8, 0, 0, 0);

            this.Controls.AddRange(new Control[] {
                _colorPreview, _btnPick, _btnCopy,
                lblHex, _txtHex,
                _chkAlwaysOnTop, _chkAutoCopy,
                _grpRgb, _lblStatus
            });
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
        }

        private void ApplySettings()
        {
            _chkAlwaysOnTop.Checked = SettingsManager.AlwaysOnTop;
            _chkAutoCopy.Checked = SettingsManager.AutoCopyToClipboard;
            this.TopMost = SettingsManager.AlwaysOnTop;
        }

        private void UpdateDisplay()
        {
            _colorPreview.BackColor = _currentColor;
            _colorPreview.Invalidate();
            _txtHex.Text = ColorHelpers.FormatHtml(_currentColor, true, false);
            _txtR.Text = _currentColor.R.ToString();
            _txtG.Text = _currentColor.G.ToString();
            _txtB.Text = _currentColor.B.ToString();
        }

        private void BtnPick_Click(object sender, EventArgs e)
        {
            if (_eyeDropping) StopEyeDropper();
            else StartEyeDropper();
        }

        private void StartEyeDropper()
        {
            _eyeDropping = true;
            _btnPick.Text = "停止";
            _lblStatus.Text = "取色中... 点击左键确认";
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            _eyeDropperTimer.Start();
        }

        private void StopEyeDropper()
        {
            _eyeDropping = false;
            _btnPick.Text = "取色";
            _lblStatus.Text = string.Format("RGB({0}, {1}, {2})  #{3:X2}{4:X2}{5:X2}",
                _currentColor.R, _currentColor.G, _currentColor.B,
                _currentColor.R, _currentColor.G, _currentColor.B);
            _eyeDropperTimer.Stop();
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void EyeDropperTimer_Tick(object sender, EventArgs e)
        {
            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            _currentColor = ScreenSampler.GetPixel(pt.X, pt.Y);
            UpdateDisplay();
            _lblStatus.Text = string.Format("取色中... ({0}, {1})  RGB({2},{3},{4})",
                pt.X, pt.Y, _currentColor.R, _currentColor.G, _currentColor.B);

            if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0)
            {
                StopEyeDropper();
                if (_chkAutoCopy.Checked) CopyToClipboard();
            }
        }

        private void BtnCopy_Click(object sender, EventArgs e) { CopyToClipboard(); }

        private void CopyToClipboard()
        {
            if (!string.IsNullOrEmpty(_txtHex.Text))
            {
                Clipboard.SetText(_txtHex.Text);
                _lblStatus.Text = "已复制: " + _txtHex.Text;
            }
        }

        private void TxtHex_Click(object sender, EventArgs e) { _txtHex.SelectAll(); }

        private void ChkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMost = _chkAlwaysOnTop.Checked;
            SettingsManager.AlwaysOnTop = _chkAlwaysOnTop.Checked;
        }

        private void ChkAutoCopy_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.AutoCopyToClipboard = _chkAutoCopy.Checked;
        }

        private void ColorPreview_Paint(object sender, PaintEventArgs e)
        {
            using (Brush b = new SolidBrush(_currentColor))
                e.Graphics.FillRectangle(b, 0, 0, _colorPreview.Width, _colorPreview.Height);
        }

        private void RegisterHotKey()
        {
            NativeMethods.RegisterHotKey(this.Handle, HOTKEY_ID, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, (int)Keys.C);
        }

        private void UnregisterHotKey()
        {
            NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_ID);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                if (this.Visible) this.Hide();
                else this.ShowWindow();
                return;
            }
            if (m.Msg == NativeMethods.WM_SHOW_COLORCOP)
            {
                this.ShowWindow();
                return;
            }
            base.WndProc(ref m);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            UnregisterHotKey();
            if (_eyeDropperTimer != null)
            {
                _eyeDropperTimer.Stop();
                _eyeDropperTimer.Dispose();
                _eyeDropperTimer = null;
            }
            SettingsManager.AlwaysOnTop = _chkAlwaysOnTop.Checked;
            SettingsManager.AutoCopyToClipboard = _chkAutoCopy.Checked;
            SettingsManager.WindowX = this.Location.X;
            SettingsManager.WindowY = this.Location.Y;
            SettingsManager.Save();
            if (_trayManager != null) _trayManager.Dispose();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                if (_trayManager != null) _trayManager.Show();
                this.Hide();
            }
        }

        public void ShowWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
            if (_trayManager != null) _trayManager.Hide();
        }

        public void ExitApplication()
        {
            Application.Exit();
        }
    }

    // ========== Program ==========
    internal static class Program
    {
        private static System.Threading.Mutex _mutex = null;
        private const string MutexName = "ColorCopSingleInstance";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOW_COLORCOP, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Close();
                }
            }
        }
    }
}
