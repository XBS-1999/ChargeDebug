using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataModel;
using Log;

#pragma warning disable
namespace CommunicationProtocols
{
    /// <summary>
    /// Chroma 19572 接地连接测试仪 RS232 通信类（SCPI ASCII 协议）
    /// 使用 RS232Manager 进行串口通信
    /// </summary>
    public sealed class Chroma19572_RS232Communicator : IDisposable
    {
        private static readonly Lazy<Chroma19572_RS232Communicator> _instance =
            new Lazy<Chroma19572_RS232Communicator>(() => new Chroma19572_RS232Communicator());

        private string _portName;
        private bool _isConnected = false;
        private bool _disposed = false;

        // 命令结束符（设备可自动识别 LF 或 CR+LF，此处使用 CR+LF 以确保兼容性）
        private const string COMMAND_TERMINATOR = "\r\n";

        // 响应读取超时（毫秒）
        private const int RESPONSE_TIMEOUT_MS = 2000;

        private readonly object _commLock = new object();

        private Chroma19572_RS232Communicator() { }

        public static Chroma19572_RS232Communicator Instance => _instance.Value;

        #region 连接管理

        /// <summary>
        /// 连接到 Chroma 19572 测试仪
        /// </summary>
        public bool Connect(string portName, int baudRate = 9600, int dataBits = 8,
                            string stopBits = "1", string parity = "无", string flowControl = "None")
        {
            try
            {
                if (_isConnected)
                    Disconnect();

                var config = new EquipmentModel
                {
                    ComPort = portName,
                    BaudRate = baudRate.ToString(),
                    DataBits = dataBits.ToString(),
                    StopBits = stopBits,
                    Parity = parity,
                    FlowControl = flowControl
                };

                bool success = RS232Manager.Instance.RegisterChannel(config);
                if (!success)
                {
                    LogService.Log($"注册串口 {portName} 失败");
                    return false;
                }

                _portName = portName;

                // 清空缓冲区
                RS232Manager.Instance.ReadBuffer(_portName, true);

                // 发送 *IDN? 查询仪器标识
                string idn = QueryString("*IDN?");
                if (string.IsNullOrEmpty(idn) ||
                    (!idn.Contains("CHROMA") && !idn.Contains("19572") && !idn.Contains("Chroma")))
                {
                    Disconnect();
                    LogService.Log($"设备响应不是预期的 Chroma 19572: {idn}");
                    return false;
                }

                _isConnected = true;
                LogService.Log($"Chroma 19572 连接成功，端口 {portName}，标识: {idn}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"连接 Chroma 19572 失败: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            if (!string.IsNullOrEmpty(_portName))
            {
                RS232Manager.Instance.CloseChannel(_portName);
                _portName = null;
            }
            _isConnected = false;
            LogService.Log("Chroma 19572 已断开连接");
        }

        public bool IsConnected => _isConnected && !string.IsNullOrEmpty(_portName) &&
                                   RS232Manager.Instance.IsChannelOpen(_portName);

        #endregion

        #region 底层通信

        /// <summary>
        /// 发送命令并读取一行响应（用于查询命令）
        /// </summary>
        private string SendCommandAndReadLine(string command)
        {
            lock (_commLock)
            {
                try
                {
                    // 清空输入缓冲区
                    RS232Manager.Instance.ReadBuffer(_portName, true);

                    // 发送命令（自动添加结束符）
                    string fullCommand = command + COMMAND_TERMINATOR;
                    bool sent = RS232Manager.Instance.SendData(_portName, Encoding.ASCII.GetBytes(fullCommand), false);
                    if (!sent)
                        throw new Exception("发送命令失败");

                    // 等待并读取一行响应
                    byte[] response = RS232Manager.Instance.ReadBuffer(_portName, true);
                    if (response.Length == 0)
                        throw new Exception("未收到响应（超时）");

                    string responseStr = Encoding.ASCII.GetString(response).TrimEnd('\r', '\n');
                    LogService.Log($"Sent: {command} -> Received: {responseStr}");
                    return responseStr;
                }
                catch (Exception ex)
                {
                    LogService.Log($"命令 '{command}' 执行异常: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 发送命令但不等待响应（用于设置命令）
        /// </summary>
        private void SendCommandNoResponse(string command)
        {
            lock (_commLock)
            {
                try
                {
                    string fullCommand = command + COMMAND_TERMINATOR;
                    bool sent = RS232Manager.Instance.SendData(_portName, Encoding.ASCII.GetBytes(fullCommand), false);
                    if (!sent)
                        throw new Exception("发送命令失败");

                    LogService.Log($"Sent (no response): {command}");
                    // 可选：短暂延时以确保命令送达
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    LogService.Log($"命令 '{command}' 发送异常: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 查询并返回字符串结果
        /// </summary>
        private string QueryString(string command) => SendCommandAndReadLine(command);

        /// <summary>
        /// 查询并返回整数结果
        /// </summary>
        private int QueryInt(string command) => int.Parse(QueryString(command));

        /// <summary>
        /// 查询并返回双精度浮点数结果
        /// </summary>
        private double QueryDouble(string command) => double.Parse(QueryString(command));

        /// <summary>
        /// 查询并返回布尔结果（'1' 或 '0'）
        /// </summary>
        private bool QueryBoolean(string command) => QueryString(command) == "1";

        #endregion

        #region 基础命令

        /// <summary>
        /// 查询仪器标识 (*IDN?)
        /// </summary>
        public string GetIdn() => QueryString("*IDN?");

        /// <summary>
        /// 停止测试
        /// </summary>
        public void StopTest() => SendCommandNoResponse(":SOURce:SAFETy:STOP");

        /// <summary>
        /// 启动测试
        /// </summary>
        public void StartTest() => SendCommandNoResponse(":SOURce:SAFETy:START");

        /// <summary>
        /// 查询装置执行状态
        /// </summary>
        /// <returns>"RUNNING" 或 "STOPPED"</returns>
        public string GetStatus() => QueryString(":SOURce:SAFETy:STATus?");

        /// <summary>
        /// 查询已设定的 STEP 个数
        /// </summary>
        public int GetStepNumber() => QueryInt(":SOURce:SAFETy:SNUMber?");

        /// <summary>
        /// 查询工作记忆中设定的 STEP 个数（同 SNUMber?）
        /// </summary>
        public int GetStepCount() => GetStepNumber();

        #endregion

        #region 步骤参数设置与查询

        /// <summary>
        /// 设置指定步骤的测试电流 (A)
        /// </summary>
        public void SetStepGBLevel(int step, double current) =>
            SendCommandNoResponse($":SOURce:SAFETy:STEP{step}:GB:LEVEL {current:F3}");

        /// <summary>
        /// 查询指定步骤的测试电流 (A)
        /// </summary>
        public double GetStepGBLevel(int step) =>
            QueryDouble($":SOURce:SAFETy:STEP{step}:GB:LEVEL?");

        /// <summary>
        /// 设置指定步骤的接地电阻上限 (Ohm)
        /// </summary>
        public void SetStepGBLimitHigh(int step, double limit) =>
            SendCommandNoResponse($":SOURce:SAFETy:STEP{step}:GB:LIMIT:HIGH {limit:F3}");

        /// <summary>
        /// 查询指定步骤的接地电阻上限 (Ohm)
        /// </summary>
        public double GetStepGBLimitHigh(int step) =>
            QueryDouble($":SOURce:SAFETy:STEP{step}:GB:LIMIT:HIGH?");

        /// <summary>
        /// 设置指定步骤的接地电阻下限 (Ohm)
        /// </summary>
        public void SetStepGBLimitLow(int step, double limit) =>
            SendCommandNoResponse($":SOURce:SAFETy:STEP{step}:GB:LIMIT:LOW {limit:F3}");

        /// <summary>
        /// 查询指定步骤的接地电阻下限 (Ohm)
        /// </summary>
        public double GetStepGBLimitLow(int step) =>
            QueryDouble($":SOURce:SAFETy:STEP{step}:GB:LIMIT:LOW?");

        /// <summary>
        /// 设置指定步骤的测试时间 (秒)
        /// </summary>
        public void SetStepGBTime(int step, double time) =>
            SendCommandNoResponse($":SOURce:SAFETy:STEP{step}:GB:TIME:TEST {time:F1}");

        /// <summary>
        /// 查询指定步骤的测试时间 (秒)
        /// </summary>
        public double GetStepGBTime(int step) =>
            QueryDouble($":SOURce:SAFETy:STEP{step}:GB:TIME:TEST?");

        /// <summary>
        /// 删除指定步骤（后续步骤前移）
        /// </summary>
        public void DeleteStep(int step) =>
            SendCommandNoResponse($":SOURce:SAFETy:STEP{step}:DELete");

        /// <summary>
        /// 查询指定步骤的所有设定值（返回逗号分隔字符串，需自行解析）
        /// </summary>
        public string GetStepSettings(int step) =>
            QueryString($":SOURce:SAFETy:STEP{step}:SET?");

        #endregion

        #region 预设参数设置与查询

        /// <summary>
        /// 设置 PASS 时蜂鸣器持续时间 (0.2~99.9 秒)
        /// </summary>
        public void SetPassBuzzerTime(double seconds) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:TIME:PASS {seconds:F1}");

        /// <summary>
        /// 查询 PASS 时蜂鸣器持续时间
        /// </summary>
        public double GetPassBuzzerTime() =>
            QueryDouble(":SOURce:SAFETy:PRESET:TIME:PASS?");

        /// <summary>
        /// 设置 STEP 之间的间隔时间 (秒) 或 KEY（手动）
        /// </summary>
        public void SetStepInterval(double seconds) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:TIME:STEP {seconds:F1}");

        /// <summary>
        /// 设置 STEP 之间的间隔为手动模式
        /// </summary>
        public void SetStepIntervalKey() =>
            SendCommandNoResponse(":SOURce:SAFETy:PRESET:TIME:STEP KEY");

        /// <summary>
        /// 查询 STEP 间隔设定（返回数值或 "KEY"）
        /// </summary>
        public string GetStepInterval() =>
            QueryString(":SOURce:SAFETy:PRESET:TIME:STEP?");

        /// <summary>
        /// 设置判定等待时间（秒）
        /// </summary>
        public void SetJudgmentDelay(double seconds) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:TIME:JUDGment {seconds:F1}");

        /// <summary>
        /// 查询判定等待时间
        /// </summary>
        public double GetJudgmentDelay() =>
            QueryDouble(":SOURce:SAFETy:PRESET:TIME:JUDGment?");

        /// <summary>
        /// 设置输出电流频率 (Hz)
        /// </summary>
        public void SetGBFrequency(double frequency) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:GB:FREQuency {frequency:F1}");

        /// <summary>
        /// 查询输出电流频率
        /// </summary>
        public double GetGBFrequency() =>
            QueryDouble(":SOURce:SAFETy:PRESET:GB:FREQuency?");

        /// <summary>
        /// 设置回路电压 (V)
        /// </summary>
        public void SetGBVoltage(double voltage) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:GB:VOLTage {voltage:F1}");

        /// <summary>
        /// 查询回路电压
        /// </summary>
        public double GetGBVoltage() =>
            QueryDouble(":SOURce:SAFETy:PRESET:GB:VOLTage?");

        /// <summary>
        /// 设置软体 AGC 开关
        /// </summary>
        public void SetSoftwareAGC(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:AGC:SOFTware {(enable ? "ON" : "OFF")}");

        /// <summary>
        /// 查询软体 AGC 状态
        /// </summary>
        public bool GetSoftwareAGC() =>
            QueryBoolean(":SOURce:SAFETy:PRESET:AGC:SOFTware?");

        /// <summary>
        /// 设置 FAIL 发生后是否继续测试下一步骤
        /// </summary>
        public void SetFailContinuity(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:FCONTinuity {(enable ? "ON" : "OFF")}");

        /// <summary>
        /// 查询 FAIL 发生后是否继续测试
        /// </summary>
        public bool GetFailContinuity() =>
            QueryBoolean(":SOURce:SAFETy:PRESET:FCONTinuity?");

        /// <summary>
        /// 设置测试画面显示开关
        /// </summary>
        public void SetScreen(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:SCREEN {(enable ? "ON" : "OFF")}");

        /// <summary>
        /// 查询测试画面显示状态
        /// </summary>
        public bool GetScreen() =>
            QueryBoolean(":SOURce:SAFETy:PRESET:SCREEN?");

        /// <summary>
        /// 设置 SMART KEY 开关
        /// </summary>
        public void SetSmartKey(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:KEYboard:SMAArt {(enable ? "ON" : "OFF")}");

        /// <summary>
        /// 查询 SMART KEY 状态
        /// </summary>
        public bool GetSmartKey() =>
            QueryBoolean(":SOURce:SAFETy:PRESET:KEYboard:SMAArt?");

        /// <summary>
        /// 设置 GBSS MODE 等待启动时间 (0.1~99.9 秒, 0 代表期间 GBSS MODE)
        /// </summary>
        public void SetAutoStartTime(double seconds) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:TIME:ASTart {seconds:F1}");

        /// <summary>
        /// 查询 GBSS MODE 等待启动时间
        /// </summary>
        public double GetAutoStartTime() =>
            QueryDouble(":SOURce:SAFETy:PRESET:TIME:ASTart?");

        /// <summary>
        /// 设置产品编号
        /// </summary>
        public void SetPartNumber(string partNumber) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:NUMBER:PART \"{partNumber}\"");

        /// <summary>
        /// 查询产品编号
        /// </summary>
        public string GetPartNumber() =>
            QueryString(":SOURce:SAFETy:PRESET:NUMBER:PART?");

        /// <summary>
        /// 设置产品批号
        /// </summary>
        public void SetLotNumber(string lotNumber) =>
            SendCommandNoResponse($":SOURce:SAFETy:PRESET:NUMBER:LOT \"{lotNumber}\"");

        /// <summary>
        /// 查询产品批号
        /// </summary>
        public string GetLotNumber() =>
            QueryString(":SOURce:SAFETy:PRESET:NUMBER:LOT?");

        #endregion

        #region 结果查询

        /// <summary>
        /// 查询所有步骤的 OUTPUT METER 读值（返回字符串数组，每个元素格式如 "1.234E-01"）
        /// </summary>
        public string[] GetAllOutputMeter()
        {
            string response = QueryString(":SOURce:SAFETy:RESULT:ALL:OMETerage?");
            return response.Split(',').Select(s => s.Trim()).ToArray();
        }

        /// <summary>
        /// 查询所有步骤的 MEASURE METER 读值
        /// </summary>
        public string[] GetAllMeasureMeter()
        {
            string response = QueryString(":SOURce:SAFETy:RESULT:ALL:MMETerage?");
            return response.Split(',').Select(s => s.Trim()).ToArray();
        }

        /// <summary>
        /// 查询所有步骤的判读结果代码（十进制）
        /// </summary>
        public int[] GetAllJudgment()
        {
            string response = QueryString(":SOURce:SAFETy:RESULT:ALL:JUDGment?");
            return response.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
        }

        /// <summary>
        /// 查询最后一个 STEP 的判读结果代码
        /// </summary>
        public int GetLastJudgment() =>
            QueryInt(":SOURce:SAFETy:RESult:LAST:JUDGment?");

        /// <summary>
        /// 查询最后一个 STEP 的 OUTPUT METER 读值
        /// </summary>
        public double GetLastOutputMeter() =>
            QueryDouble(":SOURce:SAFETy:RESult:LAST:OMETerage?");

        /// <summary>
        /// 查询最后一个 STEP 的 MEASURE METER 读值
        /// </summary>
        public double GetLastMeasureMeter() =>
            QueryDouble(":SOURce:SAFETy:RESult:LAST:MMETerage?");

        /// <summary>
        /// 查询指定 STEP 的判读结果代码
        /// </summary>
        public int GetStepJudgment(int step) =>
            QueryInt($":SOURce:SAFETy:RESult:STEP{step}:JUDGment?");

        /// <summary>
        /// 查询指定 STEP 的 MEASURE METER 读值
        /// </summary>
        public double GetStepMeasureMeter(int step) =>
            QueryDouble($":SOURce:SAFETy:RESult:STEP{step}:MMETerage?");

        /// <summary>
        /// 查询指定 STEP 的 OUTPUT METER 读值
        /// </summary>
        public double GetStepOutputMeter(int step) =>
            QueryDouble($":SOURce:SAFETy:RESult:STEP{step}:OMETerage?");

        /// <summary>
        /// 查询装置是否完成所有设定值之执行动作（返回 1 或 0）
        /// </summary>
        public bool IsTestCompleted() =>
            QueryInt(":SOURce:SAFETy:RESULT:COMPleted?") == 1;

        #endregion

        #region 自动回报设置

        public void SetAutoReportJudgment(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:RESult:AREPort:JUDGment:MESsage {(enable ? "ON" : "OFF")}");

        public bool GetAutoReportJudgment() =>
            QueryBoolean(":SOURce:SAFETy:RESult:AREPort:JUDGment:MESsage?");

        public void SetAutoReportOutputMeter(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:RESult:AREPort:OMEterage {(enable ? "ON" : "OFF")}");

        public bool GetAutoReportOutputMeter() =>
            QueryBoolean(":SOURce:SAFETy:RESult:AREPort:OMEterage?");

        public void SetAutoReportMeasureMeter(bool enable) =>
            SendCommandNoResponse($":SOURce:SAFETy:RESult:AREPort:MMETerage {(enable ? "ON" : "OFF")}");

        public bool GetAutoReportMeasureMeter() =>
            QueryBoolean(":SOURce:SAFETy:RESult:AREPort:MMETerage?");

        #endregion

        #region 系统与内存命令

        /// <summary>
        /// 查询 SCPI 版本
        /// </summary>
        public string GetSCPIVersion() =>
            QueryString(":SYSTem:VERSION?");

        /// <summary>
        /// 锁住或释放 LOCAL 键功能
        /// </summary>
        public void SetLocalKeyLock(bool lockKey) =>
            SendCommandNoResponse($":SYSTem:KLOck {(lockKey ? "ON" : "OFF")}");

        /// <summary>
        /// 查询 LOCAL 键是否被锁住
        /// </summary>
        public bool GetLocalKeyLock() =>
            QueryBoolean(":SYSTem:KLOck?");

        /// <summary>
        /// 查询是否为 REMOTE 端控制
        /// </summary>
        public string GetRemoteOwner() =>
            QueryString(":SYSTem:LOCK:OWNER?"); // 返回 "NONE" 或 "REMOTE"

        /// <summary>
        /// 切换至 REMOTE 控制
        /// </summary>
        public void RequestRemote() =>
            SendCommandNoResponse(":SYSTem:LOCK:REQUEST?");

        /// <summary>
        /// 切换至 LOCAL 控制
        /// </summary>
        public void ReleaseRemote() =>
            SendCommandNoResponse(":SYSTem:LOCK:RELEASE");

        /// <summary>
        /// 删除指定位置的记忆参数
        /// </summary>
        public void DeleteMemoryLocation(int registerNumber) =>
            SendCommandNoResponse($":MEMory:DELete:LOCATION {registerNumber}");

        /// <summary>
        /// 查询剩余 PRESET 参数数量
        /// </summary>
        public int GetFreePresetCount() =>
            QueryInt(":MEMory:FREE:STATE?");

        /// <summary>
        /// 查询剩余 STEP 数
        /// </summary>
        public int GetFreeStepCount() =>
            QueryInt(":MEMory:FREE:STEP?");

        /// <summary>
        /// 定义名称给指定位置的记忆体
        /// </summary>
        public void DefineStateName(string name, int registerNumber) =>
            SendCommandNoResponse($":MEMory:STATE:DEFINE \"{name}\",{registerNumber}");

        /// <summary>
        /// 根据名称查询记忆体位置
        /// </summary>
        public int GetStateRegisterByName(string name) =>
            QueryInt($":MEMory:STATE:DEFINE? \"{name}\"");

        /// <summary>
        /// 根据位置查询记忆体名称
        /// </summary>
        public string GetStateLabel(int registerNumber) =>
            QueryString($":MEMory:STATE:LABEL? {registerNumber}");

        /// <summary>
        /// 查询 *SAV/*RCL 可使用参数的最大值加 1
        /// </summary>
        public int GetNStates() =>
            QueryInt(":MEMory:NSTATES?");

        /// <summary>
        /// 读取下一个错误消息
        /// </summary>
        public string GetNextError() =>
            QueryString(":SYSTem:ERROR:NEXT?");

        #endregion

        #region 单电位（OFFSET）操作

        /// <summary>
        /// 设置单电位动作（GET 或 OFF）
        /// </summary>
        public void SetOffset(string mode) // mode = "GET" 或 "OFF"
        {
            if (mode != "GET" && mode != "OFF")
                throw new ArgumentException("mode 必须为 GET 或 OFF");
            SendCommandNoResponse($":SOURce:SAFETy:START:OFFSet {mode}");
        }

        /// <summary>
        /// 查询是否有单电位动作（返回 "GET" 或 "OFF"）
        /// </summary>
        public string GetOffset() =>
            QueryString(":SOURce:SAFETy:START:OFFSet?");

        #endregion

        #region 测试执行方法

        /// <summary>
        /// 执行当前设置的步骤测试，并返回第一个步骤的详细结果
        /// （适用于仅配置一个步骤的场景）
        /// </summary>
        public Chroma19572StepResult PerformTest(int timeoutSeconds = 30, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到设备");

            // 获取步骤数量
            int stepCount = GetStepNumber();
            if (stepCount == 0)
                throw new InvalidOperationException("没有设定任何 STEP，无法测试");

            // 启动测试
            StartTest();

            DateTime start = DateTime.Now;
            bool completed = false;

            try
            {
                // 轮询状态直到 STOPPED
                while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(200); // 每 200ms 查询一次

                    string status = GetStatus();
                    if (status == "STOPPED")
                    {
                        completed = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StopTest();
                throw;
            }

            // 确保测试停止
            StopTest();

            if (!completed)
                throw new TimeoutException($"测试超时（{timeoutSeconds} 秒），设备未停止");

            // 查询第一个步骤的结果（步骤索引为 1）
            const int stepIndex = 1;
            int judgment = GetStepJudgment(stepIndex);
            double outputMeter = GetStepOutputMeter(stepIndex);
            double measureMeter = GetStepMeasureMeter(stepIndex);

            return new Chroma19572StepResult
            {
                StepIndex = stepIndex,
                JudgmentCode = judgment,
                OutputMeter = outputMeter,
                MeasureMeter = measureMeter
            };
        }

        /// <summary>
        /// 执行单步骤测试（仅设置当前步骤并测试）
        /// </summary>
        public Chroma19572StepResult PerformSingleStepTest(int step, double current, double highLimit, double lowLimit, double time,
                                                            CancellationToken cancellationToken = default)
        {
            // 先清除所有步骤（可选），或仅设置指定步骤
            // 此处简单处理：先设置该步骤，确保其他步骤为空或忽略
            // 根据实际需求，可能需要先删除其他步骤或仅使用一个步骤
            // 这里假设工作记忆中仅有一个步骤，或者我们只关心该步骤的结果

            SetStepGBLevel(step, current);
            SetStepGBLimitHigh(step, highLimit);
            SetStepGBLimitLow(step, lowLimit);
            SetStepGBTime(step, time);

            return PerformTest((int)(time), cancellationToken); // 额外加 5 秒缓冲
        }

        /// <summary>
        /// 解析科学计数法字符串为 double
        /// </summary>
        private double ParseScientific(string value)
        {
            if (string.IsNullOrEmpty(value))
                return double.NaN;
            // 设备可能返回 "+9.910000E+37" 表示无测量值
            if (value.Contains("9.910000E+37"))
                return double.NaN;
            return double.Parse(value, System.Globalization.NumberStyles.Float);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Disconnect();
                _disposed = true;
            }
        }

        ~Chroma19572_RS232Communicator() => Dispose(false);

        #endregion
    }

    /// <summary>
    /// Chroma 19572 单步测试结果
    /// </summary>
    public class Chroma19572StepResult
    {
        public int StepIndex { get; set; }
        public int JudgmentCode { get; set; }      // 判读代码（十进制）
        public double OutputMeter { get; set; }     // 输出电流值 (A)
        public double MeasureMeter { get; set; }    // 量测电阻值 (Ω)

        /// <summary>
        /// 判断该步骤是否通过（根据判读代码）
        /// </summary>
        public bool Passed => JudgmentCode == 116;  // 0x74 = PASS
        public string FailReason
        {
            get
            {
                return JudgmentCode switch
                {
                    112 => "STOP",
                    113 => "USER STOP",
                    114 => "CAN NOT TEST",
                    115 => "TESTING",
                    116 => "PASS",
                    17 => "HIGH FAIL",
                    18 => "LOW FAIL",
                    22 => "OUTPUT A/D OVER",
                    23 => "METER A/D OVER",
                    _ => $"Unknown code {JudgmentCode}"
                };
            }
        }
    }
}