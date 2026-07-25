using System;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Win11FixGUI
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendNotifyMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint init);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, ref bool vparam, uint init);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, ref uint vparam, uint init);

        public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_IDLIST = 0x0000;

        public const uint SPI_GETMOUSE = 0x0003;
        public const uint SPI_SETMOUSE = 0x0004;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        // ---------- 控件定义 ----------
        private GroupBox grpSystemFix;
        private Button btnRestoreClassicMenu;
        private Button btnTaskbarNeverCombine;
        private Button btnDesktopShowIcons;
        private Button btnDisableMousePrecision;
        private Button btnAutoLock;

        private GroupBox grpActivation;
        private Button btnActivateOnline;
        private Button btnActivateLocal;
        private Button btnCheckActivation;

        private GroupBox grpTools;
        private Button btnCPUZ;
        private Button btnGPUZ;
        private Button btnNotepadPP;
        private Button btnWinRAR;
        private Button btnPutty;
        private Button btnDismPP;
        private Button btnCrystalDiskInfo;
        private Button btnDisableDefender;
        private Button btnDisableDefenderV21;
        private Button btnDisableUpdate;

        private GroupBox grpSysAdmin;
        private Button btnControlPanel;
        private Button btnSecpol;
        private Button btnServices;
        private Button btnRestartExplorer;

        private GroupBox grpNetwork;
        private Button btnNetworkInfo;
        private Button btnShowWifiPassword;

        private RichTextBox txtLog;

        private int buildNumber = 0;
        private string systemVersion = "未知系统";
        private bool isWin11 = false;
        private bool isClassicMenuEnabled = false;

        private const int BTN_WIDTH = 138;
        private const int BTN_HEIGHT = 27;
        private const int BTN_GAP = 8;
        private const int LEFT_MARGIN = 10;

        private const string VERSION = "v1.0";

        private int _activeTasks = 0;

        // ---------- 注册表/系统常量 ----------
        private const string CLASSIC_MENU_KEY_PATH = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        private const string MOUSE_REGISTRY_PATH = @"Control Panel\Mouse";
        private const string BUILD_KEY_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        private const string ACTIVATION_SCRIPT_URL = "https://get.activated.win";

        // ---------- 缓存的字体对象（避免重复创建） ----------
        private readonly Font _logTagFont;
        private readonly Font _logContentFont;
        private readonly Font _logMutedFont;

        public MainForm()
        {
            _logTagFont = new Font("Consolas", 9F, FontStyle.Bold);
            _logContentFont = new Font("Consolas", 9F, FontStyle.Regular);
            _logMutedFont = new Font("Consolas", 9F, FontStyle.Regular);

            InitializeComponent();
            this.Shown += (sender, e) => InitializeSystemInfo();
            this.FormClosing += MainForm_FormClosing;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_logTagFont != null) _logTagFont.Dispose();
                if (_logContentFont != null) _logContentFont.Dispose();
                if (_logMutedFont != null) _logMutedFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------- 初始化 UI 组件 ----------
        private void InitializeComponent()
        {
            this.txtLog = new RichTextBox();

            this.grpSystemFix = new GroupBox();
            this.grpActivation = new GroupBox();
            this.grpTools = new GroupBox();
            this.grpSysAdmin = new GroupBox();
            this.grpNetwork = new GroupBox();

            this.btnRestoreClassicMenu = new Button();
            this.btnTaskbarNeverCombine = new Button();
            this.btnDesktopShowIcons = new Button();
            this.btnDisableMousePrecision = new Button();
            this.btnAutoLock = new Button();

            this.btnActivateOnline = new Button();
            this.btnActivateLocal = new Button();
            this.btnCheckActivation = new Button();

            this.btnCPUZ = new Button();
            this.btnGPUZ = new Button();
            this.btnNotepadPP = new Button();
            this.btnWinRAR = new Button();
            this.btnPutty = new Button();
            this.btnDismPP = new Button();
            this.btnCrystalDiskInfo = new Button();
            this.btnDisableDefender = new Button();
            this.btnDisableDefenderV21 = new Button();
            this.btnDisableUpdate = new Button();

            this.btnControlPanel = new Button();
            this.btnSecpol = new Button();
            this.btnServices = new Button();
            this.btnRestartExplorer = new Button();

            this.btnNetworkInfo = new Button();
            this.btnShowWifiPassword = new Button();

            this.SuspendLayout();

            this.Text = "Windows 综合优化与维护工具 " + VERSION;
            this.Size = new Size(660, 610);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.BackColor = Color.FromArgb(248, 249, 250);

            this.txtLog.Location = new Point(15, 12);
            this.txtLog.Size = new Size(615, 140);
            this.txtLog.ReadOnly = true;
            this.txtLog.BackColor = Color.FromArgb(30, 32, 38);
            this.txtLog.ForeColor = Color.FromArgb(230, 235, 240);
            this.txtLog.Font = new Font("Consolas", 9F, FontStyle.Regular);
            this.txtLog.BorderStyle = BorderStyle.None;

            int currentY = 162;
            int groupWidth = 615;
            int groupHeight = 54;

            // 系统设置（双行布局，新增自动锁屏）
            int systemFixGroupHeight = 80;
            SetupGroupBox(grpSystemFix, "系统设置", 15, currentY, groupWidth, systemFixGroupHeight);
            SetButtonProps(btnRestoreClassicMenu, "恢复经典右键", BtnRestoreClassicMenu_Click);
            SetButtonProps(btnTaskbarNeverCombine, "任务栏设置", BtnTaskbarNeverCombine_Click);
            SetButtonProps(btnDesktopShowIcons, "桌面图标设置", BtnDesktopShowIcons_Click);
            SetButtonProps(btnDisableMousePrecision, "关闭鼠标精准", BtnDisableMousePrecision_Click);
            SetButtonProps(btnAutoLock, "自动锁屏", BtnAutoLock_Click);
            grpSystemFix.Controls.Add(btnRestoreClassicMenu);
            grpSystemFix.Controls.Add(btnTaskbarNeverCombine);
            grpSystemFix.Controls.Add(btnDesktopShowIcons);
            grpSystemFix.Controls.Add(btnDisableMousePrecision);
            grpSystemFix.Controls.Add(btnAutoLock);
            // 第一行: 恢复经典右键, 任务栏设置, 桌面图标设置, 关闭鼠标精准
            btnRestoreClassicMenu.Location = new Point(LEFT_MARGIN, 18);
            btnTaskbarNeverCombine.Location = new Point(LEFT_MARGIN + (BTN_WIDTH + BTN_GAP), 18);
            btnDesktopShowIcons.Location = new Point(LEFT_MARGIN + (BTN_WIDTH + BTN_GAP) * 2, 18);
            btnDisableMousePrecision.Location = new Point(LEFT_MARGIN + (BTN_WIDTH + BTN_GAP) * 3, 18);
            // 第二行: 自动锁屏
            btnAutoLock.Location = new Point(LEFT_MARGIN, 18 + BTN_HEIGHT + 4);
            currentY += systemFixGroupHeight + 6;

            // 系统激活
            SetupGroupBox(grpActivation, "系统激活", 15, currentY, groupWidth, groupHeight);
            SetButtonProps(btnActivateOnline, "在线激活 Windows", BtnActivateOnline_Click);
            SetButtonProps(btnActivateLocal, "本地激活 (MAS)", BtnActivateLocal_Click);
            SetButtonProps(btnCheckActivation, "查询激活状态", BtnCheckActivation_Click);
            grpActivation.Controls.Add(btnActivateOnline);
            grpActivation.Controls.Add(btnActivateLocal);
            grpActivation.Controls.Add(btnCheckActivation);
            AlignButtonsLeft(grpActivation, btnActivateOnline, btnActivateLocal, btnCheckActivation);
            currentY += 60;

            // 工具（三行布局）
            int toolsGroupHeight = 110;
            SetupGroupBox(grpTools, "工具", 15, currentY, groupWidth, toolsGroupHeight);
            SetButtonProps(btnCPUZ, "CPU-Z", delegate { RunEmbeddedTool("cpuz.exe"); });
            SetButtonProps(btnGPUZ, "GPU-Z", delegate { RunEmbeddedTool("GPU-Z.exe"); });
            SetButtonProps(btnNotepadPP, "Notepad++", delegate { RunEmbeddedTool("Notepad++.zip"); });
            SetButtonProps(btnWinRAR, "WinRAR", delegate { RunEmbeddedTool("winrar.exe"); });
            SetButtonProps(btnPutty, "PuTTY", delegate { RunEmbeddedTool("putty.exe"); });
            SetButtonProps(btnDismPP, "Dism++", delegate { RunEmbeddedTool("Dism++.zip"); });
            SetButtonProps(btnCrystalDiskInfo, "CrystalDiskInfo", delegate { RunEmbeddedTool("CrystalDiskInfo.zip"); });
            SetButtonProps(btnDisableDefender, "Defender_v1.6", delegate { RunEmbeddedTool("关闭windows Defender.zip"); });
            SetButtonProps(btnDisableDefenderV21, "Defender_v2.1", delegate { RunEmbeddedTool("关闭windows Defender_v2.1.zip"); });
            SetButtonProps(btnDisableUpdate, "关闭 Update", delegate { RunEmbeddedTool("关闭windows update.zip"); });
            grpTools.Controls.Add(btnCPUZ);
            grpTools.Controls.Add(btnGPUZ);
            grpTools.Controls.Add(btnNotepadPP);
            grpTools.Controls.Add(btnWinRAR);
            grpTools.Controls.Add(btnPutty);
            grpTools.Controls.Add(btnDismPP);
            grpTools.Controls.Add(btnCrystalDiskInfo);
            grpTools.Controls.Add(btnDisableDefender);
            grpTools.Controls.Add(btnDisableDefenderV21);
            grpTools.Controls.Add(btnDisableUpdate);
            int rowStep = BTN_HEIGHT + 4;
            int rowGap = BTN_WIDTH + BTN_GAP;
            // 第一行: CPU-Z, GPU-Z, Notepad++, WinRAR
            btnCPUZ.Location = new Point(LEFT_MARGIN, 18);
            btnGPUZ.Location = new Point(LEFT_MARGIN + rowGap, 18);
            btnNotepadPP.Location = new Point(LEFT_MARGIN + rowGap * 2, 18);
            btnWinRAR.Location = new Point(LEFT_MARGIN + rowGap * 3, 18);
            // 第二行: PuTTY, Dism++, CrystalDiskInfo
            int row2Y = 18 + rowStep;
            btnPutty.Location = new Point(LEFT_MARGIN, row2Y);
            btnDismPP.Location = new Point(LEFT_MARGIN + rowGap, row2Y);
            btnCrystalDiskInfo.Location = new Point(LEFT_MARGIN + rowGap * 2, row2Y);
            // 第三行: Defender_v1.6, Defender_v2.1, 关闭 Update
            int row3Y = 18 + rowStep * 2;
            btnDisableDefender.Location = new Point(LEFT_MARGIN, row3Y);
            btnDisableDefenderV21.Location = new Point(LEFT_MARGIN + rowGap, row3Y);
            btnDisableUpdate.Location = new Point(LEFT_MARGIN + rowGap * 2, row3Y);
            currentY += toolsGroupHeight + 6;

            // 系统管理组件
            SetupGroupBox(grpSysAdmin, "系统管理组件", 15, currentY, groupWidth, groupHeight);
            SetButtonProps(btnControlPanel, "控制面板", BtnControlPanel_Click);
            SetButtonProps(btnServices, "系统服务", BtnServices_Click);
            SetButtonProps(btnSecpol, "本地安全策略", BtnSecpol_Click);
            SetButtonProps(btnRestartExplorer, "重启Explorer", BtnRestartExplorer_Click);
            grpSysAdmin.Controls.Add(btnControlPanel);
            grpSysAdmin.Controls.Add(btnServices);
            grpSysAdmin.Controls.Add(btnSecpol);
            grpSysAdmin.Controls.Add(btnRestartExplorer);
            AlignButtonsLeft(grpSysAdmin, btnControlPanel, btnServices, btnSecpol, btnRestartExplorer);
            currentY += 60;

            // 网络查看
            SetupGroupBox(grpNetwork, "网络查看", 15, currentY, groupWidth, groupHeight);
            SetButtonProps(btnNetworkInfo, "获取网络信息", BtnNetworkInfo_Click);
            SetButtonProps(btnShowWifiPassword, "查看 WiFi 密码", BtnShowWifiPassword_Click);
            grpNetwork.Controls.Add(btnNetworkInfo);
            grpNetwork.Controls.Add(btnShowWifiPassword);
            AlignButtonsLeft(grpNetwork, btnNetworkInfo, btnShowWifiPassword);

            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.grpSystemFix);
            this.Controls.Add(this.grpActivation);
            this.Controls.Add(this.grpTools);
            this.Controls.Add(this.grpSysAdmin);
            this.Controls.Add(this.grpNetwork);

            this.ResumeLayout(false);
        }

        private void SetupGroupBox(GroupBox grp, string text, int x, int y, int width, int height)
        {
            grp.Location = new Point(x, y);
            grp.Size = new Size(width, height);
            grp.Text = text;
            grp.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            grp.ForeColor = Color.FromArgb(50, 55, 65);
        }

        private void SetButtonProps(Button btn, string text, EventHandler clickEvent)
        {
            btn.Size = new Size(BTN_WIDTH, BTN_HEIGHT);
            btn.Text = text;
            btn.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(35, 35, 40);
            btn.Cursor = Cursors.Hand;
            btn.Click += clickEvent;
        }

        private void AlignButtonsLeft(GroupBox parent, params Button[] buttons)
        {
            int currentX = LEFT_MARGIN;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Location = new Point(currentX, 18);
                currentX += BTN_WIDTH + BTN_GAP;
            }
        }

        // ---------- 系统信息初始化 ----------
        private void InitializeSystemInfo()
        {
            buildNumber = GetWindowsBuildNumber();
            systemVersion = GetOSName(buildNumber);
            isWin11 = (buildNumber >= 22000);
            isClassicMenuEnabled = CheckClassicMenuEnabled();

            WriteLog("==========================================================");
            WriteLog(string.Format(" 操作系统: {0} (Build {1})", systemVersion, buildNumber));
            WriteLog(string.Format(" 管理员权限: {0}", IsAdmin() ? "Yes" : "No"));
            WriteLog(string.Format(" 版本: {0}", VERSION));
            WriteLog("==========================================================");

            UpdateUI();
        }

        private int GetWindowsBuildNumber()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(BUILD_KEY_PATH))
                {
                    if (key != null)
                    {
                        object buildVal = key.GetValue("CurrentBuildNumber") ?? key.GetValue("CurrentBuild");
                        if (buildVal != null)
                        {
                            int build;
                            if (int.TryParse(buildVal.ToString(), out build))
                                return build;
                        }
                    }
                }
            }
            catch { }
            return Environment.OSVersion.Version.Build;
        }

        private string GetOSName(int build)
        {
            if (build < 9200) return "Windows 7 / Server 2008 R2";
            if (build < 10240) return "Windows 8 / 8.1";
            if (build < 22000) return "Windows 10";
            if (build >= 26100) return "Windows 11 24H2+";
            if (build >= 22000) return "Windows 11";
            return "未知系统";
        }

        private bool IsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private bool CheckClassicMenuEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CLASSIC_MENU_KEY_PATH))
                {
                    return key != null;
                }
            }
            catch { return false; }
        }

        // ---------- 日志方法 ----------
        private void WriteLog(string text)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(WriteLog), text);
                return;
            }

            try
            {
                text = text.TrimStart('\r', '\n');

                int closeBracket = text.IndexOf(']');
                if (text.StartsWith("[") && closeBracket > 0)
                {
                    string tag = text.Substring(0, closeBracket + 1);
                    string content = text.Substring(closeBracket + 1);

                    Color tagColor = Color.FromArgb(100, 181, 246); // 默认蓝色
                    if (tag.Length >= 2)
                    {
                        if (tag[1] == '×') tagColor = Color.FromArgb(255, 99, 99);
                        else if (tag[1] == '√') tagColor = Color.FromArgb(87, 219, 131);
                        else if (tag[1] == '!') tagColor = Color.FromArgb(255, 193, 7);
                    }

                    this.txtLog.SelectionStart = this.txtLog.TextLength;
                    this.txtLog.SelectionLength = 0;
                    this.txtLog.SelectionColor = tagColor;
                    this.txtLog.SelectionFont = _logTagFont;
                    this.txtLog.AppendText(tag);

                    this.txtLog.SelectionStart = this.txtLog.TextLength;
                    this.txtLog.SelectionLength = 0;
                    this.txtLog.SelectionColor = Color.FromArgb(230, 235, 240);
                    this.txtLog.SelectionFont = _logContentFont;
                    this.txtLog.AppendText(content + "\n");
                }
                else
                {
                    this.txtLog.SelectionStart = this.txtLog.TextLength;
                    this.txtLog.SelectionLength = 0;
                    this.txtLog.SelectionColor = Color.FromArgb(200, 205, 210);
                    this.txtLog.SelectionFont = _logMutedFont;
                    this.txtLog.AppendText(text + "\n");
                }

                this.txtLog.ScrollToCaret();
            }
            catch (ObjectDisposedException)
            {
                // 窗体已释放，忽略日志写入
            }
        }

        // ---------- 按钮启用控制 ----------
        private void SetButtonsEnabled(bool enabled)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(SetButtonsEnabled), enabled);
                return;
            }

            try
            {
                Action<Control.ControlCollection> toggleButtons = null;
                toggleButtons = (controls) =>
                {
                    foreach (Control c in controls)
                    {
                        Button btn = c as Button;
                        if (btn != null)
                        {
                            if (btn == btnRestoreClassicMenu && (!isWin11 || isClassicMenuEnabled))
                            {
                                btn.Enabled = false;
                                btn.Cursor = Cursors.Default;
                                continue;
                            }

                            btn.Enabled = enabled;
                            btn.Cursor = enabled ? Cursors.Hand : Cursors.Default;
                        }
                        if (c.HasChildren)
                            toggleButtons(c.Controls);
                    }
                };

                toggleButtons(this.Controls);
            }
            catch (ObjectDisposedException)
            {
                // 窗体已释放，忽略
            }
        }

        private void UpdateUI()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUI));
                return;
            }

            try
            {
                if (!isWin11 || isClassicMenuEnabled)
                {
                    this.btnRestoreClassicMenu.Enabled = false;
                    this.btnRestoreClassicMenu.Cursor = Cursors.Default;
                }
            }
            catch (ObjectDisposedException)
            {
                // 窗体已释放，忽略
            }
        }

        // ---------- 通用系统命令执行方法 ----------
        /// <summary>
        /// 启动一个系统命令（如 .msc、.cpl 等），自动处理日志和异常。
        /// </summary>
        private void RunSystemCommand(string command, string arguments, string actionDescription, string successMessage, string errorMessage)
        {
            try
            {
                WriteLog(string.Format("[>] 正在{0}...", actionDescription));
                using (Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = true }))
                {
                    // 释放 Process 对象，子进程继续运行
                }
                WriteLog(string.Format("[√] {0}", successMessage));
            }
            catch (Exception ex)
            {
                WriteLog(string.Format("[×] {0}: {1}", errorMessage, ex.Message));
            }
        }

        // ---------- 运行嵌入工具 ----------
        private void RunEmbeddedTool(string fileName)
        {
            Task.Run(() =>
            {
                Interlocked.Increment(ref _activeTasks);
                try
                {
                    SetButtonsEnabled(false);

                    string tempRoot = Path.Combine(Application.StartupPath, "temp");
                    string targetDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(targetDir);

                    string targetFilePath = Path.Combine(targetDir, fileName);

                    WriteLog(string.Format("[>] 正在释放工具组件: {0} 到 {1}...", fileName, targetDir));

                    bool extracted = TryExtractResource(fileName, targetFilePath);
                    if (!extracted)
                    {
                        WriteLog(string.Format("[×] 找不到资源文件: {0}", fileName));
                        return;
                    }

                    bool isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                    if (isZip)
                    {
                        WriteLog(string.Format("[>] 正在解压至: {0}...", targetDir));
                        ZipFile.ExtractToDirectory(targetFilePath, targetDir);
                        File.Delete(targetFilePath);
                        WriteLog(string.Format("[√] 已解压至: {0}", targetDir));
                        using (Process.Start("explorer.exe", targetDir)) { }
                    }
                    else if (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = string.Format("/k \"\"{0}\"\"", targetFilePath),
                            UseShellExecute = true,
                            WorkingDirectory = targetDir
                        };
                        using (Process p = Process.Start(psi))
                        {
                            if (p != null)
                                WriteLog(string.Format("[√] 已启动脚本: {0} (PID: {1})", fileName, p.Id));
                        }
                    }
                    else
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = targetFilePath,
                            UseShellExecute = true,
                            WorkingDirectory = targetDir
                        };
                        using (Process p = Process.Start(psi))
                        {
                            if (p != null)
                                WriteLog(string.Format("[√] 已启动程序: {0} (PID: {1})", fileName, p.Id));
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog(string.Format("[×] 执行失败: {0}", ex.Message));
                }
                finally
                {
                    int remaining = Interlocked.Decrement(ref _activeTasks);
                    if (remaining == 0)
                        SetButtonsEnabled(true);
                }
            });
        }

        private bool TryExtractResource(string resourceFileName, string outputPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            string resourceName = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName != null)
            {
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                    return true;
                }
            }

            string localPath = Path.Combine(Application.StartupPath, resourceFileName);
            if (File.Exists(localPath))
            {
                File.Copy(localPath, outputPath, true);
                return true;
            }

            return false;
        }

        // ---------- 事件处理 ----------
        private void BtnRestoreClassicMenu_Click(object sender, EventArgs e)
        {
            if (!isWin11)
            {
                WriteLog("[>] Win7/Win10 默认已是经典右键菜单，无需修复。");
                return;
            }

            if (isClassicMenuEnabled)
            {
                WriteLog("[>] 经典右键菜单已启用，无需重复操作。");
                return;
            }

            if (MessageBox.Show("确定要恢复经典右键菜单吗？\n\n此操作将修改注册表并自动重启资源管理器。", "确认提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                WriteLog("[>] 正在还原经典右键菜单...");
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CLASSIC_MENU_KEY_PATH))
                {
                    if (key != null) key.SetValue("", "");
                }

                NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                RestartExplorer();

                isClassicMenuEnabled = true;
                UpdateUI();
                WriteLog("[√] 经典右键菜单恢复成功！");
            }
            catch (Exception ex)
            {
                WriteLog("[×] 修改注册表失败: " + ex.Message);
            }
        }

        private void BtnDisableMousePrecision_Click(object sender, EventArgs e)
        {
            IntPtr pointer = IntPtr.Zero;
            try
            {
                WriteLog("[>] 正在关闭鼠标精准定位 (禁用鼠标加速)...");

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MOUSE_REGISTRY_PATH, true))
                {
                    if (key != null)
                    {
                        key.SetValue("MouseSpeed", "0");
                        key.SetValue("MouseThreshold1", "0");
                        key.SetValue("MouseThreshold2", "0");
                    }
                }

                int[] mouseParams = new int[3] { 0, 0, 0 };
                pointer = Marshal.AllocHGlobal(sizeof(int) * 3);
                Marshal.Copy(mouseParams, 0, pointer, 3);

                bool success = NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETMOUSE,
                    0,
                    pointer,
                    NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE
                );

                if (success)
                    WriteLog("[√] 鼠标精准定位已成功关闭并即时生效！");
                else
                    WriteLog("[!] 注册表已修改，但发送系统刷新指令时出现异常，重启后生效。");
            }
            catch (Exception ex)
            {
                WriteLog("[×] 关闭鼠标精准失败: " + ex.Message);
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pointer);
            }
        }

        private void BtnAutoLock_Click(object sender, EventArgs e)
        {
            if (!IsAdmin())
            {
                WriteLog("[×] 此功能需要管理员权限，请以管理员身份运行本程序。");
                return;
            }

            int seconds = ShowInputDialog("请输入息屏自动锁定的等待时间（秒）：", "息屏时间设置");
            if (seconds <= 0) return;

            try
            {
                WriteLog("[>] 正在配置息屏自动锁定...");

                // [1/6] 设置用户注册表 (HKCU)
                WriteLog("[>] [1/6] 设置用户注册表...");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (key != null)
                    {
                        key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                        key.SetValue("ScreenSaverIsSecure", "1", RegistryValueKind.String);
                        key.SetValue("ScreenSaveTimeOut", seconds.ToString(), RegistryValueKind.String);
                        key.SetValue("SCRNSAVE.EXE", "scrnsave.scr", RegistryValueKind.String);
                        WriteLog("[√] HKCU 注册表已设置");
                    }
                    else
                    {
                        WriteLog("[×] 无法打开 HKCU Control Panel\\Desktop");
                        return;
                    }
                }

                // [2/6] 设置计算机策略注册表 (HKLM)
                WriteLog("[>] [2/6] 设置计算机策略注册表...");
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Control Panel\Desktop", true))
                {
                    if (key != null)
                    {
                        key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                        key.SetValue("ScreenSaverIsSecure", "1", RegistryValueKind.String);
                        key.SetValue("ScreenSaveTimeOut", seconds.ToString(), RegistryValueKind.String);
                        WriteLog("[√] HKLM 策略注册表已设置");
                    }
                    else
                    {
                        WriteLog("[!] 无法打开 HKLM 策略注册表项，尝试创建...");
                        using (RegistryKey newKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Control Panel\Desktop"))
                        {
                            if (newKey != null)
                            {
                                newKey.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                                newKey.SetValue("ScreenSaverIsSecure", "1", RegistryValueKind.String);
                                newKey.SetValue("ScreenSaveTimeOut", seconds.ToString(), RegistryValueKind.String);
                                WriteLog("[√] HKLM 策略注册表已创建并设置");
                            }
                        }
                    }
                }

                // [3/6] 设置系统安全策略
                WriteLog("[>] [3/6] 设置系统安全策略...");
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true))
                {
                    if (key != null)
                    {
                        key.SetValue("InactivityTimeoutSecs", seconds, RegistryValueKind.DWord);
                        WriteLog("[√] 系统安全策略已设置");
                    }
                    else
                    {
                        WriteLog("[!] 无法打开系统安全策略注册表项");
                    }
                }

                // [4/6] 刷新组策略
                WriteLog("[>] [4/6] 刷新组策略...");
                try
                {
                    using (Process p = Process.Start(new ProcessStartInfo("gpupdate", "/force")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }))
                    {
                        if (p != null) p.WaitForExit();
                        WriteLog("[√] 组策略已刷新");
                    }
                }
                catch
                {
                    WriteLog("[!] 刷新组策略失败");
                }

                // [5/6] 设置电源选项
                WriteLog("[>] [5/6] 设置电源选项...");
                try
                {
                    int monitorMinutes = Math.Max(1, seconds / 60);
                    RunPowerCfg(string.Format("-change -monitor-timeout-ac {0}", monitorMinutes));
                    RunPowerCfg(string.Format("-change -monitor-timeout-dc {0}", monitorMinutes));
                    RunPowerCfg("-setacvalueindex SCHEME_CURRENT sub_none CONSOLELOCK 1");
                    RunPowerCfg("-setactive SCHEME_CURRENT");
                    WriteLog(string.Format("[√] 电源选项已设置（屏幕 {0} 分钟关闭）", monitorMinutes));
                }
                catch (Exception ex)
                {
                    WriteLog("[!] 设置电源选项时出错: " + ex.Message);
                }

                // [6/6] 通过 SystemParametersInfo 通知系统
                WriteLog("[>] [6/6] 通知系统刷新设置...");
                bool enable = true;
                NativeMethods.SystemParametersInfo(0x0011, 1, ref enable, 0x0002);
                uint timeout = (uint)seconds;
                NativeMethods.SystemParametersInfo(0x0015, timeout, ref timeout, 0x0002);

                WriteLog("==========================================================");
                WriteLog(string.Format("[√] 息屏自动锁定配置完成！{0} 秒无操作后自动锁屏。", seconds));
                WriteLog("[>] 建议注销或重启计算机使设置完全生效。");
                WriteLog("==========================================================");
            }
            catch (Exception ex)
            {
                WriteLog("[×] 配置息屏自动锁定失败: " + ex.Message);
            }
        }

        private void RunPowerCfg(string arguments)
        {
            using (Process p = Process.Start(new ProcessStartInfo("powercfg", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                if (p != null) p.WaitForExit();
            }
        }

        private int ShowInputDialog(string prompt, string title)
        {
            using (Form form = new Form())
            {
                Label label = new Label();
                TextBox textBox = new TextBox();
                Button buttonOk = new Button();
                Button buttonCancel = new Button();

                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new Size(320, 130);

                label.Text = prompt;
                label.Location = new Point(12, 12);
                label.AutoSize = true;

                textBox.Text = "60";
                textBox.Location = new Point(12, 36);
                textBox.Size = new Size(280, 23);

                buttonOk.Text = "确定";
                buttonOk.Location = new Point(135, 68);
                buttonOk.Size = new Size(75, 25);
                buttonOk.DialogResult = DialogResult.OK;

                buttonCancel.Text = "取消";
                buttonCancel.Location = new Point(220, 68);
                buttonCancel.Size = new Size(75, 25);
                buttonCancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(buttonOk);
                form.Controls.Add(buttonCancel);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    int val;
                    if (int.TryParse(textBox.Text, out val) && val > 0)
                        return val;
                    MessageBox.Show("请输入大于 0 的有效秒数。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return -1;
                }
                return -1;
            }
        }

        private void RestartExplorer()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                Thread.Sleep(300);
                using (Process.Start("explorer.exe")) { }
            }
            catch (Exception ex)
            {
                WriteLog("[!] 重启资源管理器失败: " + ex.Message);
            }
        }

        private void BtnActivateOnline_Click(object sender, EventArgs e)
        {
            try
            {
                WriteLog("[>] 正在请求在线激活服务...");
                string script = string.Format("[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; irm {0} | iex", ACTIVATION_SCRIPT_URL);

                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", string.Format("-NoExit -Command \"{0}\"", script))
                {
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                using (Process.Start(psi)) { }
                WriteLog("[√] 已调起在线激活 PowerShell 脚本。");
            }
            catch (Exception ex)
            {
                WriteLog("[×] 启动激活脚本失败: " + ex.Message);
            }
        }

        private void BtnActivateLocal_Click(object sender, EventArgs e)
        {
            RunEmbeddedTool("MAS.cmd");
        }

        // ---------- 查询激活状态 ----------
        private void BtnCheckActivation_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                Interlocked.Increment(ref _activeTasks);
                try
                {
                    SetButtonsEnabled(false);

                    string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string slmgrPath = Path.Combine(systemDir, "slmgr.vbs");
                    if (!File.Exists(slmgrPath))
                    {
                        WriteLog("[×] 找不到 slmgr.vbs，请检查系统完整性。");
                        return;
                    }

                    string output = RunSlmgr(slmgrPath, "/xpr");
                    foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                        WriteLog(line);
                }
                catch (Exception ex)
                {
                    WriteLog(string.Format("[×] 查询激活状态失败: {0}", ex.Message));
                }
                finally
                {
                    int remaining = Interlocked.Decrement(ref _activeTasks);
                    if (remaining == 0)
                        SetButtonsEnabled(true);
                }
            });
        }

        // ---------- 辅助方法 ----------
        private string RunSlmgr(string slmgrPath, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cscript.exe",
                Arguments = string.Format("//nologo \"{0}\" {1}", slmgrPath, arguments),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (Process p = Process.Start(psi))
            {
                // 先等待进程退出，再读取输出，避免死锁
                p.WaitForExit();
                return p.StandardOutput.ReadToEnd();
            }
        }

        // ---------- 其他已有功能 ----------
        private void BtnTaskbarNeverCombine_Click(object sender, EventArgs e)
        {
            try
            {
                WriteLog("[>] 正在调起系统设置页面...");
                if (buildNumber < 10240)
                {
                    using (Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,Options_RunDLL 1") { UseShellExecute = true })) { }
                }
                else
                {
                    using (Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true })) { }
                }
                WriteLog("[√] 已调起系统任务栏设置。");
            }
            catch (Exception ex)
            {
                WriteLog("[×] 打开失败: " + ex.Message);
            }
        }

        private void BtnDesktopShowIcons_Click(object sender, EventArgs e)
        {
            RunSystemCommand("control", "desk.cpl,,@0,3", "调起桌面图标设置", "已打开桌面图标设置界面。", "打开失败");
        }

        private void BtnControlPanel_Click(object sender, EventArgs e)
        {
            try
            {
                WriteLog("[>] 正在调起控制面板...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}",
                    UseShellExecute = true
                };
                using (Process.Start(psi)) { }
                WriteLog("[√] 已打开控制面板（所有控制面板项）。");
            }
            catch (Exception ex)
            {
                WriteLog("[×] Shell方式调起失败: " + ex.Message);
                try
                {
                    using (Process.Start(new ProcessStartInfo("control.exe") { UseShellExecute = true })) { }
                    WriteLog("[√] 通过备用指令打开控制面板。");
                }
                catch (Exception innerEx)
                {
                    WriteLog("[×] 备用方案启动失败: " + innerEx.Message);
                }
            }
        }

        private void BtnSecpol_Click(object sender, EventArgs e)
        {
            RunSystemCommand("secpol.msc", "", "打开本地安全策略", "已打开本地安全策略。", "打开本地安全策略失败 (注: 家庭版系统无此策略组件)");
        }

        private void BtnServices_Click(object sender, EventArgs e)
        {
            RunSystemCommand("services.msc", "", "打开系统服务管理", "已打开系统服务管理。", "打开系统服务失败");
        }

        private void BtnRestartExplorer_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                Interlocked.Increment(ref _activeTasks);
                try
                {
                    SetButtonsEnabled(false);
                    WriteLog("[>] 正在重启资源管理器...");

                    int currentSessionId = Process.GetCurrentProcess().SessionId;
                    foreach (Process p in Process.GetProcessesByName("explorer"))
                    {
                        if (p.SessionId == currentSessionId)
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                    }

                    Thread.Sleep(1000);
                    using (Process.Start("explorer.exe")) { }
                    WriteLog("[√] 资源管理器已重启");
                }
                catch (Exception ex)
                {
                    WriteLog(string.Format("[×] 重启失败: {0}", ex.Message));
                }
                finally
                {
                    int remaining = Interlocked.Decrement(ref _activeTasks);
                    if (remaining == 0)
                        SetButtonsEnabled(true);
                }
            });
        }

        private void BtnNetworkInfo_Click(object sender, EventArgs e)
        {
            WriteLog("-------------------- 网络适配器详细信息 --------------------");
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                if (interfaces.Count == 0)
                {
                    WriteLog("[!] 当前系统未检测到活动的网络连接。");
                    return;
                }

                foreach (NetworkInterface ni in interfaces)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();

                    var ipv4Addresses = ipProps.UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(u => u.Address.ToString())
                        .ToList();

                    if (ipv4Addresses.Count == 0) continue;

                    var gateways = ipProps.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.ToString())
                        .ToList();

                    string rawMac = ni.GetPhysicalAddress().ToString();
                    string formattedMac = string.Join("-", Enumerable.Range(0, rawMac.Length / 2)
                        .Select(i => rawMac.Substring(i * 2, 2)));

                    WriteLog(string.Format("网卡名称: {0} ({1})", ni.Name, ni.Description));
                    WriteLog(string.Format("  IPv4 地址 : {0}", string.Join(", ", ipv4Addresses)));
                    WriteLog(string.Format("  默认网关 : {0}", gateways.Count > 0 ? string.Join(", ", gateways) : "无"));
                    WriteLog(string.Format("  MAC 地址 : {0}", string.IsNullOrEmpty(formattedMac) ? "未知" : formattedMac));
                    WriteLog("----------------------------------------------------------");
                }
            }
            catch (Exception ex)
            {
                WriteLog("[×] 获取网络信息失败: " + ex.Message);
            }
        }

        private void BtnShowWifiPassword_Click(object sender, EventArgs e)
        {
            try
            {
                WriteLog("[>] 正在查询本机已保存的 WiFi 密码...");
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "wlan show profiles")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default
                };

                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();

                        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        var profiles = lines
                            .Where(l => l.Contains(":") && (l.Contains("所有用户配置文件") || l.Contains("All User Profile")))
                            .Select(l =>
                            {
                                int colonIndex = l.IndexOf(':');
                                return (colonIndex >= 0 && colonIndex < l.Length - 1) ? l.Substring(colonIndex + 1).Trim() : string.Empty;
                            })
                            .Where(name => !string.IsNullOrEmpty(name))
                            .ToList();

                        if (profiles.Count == 0)
                        {
                            WriteLog("[!] 未找到任何连接过的 WiFi 配置文件。");
                            return;
                        }

                        foreach (string profile in profiles)
                        {
                            ProcessStartInfo pwdPsi = new ProcessStartInfo("netsh", string.Format("wlan show profile name=\"{0}\" key=clear", profile))
                            {
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                StandardOutputEncoding = Encoding.Default
                            };

                            using (Process pwdP = Process.Start(pwdPsi))
                            {
                                if (pwdP != null)
                                {
                                    string pwdOutput = pwdP.StandardOutput.ReadToEnd();
                                    pwdP.WaitForExit();

                                    string password = "无密码 / 未保存";
                                    var pwdLines = pwdOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var line in pwdLines)
                                    {
                                        if (line.Contains("关键内容") || line.Contains("Key Content"))
                                        {
                                            int colonIndex = line.IndexOf(':');
                                            if (colonIndex >= 0 && colonIndex < line.Length - 1)
                                            {
                                                password = line.Substring(colonIndex + 1).Trim();
                                            }
                                            break;
                                        }
                                    }

                                    WriteLog(string.Format("WiFi 名称: {0} | 密码: {1}", profile, password));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("[×] 查询 WiFi 密码失败: " + ex.Message);
            }
        }

        // ---------- 窗体关闭清理 ----------
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            string tempPath = Path.Combine(Application.StartupPath, "temp");
            if (Directory.Exists(tempPath))
                CleanDirectorySafely(tempPath);
        }

        private void CleanDirectorySafely(string path)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(path);

                foreach (FileInfo file in dir.GetFiles())
                {
                    try { file.Delete(); }
                    catch { }
                }

                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    CleanDirectorySafely(subDir.FullName);
                    try
                    {
                        if (!subDir.EnumerateFileSystemInfos().Any())
                            subDir.Delete();
                    }
                    catch { }
                }

                if (!dir.EnumerateFileSystemInfos().Any())
                    dir.Delete();
            }
            catch { }
        }
    }
}
