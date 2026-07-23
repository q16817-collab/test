using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinShareFixer
{
    // ==========================================
    // 启动入口点类
    // ==========================================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            Application.Run(new MainForm());
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show("程序发生未处理异常：\r\n" + e.Exception.Message, "运行错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            string message = ex == null ? Convert.ToString(e.ExceptionObject) : ex.Message;
            MessageBox.Show("程序发生未处理异常：\r\n" + message, "运行错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==========================================
    // 主窗体逻辑类
    // ==========================================
    public class MainForm : Form
    {
        private Button btnGuestFix;
        private Button btnSmbFix;
        private Button btnPrinterFix;
        private Button btnPauseUpdate;
        private RichTextBox txtLog;
        private Label lblStatus;

        // 字体缓存（优化：避免频繁创建Font对象）
        // 改为实例字段，避免多窗体实例间共享导致资源冲突或已释放异常
        private Font _fontBold10;
        private Font _fontBold95;

        public MainForm()
        {
            // 初始化字体缓存
            if (_fontBold10 == null)
                _fontBold10 = new Font("Microsoft YaHei", 10F, FontStyle.Bold);
            if (_fontBold95 == null)
                _fontBold95 = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold);

            InitializeComponent();
            
            // 优化：提前检查管理员权限
            if (!IsAdmin())
            {
                LogSystemInformation();
                MessageBox.Show(
                    "本程序需要管理员权限才能运行。\r\n\r\n请右键选择\"以管理员身份运行\"。",
                    "权限不足",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                // 禁用所有按钮
                this.btnGuestFix.Enabled = false;
                this.btnSmbFix.Enabled = false;
                this.btnPrinterFix.Enabled = false;
                this.btnPauseUpdate.Enabled = false;
            }
            else
            {
                LogSystemInformation();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontBold10 != null)
                {
                    _fontBold10.Dispose();
                    _fontBold10 = null;
                }
                if (_fontBold95 != null)
                {
                    _fontBold95.Dispose();
                    _fontBold95 = null;
                }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnGuestFix = new Button();
            this.btnSmbFix = new Button();
            this.btnPrinterFix = new Button();
            this.btnPauseUpdate = new Button();
            this.txtLog = new RichTextBox();
            this.lblStatus = new Label();
            this.SuspendLayout();

            // 主窗口设置
            this.Text = "Windows 局域网共享与系统维护工具 (企业增强版)";
            this.Size = new Size(820, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // 状态标签
            this.lblStatus.Location = new Point(20, 15);
            this.lblStatus.Size = new Size(760, 25);
            this.lblStatus.Text = "状态：准备就绪，请选择下方修复模式或维护功能。";
            this.lblStatus.Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold);
            this.lblStatus.ForeColor = Color.FromArgb(30, 30, 30);

            // 日志文本框
            this.txtLog.Location = new Point(20, 45);
            this.txtLog.Size = new Size(760, 370);
            this.txtLog.ReadOnly = true;
            this.txtLog.BackColor = Color.FromArgb(20, 22, 25);
            this.txtLog.ForeColor = Color.FromArgb(220, 220, 220);
            this.txtLog.Font = new Font("Consolas", 9.5F, FontStyle.Regular);

            // 按钮设置
            this.btnGuestFix.Location = new Point(20, 435);
            this.btnGuestFix.Size = new Size(175, 50);
            this.btnGuestFix.Text = "Guest 修复";
            this.btnGuestFix.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            this.btnGuestFix.UseVisualStyleBackColor = true;
            this.btnGuestFix.Click += new EventHandler(this.BtnGuestFix_Click);

            this.btnSmbFix.Location = new Point(215, 435);
            this.btnSmbFix.Size = new Size(175, 50);
            this.btnSmbFix.Text = "SMB 共享修复";
            this.btnSmbFix.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            this.btnSmbFix.UseVisualStyleBackColor = true;
            this.btnSmbFix.Click += new EventHandler(this.BtnSmbFix_Click);

            this.btnPrinterFix.Location = new Point(410, 435);
            this.btnPrinterFix.Size = new Size(175, 50);
            this.btnPrinterFix.Text = "打印机深度修复";
            this.btnPrinterFix.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            this.btnPrinterFix.UseVisualStyleBackColor = true;
            this.btnPrinterFix.Click += new EventHandler(this.BtnPrinterFix_Click);

            this.btnPauseUpdate.Location = new Point(605, 435);
            this.btnPauseUpdate.Size = new Size(175, 50);
            this.btnPauseUpdate.Text = "暂停 Windows 更新";
            this.btnPauseUpdate.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            this.btnPauseUpdate.UseVisualStyleBackColor = true;
            this.btnPauseUpdate.Click += new EventHandler(this.BtnPauseUpdate_Click);

            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.btnGuestFix);
            this.Controls.Add(this.btnSmbFix);
            this.Controls.Add(this.btnPrinterFix);
            this.Controls.Add(this.btnPauseUpdate);
            this.ResumeLayout(false);
        }

        #region 系统识别与环境诊断
        private int GetWindowsBuildNumber()
        {
            try
            {
                using (RegistryKey key = OpenLocalMachineSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object buildVal = key.GetValue("CurrentBuildNumber");
                        if (buildVal == null)
                        {
                            buildVal = key.GetValue("CurrentBuild");
                        }

                        int build;
                        if (buildVal != null && int.TryParse(buildVal.ToString(), out build))
                        {
                            return build;
                        }
                    }
                }
            }
            catch { }
            return Environment.OSVersion.Version.Build;
        }

        private bool IsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private void LogSystemInformation()
        {
            int build = GetWindowsBuildNumber();
            string osName = "Unknown OS";

            if (build < 9200) osName = "Windows 7 / Server 2008 R2";
            else if (build < 10240) osName = "Windows 8 / 8.1";
            else if (build < 22000) osName = "Windows 10";
            else if (build >= 26100) osName = "Windows 11 24H2+";
            else if (build >= 22000) osName = "Windows 11";

            string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            bool isAdmin = IsAdmin();

            Log("==================================================");
            Log(" 操作系统: " + osName + " (Build " + build.ToString() + ") " + arch);
            Log(" 运行权限: Administrator: " + (isAdmin ? "YES" : "NO"));
            Log("==================================================\n");
        }
        #endregion

        #region 日志输出与精准高亮
        private void Log(string text)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(Log), text);
                return;
            }

            int startLen = this.txtLog.TextLength;
            this.txtLog.AppendText(text + "\n");
            int endLen = this.txtLog.TextLength;

            HighlightRange(startLen, endLen - startLen);

            this.txtLog.SelectionStart = this.txtLog.TextLength;
            this.txtLog.ScrollToCaret();
        }

        private void HighlightRange(int lineStart, int length)
        {
            if (lineStart < 0 || lineStart >= this.txtLog.TextLength || length <= 0) return;
            if (lineStart + length > this.txtLog.TextLength)
            {
                length = this.txtLog.TextLength - lineStart;
            }

            string text = this.txtLog.Text.Substring(lineStart, length);

            if (text.Contains("========"))
            {
                this.txtLog.Select(lineStart, length);
                this.txtLog.SelectionColor = Color.FromArgb(120, 140, 160);
                return;
            }

            if (Regex.IsMatch(text, @"^\[\d+/\d+\]"))
            {
                this.txtLog.Select(lineStart, length);
                this.txtLog.SelectionFont = _fontBold10;
                this.txtLog.SelectionColor = Color.FromArgb(255, 255, 255);
                return;
            }

            ApplyKeywordColor(lineStart, text, "[成功]", Color.FromArgb(0, 255, 127), true);
            ApplyKeywordColor(lineStart, text, "[ OK ]", Color.FromArgb(0, 255, 127), true);
            ApplyKeywordColor(lineStart, text, "[错误]", Color.FromArgb(255, 60, 60), true);
            ApplyKeywordColor(lineStart, text, "[ ERROR ]", Color.FromArgb(255, 60, 60), true);
            ApplyKeywordColor(lineStart, text, "[警告]", Color.FromArgb(255, 180, 0), true);
            ApplyKeywordColor(lineStart, text, "[提示]", Color.FromArgb(0, 191, 255), true);
        }

        private void ApplyKeywordColor(int lineStart, string lineText, string keyword, Color color, bool isBold)
        {
            int index = lineText.IndexOf(keyword);
            if (index >= 0)
            {
                this.txtLog.Select(lineStart + index, keyword.Length);
                this.txtLog.SelectionColor = color;
                if (isBold)
                {
                    // 使用缓存的字体，避免频繁创建
                    this.txtLog.SelectionFont = _fontBold95;
                }
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(SetButtonsEnabled), enabled);
                return;
            }
            this.btnGuestFix.Enabled = enabled;
            this.btnSmbFix.Enabled = enabled;
            this.btnPrinterFix.Enabled = enabled;
            this.btnPauseUpdate.Enabled = enabled;
        }

        private void SetStatusText(string text)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(SetStatusText), text);
                return;
            }
            this.lblStatus.Text = text;
        }
        #endregion

        #region 事件处理
        private void BtnGuestFix_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            this.lblStatus.Text = "状态：正在执行 Guest 免密共享修复...";
            this.txtLog.Clear();
            LogSystemInformation();

            // 优化：添加超时保护
            System.Threading.Timer timeoutTimer = null;
            timeoutTimer = new System.Threading.Timer(delegate(object state)
            {
                Log("\n        [警告] 操作超时（超过 10 分钟），已强制停止。");
                SetStatusText("状态：操作超时，请重试。");
                SetButtonsEnabled(true);
                if (timeoutTimer != null) timeoutTimer.Dispose();
            }, null, 600000, System.Threading.Timeout.Infinite);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    RunGuestFixLogic();
                    SetStatusText("状态：Guest 修复完成！建议重启电脑。");
                }
                catch (Exception ex)
                {
                    Log("\n        [错误] Guest 修复过程异常: " + ex.Message);
                    SetStatusText("状态：Guest 修复失败，请查看日志。");
                }
                finally
                {
                    if (timeoutTimer != null) timeoutTimer.Dispose();
                    SetButtonsEnabled(true);
                }
            });
        }

        private void BtnSmbFix_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            this.lblStatus.Text = "状态：正在执行 SMB 核心共享修复与诊断...";
            this.txtLog.Clear();
            LogSystemInformation();

            System.Threading.Timer timeoutTimer = null;
            timeoutTimer = new System.Threading.Timer(delegate(object state)
            {
                Log("\n        [警告] 操作超时（超过 10 分钟），已强制停止。");
                SetStatusText("状态：操作超时，请重试。");
                SetButtonsEnabled(true);
                if (timeoutTimer != null) timeoutTimer.Dispose();
            }, null, 600000, System.Threading.Timeout.Infinite);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    RunSmbFixLogic();
                    SetStatusText("状态：SMB 共享配置与诊断就绪！");
                }
                catch (Exception ex)
                {
                    Log("\n        [错误] SMB 修复过程异常: " + ex.Message);
                    SetStatusText("状态：SMB 修复失败，请查看日志。");
                }
                finally
                {
                    if (timeoutTimer != null) timeoutTimer.Dispose();
                    SetButtonsEnabled(true);
                }
            });
        }

        private void BtnPrinterFix_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            this.lblStatus.Text = "状态：正在深度修复打印服务与连接...";
            this.txtLog.Clear();
            LogSystemInformation();

            System.Threading.Timer timeoutTimer = null;
            timeoutTimer = new System.Threading.Timer(delegate(object state)
            {
                Log("\n        [警告] 操作超时（超过 10 分钟），已强制停止。");
                SetStatusText("状态：操作超时，请重试。");
                SetButtonsEnabled(true);
                if (timeoutTimer != null) timeoutTimer.Dispose();
            }, null, 600000, System.Threading.Timeout.Infinite);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    RunPrinterFixLogic();
                    SetStatusText("状态：打印服务与策略修复完毕！建议重启电脑。");
                }
                catch (Exception ex)
                {
                    Log("\n        [错误] 打印机修复过程异常: " + ex.Message);
                    SetStatusText("状态：打印机修复失败，请查看日志。");
                }
                finally
                {
                    if (timeoutTimer != null) timeoutTimer.Dispose();
                    SetButtonsEnabled(true);
                }
            });
        }

        private void BtnPauseUpdate_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            this.lblStatus.Text = "状态：正在配置 Windows 自动更新暂停策略...";
            this.txtLog.Clear();
            LogSystemInformation();

            System.Threading.Timer timeoutTimer = null;
            timeoutTimer = new System.Threading.Timer(delegate(object state)
            {
                Log("\n        [警告] 操作超时（超过 5 分钟），已强制停止。");
                SetStatusText("状态：操作超时，请重试。");
                SetButtonsEnabled(true);
                if (timeoutTimer != null) timeoutTimer.Dispose();
            }, null, 300000, System.Threading.Timeout.Infinite);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    RunPauseUpdateLogic();
                    SetStatusText("状态：Windows 自动更新策略配置完毕！");
                }
                catch (Exception ex)
                {
                    Log("\n        [错误] 暂停更新过程异常: " + ex.Message);
                    SetStatusText("状态：Windows 更新策略配置失败，请查看日志。");
                }
                finally
                {
                    if (timeoutTimer != null) timeoutTimer.Dispose();
                    SetButtonsEnabled(true);
                }
            });
        }
        #endregion

        #region 1. Guest 修复逻辑
        private void RunGuestFixLogic()
        {
            Log("==================================================");
            Log("        正在配置 Guest 免密共享与系统安全策略...");
            Log("==================================================\n");

            RunQuietProcess("net", "user Guest /active:yes");
            FixRegistryPoliciesApi();
            Log("        [成功] 匿名共享注册表策略已通过 API 修复。");

            RemoveGuestFromDenyLogon();

            int build = GetWindowsBuildNumber();
            if (build < 9200)
            {
                StartServiceApi("Browser", "Computer Browser 服务");
            }

            StartServiceApi("LanmanServer", "Server 服务");
            StartServiceApi("LanmanWorkstation", "Workstation 服务");
            StartServiceApi("fdPHost", "Function Discovery Provider Host 服务");
            StartServiceApi("FDResPub", "Function Discovery Resource Publication 服务");
            StartServiceApi("Dnscache", "DNS Client 服务");
            StartServiceApi("lmhosts", "TCP/IP NetBIOS Helper 服务");

            EnableFirewallRules();
            RefreshNetworkCache();

            Log("\n==================================================");
            Log("              [ OK ] Guest 免密共享修复完成！");
            Log("==================================================");
        }
        #endregion

        #region 2. SMB 修复逻辑
        private void RunSmbFixLogic()
        {
            Log("==================================================");
            Log("      正在配置并修复局域网 SMB 共享服务...");
            Log("==================================================\n");

            int build = GetWindowsBuildNumber();

            Log("[1/3] 检测与配置 SMB1 协议组件...");
            if (build < 9200)
            {
                Log("        [提示] 检测到 Windows 7 系统，默认开启 SMBv1。");
            }
            else
            {
                string checkCmd = "Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol | Select-Object -ExpandProperty State";
                string checkResult = RunPowerShellCommand(checkCmd);

                if (string.IsNullOrEmpty(checkResult) || checkResult.Contains("Unknown"))
                {
                    Log("        [提示] 当前 Windows 系统已彻底移除 SMB1Protocol 功能项，自动跳过安装。");
                }
                else if (checkResult.Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    Log("        [成功] 检测到 SMB1Protocol 已经是 Enabled 激活状态。");
                }
                else
                {
                    Log("        正在启用 SMB1Protocol...（CBS 事务执行中）");
                    RunQuietProcessWithTimeout("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart -ErrorAction SilentlyContinue\"", 20000);

                    Log("        正在等待 CBS 写入并校验 SMB1 激活状态...");
                    bool isEnabled = false;
                    for (int i = 0; i < 15; i++)
                    {
                        Thread.Sleep(2000);
                        string status = RunPowerShellCommand(checkCmd).Trim();
                        if (status.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            isEnabled = true;
                            break;
                        }
                    }

                    if (isEnabled)
                        Log("        [成功] SMB1 协议已成功激活并确认生效。");
                    else
                        Log("        [警告] SMB1 协议已提交开启请求，后台 CBS 尚在处理中或需要重启电脑生效。");
                }
            }

            FixRegistryPoliciesApi();
            Log("        [成功] HKLM 核心共享注册表项修改完成。");

            StartServiceApi("LanmanServer", "Server (LanmanServer)");
            StartServiceApi("LanmanWorkstation", "Workstation (LanmanWorkstation)");

            Log("\n[2/3] 配置防火墙规则，放行文件与打印机共享通道...");
            EnableFirewallRules();

            Log("\n[3/3] 补全启动网络发现核心服务并刷新缓存...");
            StartServiceApi("fdPHost", "fdPHost 服务");
            StartServiceApi("FDResPub", "FDResPub 服务");
            StartServiceApi("SSDPSRV", "SSDPSRV 服务");
            StartServiceApi("upnphost", "upnphost 服务");
            StartServiceApi("lmhosts", "lmhosts 服务");

            RefreshNetworkCache();

            Log("\n==================================================");
            Log("                    检测结果");
            Log("==================================================");
            Log("  Server............." + GetServiceStatusDisplay("LanmanServer"));
            Log("  Workstation........" + GetServiceStatusDisplay("LanmanWorkstation"));
            Log("  FDResPub..........." + GetServiceStatusDisplay("FDResPub"));
            Log("  Guest 匿名授权......" + GetGuestAuthStatusDisplay());
            Log("==================================================");
        }
        #endregion

        #region 3. 打印机修复逻辑
        private void RunPrinterFixLogic()
        {
            Log("==================================================");
            Log("     正在全面修复局域网打印与 Print Spooler 服务...");
            Log("==================================================\n");

            Log("[1/6] 修复跨系统版本安全更新连接报错 (0x0000011b / 0x00000709 等)...");
            FixPrinterRegistryApi();
            Log("        [成功] 注册表打印 RPC 与免密驱动安装策略修复完成。");

            Log("\n[2/6] 优化网络访问模式与网络共享策略...");
            FixLsaPoliciesApi();
            Log("        [成功] LSA 网络访问策略优化完成。");

            Log("\n[3/6] 正在检查并激活 Print Spooler 核心依赖服务 (RPC/DCOM/Workstation)...");
            StartServiceApi("RpcSs", "RPC Core (RpcSs)");
            StartServiceApi("DcomLaunch", "DCOM Process Launcher");
            StartServiceApi("RpcEptMapper", "RPC Endpoint Mapper");
            StartServiceApi("LanmanWorkstation", "Workstation 服务");

            Log("\n[4/6] 安全停止打印服务并清理积压打印任务缓存...");
            StopSpoolerServiceApi();
            CleanSpoolPrintersDirectoryApi();

            Log("\n[5/6] 重新启动 Print Spooler 与 PrintNotify / 网络解析服务...");
            StartServiceApi("PrintNotify", "PrintNotify 打印通知服务");
            StartServiceApi("lmhosts", "lmhosts 服务");
            StartServiceApi("FDResPub", "FDResPub 服务");
            StartServiceApi("Spooler", "Print Spooler 打印后台服务");
            RefreshNetworkCache();

            Log("\n[6/6] 最终状态判定与诊断...");
            bool isSpoolerRunning = IsServiceRunning("Spooler");

            if (isSpoolerRunning)
            {
                Log("        [成功] Print Spooler 运行状态正常 [ RUNNING ]！");
                Log("\n==================================================");
                Log("        [ OK ] 打印服务全套修复完毕！");
                Log("  提示：");
                Log("  1. 卡死的打印任务已安全强制清空。");
                Log("  2. 【重要】部分 RPC 注册表修改需要重启电脑后方可完全生效！");
                Log("  3. 若仍然提示错误，请将【客户端】和【服务端主机】均重启一遍。");
                Log("==================================================");
            }
            else
            {
                Log("        [错误] 打印服务 (Spooler) 启动失败或异常终止！");
                Log("\n==================================================");
                Log("          [ ERROR ] 打印后台服务启动失败");
                Log("  请按 Win+R 输入 services.msc 手动尝试启动 Print Spooler，检查依赖项。");
                Log("==================================================");
            }
        }
        #endregion

        #region 4. 暂停更新逻辑
        private void RunPauseUpdateLogic()
        {
            Log("==================================================");
            Log("      正在配置 Windows 更新暂停策略...");
            Log("==================================================\n");

            int build = GetWindowsBuildNumber();

            try
            {
                if (build < 9200)
                {
                    Log("[1/2] 检测到 Windows 7 系统，直接关闭 Windows Update 服务...");
                    RunQuietProcess("sc", "config wuauserv start= disabled");
                    RunQuietProcess("net", "stop wuauserv");
                    Log("        [成功] Win7 Windows Update 服务已停止并禁用。");
                }
                else
                {
                    Log("[1/3] 配置 UpdatePolicy Settings...");
                    using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings"))
                    {
                        if (key != null)
                        {
                            key.SetValue("PausedFeatureStatus", 0, RegistryValueKind.DWord);
                            key.SetValue("PausedQualityStatus", 0, RegistryValueKind.DWord);
                            Log("        [成功] 标记功能更新与质量更新为暂停状态。");
                        }
                    }

                    Log("\n[2/3] 写入 UX Settings 暂停时间线至 2050 年...");
                    using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"))
                    {
                        if (key != null)
                        {
                            key.SetValue("FlightSettingsMaxPauseDays", 10000, RegistryValueKind.DWord);
                            key.SetValue("PauseFeatureUpdatesStartTime", "2023-07-07T10:00:52Z", RegistryValueKind.String);
                            key.SetValue("PauseFeatureUpdatesEndTime", "2050-09-05T09:59:52Z", RegistryValueKind.String);
                            key.SetValue("PauseQualityUpdatesStartTime", "2023-07-07T10:00:52Z", RegistryValueKind.String);
                            key.SetValue("PauseQualityUpdatesEndTime", "2050-09-05T09:59:52Z", RegistryValueKind.String);
                            key.SetValue("PauseUpdatesStartTime", "2023-07-07T09:59:52Z", RegistryValueKind.String);
                            key.SetValue("PauseUpdatesExpiryTime", "2050-09-05T09:59:52Z", RegistryValueKind.String);
                            Log("        [成功] UX Settings 暂停时间线写入完成。");
                        }
                    }
                }

                Log("\n[策略加固] 配置 AU 组策略级别关闭规则...");
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"))
                {
                    if (key != null)
                    {
                        key.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        key.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                        Log("        [成功] 组策略防反弹参数设置完成。");
                    }
                }

                Log("\n==================================================");
                Log("        [ OK ] Windows 自动更新策略已生效！");
                Log("==================================================");
            }
            catch (Exception ex)
            {
                Log("\n        [错误] 写入注册表异常: " + ex.Message);
            }
        }
        #endregion

        #region 底层工具与 API 函数
        private RegistryKey OpenLocalMachineSubKey(string subKeyName)
        {
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    return baseKey.OpenSubKey(subKeyName);
                }
            }
            catch
            {
                try { return Registry.LocalMachine.OpenSubKey(subKeyName); } catch { return null; }
            }
        }

        private RegistryKey CreateLocalMachineSubKey(string subKeyName)
        {
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    return baseKey.CreateSubKey(subKeyName);
                }
            }
            catch
            {
                try { return Registry.LocalMachine.CreateSubKey(subKeyName); } catch { return null; }
            }
        }

        private int RunQuietProcess(string fileName, string arguments)
        {
            return RunQuietProcessWithTimeout(fileName, arguments, 10000);
        }

        // 移除未使用的 waitForExit 参数，简化方法签名
        private int RunQuietProcessWithTimeout(string fileName, string arguments, int timeoutMilliseconds)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        if (p.WaitForExit(timeoutMilliseconds))
                        {
                            return p.ExitCode;
                        }
                        else
                        {
                            try { p.Kill(); } catch { }
                            return -1;
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        // 修复标准输出/错误重定向可能导致的死锁问题
        // 原代码先调用 WaitForExit 再读取输出，当输出超过系统缓冲区大小时会触发死锁。
        // 现改用 BeginOutputReadLine / BeginErrorReadLine 进行异步读取。
        private string RunPowerShellCommand(string cmd)
        {
            try
            {
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        StringBuilder outputBuilder = new StringBuilder();
                        StringBuilder errorBuilder = new StringBuilder();

                        p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                        {
                            if (args.Data != null)
                            {
                                lock (outputBuilder)
                                {
                                    if (outputBuilder.Length > 0) outputBuilder.Append("\n");
                                    outputBuilder.Append(args.Data);
                                }
                            }
                        };
                        p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                        {
                            if (args.Data != null)
                            {
                                lock (errorBuilder)
                                {
                                    if (errorBuilder.Length > 0) errorBuilder.Append("\n");
                                    errorBuilder.Append(args.Data);
                                }
                            }
                        };

                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();

                        if (!p.WaitForExit(10000))
                        {
                            try { p.Kill(); } catch { }
                            return string.Empty;
                        }

                        string output = outputBuilder.ToString().Trim();
                        string error = errorBuilder.ToString().Trim();

                        if (!string.IsNullOrEmpty(error) && !error.Contains("Microsoft.PowerShell") && !error.Contains("Warning"))
                        {
                            Log("        [调试] PowerShell 错误: " + error.Substring(0, Math.Min(100, error.Length)));
                        }

                        return string.IsNullOrEmpty(output) ? error : output;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("        [调试] PowerShell 执行异常: " + ex.Message);
            }
            return string.Empty;
        }

        private void StartServiceApi(string serviceName, string displayName)
        {
            try
            {
                RunQuietProcess("sc", "config \"" + serviceName + "\" start= auto");
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        try
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                            Log("        [成功] " + displayName + " 正常运行。");
                        }
                        catch (System.ServiceProcess.TimeoutException)
                        {
                            Log("        [警告] " + displayName + " 启动超时，但已发送启动命令，可能需要重启电脑生效。");
                        }
                    }
                    else
                    {
                        Log("        [成功] " + displayName + " 已在运行中。");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                Log("        [警告] " + displayName + " 不存在或无法访问，跳过。");
            }
            catch (Exception ex)
            {
                Log("        [警告] " + displayName + " 操作失败: " + ex.Message);
            }
        }

        private void StopSpoolerServiceApi()
        {
            try
            {
                using (ServiceController sc = new ServiceController("Spooler"))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                        Log("        [成功] Print Spooler 服务已安全停止。");
                    }
                }
            }
            catch
            {
                RunQuietProcess("net", "stop Spooler /y");
            }
            Thread.Sleep(1000);
        }

        private void RemoveGuestFromDenyLogon()
        {
            int build = GetWindowsBuildNumber();

            if (build < 9200)
            {
                Log("        [提示] Windows 7 系统检测到，使用注册表直接修复方案...");
                try
                {
                    using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                    {
                        if (key != null)
                        {
                            key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                            Log("        [成功] LSA 策略已配置，Guest 访问权限已放开。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("        [警告] 注册表修复异常: " + ex.Message);
                }
                return;
            }

            string tempId = Guid.NewGuid().ToString();
            string cfgPath = Path.Combine(Path.GetTempPath(), "sec_policy_" + tempId + ".cfg");
            string sdbPath = Path.Combine(Path.GetTempPath(), "sec_sdb_" + tempId + ".sdb");

            try
            {
                if (File.Exists(cfgPath)) File.Delete(cfgPath);
                if (File.Exists(sdbPath)) File.Delete(sdbPath);

                int exportCode = RunQuietProcessWithTimeout("secedit", "/export /cfg \"" + cfgPath + "\"", 15000);
                
                if (exportCode != 0)
                {
                    Log("        [警告] secedit 导出失败，错误码: " + exportCode);
                    if (exportCode == -1)
                        Log("              原因：超时或进程被杀死");
                    else if (exportCode == 2)
                        Log("              原因：文件访问被拒绝（权限问题）");
                    else if (exportCode == 5)
                        Log("              原因：访问被拒绝，可能需要更高权限");
                    Log("              已跳过 Guest 拒绝策略清理。");
                    return;
                }

                if (!File.Exists(cfgPath))
                {
                    Log("        [警告] secedit 导出的文件不存在，可能被杀毒软件拦截。");
                    return;
                }

                string content = File.ReadAllText(cfgPath, Encoding.Unicode);
                string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                bool changed = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("SeDenyNetworkLogonRight", StringComparison.OrdinalIgnoreCase))
                    {
                        string updated = RemoveGuestFromPolicyLine(lines[i]);
                        if (!string.Equals(lines[i], updated, StringComparison.Ordinal))
                        {
                            lines[i] = updated;
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    Log("        [提示] 本地安全策略中未发现 Guest 网络拒绝拦截。");
                    return;
                }

                File.WriteAllText(cfgPath, string.Join(Environment.NewLine, lines), Encoding.Unicode);
                int configureCode = RunQuietProcessWithTimeout("secedit", "/configure /db \"" + sdbPath + "\" /cfg \"" + cfgPath + "\" /areas USER_RIGHTS /overwrite", 15000);
                
                if (configureCode == 0)
                    Log("        [成功] 本地安全策略中 Guest 网络拒绝拦截已清除！");
                else
                {
                    Log("        [警告] secedit 配置返回错误码: " + configureCode);
                    Log("              但策略文件已修改，可能需要重启电脑生效。");
                }
            }
            catch (Exception ex)
            {
                Log("        [警告] 清理 Guest 拒绝策略异常: " + ex.Message);
            }
            finally
            {
                try { if (File.Exists(cfgPath)) File.Delete(cfgPath); } catch { }
                try { if (File.Exists(sdbPath)) File.Delete(sdbPath); } catch { }
            }
        }

        private string RemoveGuestFromPolicyLine(string line)
        {
            int equalIndex = line.IndexOf('=');
            if (equalIndex < 0) return line;

            string left = line.Substring(0, equalIndex).Trim();
            string right = line.Substring(equalIndex + 1).Trim();
            if (right.Length == 0) return left + " = ";

            string[] parts = right.Split(',');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                if (IsGuestAccountToken(part)) continue;

                if (sb.Length > 0) sb.Append(",");
                sb.Append(part);
            }

            return left + " = " + sb.ToString();
        }

        private bool IsGuestAccountToken(string token)
        {
            if (string.Equals(token, "Guest", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "*S-1-5-32-546", StringComparison.OrdinalIgnoreCase)) return true;
            return Regex.IsMatch(token, @"^\*S-1-5-21-\d+-\d+-\d+-501$", RegexOptions.IgnoreCase);
        }

        private void EnableFirewallRules()
        {
            int build = GetWindowsBuildNumber();

            if (build >= 10240)
            {
                RunQuietProcess("powershell", "-NoProfile -Command \"Enable-NetFirewallRule -DisplayGroup '文件和打印机共享','网络发现' -ErrorAction SilentlyContinue\"");
                RunQuietProcess("powershell", "-NoProfile -Command \"Enable-NetFirewallRule -DisplayGroup 'File and Printer Sharing','Network Discovery' -ErrorAction SilentlyContinue\"");
            }

            RunQuietProcess("netsh", "advfirewall firewall set rule group=\"文件和打印机共享\" new enable=Yes");
            RunQuietProcess("netsh", "advfirewall firewall set rule group=\"网络发现\" new enable=Yes");
            RunQuietProcess("netsh", "advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes");
            RunQuietProcess("netsh", "advfirewall firewall set rule group=\"Network Discovery\" new enable=Yes");

            Log("        [成功] 防火墙共享与网络发现规则已正常放行。");
        }

        private void RefreshNetworkCache()
        {
            Log("        正在刷新 DNS 与 NetBIOS 网络缓存...");
            RunQuietProcess("ipconfig", "/flushdns");
            RunQuietProcess("nbtstat", "-R");
            RunQuietProcess("nbtstat", "-RR");
            Log("        [成功] 网络缓存刷新完成。");
        }

        private void FixRegistryPoliciesApi()
        {
            try
            {
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation"))
                {
                    if (key != null) key.SetValue("AllowInsecureGuestAuth", 1, RegistryValueKind.DWord);
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                {
                    if (key != null) key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters"))
                {
                    if (key != null) key.SetValue("RestrictNullSessAccess", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Log("        [警告] API 写入注册表失败: " + ex.Message);
            }
        }

        private void FixPrinterRegistryApi()
        {
            try
            {
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Print"))
                {
                    if (key != null) key.SetValue("RpcAuthnLevelPrivacyEnabled", 0, RegistryValueKind.DWord);
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\RPC"))
                {
                    if (key != null)
                    {
                        key.SetValue("RpcUseNamedPipeProtocol", 1, RegistryValueKind.DWord);
                        key.SetValue("RpcProtocols", 7, RegistryValueKind.DWord);
                    }
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint"))
                {
                    if (key != null)
                    {
                        key.SetValue("RestrictDriverInstallationToAdministrators", 0, RegistryValueKind.DWord);
                        key.SetValue("NoWarningNoElevationOnInstall", 1, RegistryValueKind.DWord);
                        key.SetValue("UpdatePromptSettings", 2, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("        [警告] 打印机注册表写入异常: " + ex.Message);
            }
        }

        private void FixLsaPoliciesApi()
        {
            try
            {
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                {
                    if (key != null)
                    {
                        key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                        key.SetValue("forceguest", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private void CleanSpoolPrintersDirectoryApi()
        {
            try
            {
                string spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\PRINTERS");
                if (Directory.Exists(spoolPath))
                {
                    string[] files = Directory.GetFiles(spoolPath);
                    int count = 0;
                    foreach (string file in files)
                    {
                        try { File.Delete(file); count++; } catch { }
                    }
                    Log("        [成功] 打印临时队列文件夹已清空（共清除 " + count + " 个积压任务文件）。");
                }
            }
            catch { }
        }

        private bool IsServiceRunning(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch { return false; }
        }

        private string GetServiceStatusDisplay(string serviceName)
        {
            return IsServiceRunning(serviceName) ? "[成功] 正常" : "[错误] 停止";
        }

        private string GetGuestAuthStatusDisplay()
        {
            try
            {
                using (RegistryKey key = OpenLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AllowInsecureGuestAuth");
                        if (val is int && (int)val == 1) return "[成功] 允许";
                    }
                }
            }
            catch { }
            return "[警告] 阻止";
        }
        #endregion
    }
}
