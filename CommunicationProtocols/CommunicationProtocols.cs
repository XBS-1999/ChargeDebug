using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NationalInstruments.Visa;

#pragma warning disable
namespace CommunicationProtocols
{
    /// <summary>
    /// GVM-9102 USB TMC 通讯类（基于 NationalInstruments.Visa，需安装 NI-VISA 运行时）
    /// Connect 传 VISA 资源字符串（如 "USB0::0x2184::0x005A::序列号::INSTR"）；传 null 自动枚举并 *IDN? 识别。
    /// 前置：面板 USB-Protocol=USBTMC、已装 NI-VISA。
    /// </summary>
    public sealed class GwinstekGVM9102_TMCCommunicator : IDisposable
    {
        private static readonly Lazy<GwinstekGVM9102_TMCCommunicator> _instance =
            new Lazy<GwinstekGVM9102_TMCCommunicator>(() => new GwinstekGVM9102_TMCCommunicator());

        private ResourceManager _rmSession;      // ★ 必须为字段：using 块结束会销毁由其打开的所有会话
        private MessageBasedSession _mbSession;
        private bool _isConnected;
        private bool _disposed;
        private readonly object _syncRoot = new object();

        private const int QueryRetryCount = 3;

        private GwinstekGVM9102_TMCCommunicator() { }
        public static GwinstekGVM9102_TMCCommunicator Instance => _instance.Value;
        public Action<string> Log { get; set; }
        public bool IsConnected => _isConnected && _mbSession != null && _rmSession != null;

        /// <summary>返回当前连接的 VISA 资源字符串（便于排查/日志）</summary>
        public string CurrentResource => _mbSession?.ResourceName;

        #region 连接管理
        public bool Connect(string resourceString = null)
        {
            lock (_syncRoot)
            {
                try
                {
                    if (IsConnected) Disconnect();

                    _rmSession = new ResourceManager();   // ★ 不用 using
                    if (string.IsNullOrEmpty(resourceString))
                    {
                        resourceString = AutoDetectResource();
                        if (resourceString == null)
                            throw new Exception(
                                "未找到 GVM-9102 VISA 资源：检查面板 USB-Protocol=USBTMC、NI-VISA 已装、设备上电；" +
                                "或手动传入资源字符串（USB-TMC: USB0::0x2184::0x005A::SN::INSTR / LAN: TCPIP0::IP::3001::SOCKET）");
                    }

                    _mbSession = (MessageBasedSession)_rmSession.Open(resourceString);
                    _mbSession.TimeoutMilliseconds = 10000;
                    _mbSession.TerminationCharacter = (byte)'\n';   // ★ SOCKET/串口必须显式设置
                    _mbSession.TerminationCharacterEnabled = true;
                    _mbSession.Clear();

                    string idn = QueryWithRetry("*IDN?");
                    if (!idn.Contains("GVM9102") && !idn.Contains("GWInstek"))
                    {
                        Disconnect();
                        throw new Exception($"该资源不是固纬 GVM-9102，设备响应: {idn}");
                    }
                    _isConnected = true;
                    Log?.Invoke($"已连接 {resourceString}，设备: {idn}");
                    InitializeMeter();
                    return true;
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    DisposeSession();
                    Log?.Invoke($"连接失败: {ex.Message}");
                    throw new Exception($"连接失败: {ex.Message}（资源: {resourceString ?? "自动检测"}）");
                }
            }
        }

