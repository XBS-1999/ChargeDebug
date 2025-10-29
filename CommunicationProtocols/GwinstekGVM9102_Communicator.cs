using NationalInstruments.Visa;
using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable
namespace CommunicationProtocols
{
    public sealed class GwinstekGVM9102_Communicator : IDisposable
    {
        private static readonly Lazy<GwinstekGVM9102_Communicator> _instance =
            new Lazy<GwinstekGVM9102_Communicator>(() => new GwinstekGVM9102_Communicator());

        private MessageBasedSession _mbSession;
        private bool _isConnected = false;
        private bool _disposed = false;

        // 私有构造函数防止外部实例化
        private GwinstekGVM9102_Communicator()
        {
            // 初始化代码
        }

        /// <summary>
        /// 获取GwinstekGVM9102_Communicator的单例实例
        /// </summary>
        public static GwinstekGVM9102_Communicator Instance
        {
            get { return _instance.Value; }
        }

        #region 连接管理方法

        /// <summary>
        /// 连接到固纬 GVM-9102 高压表
        /// </summary>
        /// <param name="resourceString">VISA资源字符串，如果为null则自动检测</param>
        /// <returns>连接成功返回true</returns>
        public bool Connect(string resourceString = null)
        {
            try
            {
                if (_isConnected)
                    Disconnect();

                using (ResourceManager rmSession = new ResourceManager())
                {
                    // 如果没有指定资源字符串，尝试自动检测
                    if (string.IsNullOrEmpty(resourceString))
                    {
                        // 固纬 GVM-9102 的标识符
                        string[] searchPatterns = {
                            "USB?*::0x2184::0x005A?*::INSTR", // 固纬典型USB VID/PID
                            "TCPIP?*::INSTR",
                            "ASRL?*::INSTR"
                        };

                        foreach (string pattern in searchPatterns)
                        {
                            try
                            {
                                string[] resources = rmSession.Find(pattern).ToArray();
                                if (resources.Length > 0)
                                {
                                    resourceString = resources[0];
                                    break;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (string.IsNullOrEmpty(resourceString))
                        {
                            throw new Exception("未找到GVM-9102设备，请确保高压表已连接并打开电源");
                        }
                    }

                    // 打开会话
                    _mbSession = (MessageBasedSession)rmSession.Open(resourceString);
                    _mbSession.TimeoutMilliseconds = 10000; // 设置超时时间为10秒

                    // 清空缓冲区
                    _mbSession.Clear();

                    // 验证设备
                    _mbSession.RawIO.Write("*IDN?\n");
                    string response = _mbSession.RawIO.ReadString().Trim();

                    if (response.Contains("GVM-9102") || response.Contains("GWINSTEK"))
                    {
                        _isConnected = true;
                        InitializeMeter();
                        return true;
                    }
                    else
                    {
                        Disconnect();
                        throw new Exception($"连接的设备不是固纬 GVM-9102。设备响应: {response}");
                    }
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                throw new Exception($"连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开与高压表的连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_mbSession != null)
                {
                    _mbSession.Dispose();
                    _mbSession = null;
                }
                _isConnected = false;
            }
            catch (Exception ex)
            {
                throw new Exception($"断开连接时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否已连接
        /// </summary>
        public bool IsConnected
        {
            get { return _isConnected && _mbSession != null; }
        }

        #endregion

        #region 设备初始化与配置

        // 初始化高压表设置
        private void InitializeMeter()
        {
            try
            {
                // 重置设备到默认状态
                SendCommand("*RST");
                System.Threading.Thread.Sleep(1500); // 等待重置完成

                // 设置ASCII格式数据
                SendCommand(":FORMAT:DATA ASCII");

                // 设置显示模式
                SendCommand(":DISPLAY:MODE DIGITAL");

                // 设置采样率（10k/s 是GVM-9102的最大能力）
                SetSampleRate(1000); // 默认设置为1000次/秒

                // 清除状态
                SendCommand("*CLS");

                // 设置高精度模式
                SendCommand(":SENSE:VOLTAGE:RESOLUTION HIGH");
            }
            catch (Exception ex)
            {
                throw new Exception($"初始化高压表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置高压表到默认设置
        /// </summary>
        public void Reset()
        {
            SendCommand("*RST");
            System.Threading.Thread.Sleep(1500);
        }

        #endregion

        #region SCPI命令通信基础方法

        /// <summary>
        /// 发送SCPI命令到高压表
        /// </summary>
        /// <param name="command">SCPI命令</param>
        public void SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("未连接到高压表");

            try
            {
                if (!command.EndsWith("\n"))
                    command += "\n";

                _mbSession.RawIO.Write(command);
            }
            catch (Exception ex)
            {
                throw new Exception($"发送命令失败: {ex.Message}, 命令: {command}");
            }
        }

        /// <summary>
        /// 查询高压表并返回响应
        /// </summary>
        /// <param name="query">SCPI查询命令</param>
        /// <returns>高压表的响应</returns>
        public string Query(string query)
        {
            if (!IsConnected)
                throw new Exception("未连接到高压表");

            try
            {
                if (!query.EndsWith("?"))
                    throw new ArgumentException("查询命令必须以问号(?)结尾");

                if (!query.EndsWith("?\n"))
                    query += "\n";

                // 清空缓冲区以确保没有残留数据
                _mbSession.Clear();

                // 发送查询命令
                _mbSession.RawIO.Write(query);

                // 读取响应
                string response = _mbSession.RawIO.ReadString().Trim();

                return response;
            }
            catch (Exception ex)
            {
                throw new Exception($"查询失败: {ex.Message}, 查询: {query}");
            }
        }

        #endregion

        #region 测量配置方法

        /// <summary>
        /// 设置采样率
        /// </summary>
        /// <param name="rate">采样率（次/秒），GVM-9102最高支持10000次/秒</param>
        public void SetSampleRate(int rate)
        {
            if (!IsConnected)
                throw new Exception("未连接到高压表");

            try
            {
                if (rate < 1 || rate > 10000)
                    throw new ArgumentException("采样率必须在1到10000次/秒之间");

                SendCommand($":SENSE:VOLTAGE:SRATE {rate}");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置采样率失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置测量量程
        /// </summary>
        /// <param name="range">量程值：2, 20, 200, 1000（伏特）或 0（自动）</param>
        public void SetRange(double range = 0)
        {
            if (!IsConnected)
                throw new Exception("未连接到高压表");

            try
            {
                if (range == 0)
                {
                    SendCommand(":SENSE:VOLTAGE:RANGE:AUTO 1");
                }
                else
                {
                    double[] validRanges = { 2, 20, 200, 1000 };
                    if (!validRanges.Contains(range))
                        throw new ArgumentException("量程必须是2V, 20V, 200V, 1000V或0（自动）");

                    SendCommand($":SENSE:VOLTAGE:RANGE {range}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"设置量程失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置测量分辨率
        /// </summary>
        /// <param name="resolution">分辨率模式：HIGH, MEDIUM, LOW</param>
        public void SetResolution(string resolution = "HIGH")
        {
            if (!IsConnected)
                throw new Exception("未连接到高压表");

            try
            {
                string[] validResolutions = { "HIGH", "MEDIUM", "LOW" };
                if (!validResolutions.Contains(resolution.ToUpper()))
                    throw new ArgumentException("分辨率必须是HIGH, MEDIUM或LOW");

                SendCommand($":SENSE:VOLTAGE:RESOLUTION {resolution}");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置分辨率失败: {ex.Message}");
            }
        }

        #endregion

        #region 测量命令

        /// <summary>
        /// 测量直流电压（GVM-9102的主要功能）
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率模式</param>
        /// <returns>电压值（伏特）</returns>
        public double MeasureDCVoltage(double range = 0, string resolution = "HIGH")
        {
            try
            {
                // 设置量程
                SetRange(range);

                // 设置分辨率
                SetResolution(resolution);

                // 触发单次测量并读取结果
                SendCommand(":READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                {
                    return result;
                }
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"直流电压测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始连续测量
        /// </summary>
        public void StartContinuousMeasurement()
        {
            try
            {
                SendCommand(":INIT:CONTINUOUS ON");
            }
            catch (Exception ex)
            {
                throw new Exception($"启动连续测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止连续测量
        /// </summary>
        public void StopContinuousMeasurement()
        {
            try
            {
                SendCommand(":INIT:CONTINUOUS OFF");
            }
            catch (Exception ex)
            {
                throw new Exception($"停止连续测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取当前测量值（用于连续测量模式）
        /// </summary>
        /// <returns>电压值（伏特）</returns>
        public double FetchMeasurement()
        {
            try
            {
                string response = Query(":FETCH?");
                if (double.TryParse(response, out double result))
                {
                    return result;
                }
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"读取测量值失败: {ex.Message}");
            }
        }

        #endregion

        #region 数学运算功能（GVM-9102特色功能）

        /// <summary>
        /// 设置dB相对电平参考
        /// </summary>
        /// <param name="reference">参考电平（伏特）</param>
        public void SetdBReference(double reference)
        {
            try
            {
                SendCommand($":CALCULATE:DB:REFERENCE {reference}");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置dB参考失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启用dBm计算
        /// </summary>
        /// <param name="impedance">阻抗值（欧姆）</param>
        public void EnabledBmCalculation(double impedance = 600)
        {
            try
            {
                SendCommand($":CALCULATE:DBM:IMPEDANCE {impedance}");
                SendCommand(":CALCULATE:DBM:STATE ON");
            }
            catch (Exception ex)
            {
                throw new Exception($"启用dBm计算失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置比较功能
        /// </summary>
        /// <param name="highLimit">上限值</param>
        /// <param name="lowLimit">下限值</param>
        public void SetCompareFunction(double highLimit, double lowLimit)
        {
            try
            {
                SendCommand($":CALCULATE:COMPARE:HIGH {highLimit}");
                SendCommand($":CALCULATE:COMPARE:LOW {lowLimit}");
                SendCommand(":CALCULATE:COMPARE:STATE ON");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置比较功能失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置MX+B比例运算
        /// </summary>
        /// <param name="scale">比例系数M</param>
        /// <param name="offset">偏移量B</param>
        public void SetScaleOffset(double scale, double offset)
        {
            try
            {
                SendCommand($":CALCULATE:SCALE {scale}");
                SendCommand($":CALCULATE:OFFSET {offset}");
                SendCommand(":CALCULATE:SCALE:STATE ON");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置比例偏移失败: {ex.Message}");
            }
        }

        #endregion

        #region 显示控制

        /// <summary>
        /// 设置显示模式
        /// </summary>
        /// <param name="mode">显示模式：DIGITAL, BARGRAPH, TREND, HISTOGRAM</param>
        public void SetDisplayMode(string mode = "DIGITAL")
        {
            try
            {
                string[] validModes = { "DIGITAL", "BARGRAPH", "TREND", "HISTOGRAM" };
                if (!validModes.Contains(mode.ToUpper()))
                    throw new ArgumentException("显示模式必须是DIGITAL, BARGRAPH, TREND或HISTOGRAM");

                SendCommand($":DISPLAY:MODE {mode}");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置显示模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置显示语言
        /// </summary>
        /// <param name="language">语言：ENGLISH, SIMPLIFIED_CHINESE, TRADITIONAL_CHINESE, JAPANESE, KOREAN</param>
        public void SetDisplayLanguage(string language = "ENGLISH")
        {
            try
            {
                SendCommand($":DISPLAY:LANGUAGE {language}");
            }
            catch (Exception ex)
            {
                throw new Exception($"设置显示语言失败: {ex.Message}");
            }
        }

        #endregion

        #region 数据存储功能

        /// <summary>
        /// 开始数据记录
        /// </summary>
        /// <param name="points">记录点数</param>
        public void StartDataLogging(int points = 1000)
        {
            try
            {
                SendCommand($":MEMORY:POINTS {points}");
                SendCommand(":MEMORY:LOG START");
            }
            catch (Exception ex)
            {
                throw new Exception($"启动数据记录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止数据记录
        /// </summary>
        public void StopDataLogging()
        {
            try
            {
                SendCommand(":MEMORY:LOG STOP");
            }
            catch (Exception ex)
            {
                throw new Exception($"停止数据记录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取存储的数据
        /// </summary>
        /// <returns>存储的测量数据数组</returns>
        public double[] ReadStoredData()
        {
            try
            {
                string response = Query(":MEMORY:DATA?");
                string[] dataStrings = response.Split(',');
                List<double> data = new List<double>();

                foreach (string dataString in dataStrings)
                {
                    if (double.TryParse(dataString.Trim(), out double value))
                    {
                        data.Add(value);
                    }
                }

                return data.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"读取存储数据失败: {ex.Message}");
            }
        }

        #endregion

        #region 系统命令

        /// <summary>
        /// 获取设备标识信息
        /// </summary>
        public string GetIdn()
        {
            return Query("*IDN?");
        }

        /// <summary>
        /// 检查系统错误
        /// </summary>
        public string GetSystemError()
        {
            return Query(":SYSTEM:ERROR?");
        }

        /// <summary>
        /// 执行自检
        /// </summary>
        public string SelfTest()
        {
            return Query("*TST?");
        }

        /// <summary>
        /// 获取设备状态
        /// </summary>
        public string GetStatus()
        {
            return Query(":STATUS:QUESTIONABLE?");
        }

        #endregion

        #region IDisposable实现

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~GwinstekGVM9102_Communicator()
        {
            Dispose(false);
        }

        #endregion
    }
}