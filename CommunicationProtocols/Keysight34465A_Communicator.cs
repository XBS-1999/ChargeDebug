using NationalInstruments.Visa;

#pragma warning disable
namespace CommunicationProtocols
{
    public sealed class Keysight34465A_Communicator : IDisposable
    {
        private static readonly Lazy<Keysight34465A_Communicator> _instance =
            new Lazy<Keysight34465A_Communicator>(() => new Keysight34465A_Communicator());

        private MessageBasedSession _mbSession;
        private bool _isConnected = false;
        private bool _disposed = false;

        // 私有构造函数防止外部实例化
        private Keysight34465A_Communicator()
        {
            // 初始化代码（如果需要）
        }

        /// <summary>
        /// 获取Keysight34465A_Communicator的单例实例
        /// </summary>
        public static Keysight34465A_Communicator Instance
        {
            get { return _instance.Value; }
        }

        #region 连接管理方法

        /// <summary>
        /// 连接到Keysight 34465A万用表
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
                        // Keysight 34465A的USB标识符
                        resourceString = "USB?*::0x2A8D::0x0101?*::INSTR";

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
                                throw new Exception("未找到USB设备，请确保万用表已通过USB连接并打开电源");
                            }
                            resourceString = resources[0];
                        }
                    }

                    // 打开会话
                    _mbSession = (MessageBasedSession)rmSession.Open(resourceString);
                    _mbSession.TimeoutMilliseconds = 5000; // 设置超时时间为5秒

                    // 清空缓冲区
                    _mbSession.Clear();

                    // 验证设备
                    _mbSession.RawIO.Write("*IDN?\n");
                    string response = _mbSession.RawIO.ReadString().Trim();

                    if (response.Contains("34465A") || response.Contains("Keysight"))
                    {
                        _isConnected = true;
                        InitializeMeter();
                        return true;
                    }
                    else
                    {
                        Disconnect();
                        throw new Exception($"连接的设备不是Keysight 34465A。设备响应: {response}");
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
        /// 断开与万用表的连接
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

        // 初始化万用表设置
        private void InitializeMeter()
        {
            try
            {
                // 重置设备到默认状态
                SendCommand("*RST");
                System.Threading.Thread.Sleep(1000); // 等待重置完成

                // 设置ASCII格式数据
                SendCommand("FORM:DATA ASCII");

                // 清除状态
                SendCommand("*CLS");
            }
            catch (Exception ex)
            {
                throw new Exception($"初始化万用表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置万用表到默认设置
        /// </summary>
        public void Reset()
        {
            SendCommand("*RST");
            System.Threading.Thread.Sleep(1000);
        }

        #endregion

        #region SCPI命令通信基础方法

        /// <summary>
        /// 发送SCPI命令到万用表
        /// </summary>
        /// <param name="command">SCPI命令</param>
        public void SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("未连接到万用表");

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
        /// 查询万用表并返回响应
        /// </summary>
        /// <param name="query">SCPI查询命令</param>
        /// <returns>万用表的响应</returns>
        public string Query(string query)
        {
            if (!IsConnected)
                throw new Exception("未连接到万用表");

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

                // 处理可能的命令回显（某些设备会回显命令）
                if (response.StartsWith(query.Trim()))
                {
                    // 如果是命令回显，再次读取实际数据
                    response = _mbSession.RawIO.ReadString().Trim();
                }

                return response;
            }
            catch (Exception ex)
            {
                throw new Exception($"查询失败: {ex.Message}, 查询: {query}");
            }
        }

        #endregion

        #region 测量命令

        /// <summary>
        /// 测量直流电压
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率</param>
        /// <returns>电压值（伏特）</returns>
        public double MeasureDCVoltage(double range = 0, double resolution = 0.001)
        {
            try
            {
                if (range > 0)
                    SendCommand($"CONF:VOLT:DC {range},{resolution}");
                else
                    SendCommand("CONF:VOLT:DC AUTO");

                // 触发并读取测量值
                SendCommand("READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"直流电压测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量交流电压
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率</param>
        /// <returns>电压值（伏特）</returns>
        public double MeasureACVoltage(double range = 0, double resolution = 0.001)
        {
            try
            {
                if (range > 0)
                    SendCommand($"CONF:VOLT:AC {range},{resolution}");
                else
                    SendCommand("CONF:VOLT:AC AUTO");

                SendCommand("READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"交流电压测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量电阻
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率</param>
        /// <returns>电阻值（欧姆）</returns>
        public double MeasureResistance(double range = 0, double resolution = 0.1)
        {
            try
            {
                if (range > 0)
                    SendCommand($"CONF:RES {range},{resolution}");
                else
                    SendCommand("CONF:RES AUTO");

                SendCommand("READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"电阻测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量直流电流
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率</param>
        /// <returns>电流值（安培）</returns>
        public double MeasureDCCurrent(double range = 0, double resolution = 0.0001)
        {
            try
            {
                if (range > 0)
                    SendCommand($"CONF:CURR:DC {range},{resolution}");
                else
                    SendCommand("CONF:CURR:DC AUTO");

                SendCommand("READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"直流电流测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量交流电流
        /// </summary>
        /// <param name="range">量程，0表示自动量程</param>
        /// <param name="resolution">分辨率</param>
        /// <returns>电流值（安培）</returns>
        public double MeasureACCurrent(double range = 0, double resolution = 0.0001)
        {
            try
            {
                if (range > 0)
                    SendCommand($"CONF:CURR:AC {range},{resolution}");
                else
                    SendCommand("CONF:CURR:AC AUTO");

                SendCommand("READ?");
                System.Threading.Thread.Sleep(100);

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"交流电流测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量频率
        /// </summary>
        /// <param name="voltageRange">电压量程</param>
        /// <returns>频率值（赫兹）</returns>
        public double MeasureFrequency(double voltageRange = 10)
        {
            try
            {
                SendCommand($"CONF:FREQ {voltageRange}");

                SendCommand("READ?");
                System.Threading.Thread.Sleep(300); // 频率测量需要更长时间

                string response = _mbSession.RawIO.ReadString().Trim();
                if (double.TryParse(response, out double result))
                    return result;
                else
                    throw new Exception($"无效的响应格式: {response}");
            }
            catch (Exception ex)
            {
                throw new Exception($"频率测量失败: {ex.Message}");
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
            return Query("SYST:ERR?");
        }

        /// <summary>
        /// 设置测量触发延迟
        /// </summary>
        /// <param name="delay">延迟时间（秒）</param>
        public void SetTriggerDelay(double delay)
        {
            SendCommand($"TRIG:DEL {delay}");
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

        ~Keysight34465A_Communicator()
        {
            Dispose(false);
        }

        #endregion
    }
}