        /// <summary>自动检测：枚举 + 逐个 *IDN? 验证。TCPIP SOCKET 无法自动发现，LAN 必须手动传资源字符串</summary>
        private string AutoDetectResource()
        {
            string[] patterns = { "USB?*::INSTR", "GPIB?*::INSTR", "ASRL?*::INSTR" };
            foreach (string pattern in patterns)
            {
                IEnumerable<string> found;
                try { found = _rmSession.Find(pattern); }
                catch { continue; }
                foreach (string r in found)
                {
                    try
                    {
                        using (var probe = (MessageBasedSession)_rmSession.Open(r))
                        {
                            probe.TimeoutMilliseconds = 3000;
                            string eol = r.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase) ? "\r\n" : "\n";
                            probe.RawIO.Write("*IDN?" + eol);
                            string idn = probe.RawIO.ReadString().Trim();
                            if (idn.Contains("GVM9102") || idn.Contains("GWInstek"))
                                return r;
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        public void Disconnect()
        {
            lock (_syncRoot) { DisposeSession(); }
        }

        private void DisposeSession()
        {
            _mbSession?.Dispose();
            _mbSession = null;
            _rmSession?.Dispose();   // 最后释放管理器（连带其会话）
            _rmSession = null;
            _isConnected = false;
        }
        #endregion

        #region SCPI 基础
        private string LineEndForCurrentResource =>
            CurrentResource?.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase) == true ? "\r\n" : "\n";

        public void SendCommand(string command)
        {
            if (!IsConnected) throw new Exception("未连接到高压表");
            lock (_syncRoot)
            {
                try { _mbSession.RawIO.Write(command + LineEndForCurrentResource); }
                catch (Exception ex) { throw new Exception($"发送命令失败: {ex.Message}, 命令: {command}"); }
            }
        }

        public string Query(string query)
        {
            if (!IsConnected) throw new Exception("未连接到高压表");
            lock (_syncRoot)
            {
                try { return QueryWithRetry(query); }
                catch (Exception ex) { throw new Exception($"查询失败: {ex.Message}, 查询: {query}"); }
            }
        }

        private string QueryWithRetry(string query)
        {
            Exception lastEx = null;
            for (int i = 0; i < QueryRetryCount; i++)
            {
                try
                {
                    _mbSession.Clear();                       // 清残留，保证一问一答对齐
                    _mbSession.RawIO.Write(query + LineEndForCurrentResource);
                    return _mbSession.RawIO.ReadString().Trim();
                }
                catch (Exception ex) { lastEx = ex; Thread.Sleep(100); }
            }
            throw new Exception($"查询失败(重试{QueryRetryCount}次): {lastEx?.Message}");
        }
        #endregion

        #region 设备初始化
        private void InitializeMeter()
        {
            try
            {
                try { SendCommand("SYST:REM"); } catch { }
                SendCommand("*RST");
                Thread.Sleep(1500);
                SendCommand("*CLS");
                SendCommand(":FORMAT:DATA ASCII");
                SendCommand(":DISPLAY:MODE DIGITAL");
                SetRange(0);
                SetResolution("HIGH");
                SetSampleRate(5);
            }
            catch (Exception ex) { throw new Exception($"初始化高压表失败: {ex.Message}"); }
        }

        public void Reset()
        {
            SendCommand("*RST");
            Thread.Sleep(1500);
        }
        #endregion

        #region 测量配置
        public void SetSampleRate(int rate)
        {
            if (rate < 1 || rate > 10000) throw new ArgumentException("采样率必须在1到10000次/秒之间");
            SendCommand($":SENSE:VOLTAGE:SRATE {rate}");
        }

        public void SetRange(double range = 0)
        {
            if (range == 0) SendCommand(":SENSE:VOLTAGE:RANGE:AUTO 1");
            else
            {
                double[] validRanges = { 2, 20, 200, 1000 };
                if (!validRanges.Contains(range)) throw new ArgumentException("量程必须是2V, 20V, 200V, 1000V或0（自动）");
                SendCommand($":SENSE:VOLTAGE:RANGE {range}");
            }
        }

        public void SetResolution(string resolution = "HIGH")
        {
            string[] validResolutions = { "HIGH", "MEDIUM", "LOW" };
            if (!validResolutions.Contains(resolution.ToUpper())) throw new ArgumentException("分辨率必须是HIGH, MEDIUM或LOW");
            SendCommand($":SENSE:VOLTAGE:RESOLUTION {resolution}");
        }
        #endregion

        #region 测量
        public double MeasureDCVoltage(double range = 0, string resolution = "HIGH", int rate = 5,
                                      int measurementTimeMs = 1000, int minMeasurements = 5, int maxMeasurements = 100)
        {
            if (!IsConnected) throw new Exception("未连接到高压表");
            try
            {
                if (measurementTimeMs < 100) throw new Exception("测量时间不能小于100ms");
                if (minMeasurements < 1) throw new Exception("最小测量次数不能小于1");
                if (maxMeasurements < minMeasurements) throw new Exception("最大测量次数不能小于最小测量次数");

                if (range != 0) SetRange(range);
                if (resolution != "HIGH") SetResolution(resolution);
                if (rate != 5) SetSampleRate(rate);

                List<double> measurements = new List<double>();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                int intervalMs = Math.Max(10, measurementTimeMs / maxMeasurements);

                while (stopwatch.ElapsedMilliseconds < measurementTimeMs && measurements.Count < maxMeasurements)
                {
                    string response = Query(":READ?");
                    if (double.TryParse(response, out double result))
                        measurements.Add(result);
                    else
                        throw new Exception($"无效的响应格式: {response}");

                    if (stopwatch.ElapsedMilliseconds + intervalMs < measurementTimeMs && measurements.Count < maxMeasurements)
                        Thread.Sleep(intervalMs);
                }
                stopwatch.Stop();

                if (measurements.Count < minMeasurements)
                    throw new Exception($"测量数据不足。期望至少{minMeasurements}次，实际{measurements.Count}次");

                return Math.Round(measurements.Average(), 5);
            }
            catch (Exception ex) { throw new Exception($"直流电压测量失败: {ex.Message}"); }
        }

        public void StartContinuousMeasurement() => SendCommand(":INIT:CONTINUOUS ON");
        public void StopContinuousMeasurement() => SendCommand(":INIT:CONTINUOUS OFF");

        public double FetchMeasurement()
        {
            string response = Query(":FETCH?");
            if (double.TryParse(response, out double result)) return result;
            throw new Exception($"无效的响应格式: {response}");
        }
        #endregion

        #region 数学运算
        public void SetdBReference(double reference) => SendCommand($":CALCULATE:DB:REFERENCE {reference}");

        public void EnabledBmCalculation(double impedance = 600)
        {
            SendCommand($":CALCULATE:DBM:IMPEDANCE {impedance}");
            SendCommand(":CALCULATE:DBM:STATE ON");
        }

        public void SetCompareFunction(double highLimit, double lowLimit)
        {
            SendCommand($":CALCULATE:COMPARE:HIGH {highLimit}");
            SendCommand($":CALCULATE:COMPARE:LOW {lowLimit}");
            SendCommand(":CALCULATE:COMPARE:STATE ON");
        }

        public void SetScaleOffset(double scale, double offset)
        {
            SendCommand($":CALCULATE:SCALE {scale}");
            SendCommand($":CALCULATE:OFFSET {offset}");
            SendCommand(":CALCULATE:SCALE:STATE ON");
        }
        #endregion

        #region 显示控制
        public void SetDisplayMode(string mode = "DIGITAL")
        {
            string[] validModes = { "DIGITAL", "BARGRAPH", "TREND", "HISTOGRAM" };
            if (!validModes.Contains(mode.ToUpper())) throw new ArgumentException("显示模式必须是DIGITAL, BARGRAPH, TREND或HISTOGRAM");
            SendCommand($":DISPLAY:MODE {mode}");
        }

        public void SetDisplayLanguage(string language = "ENGLISH")
        {
            string[] validLanguages = { "ENGLISH", "SIMPLIFIED_CHINESE", "TRADITIONAL_CHINESE", "JAPANESE", "KOREAN" };
            if (!validLanguages.Contains(language.ToUpper())) throw new ArgumentException("语言参数无效");
            SendCommand($":DISPLAY:LANGUAGE {language}");
        }
        #endregion

        #region 数据存储
        public void StartDataLogging(int points = 1000)
        {
            SendCommand($":MEMORY:POINTS {points}");
            SendCommand(":MEMORY:LOG START");
        }

        public void StopDataLogging() => SendCommand(":MEMORY:LOG STOP");

        public double[] ReadStoredData()
        {
            string response = Query(":MEMORY:DATA?");
            List<double> data = new List<double>();
            foreach (string s in response.Split(','))
                if (double.TryParse(s.Trim(), out double v)) data.Add(v);
            return data.ToArray();
        }
        #endregion

        #region 系统命令
        public string GetIdn() => Query("*IDN?");
        public string GetSystemError() => Query(":SYSTEM:ERROR?");
        public string SelfTest() => Query("*TST?");
        public string GetStatus() => Query(":STATUS:QUESTIONABLE?");
        #endregion

        #region IDisposable
        private void Dispose(bool disposing)
        {
            if (!_disposed) { if (disposing) Disconnect(); _disposed = true; }
        }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        ~GwinstekGVM9102_TMCCommunicator() => Dispose(false);
        #endregion
    }
}
