using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

#pragma warning disable
namespace CommunicationProtocols
{
    /// <summary>
    /// GVM-9102 USB CDC 通讯类（虚拟串口，不依赖 NI-VISA）
    /// Connect 传端口号（如 "COM5"）；传 null 自动扫描并 *IDN? 识别。
    /// 前置：后面板 USB Device 口(Type B)接电脑、USB-Protocol=USBCDC、CDC 驱动已装。
    /// </summary>
    public sealed class GwinstekGVM9102_CDCCommunicator : IDisposable
    {
        private static readonly Lazy<GwinstekGVM9102_CDCCommunicator> _instance =
            new Lazy<GwinstekGVM9102_CDCCommunicator>(() => new GwinstekGVM9102_CDCCommunicator());

        private SerialPort _serialPort;
        private bool _isConnected;
        private bool _disposed;
        private readonly object _syncRoot = new object();

        private const int DefaultBaudRate = 115200;
        private const string Eol = "\r\n";          // 固纬 EOL = CR+LF
        private const int OpenDelayMs = 200;
        private const int QueryRetryCount = 3;

        private GwinstekGVM9102_CDCCommunicator() { }
        public static GwinstekGVM9102_CDCCommunicator Instance => _instance.Value;
        public Action<string> Log { get; set; }
        public bool IsConnected => _isConnected && _serialPort != null && _serialPort.IsOpen;

        #region 连接管理
        public bool Connect(string portName = null)
        {
            lock (_syncRoot)
            {
                try
                {
                    if (IsConnected) Disconnect();
                    if (string.IsNullOrEmpty(portName))
                    {
                        portName = AutoDetectPort();
                        if (portName == null)
                            throw new Exception(
                                "未找到 GVM-9102 CDC 串口：检查后面板 USB Device 口(Type B)、" +
                                "USB-Protocol=USBCDC、CDC 驱动已装；或手动传端口号 Connect(\"COM5\")");
                    }
                    _serialPort = new SerialPort(portName, DefaultBaudRate, Parity.None, 8, StopBits.One)
                    {
                        Encoding = Encoding.ASCII,
                        NewLine = "\n",
                        ReadTimeout = 5000,
                        WriteTimeout = 3000
                    };
                    _serialPort.Open();
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    Thread.Sleep(OpenDelayMs);          // 等 CDC 驱动就绪

                    string idn = QueryWithRetry("*IDN?");
                    if (!idn.Contains("GVM9102") && !idn.Contains("GWInstek"))
                    {
                        Disconnect();
                        throw new Exception($"该串口不是固纬 GVM-9102，设备响应: {idn}");
                    }
                    _isConnected = true;
                    Log?.Invoke($"已连接 {portName}，设备: {idn}");
                    InitializeMeter();
                    return true;
                }
                catch (Exception ex)
                {
                    Disconnect();
                    Log?.Invoke($"连接失败: {ex.Message}");
                    throw new Exception($"连接失败: {ex.Message}");
                }
            }
        }

        private string AutoDetectPort()
        {
            foreach (string port in SerialPort.GetPortNames())
            {
                SerialPort probe = null;
                try
                {
                    probe = new SerialPort(port, DefaultBaudRate, Parity.None, 8, StopBits.One)
                    { Encoding = Encoding.ASCII, NewLine = "\n", ReadTimeout = 1500, WriteTimeout = 1500 };
                    probe.Open();
                    probe.DiscardInBuffer();
                    probe.Write("*IDN?" + Eol);
                    string resp = probe.ReadLine();
                    if (resp.Contains("GVM9102") || resp.Contains("GWInstek"))
                        return port;
                }
                catch { }
                finally { if (probe != null) { try { probe.Close(); } catch { } probe.Dispose(); } }
            }
            return null;
        }

        public void Disconnect()
        {
            lock (_syncRoot)
            {
                if (_serialPort != null)
                {
                    try { _serialPort.Close(); } catch { }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
                _isConnected = false;
            }
        }
        #endregion

        #region SCPI 基础
        public void SendCommand(string command)
        {
            if (!IsConnected) throw new Exception("未连接到高压表");
            lock (_syncRoot)
            {
                try { _serialPort.Write(command + Eol); }
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
                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(query + Eol);
                    return _serialPort.ReadLine().Trim();
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
        ~GwinstekGVM9102_CDCCommunicator() => Dispose(false);
        #endregion
    }
}
