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
        // [修正] 改为实例字段，避免多窗体实例间共享导致资源冲突或已释放异常
        private Font _fontBold10;
        private Font _fontBold95;
        private readonly object _operationLock = new object();
        private CancellationTokenSource _currentOperationCts;
        private System.Threading.Timer _currentTimeoutTimer;
        private volatile bool _isClosing;

        private sealed class ProcessExecutionResult
        {
            public bool Started;
            public bool TimedOut;
            public bool Cancelled;
            public int ExitCode = -1;
            public string Output = string.Empty;
            public string Error = string.Empty;
        }

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
                CancelCurrentOperation();

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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CancelCurrentOperation();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _isClosing = true;
            base.OnFormClosed(e);
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
        private void TryBeginInvoke(Action action)
        {
            if (_isClosing || this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                this.BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
            catch (ObjectDisposedException) { }
        }

        private void ResetLogView()
        {
            if (_isClosing || this.txtLog == null || this.txtLog.IsDisposed) return;

            if (this.InvokeRequired)
            {
                TryBeginInvoke(new Action(ResetLogView));
                return;
            }

            this.txtLog.Clear();
        }

        private void Log(string text)
        {
            if (_isClosing || this.txtLog == null || this.txtLog.IsDisposed) return;

            if (this.InvokeRequired)
            {
                TryBeginInvoke(delegate { Log(text); });
                return;
            }

            if (_isClosing || this.txtLog == null || this.txtLog.IsDisposed) return;

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
            if (_isClosing) return;

            if (this.InvokeRequired)
            {
                TryBeginInvoke(delegate { SetButtonsEnabled(enabled); });
                return;
            }
            this.btnGuestFix.Enabled = enabled;
            this.btnSmbFix.Enabled = enabled;
            this.btnPrinterFix.Enabled = enabled;
            this.btnPauseUpdate.Enabled = enabled;
        }

        private void SetStatusText(string text)
        {
            if (_isClosing || this.lblStatus == null || this.lblStatus.IsDisposed) return;

            if (this.InvokeRequired)
            {
                TryBeginInvoke(delegate { SetStatusText(text); });
                return;
            }
            this.lblStatus.Text = text;
        }

        private void CancelCurrentOperation()
        {
            CancellationTokenSource cts = null;
            System.Threading.Timer timer = null;

            lock (_operationLock)
            {
                cts = _currentOperationCts;
                timer = _currentTimeoutTimer;
                _currentOperationCts = null;
                _currentTimeoutTimer = null;
            }

            if (timer != null)
            {
                try { timer.Dispose(); } catch { }
            }

            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }
        }

        private void FinishOperation(CancellationTokenSource cts)
        {
            System.Threading.Timer timerToDispose = null;

            lock (_operationLock)
            {
                if (ReferenceEquals(_currentOperationCts, cts))
                {
                    timerToDispose = _currentTimeoutTimer;
                    _currentTimeoutTimer = null;
                    _currentOperationCts = null;
                }
            }

            if (timerToDispose != null)
            {
                try { timerToDispose.Dispose(); } catch { }
            }

            if (cts != null)
            {
                try { cts.Dispose(); } catch { }
            }

            if (!_isClosing)
            {
                SetButtonsEnabled(true);
            }
        }

        private string FormatTimeoutText(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds % 60000 == 0)
            {
                return (timeoutMilliseconds / 60000).ToString() + " 分钟";
            }

            return (timeoutMilliseconds / 1000).ToString() + " 秒";
        }

        private void StartBackgroundOperation(
            string operationName,
            string runningStatus,
            string successStatus,
            string failureStatus,
            int timeoutMilliseconds,
            Action<CancellationToken> action)
        {
            SetButtonsEnabled(false);
            SetStatusText(runningStatus);
            ResetLogView();
            LogSystemInformation();

            CancellationTokenSource cts = new CancellationTokenSource();
            int timeoutTriggered = 0;

            lock (_operationLock)
            {
                _currentOperationCts = cts;
                _currentTimeoutTimer = new System.Threading.Timer(delegate(object state)
                {
                    if (Interlocked.Exchange(ref timeoutTriggered, 1) == 0)
                    {
                        Log("\n        [警告] 操作超时（超过 " + FormatTimeoutText(timeoutMilliseconds) + "），正在取消后台任务...");
                        SetStatusText("状态：正在取消超时任务，请稍候...");
                        try { cts.Cancel(); } catch { }
                    }
                }, null, timeoutMilliseconds, System.Threading.Timeout.Infinite);
            }

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    action(cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    SetStatusText(successStatus);
                }
                catch (OperationCanceledException)
                {
                    if (!_isClosing)
                    {
                        if (Interlocked.CompareExchange(ref timeoutTriggered, 0, 0) == 1)
                            SetStatusText("状态：操作超时，后台任务已取消。");
                        else
                            SetStatusText("状态：操作已取消。");
                    }
                }
                catch (Exception ex)
                {
                    Log("\n        [错误] " + operationName + "过程异常: " + ex.Message);
                    SetStatusText(failureStatus);
                }
                finally
                {
                    FinishOperation(cts);
                }
            });
        }
        #endregion

        #region 事件处理
        private void BtnGuestFix_Click(object sender, EventArgs e)
        {
            StartBackgroundOperation(
                "Guest 修复",
                "状态：正在执行 Guest 免密共享修复...",
                "状态：Guest 修复完成！建议重启电脑。",
                "状态：Guest 修复失败，请查看日志。",
                600000,
                RunGuestFixLogic);
        }

        private void BtnSmbFix_Click(object sender, EventArgs e)
        {
            StartBackgroundOperation(
                "SMB 修复",
                "状态：正在执行 SMB 核心共享修复与诊断...",
                "状态：SMB 共享配置与诊断就绪！",
                "状态：SMB 修复失败，请查看日志。",
                600000,
                RunSmbFixLogic);
        }

        private void BtnPrinterFix_Click(object sender, EventArgs e)
        {
            StartBackgroundOperation(
                "打印机修复",
                "状态：正在深度修复打印服务与连接...",
                "状态：打印服务与策略修复完毕！建议重启电脑。",
                "状态：打印机修复失败，请查看日志。",
                600000,
                RunPrinterFixLogic);
        }

        private void BtnPauseUpdate_Click(object sender, EventArgs e)
        {
            StartBackgroundOperation(
                "暂停更新",
                "状态：正在配置 Windows 自动更新暂停策略...",
                "状态：Windows 自动更新策略配置完毕！",
                "状态：Windows 更新策略配置失败，请查看日志。",
                300000,
                RunPauseUpdateLogic);
        }
        #endregion

        #region 1. Guest 修复逻辑
        private void RunGuestFixLogic(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log("==================================================");
            Log("        正在配置 Guest 免密共享与系统安全策略...");
            Log("==================================================\n");

            int guestCode = RunQuietProcess("net", "user Guest /active:yes", token);
            if (guestCode == 0)
                Log("        [成功] Guest 账户启用命令执行完成。");
            else
                Log("        [警告] Guest 账户启用命令返回码: " + guestCode);

            bool registryFixed = FixRegistryPoliciesApi(token);
            if (registryFixed)
                Log("        [成功] 匿名共享注册表策略已通过 API 修复。");
            else
                Log("        [警告] 匿名共享注册表策略未完全写入成功，请查看前序日志。");

            token.ThrowIfCancellationRequested();
            RemoveGuestFromDenyLogon(token);

            int build = GetWindowsBuildNumber();
            if (build < 9200)
            {
                StartServiceApi("Browser", "Computer Browser 服务", token);
            }

            StartServiceApi("LanmanServer", "Server 服务", token);
            StartServiceApi("LanmanWorkstation", "Workstation 服务", token);
            StartServiceApi("fdPHost", "Function Discovery Provider Host 服务", token);
            StartServiceApi("FDResPub", "Function Discovery Resource Publication 服务", token);
            StartServiceApi("Dnscache", "DNS Client 服务", token);
            StartServiceApi("lmhosts", "TCP/IP NetBIOS Helper 服务", token);

            EnableFirewallRules(token);
            RefreshNetworkCache(token);

            Log("\n==================================================");
            Log("              [ OK ] Guest 免密共享修复完成！");
            Log("==================================================");
        }
        #endregion

        #region 2. SMB 修复逻辑
        private void RunSmbFixLogic(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
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
                bool checkSucceeded;
                string checkResult = RunPowerShellCommand(checkCmd, token, out checkSucceeded);

                if (!checkSucceeded)
                {
                    Log("        [警告] 无法可靠检测 SMB1Protocol 状态，已跳过自动启用以避免误判。");
                }
                else if (string.IsNullOrEmpty(checkResult) || checkResult.Contains("Unknown"))
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
                    int enableCode = RunQuietProcessWithTimeout("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart -ErrorAction SilentlyContinue\"", 20000, token);

                    if (enableCode != 0 && enableCode != 3010)
                    {
                        Log("        [警告] 启用 SMB1Protocol 命令返回码: " + enableCode);
                    }
                    else
                    {
                        Log("        正在等待 CBS 写入并校验 SMB1 激活状态...");
                        bool isEnabled = false;
                        for (int i = 0; i < 15; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(2000);

                            bool recheckSucceeded;
                            string status = RunPowerShellCommand(checkCmd, token, out recheckSucceeded).Trim();
                            if (!recheckSucceeded)
                            {
                                Log("        [警告] SMB1 激活状态复检失败，可能需要重启后再确认。");
                                break;
                            }

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
            }

            bool registryFixed = FixRegistryPoliciesApi(token);
            if (registryFixed)
                Log("        [成功] HKLM 核心共享注册表项修改完成。");
            else
                Log("        [警告] HKLM 核心共享注册表项未完全写入成功。");

            StartServiceApi("LanmanServer", "Server (LanmanServer)", token);
            StartServiceApi("LanmanWorkstation", "Workstation (LanmanWorkstation)", token);

            Log("\n[2/3] 配置防火墙规则，放行文件与打印机共享通道...");
            EnableFirewallRules(token);

            Log("\n[3/3] 补全启动网络发现核心服务并刷新缓存...");
            StartServiceApi("fdPHost", "fdPHost 服务", token);
            StartServiceApi("FDResPub", "FDResPub 服务", token);
            StartServiceApi("SSDPSRV", "SSDPSRV 服务", token);
            StartServiceApi("upnphost", "upnphost 服务", token);
            StartServiceApi("lmhosts", "lmhosts 服务", token);

            RefreshNetworkCache(token);

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
        private void RunPrinterFixLogic(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log("==================================================");
            Log("     正在全面修复局域网打印与 Print Spooler 服务...");
            Log("==================================================\n");

            Log("[1/6] 修复跨系统版本安全更新连接报错 (0x0000011b / 0x00000709 等)...");
            bool printerRegistryFixed = FixPrinterRegistryApi(token);
            if (printerRegistryFixed)
                Log("        [成功] 注册表打印 RPC 与免密驱动安装策略修复完成。");
            else
                Log("        [警告] 打印 RPC 或驱动安装策略未完全写入成功。");

            Log("\n[2/6] 优化网络访问模式与网络共享策略...");
            bool lsaFixed = FixLsaPoliciesApi(token);
            if (lsaFixed)
                Log("        [成功] LSA 网络访问策略优化完成。");
            else
                Log("        [警告] LSA 网络访问策略未完全写入成功。");

            Log("\n[3/6] 正在检查并激活 Print Spooler 核心依赖服务 (RPC/DCOM/Workstation)...");
            StartServiceApi("RpcSs", "RPC Core (RpcSs)", token);
            StartServiceApi("DcomLaunch", "DCOM Process Launcher", token);
            StartServiceApi("RpcEptMapper", "RPC Endpoint Mapper", token);
            StartServiceApi("LanmanWorkstation", "Workstation 服务", token);

            Log("\n[4/6] 安全停止打印服务并清理积压打印任务缓存...");
            StopSpoolerServiceApi(token);
            CleanSpoolPrintersDirectoryApi(token);

            Log("\n[5/6] 重新启动 Print Spooler 与 PrintNotify / 网络解析服务...");
            StartServiceApi("PrintNotify", "PrintNotify 打印通知服务", token);
            StartServiceApi("lmhosts", "lmhosts 服务", token);
            StartServiceApi("FDResPub", "FDResPub 服务", token);
            StartServiceApi("Spooler", "Print Spooler 打印后台服务", token);
            RefreshNetworkCache(token);

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
        private void RunPauseUpdateLogic(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log("==================================================");
            Log("      正在配置 Windows 更新暂停策略...");
            Log("==================================================\n");

            int build = GetWindowsBuildNumber();

            try
            {
                if (build < 9200)
                {
                    Log("[1/2] 检测到 Windows 7 系统，直接关闭 Windows Update 服务...");
                    int configCode = RunQuietProcess("sc", "config wuauserv start= disabled", token);
                    int stopCode = RunQuietProcess("net", "stop wuauserv", token);
                    if (configCode == 0 && (stopCode == 0 || stopCode == 2))
                        Log("        [成功] Win7 Windows Update 服务已停止并禁用。");
                    else
                        Log("        [警告] Win7 Windows Update 服务处理返回码: config=" + configCode + ", stop=" + stopCode);
                }
                else
                {
                    token.ThrowIfCancellationRequested();
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

                    token.ThrowIfCancellationRequested();
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

                token.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException) { throw; }
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
            return RunQuietProcess(fileName, arguments, CancellationToken.None);
        }

        private int RunQuietProcess(string fileName, string arguments, CancellationToken token)
        {
            return RunQuietProcessWithTimeout(fileName, arguments, 10000, token);
        }

        // [修正] 移除未使用的 waitForExit 参数，简化方法签名
        private int RunQuietProcessWithTimeout(string fileName, string arguments, int timeoutMilliseconds)
        {
            return RunQuietProcessWithTimeout(fileName, arguments, timeoutMilliseconds, CancellationToken.None);
        }

        private int RunQuietProcessWithTimeout(string fileName, string arguments, int timeoutMilliseconds, CancellationToken token)
        {
            ProcessExecutionResult result = ExecuteProcess(fileName, arguments, timeoutMilliseconds, false, token);
            if (result.Cancelled) return -2;
            if (result.TimedOut) return -1;
            return result.ExitCode;
        }

        private ProcessExecutionResult ExecuteProcess(string fileName, string arguments, int timeoutMilliseconds, bool captureOutput, CancellationToken token)
        {
            ProcessExecutionResult result = new ProcessExecutionResult();
            Process p = null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = captureOutput,
                    RedirectStandardError = captureOutput,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        result.Error = "进程启动失败。";
                        return result;
                    }

                    result.Started = true;
                    StringBuilder outputBuilder = captureOutput ? new StringBuilder() : null;
                    StringBuilder errorBuilder = captureOutput ? new StringBuilder() : null;

                    if (captureOutput)
                    {
                        p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                        {
                            if (args.Data != null)
                            {
                                outputBuilder.AppendLine(args.Data);
                            }
                        };
                        p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                        {
                            if (args.Data != null)
                            {
                                errorBuilder.AppendLine(args.Data);
                            }
                        };

                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }

                    DateTime deadline = timeoutMilliseconds > 0
                        ? DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds)
                        : DateTime.MaxValue;

                    while (true)
                    {
                        token.ThrowIfCancellationRequested();

                        int waitSlice = 200;
                        if (deadline != DateTime.MaxValue)
                        {
                            double remaining = (deadline - DateTime.UtcNow).TotalMilliseconds;
                            if (remaining <= 0)
                            {
                                try { if (!p.HasExited) p.Kill(); } catch { }
                                result.TimedOut = true;
                                result.Error = "进程执行超时。";
                                return result;
                            }

                            waitSlice = (int)Math.Min(waitSlice, remaining);
                        }

                        if (p.WaitForExit(waitSlice))
                        {
                            break;
                        }
                    }

                    if (captureOutput)
                    {
                        p.WaitForExit();
                        result.Output = outputBuilder.ToString();
                        result.Error = errorBuilder.ToString();
                    }

                    result.ExitCode = p.ExitCode;
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                try { if (p != null && !p.HasExited) p.Kill(); } catch { }
                result.Cancelled = true;
                result.Error = "操作已取消。";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        private string RunPowerShellCommand(string cmd, CancellationToken token, out bool success)
        {
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
            ProcessExecutionResult result = ExecuteProcess(
                "powershell",
                "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                10000,
                true,
                token);

            if (result.Cancelled)
            {
                success = false;
                return string.Empty;
            }

            if (result.TimedOut)
            {
                Log("        [调试] PowerShell 执行超时。");
                success = false;
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(result.Error) && !result.Error.Contains("Microsoft.PowerShell") && !result.Error.Contains("Warning"))
            {
                Log("        [调试] PowerShell 错误: " + result.Error.Substring(0, Math.Min(100, result.Error.Length)));
            }

            if (result.ExitCode != 0)
            {
                success = false;
                return string.Empty;
            }

            success = true;
            return result.Output.Trim();
        }

        private void StartServiceApi(string serviceName, string displayName, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                RunQuietProcess("sc", "config \"" + serviceName + "\" start= auto", token);
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        try
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                            token.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log("        [警告] " + displayName + " 操作失败: " + ex.Message);
            }
        }

        private void StopSpoolerServiceApi(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException) { throw; }
            catch
            {
                RunQuietProcess("net", "stop Spooler /y", token);
            }
            token.ThrowIfCancellationRequested();
            Thread.Sleep(1000);
            token.ThrowIfCancellationRequested();
        }

        private void RemoveGuestFromDenyLogon(CancellationToken token)
        {
            int build = GetWindowsBuildNumber();

            if (build < 9200)
            {
                Log("        [提示] Windows 7 系统检测到，使用注册表直接修复方案...");
                try
                {
                    token.ThrowIfCancellationRequested();
                    using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                    {
                        if (key != null)
                        {
                            key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                            Log("        [成功] LSA 策略已配置，Guest 访问权限已放开。");
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
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
                token.ThrowIfCancellationRequested();
                if (File.Exists(cfgPath)) File.Delete(cfgPath);
                if (File.Exists(sdbPath)) File.Delete(sdbPath);

                int exportCode = RunQuietProcessWithTimeout("secedit", "/export /cfg \"" + cfgPath + "\"", 15000, token);
                
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
                    token.ThrowIfCancellationRequested();
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
                int configureCode = RunQuietProcessWithTimeout("secedit", "/configure /db \"" + sdbPath + "\" /cfg \"" + cfgPath + "\" /areas USER_RIGHTS /overwrite", 15000, token);
                
                if (configureCode == 0)
                    Log("        [成功] 本地安全策略中 Guest 网络拒绝拦截已清除！");
                else
                {
                    Log("        [警告] secedit 配置返回错误码: " + configureCode);
                    Log("              但策略文件已修改，可能需要重启电脑生效。");
                }
            }
            catch (OperationCanceledException) { throw; }
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

        private void EnableFirewallRules(CancellationToken token)
        {
            int build = GetWindowsBuildNumber();
            bool anySucceeded = false;

            if (build >= 10240)
            {
                if (RunQuietProcess("powershell", "-NoProfile -Command \"Enable-NetFirewallRule -DisplayGroup '文件和打印机共享','网络发现' -ErrorAction SilentlyContinue\"", token) == 0) anySucceeded = true;
                if (RunQuietProcess("powershell", "-NoProfile -Command \"Enable-NetFirewallRule -DisplayGroup 'File and Printer Sharing','Network Discovery' -ErrorAction SilentlyContinue\"", token) == 0) anySucceeded = true;
            }

            if (RunQuietProcess("netsh", "advfirewall firewall set rule group=\"文件和打印机共享\" new enable=Yes", token) == 0) anySucceeded = true;
            if (RunQuietProcess("netsh", "advfirewall firewall set rule group=\"网络发现\" new enable=Yes", token) == 0) anySucceeded = true;
            if (RunQuietProcess("netsh", "advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes", token) == 0) anySucceeded = true;
            if (RunQuietProcess("netsh", "advfirewall firewall set rule group=\"Network Discovery\" new enable=Yes", token) == 0) anySucceeded = true;

            if (anySucceeded)
                Log("        [成功] 防火墙共享与网络发现规则已正常放行。");
            else
                Log("        [警告] 防火墙规则启用命令未返回成功，请手动核查系统防火墙策略。");
        }

        private void RefreshNetworkCache(CancellationToken token)
        {
            Log("        正在刷新 DNS 与 NetBIOS 网络缓存...");
            int successCount = 0;
            if (RunQuietProcess("ipconfig", "/flushdns", token) == 0) successCount++;
            if (RunQuietProcess("nbtstat", "-R", token) == 0) successCount++;
            if (RunQuietProcess("nbtstat", "-RR", token) == 0) successCount++;

            if (successCount > 0)
                Log("        [成功] 网络缓存刷新完成。");
            else
                Log("        [警告] 网络缓存刷新命令未返回成功，请手动检查网络组件状态。");
        }

        private bool FixRegistryPoliciesApi(CancellationToken token)
        {
            bool success = true;
            try
            {
                token.ThrowIfCancellationRequested();
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation"))
                {
                    if (key != null) key.SetValue("AllowInsecureGuestAuth", 1, RegistryValueKind.DWord);
                    else success = false;
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                {
                    if (key != null) key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                    else success = false;
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters"))
                {
                    if (key != null) key.SetValue("RestrictNullSessAccess", 0, RegistryValueKind.DWord);
                    else success = false;
                }
                return success;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log("        [警告] API 写入注册表失败: " + ex.Message);
                return false;
            }
        }

        private bool FixPrinterRegistryApi(CancellationToken token)
        {
            bool success = true;
            try
            {
                token.ThrowIfCancellationRequested();
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Print"))
                {
                    if (key != null) key.SetValue("RpcAuthnLevelPrivacyEnabled", 0, RegistryValueKind.DWord);
                    else success = false;
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\RPC"))
                {
                    if (key != null)
                    {
                        key.SetValue("RpcUseNamedPipeProtocol", 1, RegistryValueKind.DWord);
                        key.SetValue("RpcProtocols", 7, RegistryValueKind.DWord);
                    }
                    else success = false;
                }
                using (RegistryKey key = CreateLocalMachineSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint"))
                {
                    if (key != null)
                    {
                        key.SetValue("RestrictDriverInstallationToAdministrators", 0, RegistryValueKind.DWord);
                        key.SetValue("NoWarningNoElevationOnInstall", 1, RegistryValueKind.DWord);
                        key.SetValue("UpdatePromptSettings", 2, RegistryValueKind.DWord);
                    }
                    else success = false;
                }
                return success;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log("        [警告] 打印机注册表写入异常: " + ex.Message);
                return false;
            }
        }

        private bool FixLsaPoliciesApi(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using (RegistryKey key = CreateLocalMachineSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
                {
                    if (key != null)
                    {
                        key.SetValue("LimitBlankPasswordUse", 0, RegistryValueKind.DWord);
                        key.SetValue("forceguest", 0, RegistryValueKind.DWord);
                        return true;
                    }
                }
                return false;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            return false;
        }

        private void CleanSpoolPrintersDirectoryApi(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                string spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\PRINTERS");
                if (Directory.Exists(spoolPath))
                {
                    string[] files = Directory.GetFiles(spoolPath);
                    int count = 0;
                    foreach (string file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        try { File.Delete(file); count++; } catch { }
                    }
                    Log("        [成功] 打印临时队列文件夹已清空（共清除 " + count + " 个积压任务文件）。");
                }
            }
            catch (OperationCanceledException) { throw; }
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
