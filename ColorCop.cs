using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace ColorCop
{
    internal static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xFFFF;
        public const int WM_HOTKEY = 0x0312;
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_ESCAPE = 0x1B;

        public static readonly int WM_SHOW_COLORCOP = RegisterWindowMessage("ColorCop.Show");

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern int GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        public static extern int GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);
    }

    public static class ColorHelpers
    {
        public static string FormatHtml(Color color, bool uppercase, bool omitSymbol)
        {
            string format = uppercase ? "{0:X2}{1:X2}{2:X2}" : "{0:x2}{1:x2}{2:x2}";
            string hex = string.Format(format, color.R, color.G, color.B);
            return omitSymbol ? hex : "#" + hex;
        }

        public static Color GetReadableTextColor(Color background)
        {
            double luminance = (background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114);
            return luminance >= 160 ? Color.FromArgb(32, 32, 32) : Color.White;
        }
    }

    public static class ScreenSampler
    {
        public static Color GetPixel(int x, int y)
        {
            IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                return Color.Black;
            }

            try
            {
                int pixel = NativeMethods.GetPixel(hdc, x, y);
                if (pixel == -1)
                {
                    return Color.Black;
                }

                int r = pixel & 0x000000FF;
                int g = (pixel & 0x0000FF00) >> 8;
                int b = (pixel & 0x00FF0000) >> 16;
                return Color.FromArgb(r, g, b);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            }
        }
    }

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
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            _settingsPath = System.IO.Path.Combine(dir, "ColorCop.settings");
            LoadDefaults();
            Load();
        }

        private static void LoadDefaults()
        {
            AlwaysOnTop = false;
            AutoCopyToClipboard = false;
            WindowX = int.MinValue;
            WindowY = int.MinValue;
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(_settingsPath))
            {
                return;
            }

            try
            {
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true
                };

                using (XmlWriter writer = XmlWriter.Create(_settingsPath, settings))
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
            if (string.IsNullOrEmpty(_settingsPath) || !System.IO.File.Exists(_settingsPath))
            {
                return;
            }

            try
            {
                using (XmlReader reader = XmlReader.Create(_settingsPath))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        if (reader.Name == "ColorCopSettings")
                        {
                            continue;
                        }

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

            int intValue;
            bool boolValue;

            if (name == "WindowX" && int.TryParse(content, out intValue))
            {
                WindowX = intValue;
                return;
            }

            if (name == "WindowY" && int.TryParse(content, out intValue))
            {
                WindowY = intValue;
                return;
            }

            if (name == "AlwaysOnTop" && bool.TryParse(content, out boolValue))
            {
                AlwaysOnTop = boolValue;
                return;
            }

            if (name == "AutoCopyToClipboard" && bool.TryParse(content, out boolValue))
            {
                AutoCopyToClipboard = boolValue;
            }
        }
    }

    public sealed class TrayManager : IDisposable
    {
        private readonly MainForm _mainForm;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;

        public TrayManager(MainForm mainForm)
        {
            _mainForm = mainForm;
            Initialize();
        }

        private void Initialize()
        {
            _contextMenu = new ContextMenuStrip
            {
                Font = new Font("Segoe UI", 9F)
            };

            ToolStripItem restoreItem = _contextMenu.Items.Add("显示取色器");
            restoreItem.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            restoreItem.Click += delegate { _mainForm.ShowWindow(); };

            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("退出程序", null, delegate { _mainForm.ExitApplication(); });

            _notifyIcon = new NotifyIcon
            {
                Text = "Color Cop 取色器",
                Visible = false,
                ContextMenuStrip = _contextMenu
            };

            try
            {
                _notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
        }

        private void NotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _mainForm.ShowWindow();
            }
        }

        public void Show()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
            }
        }

        public void Hide()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
        }

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

    public sealed class MainForm : Form
    {
        private const int HotKeyId = 1;
        private const int EyeDropperInterval = 40;

        private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
        private static readonly Color FormBackgroundColor = Color.FromArgb(245, 247, 250);
        private static readonly Color CardColor = Color.White;
        private static readonly Color SoftBorderColor = Color.FromArgb(222, 226, 232);
        private static readonly Color SoftTextColor = Color.FromArgb(96, 100, 108);
        private static readonly Color StatusBarColor = Color.FromArgb(238, 242, 247);

        private readonly Timer _eyeDropperTimer;

        private Panel _previewCard;
        private Panel _previewSwatch;
        private Label _lblPreviewHex;
        private Label _lblPreviewHint;
        private Label _lblHotKeyHint;
        private Button _btnPick;
        private Button _btnCopy;
        private TextBox _txtHex;
        private TextBox _txtR;
        private TextBox _txtG;
        private TextBox _txtB;
        private CheckBox _chkAlwaysOnTop;
        private CheckBox _chkAutoCopy;
        private Label _lblStatus;
        private Panel _rgbCard;
        private TrayManager _trayManager;

        private Color _currentColor;
        private bool _eyeDropping;
        private bool _awaitingMouseRelease;
        private bool _hotKeyRegistered;

        public MainForm()
        {
            _currentColor = Color.FromArgb(0, 120, 212);
            _eyeDropperTimer = new Timer();
            _eyeDropperTimer.Interval = EyeDropperInterval;
            _eyeDropperTimer.Tick += EyeDropperTimer_Tick;

            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);
            BackColor = FormBackgroundColor;

            InitializeComponent();
            ApplySettings();
            RestoreWindowPosition();
            UpdateDisplay();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _trayManager = new TrayManager(this);
            SetStatus("就绪，点击“开始取色”或按 Ctrl+Alt+C。");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey();
            base.OnHandleDestroyed(e);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Color Cop 取色器";
            ClientSize = new Size(470, 373);
            MinimumSize = new Size(486, 412);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            Padding = new Padding(0);
            StartPosition = FormStartPosition.CenterScreen;

            Panel root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            _previewCard = CreateCardPanel(new Rectangle(16, 16, 150, 212));
            _previewSwatch = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(122, 122),
                BackColor = _currentColor
            };
            _previewSwatch.Paint += PreviewSwatch_Paint;

            _lblPreviewHex = new Label
            {
                Location = new Point(14, 149),
                Size = new Size(122, 24),
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _lblPreviewHint = new Label
            {
                Location = new Point(14, 177),
                Size = new Size(122, 18),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = SoftTextColor,
                Text = "实时颜色预览",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _previewCard.Controls.AddRange(new Control[]
            {
                _previewSwatch, _lblPreviewHex, _lblPreviewHint
            });

            Panel rightCard = CreateCardPanel(new Rectangle(178, 16, 276, 212));

            Label lblTitle = new Label
            {
                Location = new Point(18, 14),
                Size = new Size(180, 26),
                Text = "屏幕取色器",
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 32, 32)
            };

            _lblHotKeyHint = new Label
            {
                Location = new Point(18, 40),
                Size = new Size(220, 20),
                Text = "快捷键: Ctrl+Alt+C",
                ForeColor = SoftTextColor
            };

            _btnPick = new Button
            {
                Location = new Point(18, 69),
                Size = new Size(112, 36),
                Text = "开始取色",
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnPick.FlatAppearance.BorderSize = 0;
            _btnPick.Click += BtnPick_Click;

            _btnCopy = new Button
            {
                Location = new Point(142, 69),
                Size = new Size(112, 36),
                Text = "复制 HEX",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(248, 249, 251),
                ForeColor = Color.FromArgb(40, 40, 40),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCopy.FlatAppearance.BorderColor = SoftBorderColor;
            _btnCopy.FlatAppearance.BorderSize = 1;
            _btnCopy.Click += BtnCopy_Click;

            Label lblHex = CreateFieldLabel("HEX", new Point(18, 119));
            _txtHex = CreateValueTextBox(new Point(18, 140), 236, new Font("Consolas", 12F, FontStyle.Bold));
            _txtHex.Click += TxtHex_Click;
            _txtHex.DoubleClick += delegate { CopyToClipboard(); };

            _chkAlwaysOnTop = new CheckBox
            {
                Location = new Point(18, 176),
                Size = new Size(100, 22),
                Text = "总在最前",
                AutoSize = false
            };
            _chkAlwaysOnTop.CheckedChanged += ChkAlwaysOnTop_CheckedChanged;

            _chkAutoCopy = new CheckBox
            {
                Location = new Point(132, 176),
                Size = new Size(100, 22),
                Text = "自动复制",
                AutoSize = false
            };
            _chkAutoCopy.CheckedChanged += ChkAutoCopy_CheckedChanged;

            rightCard.Controls.AddRange(new Control[]
            {
                lblTitle, _lblHotKeyHint, _btnPick, _btnCopy,
                lblHex, _txtHex, _chkAlwaysOnTop, _chkAutoCopy
            });

            _rgbCard = CreateCardPanel(new Rectangle(16, 240, 438, 72));
            Label lblRgbTitle = new Label
            {
                Location = new Point(16, 10),
                Size = new Size(120, 20),
                Text = "RGB 数值",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = SoftTextColor
            };

            _txtR = CreateRgbBox(_rgbCard, "R", Color.FromArgb(212, 61, 61), new Point(16, 34));
            _txtG = CreateRgbBox(_rgbCard, "G", Color.FromArgb(43, 160, 92), new Point(156, 34));
            _txtB = CreateRgbBox(_rgbCard, "B", Color.FromArgb(53, 112, 214), new Point(296, 34));

            _rgbCard.Controls.Add(lblRgbTitle);

            Panel statusPanel = new Panel
            {
                Location = new Point(16, 321),
                Size = new Size(438, 36),
                BackColor = StatusBarColor
            };
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SoftTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0)
            };
            statusPanel.Controls.Add(_lblStatus);

            root.Controls.AddRange(new Control[]
            {
                _previewCard, rightCard, _rgbCard, statusPanel
            });

            Controls.Add(root);
            FormClosing += MainForm_FormClosing;
            Resize += MainForm_Resize;

            ResumeLayout(false);
        }

        private Panel CreateCardPanel(Rectangle bounds)
        {
            Panel panel = new Panel
            {
                Location = bounds.Location,
                Size = bounds.Size,
                BackColor = CardColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            return panel;
        }

        private static Label CreateFieldLabel(string text, Point location)
        {
            return new Label
            {
                Location = location,
                Size = new Size(120, 18),
                Text = text,
                ForeColor = SoftTextColor,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
        }

        private static TextBox CreateValueTextBox(Point location, int width, Font font)
        {
            return new TextBox
            {
                Location = location,
                Size = new Size(width, 30),
                BorderStyle = BorderStyle.FixedSingle,
                Font = font,
                ReadOnly = true,
                BackColor = Color.White,
                TabStop = false
            };
        }

        private static TextBox CreateRgbBox(Panel parent, string title, Color titleColor, Point location)
        {
            Panel group = new Panel
            {
                Location = location,
                Size = new Size(126, 24),
                BackColor = Color.Transparent
            };

            Label lbl = new Label
            {
                Location = new Point(0, 2),
                Size = new Size(20, 20),
                Text = title,
                ForeColor = titleColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            TextBox box = new TextBox
            {
                Location = new Point(28, 0),
                Size = new Size(86, 24),
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10.5F),
                TextAlign = HorizontalAlignment.Center,
                TabStop = false
            };

            group.Controls.Add(lbl);
            group.Controls.Add(box);
            parent.Controls.Add(group);
            return box;
        }

        private void ApplySettings()
        {
            _chkAlwaysOnTop.Checked = SettingsManager.AlwaysOnTop;
            _chkAutoCopy.Checked = SettingsManager.AutoCopyToClipboard;
            TopMost = SettingsManager.AlwaysOnTop;
        }

        private void RestoreWindowPosition()
        {
            if (SettingsManager.WindowX == int.MinValue || SettingsManager.WindowY == int.MinValue)
            {
                return;
            }

            Rectangle target = new Rectangle(SettingsManager.WindowX, SettingsManager.WindowY, Width, Height);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(target))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(SettingsManager.WindowX, SettingsManager.WindowY);
                    return;
                }
            }
        }

        private void UpdateDisplay()
        {
            string hex = ColorHelpers.FormatHtml(_currentColor, true, false);
            Color textColor = ColorHelpers.GetReadableTextColor(_currentColor);

            _previewSwatch.BackColor = _currentColor;
            _previewSwatch.Invalidate();

            _lblPreviewHex.Text = hex;
            _lblPreviewHex.ForeColor = textColor;

            _txtHex.Text = hex;
            _txtR.Text = _currentColor.R.ToString();
            _txtG.Text = _currentColor.G.ToString();
            _txtB.Text = _currentColor.B.ToString();
        }

        private void BtnPick_Click(object sender, EventArgs e)
        {
            if (_eyeDropping)
            {
                CancelEyeDropper("已取消取色。");
            }
            else
            {
                StartEyeDropper();
            }
        }

        private void StartEyeDropper()
        {
            _eyeDropping = true;
            _awaitingMouseRelease = true;
            _btnPick.Text = "取消取色";
            SetStatus("取色中，左键确认，Esc 或右键取消。");
            WindowState = FormWindowState.Minimized;
            Hide();
            _eyeDropperTimer.Start();
        }

        private void StopEyeDropper(bool keepSample, string statusText)
        {
            _eyeDropping = false;
            _awaitingMouseRelease = false;
            _btnPick.Text = "开始取色";
            _eyeDropperTimer.Stop();
            ShowWindow();
            SetStatus(statusText);

            if (keepSample && _chkAutoCopy.Checked)
            {
                CopyToClipboard();
            }
        }

        private void CancelEyeDropper(string statusText)
        {
            StopEyeDropper(false, statusText);
        }

        private void ConfirmEyeDropper()
        {
            string statusText = string.Format(
                "已选取 {0}  RGB({1}, {2}, {3})",
                ColorHelpers.FormatHtml(_currentColor, true, false),
                _currentColor.R,
                _currentColor.G,
                _currentColor.B);

            StopEyeDropper(true, statusText);
        }

        private void EyeDropperTimer_Tick(object sender, EventArgs e)
        {
            NativeMethods.POINT pt;
            if (NativeMethods.GetCursorPos(out pt) == 0)
            {
                return;
            }

            _currentColor = ScreenSampler.GetPixel(pt.X, pt.Y);
            UpdateDisplay();
            SetStatus(string.Format("取色中... 坐标 ({0}, {1})  RGB({2}, {3}, {4})", pt.X, pt.Y, _currentColor.R, _currentColor.G, _currentColor.B));

            bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            bool rightDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
            bool escDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;

            if (_awaitingMouseRelease)
            {
                if (!leftDown)
                {
                    _awaitingMouseRelease = false;
                }
                return;
            }

            if (escDown || rightDown)
            {
                CancelEyeDropper("已取消取色。");
                return;
            }

            if (leftDown)
            {
                ConfirmEyeDropper();
            }
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            CopyToClipboard();
        }

        private void CopyToClipboard()
        {
            if (string.IsNullOrWhiteSpace(_txtHex.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_txtHex.Text);
                SetStatus("已复制到剪贴板: " + _txtHex.Text);
            }
            catch (Exception ex)
            {
                SetStatus("复制失败，请稍后再试。");
                System.Diagnostics.Debug.WriteLine("Clipboard error: " + ex.Message);
            }
        }

        private void TxtHex_Click(object sender, EventArgs e)
        {
            _txtHex.SelectAll();
        }

        private void ChkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            TopMost = _chkAlwaysOnTop.Checked;
            SettingsManager.AlwaysOnTop = _chkAlwaysOnTop.Checked;
        }

        private void ChkAutoCopy_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.AutoCopyToClipboard = _chkAutoCopy.Checked;
        }

        private void PreviewSwatch_Paint(object sender, PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_currentColor))
            {
                e.Graphics.FillRectangle(brush, 0, 0, _previewSwatch.Width, _previewSwatch.Height);
            }

            Rectangle border = new Rectangle(0, 0, _previewSwatch.Width - 1, _previewSwatch.Height - 1);
            using (Pen pen = new Pen(Color.FromArgb(255, 255, 255, 255)))
            {
                e.Graphics.DrawRectangle(pen, border);
            }

            string shortRgb = string.Format("{0}, {1}, {2}", _currentColor.R, _currentColor.G, _currentColor.B);
            using (SolidBrush textBrush = new SolidBrush(ColorHelpers.GetReadableTextColor(_currentColor)))
            using (Font overlayFont = new Font("Segoe UI", 9F, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(shortRgb, overlayFont, textBrush, border, format);
            }
        }

        private void RegisterHotKey()
        {
            if (_hotKeyRegistered || !IsHandleCreated)
            {
                return;
            }

            _hotKeyRegistered = NativeMethods.RegisterHotKey(
                Handle,
                HotKeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
                (int)Keys.C);

            if (!_hotKeyRegistered && _lblHotKeyHint != null)
            {
                _lblHotKeyHint.Text = "快捷键注册失败，请检查是否被占用";
                _lblHotKeyHint.ForeColor = Color.FromArgb(180, 70, 70);
            }
        }

        private void UnregisterHotKey()
        {
            if (!_hotKeyRegistered || !IsHandleCreated)
            {
                return;
            }

            NativeMethods.UnregisterHotKey(Handle, HotKeyId);
            _hotKeyRegistered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                if (Visible && WindowState != FormWindowState.Minimized)
                {
                    HideToTray();
                }
                else
                {
                    ShowWindow();
                }
                return;
            }

            if (m.Msg == NativeMethods.WM_SHOW_COLORCOP)
            {
                ShowWindow();
                return;
            }

            base.WndProc(ref m);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_eyeDropperTimer != null)
            {
                _eyeDropperTimer.Stop();
                _eyeDropperTimer.Dispose();
            }

            if (WindowState == FormWindowState.Normal)
            {
                SettingsManager.WindowX = Location.X;
                SettingsManager.WindowY = Location.Y;
            }

            SettingsManager.AlwaysOnTop = _chkAlwaysOnTop.Checked;
            SettingsManager.AutoCopyToClipboard = _chkAutoCopy.Checked;
            SettingsManager.Save();

            if (_trayManager != null)
            {
                _trayManager.Dispose();
                _trayManager = null;
            }
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && !_eyeDropping)
            {
                HideToTray();
            }
        }

        private void HideToTray()
        {
            if (_trayManager != null)
            {
                _trayManager.Show();
            }

            Hide();
        }

        public void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();

            if (_trayManager != null)
            {
                _trayManager.Hide();
            }
        }

        public void ExitApplication()
        {
            Close();
        }

        private void SetStatus(string text)
        {
            if (_lblStatus != null)
            {
                _lblStatus.Text = text;
            }
        }
    }

    internal static class Program
    {
        private static System.Threading.Mutex _mutex;
        private const string MutexName = "ColorCopSingleInstance";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOW_COLORCOP,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return;
            }

            SettingsManager.Initialize();

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
                    _mutex = null;
                }
            }
        }
    }
}
