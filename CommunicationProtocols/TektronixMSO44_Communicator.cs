using NationalInstruments.Visa;
using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable
namespace CommunicationProtocols
{
    public sealed class TektronixMSO44_Communicator : IDisposable
    {
        private static readonly Lazy<TektronixMSO44_Communicator> _instance =
            new Lazy<TektronixMSO44_Communicator>(() => new TektronixMSO44_Communicator());

        private MessageBasedSession _mbSession;
        private bool _isConnected = false;
        private bool _disposed = false;

        // 私有构造函数防止外部实例化
        private TektronixMSO44_Communicator()
        {
            // 初始化代码
        }

        /// <summary>
        /// 获取TektronixMSO44_Communicator的单例实例
        /// </summary>
        public static TektronixMSO44_Communicator Instance
        {
            get { return _instance.Value; }
        }

        #region 连接管理方法

        /// <summary>
        /// 连接到Tektronix MSO44示波器
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
                        // Tektronix MSO44的标识符
                        resourceString = "USB?*::0x0699::0x0528?*::INSTR";

                        // 将IEnumerable转换为数组以支持索引访问
                        string[] resources = rmSession.Find(resourceString).ToArray();

                        if (resources.Length > 0)
                        {
                            resourceString = resources[0];
                        }
                        else
                        {
                            // 尝试更通用的搜索模式
                            resources = rmSession.Find("USB?*::INSTR").ToArray();
                            if (resources.Length == 0)
                            {
                                // 尝试TCP/IP连接
                                resources = rmSession.Find("TCPIP?*::INSTR").ToArray();
                                if (resources.Length == 0)
                                {
                                    throw new Exception("未找到Tektronix设备，请确保示波器已连接并打开电源");
                                }
                            }
                            resourceString = resources[0];
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

                    if (response.Contains("TEKTRONIX") || response.Contains("MSO44") || response.Contains("MSO4"))
                    {
                        _isConnected = true;
                        InitializeScope();
                        return true;
                    }
                    else
                    {
                        Disconnect();
                        throw new Exception($"连接的设备不是Tektronix MSO44。设备响应: {response}");
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
        /// 断开与示波器的连接
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

        // 初始化示波器设置
        private void InitializeScope()
        {
            try
            {
                // 重置设备到默认状态
                SendCommand("*RST");
                System.Threading.Thread.Sleep(2000); // 等待重置完成

                // 设置数据格式
                SendCommand("DAT:ENC ASCI"); // ASCII格式
                SendCommand("DAT:WID 1");    // 1字节 per data point

                // 清除状态
                SendCommand("*CLS");

                // 设置默认触发
                SendCommand("TRIG:A:TYP EDGE");
                SendCommand("TRIG:A:LEV 0.0");

                Console.WriteLine("Tektronix MSO44示波器初始化完成");
            }
            catch (Exception ex)
            {
                throw new Exception($"初始化示波器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置示波器到默认设置
        /// </summary>
        public void Reset()
        {
            SendCommand("*RST");
            System.Threading.Thread.Sleep(2000);
        }

        #endregion

        #region SCPI命令通信基础方法

        /// <summary>
        /// 发送SCPI命令到示波器
        /// </summary>
        /// <param name="command">SCPI命令</param>
        public void SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("未连接到示波器");

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
        /// 查询示波器并返回响应
        /// </summary>
        /// <param name="query">SCPI查询命令</param>
        /// <returns>示波器的响应</returns>
        public string Query(string query)
        {
            if (!IsConnected)
                throw new Exception("未连接到示波器");

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

        #region 通道设置方法

        /// <summary>
        /// 设置通道垂直刻度
        /// </summary>
        /// <param name="channel">通道号 (1-4)</param>
        /// <param name="scale">垂直刻度 (伏特/格)</param>
        public void SetChannelScale(int channel, double scale)
        {
            if (channel < 1 || channel > 4)
                throw new ArgumentException("通道号必须在1-4之间");

            SendCommand($"CH{channel}:SCA {scale}");
        }

        /// <summary>
        /// 设置通道偏置
        /// </summary>
        /// <param name="channel">通道号 (1-4)</param>
        /// <param name="offset">偏置电压 (伏特)</param>
        public void SetChannelOffset(int channel, double offset)
        {
            if (channel < 1 || channel > 4)
                throw new ArgumentException("通道号必须在1-4之间");

            SendCommand($"CH{channel}:OFFS {offset}");
        }

        /// <summary>
        /// 设置通道耦合方式
        /// </summary>
        /// <param name="channel">通道号 (1-4)</param>
        /// <param name="coupling">耦合方式: DC, AC, GND</param>
        public void SetChannelCoupling(int channel, string coupling)
        {
            if (channel < 1 || channel > 4)
                throw new ArgumentException("通道号必须在1-4之间");

            coupling = coupling.ToUpper();
            if (coupling != "DC" && coupling != "AC" && coupling != "GND")
                throw new ArgumentException("耦合方式必须是DC, AC或GND");

            SendCommand($"CH{channel}:COUP {coupling}");
        }

        /// <summary>
        /// 启用或禁用通道
        /// </summary>
        /// <param name="channel">通道号 (1-4)</param>
        /// <param name="state">true:启用, false:禁用</param>
        public void SetChannelState(int channel, bool state)
        {
            if (channel < 1 || channel > 4)
                throw new ArgumentException("通道号必须在1-4之间");

            string stateStr = state ? "ON" : "OFF";
            SendCommand($"SEL:CH{channel} {stateStr}");
        }

        #endregion

        #region 水平设置方法

        /// <summary>
        /// 设置水平时基
        /// </summary>
        /// <param name="timebase">时基 (秒/格)</param>
        public void SetTimebase(double timebase)
        {
            SendCommand($"HOR:SCA {timebase}");
        }

        /// <summary>
        /// 设置水平延迟
        /// </summary>
        /// <param name="delay">延迟时间 (秒)</param>
        public void SetHorizontalDelay(double delay)
        {
            SendCommand($"HOR:DEL:TIM {delay}");
        }

        /// <summary>
        /// 设置采样率
        /// </summary>
        /// <param name="sampleRate">采样率 (样本/秒)</param>
        public void SetSampleRate(double sampleRate)
        {
            SendCommand($"HOR:ACQ:SAMPLER {sampleRate}");
        }

        /// <summary>
        /// 设置记录长度
        /// </summary>
        /// <param name="recordLength">记录长度 (样本数)</param>
        public void SetRecordLength(long recordLength)
        {
            SendCommand($"HOR:ACQ:RECO {recordLength}");
        }

        #endregion

        #region 触发设置方法

        /// <summary>
        /// 设置边沿触发
        /// </summary>
        /// <param name="source">触发源 (CH1, CH2, CH3, CH4, LINE, AUX)</param>
        /// <param name="level">触发电平 (伏特)</param>
        /// <param name="slope">触发边沿 (RISe, FALL)</param>
        public void SetEdgeTrigger(string source, double level, string slope = "RISe")
        {
            SendCommand("TRIG:A:TYP EDGE");
            SendCommand($"TRIG:A:EDGE:SOU {source}");
            SendCommand($"TRIG:A:LEV {level}");
            SendCommand($"TRIG:A:EDGE:SLO {slope}");
        }

        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="mode">触发模式: NORM, AUTO</param>
        public void SetTriggerMode(string mode)
        {
            mode = mode.ToUpper();
            if (mode != "NORM" && mode != "AUTO")
                throw new ArgumentException("触发模式必须是NORM或AUTO");

            SendCommand($"TRIG:A:MOD {mode}");
        }

        #endregion

        #region 测量命令

        /// <summary>
        /// 获取波形数据
        /// </summary>
        /// <param name="channel">通道号 (1-4)</param>
        /// <param name="points">要获取的数据点数 (最大1000000)</param>
        /// <returns>波形数据数组</returns>
        public double[] GetWaveformData(int channel, int points = 1000)
        {
            if (!IsConnected)
                throw new Exception("未连接到示波器");

            try
            {
                if (channel < 1 || channel > 4)
                    throw new ArgumentException("通道号必须在1-4之间");

                // 选择数据源
                SendCommand($"DAT:SOU CH{channel}");

                // 设置数据参数
                SendCommand("DAT:ENC ASCI"); // ASCII格式
                SendCommand("DAT:WID 1");    // 1字节 per data point
                SendCommand($"DAT:STAR 1");  // 起始点
                SendCommand($"DAT:STOP {points}"); // 结束点

                // 获取数据
                SendCommand("CURV?");
                System.Threading.Thread.Sleep(500);

                // 读取波形数据
                string response = _mbSession.RawIO.ReadString().Trim();

                // 解析数据 (格式: #8xxxxxxx<data> 或直接是逗号分隔的数据)
                if (response.StartsWith("#"))
                {
                    // 处理二进制格式 (如果需要)
                    throw new Exception("当前只支持ASCII格式数据");
                }
                else
                {
                    // ASCII格式数据，逗号分隔
                    string[] dataStrings = response.Split(',');
                    double[] waveformData = new double[dataStrings.Length];

                    for (int i = 0; i < dataStrings.Length; i++)
                    {
                        if (double.TryParse(dataStrings[i], out double value))
                        {
                            waveformData[i] = value;
                        }
                        else
                        {
                            waveformData[i] = 0;
                        }
                    }

                    return waveformData;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"获取波形数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取波形参数 (X增量, X零点, Y增量, Y零点)
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <returns>波形参数元组 (xIncrement, xOrigin, yIncrement, yOrigin)</returns>
        public (double xIncrement, double xOrigin, double yIncrement, double yOrigin) GetWaveformParameters(int channel)
        {
            SendCommand($"DAT:SOU CH{channel}");

            double xIncrement = double.Parse(Query("WFMP:XIN?"));
            double xOrigin = double.Parse(Query("WFMP:XZE?"));
            double yIncrement = double.Parse(Query("WFMP:YMU?"));
            double yOrigin = double.Parse(Query("WFMP:YOF?"));

            return (xIncrement, xOrigin, yIncrement, yOrigin);
        }

        /// <summary>
        /// 执行自动设置
        /// </summary>
        public void AutoSet()
        {
            SendCommand("AUTOS EXEC");
            System.Threading.Thread.Sleep(3000); // 等待自动设置完成
        }

        /// <summary>
        /// 执行单次采集
        /// </summary>
        public void SingleAcquisition()
        {
            SendCommand("ACQ:STOPA SEQ");
            SendCommand("ACQ:STATE RUN");
        }

        /// <summary>
        /// 开始连续采集
        /// </summary>
        public void StartAcquisition()
        {
            SendCommand("ACQ:STATE RUN");
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public void StopAcquisition()
        {
            SendCommand("ACQ:STATE STOP");
        }

        #endregion

        #region 测量功能

        /// <summary>
        /// 添加测量项目
        /// </summary>
        /// <param name="measurement">测量类型: AMPL, FREQ, PERI, RISE, FALL, etc.</param>
        /// <param name="source">测量源: CH1, CH2, etc.</param>
        /// <returns>测量ID</returns>
        public string AddMeasurement(string measurement, string source)
        {
            string measId = $"MEAS{DateTime.Now.Ticks}";
            SendCommand($"MEASU:ADD {measurement},{source}");
            return measId;
        }

        /// <summary>
        /// 获取测量值
        /// </summary>
        /// <param name="measurementIndex">测量索引 (1-8)</param>
        /// <returns>测量值</returns>
        public double GetMeasurementValue(int measurementIndex)
        {
            if (measurementIndex < 1 || measurementIndex > 8)
                throw new ArgumentException("测量索引必须在1-8之间");

            string response = Query($"MEASU:MEAS{measurementIndex}:VAL?");
            return double.Parse(response);
        }

        /// <summary>
        /// 测量电压峰峰值
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <returns>峰峰值电压</returns>
        public double MeasurePeakToPeak(int channel)
        {
            SendCommand($"MEASU:IMM:SOU CH{channel}");
            SendCommand("MEASU:IMM:TYP PK2PK");
            System.Threading.Thread.Sleep(500);
            string response = Query("MEASU:IMM:VAL?");
            return double.Parse(response);
        }

        /// <summary>
        /// 测量频率
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <returns>频率值 (Hz)</returns>
        public double MeasureFrequency(int channel)
        {
            SendCommand($"MEASU:IMM:SOU CH{channel}");
            SendCommand("MEASU:IMM:TYP FREQ");
            System.Threading.Thread.Sleep(500);
            string response = Query("MEASU:IMM:VAL?");
            return double.Parse(response);
        }

        /// <summary>
        /// 测量上升时间
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <returns>上升时间 (秒)</returns>
        public double MeasureRiseTime(int channel)
        {
            SendCommand($"MEASU:IMM:SOU CH{channel}");
            SendCommand("MEASU:IMM:TYP RISE");
            System.Threading.Thread.Sleep(500);
            string response = Query("MEASU:IMM:VAL?");
            return double.Parse(response);
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
            return Query("SYST:ERR?");
        }

        /// <summary>
        /// 执行自检
        /// </summary>
        /// <returns>自检结果</returns>
        public string SelfTest()
        {
            return Query("*TST?");
        }

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public void SaveSetup(string filePath)
        {
            SendCommand($"SAV:SET '{filePath}'");
        }

        /// <summary>
        /// 从文件加载设置
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public void LoadSetup(string filePath)
        {
            SendCommand($"REC:SET '{filePath}'");
        }

        #endregion

        #region 屏幕截图

        /// <summary>
        /// 保存屏幕截图
        /// </summary>
        /// <param name="filePath">文件保存路径</param>
        /// <param name="format">图片格式: PNG, BMP, JPG</param>
        public void SaveScreenshot(string filePath, string format = "PNG")
        {
            format = format.ToUpper();
            if (format != "PNG" && format != "BMP" && format != "JPG")
                throw new ArgumentException("图片格式必须是PNG, BMP或JPG");

            SendCommand($"SAV:IMAG '{filePath}'");
            SendCommand($"SAV:IMAG:FILE:FORM {format}");
            SendCommand("HARDCOPY START");
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

        ~TektronixMSO44_Communicator()
        {
            Dispose(false);
        }

        #endregion
    }
}