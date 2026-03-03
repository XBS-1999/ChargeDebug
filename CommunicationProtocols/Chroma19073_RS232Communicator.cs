using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DataModel;
using Log;

#pragma warning disable
namespace CommunicationProtocols
{
    /// <summary>
    /// Chroma 19073 测试类型枚举
    /// </summary>
    public enum Chroma19073TestType
    {
        ACWithstand,   // 交流耐压
        DCWithstand,   // 直流耐压
        Insulation     // 绝缘电阻
    }

    /// <summary>
    /// Chroma 19073 绝缘耐压测试仪 RS232 通信类（二进制协议）
    /// 使用 RS232Manager 进行串口通信
    /// </summary>
    public sealed class Chroma19073_RS232Communicator : IDisposable
    {
        private static readonly Lazy<Chroma19073_RS232Communicator> _instance =
            new Lazy<Chroma19073_RS232Communicator>(() => new Chroma19073_RS232Communicator());

        private string _portName;
        private bool _isConnected = false;
        private bool _disposed = false;

        // 协议常量
        private const byte FRAME_HEADER = 0xAB;
        private const int RESPONSE_TIMEOUT_MS = 500; // 响应超时

        // 设备地址
        private byte _slaveAddress = 0x01;
        private byte _masterAddress = 0x70;

        // 命令码
        private const byte CMD_IDN = 0x90;
        private const byte CMD_STOP = 0x21;
        private const byte CMD_START = 0x22;
        private const byte CMD_OFFSET_SET = 0x23;
        private const byte CMD_OFFSET_QUERY = 0xA3;
        private const byte CMD_SET_STEP_PARAMETERS = 0x24;
        private const byte CMD_STEP_PARAMETERS_QUERY = 0xA4;
        private const byte CMD_PRESET_PARAMETER_QUERY = 0xA5;
        private const byte CMD_STORE_MEMORY = 0x26;
        private const byte CMD_RECALL_MEMORY = 0x27;
        private const byte CMD_DELETE_MEMORY = 0x28;
        private const byte CMD_SYSTEM_SETTING = 0x29;
        private const byte CMD_SYSTEM_SETTING_QUERY = 0xA9;
        private const byte CMD_KEY_LOCK = 0x2A;
        private const byte CMD_INITIALIZE_ALL_STEPS = 0x2C;
        private const byte CMD_STEP_NUMBER_QUERY = 0xAD;
        private const byte CMD_REMOTE_LOCAL = 0x2E;
        private const byte CMD_REMOTE_STATUS_QUERY = 0xAE;
        private const byte CMD_SET_C_STANDARD = 0x2F;
        private const byte CMD_RESULT_QUERY = 0xB1;
        private const byte CMD_DO_GET_C_STANDARD = 0x33;
        private const byte CMD_REPLY_MESSAGE = 0x7F;

        // 校验算法委托
        public Func<byte[], byte> ChecksumAlgorithm { get; set; }

        private readonly object _commLock = new object();

        private Chroma19073_RS232Communicator()
        {
            ChecksumAlgorithm = data =>
            {
                uint sum = 0;
                foreach (byte b in data)
                    sum += b;
                return (byte)(sum & 0xFF);
            };
        }

        public static Chroma19073_RS232Communicator Instance => _instance.Value;

        #region 连接管理

        /// <summary>
        /// 连接到 Chroma 19073 测试仪
        /// </summary>
        public bool Connect(string portName, int baudRate = 9600, int dataBits = 8,
                            string stopBits = "1", string parity = "无", string flowControl = "None",
                            byte slaveAddress = 0x01, byte masterAddress = 0x70)
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
                _slaveAddress = slaveAddress;
                _masterAddress = masterAddress;

                string idn = GetIdn();
                if (string.IsNullOrEmpty(idn) ||
                    (!idn.Contains("CHROMA") && !idn.Contains("19073") && !idn.Contains("Chroma")))
                {
                    Disconnect();
                    LogService.Log($"设备响应不是预期的 Chroma 19073: {idn}");
                    return false;
                }

                _isConnected = true;
                LogService.Log($"Chroma 19073 连接成功，端口 {portName}，标识: {idn}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"连接 Chroma 19073 失败: {ex.Message}");
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
            LogService.Log("Chroma 19073 已断开连接");
        }

        public bool IsConnected => _isConnected && !string.IsNullOrEmpty(_portName) &&
                                   RS232Manager.Instance.IsChannelOpen(_portName);

        #endregion

        #region 协议帧构建与解析（保持不变）

        private byte[] BuildCommandFrame(byte command, byte[] data = null)
        {
            data = data ?? new byte[0];
            int dataLength = data.Length;
            byte length = (byte)(1 + dataLength);
            int frameSize = 1 + 1 + 1 + 1 + 1 + dataLength + 1;
            byte[] frame = new byte[frameSize];
            int idx = 0;
            frame[idx++] = FRAME_HEADER;
            frame[idx++] = _slaveAddress;
            frame[idx++] = _masterAddress;
            frame[idx++] = length;
            frame[idx++] = command;
            if (dataLength > 0)
            {
                Array.Copy(data, 0, frame, idx, dataLength);
                idx += dataLength;
            }

            byte[] dataForChecksum = new byte[idx];
            Array.Copy(frame, 0, dataForChecksum, 0, idx);
            byte checksum = ChecksumAlgorithm(dataForChecksum);
            frame[idx] = (byte)((~checksum) + 0xAC);
            return frame;
        }

        private byte[] ParseResponseFrame(byte[] response)
        {
            if (response == null || response.Length < 6)
                throw new Exception("响应帧太短");

            if (response[0] != FRAME_HEADER)
                throw new Exception("响应帧头错误");

            byte targetAddr = response[1];
            byte sourceAddr = response[2];
            byte length = response[3];
            byte command = response[4];

            if (targetAddr != _masterAddress || sourceAddr != _slaveAddress)
                throw new Exception("响应地址不匹配");

            int expectedLength = 4 + length + 1;
            if (response.Length != expectedLength)
                throw new Exception("响应帧长度不匹配");

            byte[] dataForChecksum = new byte[response.Length - 1];
            Array.Copy(response, 0, dataForChecksum, 0, response.Length - 1);
            byte calculatedChecksum = (byte)((~ChecksumAlgorithm(dataForChecksum)) + 0xAC);
            if (calculatedChecksum != response[response.Length - 1])
                throw new Exception("校验和错误");

            int dataLength = length - 1;
            byte[] data = new byte[dataLength];
            if (dataLength > 0)
                Array.Copy(response, 5, data, 0, dataLength);
            return data;
        }

        /// <summary>
        /// 发送命令并返回响应数据域（字节数组）
        /// </summary>
        private byte[] SendCommand(byte command, byte[] data = null)
        {
            lock (_commLock)
            {
                RS232Manager.Instance.ReadBuffer(_portName, true);
                byte[] frame = BuildCommandFrame(command, data);
                bool sent = RS232Manager.Instance.SendData(_portName, frame, false);
                if (!sent)
                    throw new Exception("发送命令失败");

                Thread.Sleep(RESPONSE_TIMEOUT_MS);
                byte[] received = RS232Manager.Instance.ReadBuffer(_portName, true);
                if (received.Length == 0)
                    throw new Exception("未收到响应");

                try
                {
                    return ParseResponseFrame(received);
                }
                catch (Exception ex)
                {
                    LogService.Log($"解析响应失败: {ex.Message}, 原始数据: {BitConverter.ToString(received)}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 检查回复消息（命令 0x7F 的响应数据），若状态非0则抛出异常
        /// </summary>
        private void CheckReplyMessage(byte[] responseData)
        {
            if (responseData == null || responseData.Length < 1)
                throw new Exception("无效的回复消息");
            byte status = responseData[0];
            if (status == 0)
                return;
            string error = status switch
            {
                1 => "Command Error (Include Execution Error)",
                2 => "Parameter Error",
                _ => $"Unknown error code: {status}"
            };
            throw new Exception($"设备返回错误: {error}");
        }

        #endregion

        #region 基础命令

        public string Query(byte command, byte[] data = null)
        {
            byte[] responseData = SendCommand(command, data);
            return Encoding.ASCII.GetString(responseData).TrimEnd('\0');
        }

        public string GetIdn()
        {
            return Query(CMD_IDN);
        }

        #endregion

        #region 命令封装方法

        /// <summary>
        /// 停止测试 (0x21)
        /// </summary>
        public void StopTest()
        {
            byte[] response = SendCommand(CMD_STOP);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 启动测试 (0x22)
        /// </summary>
        public void StartTest()
        {
            byte[] response = SendCommand(CMD_START);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 切换 OFFSET 状态 (0x23)
        /// </summary>
        /// <param name="mode">0:OFF, 2:GET</param>
        public void SetOffset(byte mode)
        {
            if (mode != 0 && mode != 2)
                throw new ArgumentException("Offset mode must be 0 (OFF) or 2 (GET)");
            byte[] response = SendCommand(CMD_OFFSET_SET, new byte[] { mode });
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问 OFFSET 状态 (0xA3)
        /// </summary>
        /// <returns>0:Off, 1:On, 2:Getting</returns>
        public byte GetOffsetStatus()
        {
            byte[] response = SendCommand(CMD_OFFSET_QUERY);
            if (response.Length < 1)
                throw new Exception("无效的响应数据");
            return response[0];
        }

        /// <summary>
        /// 设置步骤参数 (0x24)
        /// </summary>
        /// <param name="stepData">28字节步骤数据</param>
        public void SetStepParameters(byte[] stepData)
        {
            if (stepData == null || stepData.Length != 28)
                throw new ArgumentException("步骤数据必须为 28 字节", nameof(stepData));
            byte[] response = SendCommand(CMD_SET_STEP_PARAMETERS, stepData);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问步骤参数 (0xA4)
        /// </summary>
        /// <param name="stepIndex">步骤序号 1~10</param>
        /// <returns>28字节原始数据</returns>
        public byte[] GetStepParameters(byte stepIndex)
        {
            if (stepIndex < 1 || stepIndex > 10)
                throw new ArgumentOutOfRangeException(nameof(stepIndex), "Step index must be 1~10");
            byte[] response = SendCommand(CMD_STEP_PARAMETERS_QUERY, new byte[] { stepIndex });
            if (response.Length != 28)
                throw new Exception($"期望28字节响应，实际收到 {response.Length} 字节");
            return response;
        }

        /// <summary>
        /// 询问 Preset 参数 (0xA5)
        /// </summary>
        /// <returns>6字节原始数据</returns>
        public byte[] GetPresetParameters()
        {
            byte[] response = SendCommand(CMD_PRESET_PARAMETER_QUERY);
            if (response.Length != 6)
                throw new Exception($"期望6字节响应，实际收到 {response.Length} 字节");
            return response;
        }

        /// <summary>
        /// 存储内存 (0x26)
        /// </summary>
        /// <param name="memoryNumber">内存序号 1~60</param>
        /// <param name="memoryName">内存名称 0~10字符（自动转为大写）</param>
        public void StoreMemory(byte memoryNumber, string memoryName)
        {
            if (memoryNumber < 1 || memoryNumber > 60)
                throw new ArgumentOutOfRangeException(nameof(memoryNumber), "Memory number must be 1~60");
            if (memoryName.Length > 10)
                throw new ArgumentException("Memory name cannot exceed 10 characters", nameof(memoryName));
            memoryName = memoryName.ToUpperInvariant();
            byte[] nameBytes = Encoding.ASCII.GetBytes(memoryName);
            byte[] data = new byte[1 + nameBytes.Length];
            data[0] = memoryNumber;
            Array.Copy(nameBytes, 0, data, 1, nameBytes.Length);
            byte[] response = SendCommand(CMD_STORE_MEMORY, data);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 调出内存 (0x27)
        /// </summary>
        /// <param name="memoryNumber">内存序号 1~60</param>
        public void RecallMemory(byte memoryNumber)
        {
            if (memoryNumber < 1 || memoryNumber > 60)
                throw new ArgumentOutOfRangeException(nameof(memoryNumber), "Memory number must be 1~60");
            byte[] response = SendCommand(CMD_RECALL_MEMORY, new byte[] { memoryNumber });
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 删除内存 (0x28)
        /// </summary>
        /// <param name="memoryNumber">内存序号 0~60（0表示清除工作内存）</param>
        public void DeleteMemory(byte memoryNumber)
        {
            if (memoryNumber > 60)
                throw new ArgumentOutOfRangeException(nameof(memoryNumber), "Memory number must be 0~60");
            byte[] response = SendCommand(CMD_DELETE_MEMORY, new byte[] { memoryNumber });
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 设置系统参数 (0x29)
        /// </summary>
        /// <param name="contrast">对比度 1~15</param>
        /// <param name="buzzerVolume">蜂鸣器音量 0:OFF,1:Low,2:Medium,3:High</param>
        /// <param name="en50191">EN50191 0:OFF,1:ON</param>
        /// <param name="dc50vAgc">DC 50V AGC 0:OFF,1:ON</param>
        /// <param name="passOn">Pass On 100ms单位 0~100 (0:OFF)</param>
        /// <param name="endOfStep">END OF STEP 0:OFF,1:ON</param>
        /// <param name="eot">EOT 0:END OF TEST,1:END OF TIMER</param>
        public void SetSystemSetting(byte contrast, byte buzzerVolume, byte en50191, byte dc50vAgc,
                                      byte passOn, byte endOfStep, byte eot)
        {
            // 可添加范围校验
            byte[] data = new byte[] { contrast, buzzerVolume, en50191, dc50vAgc, passOn, endOfStep, eot };
            byte[] response = SendCommand(CMD_SYSTEM_SETTING, data);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问系统参数 (0xA9)
        /// </summary>
        /// <returns>7字节原始数据</returns>
        public byte[] GetSystemSetting()
        {
            byte[] response = SendCommand(CMD_SYSTEM_SETTING_QUERY);
            if (response.Length != 7)
                throw new Exception($"期望7字节响应，实际收到 {response.Length} 字节");
            return response;
        }

        /// <summary>
        /// 切换键盘锁定状态 (0x2A)
        /// </summary>
        /// <param name="mode">0: keyboard lock OFF, recall key lock OFF; 1: keyboard lock ON, recall key lock OFF; 2: keyboard lock ON, recall key lock ON</param>
        public void SetKeyLock(byte mode)
        {
            if (mode > 2)
                throw new ArgumentOutOfRangeException(nameof(mode), "Key lock mode must be 0,1,2");
            byte[] response = SendCommand(CMD_KEY_LOCK, new byte[] { mode });
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 初始化所有步骤参数为默认值 (0x2C)
        /// </summary>
        public void InitializeAllSteps()
        {
            byte[] response = SendCommand(CMD_INITIALIZE_ALL_STEPS);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问已设定的步骤个数 (0xAD)
        /// </summary>
        /// <returns>步骤个数</returns>
        public byte GetStepNumber()
        {
            byte[] response = SendCommand(CMD_STEP_NUMBER_QUERY);
            if (response.Length < 1)
                throw new Exception("无效的响应数据");
            return response[0];
        }

        /// <summary>
        /// 切换远程/本地控制 (0x2E)
        /// </summary>
        /// <param name="mode">0:Local, 1:Remote, 2:Remote and Local Lockout</param>
        public void SetRemoteLocal(byte mode)
        {
            if (mode > 2)
                throw new ArgumentOutOfRangeException(nameof(mode), "Remote/Local mode must be 0,1,2");
            byte[] response = SendCommand(CMD_REMOTE_LOCAL, new byte[] { mode });
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问远程控制状态 (0xAE)
        /// </summary>
        /// <returns>0:Local, 1:Remote, 2:Remote and Local Lockout</returns>
        public byte GetRemoteStatus()
        {
            byte[] response = SendCommand(CMD_REMOTE_STATUS_QUERY);
            if (response.Length < 1)
                throw new Exception("无效的响应数据");
            return response[0];
        }

        /// <summary>
        /// 设置OS模式的C标准值 (0x2F)
        /// </summary>
        /// <param name="stepIndex">步骤索引 1~10</param>
        /// <param name="cStandard">C标准值 (pF)，小端序4字节</param>
        /// <param name="range">量程 1~3</param>
        public void SetCStandard(byte stepIndex, uint cStandard, byte range)
        {
            byte[] data = new byte[6];
            data[0] = stepIndex;
            byte[] cStdBytes = ToLittleEndian(cStandard);
            Array.Copy(cStdBytes, 0, data, 1, 4);
            data[5] = range;
            byte[] response = SendCommand(CMD_SET_C_STANDARD, data);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 查询测试结果及量测值 (0xB1)
        /// </summary>
        /// <param name="stepSpan">步幅扩展 0~10（0表示最后一个执行的步骤）</param>
        /// <param name="itemMask">量测值遮罩（位掩码，具体含义见协议）</param>
        /// <returns>原始响应字节数组，需根据模式解析</returns>
        public byte[] GetResult(byte stepSpan, byte itemMask)
        {
            if (stepSpan > 10)
                throw new ArgumentOutOfRangeException(nameof(stepSpan), "Step span must be 0-10");
            byte[] data = new byte[] { stepSpan, itemMask };
            byte[] response = SendCommand(CMD_RESULT_QUERY, data);
            return response; // 返回原始数据，由调用者按协议解析
        }

        /// <summary>
        /// 启动OS模式的C标准值抓取功能 (0x33)
        /// </summary>
        public void DoGetCStandard()
        {
            byte[] response = SendCommand(CMD_DO_GET_C_STANDARD);
            CheckReplyMessage(response);
        }

        /// <summary>
        /// 询问前一命令的执行结果 (0x7F)
        /// </summary>
        /// <returns>0:OK, 1:Command Error, 2:Parameter Error</returns>
        public byte GetReplyMessageStatus()
        {
            byte[] response = SendCommand(CMD_REPLY_MESSAGE);
            if (response.Length < 1)
                throw new Exception("无效的响应数据");
            return response[0];
        }

        #endregion

        #region 辅助方法：单位转换等

        /// <summary>
        /// 将16位无符号整数转换为小端序字节数组（2字节）
        /// </summary>
        private byte[] ToLittleEndian(ushort value)
        {
            byte[] bytes = new byte[2];
            bytes[0] = (byte)(value & 0xFF);
            bytes[1] = (byte)((value >> 8) & 0xFF);
            return bytes;
        }

        /// <summary>
        /// 将32位无符号整数转换为小端序字节数组（4字节）
        /// </summary>
        private byte[] ToLittleEndian(uint value)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(value & 0xFF);
            bytes[1] = (byte)((value >> 8) & 0xFF);
            bytes[2] = (byte)((value >> 16) & 0xFF);
            bytes[3] = (byte)((value >> 24) & 0xFF);
            return bytes;
        }

        /// <summary>
        /// 构造 AC 模式步骤参数数据（28 字节）
        /// </summary>
        private byte[] BuildACStepData(byte stepIndex, ushort sourceVoltage, ushort rampTime,
                                        ushort testTime, ushort fallTime, uint highLimit,
                                        uint lowLimit, uint arcLimit)
        {
            // 参数校验省略...
            byte[] data = new byte[28];
            int idx = 0;
            data[idx++] = stepIndex;
            data[idx++] = 0x01; // AC mode
            Buffer.BlockCopy(ToLittleEndian(sourceVoltage), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(rampTime), 0, data, idx, 2); idx += 2;
            idx += 2; // Reserved
            Buffer.BlockCopy(ToLittleEndian(testTime), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(fallTime), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(highLimit), 0, data, idx, 4); idx += 4;
            Buffer.BlockCopy(ToLittleEndian(lowLimit), 0, data, idx, 4); idx += 4;
            Buffer.BlockCopy(ToLittleEndian(arcLimit), 0, data, idx, 4); idx += 4;
            // 剩余4字节保留默认为0
            return data;
        }

        /// <summary>
        /// 构造 DC 模式步骤参数数据（28 字节）
        /// </summary>
        private byte[] BuildDCStepData(byte stepIndex, ushort sourceVoltage, ushort rampTime,
                                        ushort testTime, ushort fallTime,uint highLimit,
                                        uint lowLimit, uint arcLimit, bool inrushOn)
        {
            byte[] data = new byte[28];
            int idx = 0;
            data[idx++] = stepIndex;
            data[idx++] = 0x02; // DC mode
            Buffer.BlockCopy(ToLittleEndian(sourceVoltage), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(rampTime), 0, data, idx, 2); idx += 2;
            idx += 2;
            Buffer.BlockCopy(ToLittleEndian(testTime), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(fallTime), 0, data, idx, 2); idx += 2;
            Buffer.BlockCopy(ToLittleEndian(highLimit), 0, data, idx, 4); idx += 4;
            Buffer.BlockCopy(ToLittleEndian(lowLimit), 0, data, idx, 4); idx += 4;
            Buffer.BlockCopy(ToLittleEndian(arcLimit), 0, data, idx, 4); idx += 4;
            uint inrushValue = inrushOn ? 10000u : 0u;
            Buffer.BlockCopy(ToLittleEndian(inrushValue), 0, data, idx, 4); idx += 4;
            return data;
        }

        /// <summary>
        /// 构造 IR 模式步骤参数数据（28 字节）
        /// </summary>
        private byte[] BuildIRStepData(byte stepIndex, ushort sourceVoltage, ushort rampTime,
                                       ushort testTime, ushort fallTime,
                                       uint highLimit, uint lowLimit)
        {
            byte[] data = new byte[28];
            int idx = 0;
            data[idx++] = stepIndex;
            data[idx++] = 0x03; // IR mode
            Buffer.BlockCopy(ToLittleEndian(sourceVoltage), 0, data, idx, 2); 
            idx += 2;
            Buffer.BlockCopy(ToLittleEndian(rampTime), 0, data, idx, 2); 
            idx += 2;
            idx += 2;
            Buffer.BlockCopy(ToLittleEndian(testTime), 0, data, idx, 2); 
            idx += 2;
            Buffer.BlockCopy(ToLittleEndian(fallTime), 0, data, idx, 2); 
            idx += 2;
            Buffer.BlockCopy(ToLittleEndian(highLimit), 0, data, idx, 4);           // 电阻高限（100kΩ）
            idx += 4; 
            Buffer.BlockCopy(ToLittleEndian(lowLimit), 0, data, idx, 4);           // 电阻低限（100kΩ）
            idx += 4;  
            data[idx] = 6;        //RANGE
            idx += 3; // Reserved (原电弧字段)
            idx += 4; // Reserved (原 Inrush 字段)
            return data;
        }

        #endregion

        #region 测试执行方法

        /// <summary>
        /// 执行当前已设置的步骤测试，并返回测试结果（使用新命令 0x22 启动，0x1B 读取结果）
        /// </summary>
        /// <param name="timeoutSeconds">总超时时间（秒）</param>
        /// <returns>测试结果对象（包含测量值，但 PASS/FAIL 需由调用者根据限值判断）</returns>
        public Chroma19073Result PerformTest(int timeoutSeconds = 10, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到设备");

            // 1. 启动测试
            StartTest();  // 使用 CMD_START (0x22)

            DateTime start = DateTime.Now;
            byte[] resultData = null;
            bool completed = false;

            try
            {
                // 2. 轮询等待测试结果
                while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
                {
                    cancellationToken.ThrowIfCancellationRequested();   // 检查取消

                    Thread.Sleep(1000); // 每秒查询一次

                    resultData = GetResult(0, 0x00);

                    if (resultData?.Length >= 3)
                    {
                        if (resultData[2] == 0x73) continue;           // 测试中
                                                                       // 测试完成
                        //Thread.Sleep(100);
                        resultData = GetResult(0, 0xFF);
                        completed = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 取消时立即停止设备，并重新抛出异常
                StopTest();
                throw;
            }

            StopTest();

            if (!completed)
                throw new TimeoutException($"测试超时（{timeoutSeconds}秒），未收到有效结果");

            // 3. 解析结果数据（包括状态码判断通过/失败）
            return ParseResultData(resultData, resultData[2]);
        }

        /// <summary>
        /// 解析 GetResult 返回的 19 字节数据（itemMask=0xFF 时）
        /// 数据顺序按 Item Number 从小到大，各字段长度见协议表格
        /// </summary>
        private Chroma19073Result ParseResultData(byte[] data,byte status)
        {
            if (data == null || data.Length < 19)
                throw new ArgumentException("无效的结果数据");

            var result = new Chroma19073Result();

            int idx = 2;

            // 字节2：状态码（第三个字节）
            //byte status = data[idx];
            idx += 2;

            // Item 1: Mode (1 byte)
            byte modeByte = data[idx];
            result.Mode = modeByte switch
            {
                1 => "AC",
                2 => "DC",
                3 => "IR",
                _ => $"Unknown({modeByte})"
            };
            idx += 1;

            // 解析 Meter1（电压）
            ushort source = BitConverter.ToUInt16(data, idx);
            result.ActualVoltage = (source == 31000) ? double.NaN : source;
            idx += 2;

            // 解析 Meter2（电流/电阻）
            uint meter2 = BitConverter.ToUInt32(data, idx);
            if (meter2 == 1100000000)
            {
                if (result.Mode == "IR")
                    result.InsulationResistance = double.NaN;
                else
                    result.ActualCurrent = double.NaN;
            }
            else
            {
                if (result.Mode == "IR")
                    result.InsulationResistance = (double)meter2 / 10000; // 100kΩ → GΩ
                else
                    result.ActualCurrent = (double)meter2 / 10000;            // 100nA → mA
            }
            idx += 4;

            // 解析 Meter3（冲击电流/保留）
            uint meter3 = BitConverter.ToUInt32(data, idx);
            if (meter3 != 1100000000 && result.Mode == "DC")
                result.InrushCurrent = meter3 * 1e-7;
            idx += 4;

            // 解析上升时间
            ushort ramp = BitConverter.ToUInt16(data, idx);
            result.RampTime = (ramp == 31000) ? double.NaN : ramp * 0.1;
            idx += 2;

            // 解析 Dwell 时间
            ushort dwell = BitConverter.ToUInt16(data, idx);
            result.DwellTime = (dwell == 31000) ? double.NaN : dwell * 0.1;
            idx += 2;

            // 解析测试时间
            ushort test = BitConverter.ToUInt16(data, idx);
            result.TestTime = (test == 31000) ? double.NaN : test * 0.1;
            idx += 2;

            // 解析失效时间
            ushort fail = BitConverter.ToUInt16(data, idx);
            result.FailTime = (fail == 31000) ? double.NaN : fail * 0.1;

            // 根据状态码判断测试结果
            if (status == 0x74)
            {
                result.Passed = true;
                result.FailReason = "PASS";
            }
            else
            {
                result.Passed = false;
                // 通用状态码 (0x70~0x73)
                if (status >= 0x70 && status <= 0x73)
                {
                    result.FailReason = status switch
                    {
                        0x70 => "STOP USER INTERRUPT",
                        0x71 => "CAN NOT TEST",
                        0x72 => "TESTING PASS SKIPPED GFI TRIPPED",
                        0x73 => "SLAVE FAIL Cs/SHORT FAIL",
                        _ => $"Unknown status 0x{status:X2}"
                    };
                }
                else
                {
                    // 根据模式解析具体失败码
                    switch (result.Mode)
                    {
                        case "AC":
                            result.FailReason = status switch
                            {
                                0x11 => "HIGH FAIL",
                                0x12 => "SHORT FAIL",
                                0x13 => "LOW FAIL",
                                0x14 => "OPEN FAIL",
                                0x15 => "ARC FAIL",
                                0x16 => "I/O FAIL",
                                0x17 => "NO OUTPUT VOLTAGE",
                                0x1C => "OVER CURRENT",
                                _ => $"Unknown AC fail code 0x{status:X2}"
                            };
                            break;
                        case "DC":
                            result.FailReason = status switch
                            {
                                0x21 => "HIGH FAIL",
                                0x22 => "SHORT FAIL",
                                0x23 => "LOW FAIL",
                                0x24 => "OPEN FAIL",
                                0x25 => "ARC FAIL",
                                0x26 => "I/O FAIL",
                                0x27 => "NO OUTPUT VOLTAGE",
                                0x28 => "OVER CURRENT",
                                _ => $"Unknown DC fail code 0x{status:X2}"
                            };
                            break;
                        case "IR":
                            result.FailReason = status switch
                            {
                                0x31 => "HIGH FAIL",
                                0x32 => "SHORT FAIL",
                                0x34 => "LOW FAIL",
                                0x35 => "OPEN FAIL",
                                0x36 => "ARC FAIL",
                                0x37 => "I/O FAIL",
                                _ => $"Unknown IR fail code 0x{status:X2}"
                            };
                            break;
                        default:
                            result.FailReason = $"Unknown mode {result.Mode} with status 0x{status:X2}";
                            break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 执行交流耐压测试（使用 0x24 命令设置参数）
        /// </summary>
        /// <param name="voltage">测试电压 (V)，范围 50~5000 或 0</param>
        /// <param name="hiLimit">电流高限 (A)，将转换为 100nA 单位</param>
        /// <param name="loLimit">电流低限 (A)，0 表示关闭</param>
        /// <param name="rampTime">上升时间 (秒)，单位 0.1s，最大 999.0s</param>
        /// <param name="testTime">测试时间 (秒)，0 表示连续</param>
        /// <param name="fallTime">下降时间 (秒)</param>
        /// <param name="arcLimit">电弧侦测限值 (A)，0 表示关闭</param>
        /// <param name="stepIndex">步骤索引，默认为 1</param>
        /// <returns>测试结果</returns>
        public Chroma19073Result PerformACWithstandTest(double voltage, double hiLimit, double loLimit,
                                                        double rampTime = 1.0, double testTime = 5.0,
                                                        double fallTime = 0, double arcLimit = 0,
                                                        byte stepIndex = 1, 
                                                        CancellationToken cancellationToken = default)
        {
            // 单位转换
            ushort srcVoltage = (ushort)Math.Round(voltage);
            ushort ramp = (ushort)Math.Round(rampTime * 10);        // 100ms 单位
            ushort test = (ushort)Math.Round(testTime * 10);
            ushort fall = (ushort)Math.Round(fallTime * 10);

            // 电流转换：A -> 100nA (乘以 1e7)
            uint high = (uint)Math.Round(hiLimit * 10000);
            uint low = (uint)Math.Round(loLimit * 10000);
            uint arc = (uint)Math.Round(arcLimit * 10000);

            // 构造 AC 步骤数据
            byte[] stepData = BuildACStepData(stepIndex, srcVoltage, ramp, test, fall, high, low, arc);
            SetStepParameters(stepData);

            return PerformTest((int)(rampTime + testTime + fallTime), cancellationToken);
        }

        /// <summary>
        /// 执行直流耐压测试（使用 0x24 命令设置参数）
        /// </summary>
        /// <param name="voltage">测试电压 (V)，范围 50~6000 或 0</param>
        /// <param name="hiLimit">电流高限 (A)</param>
        /// <param name="loLimit">电流低限 (A)，0 关闭</param>
        /// <param name="rampTime">上升时间 (秒)</param>
        /// <param name="testTime">测试时间 (秒)，0 连续</param>
        /// <param name="fallTime">下降时间 (秒)</param>
        /// <param name="arcLimit">电弧侦测限值 (A)，0 关闭</param>
        /// <param name="inrushOn">是否开启 Inrush 测试</param>
        /// <param name="stepIndex">步骤索引，默认为 1</param>
        /// <returns>测试结果</returns>
        public Chroma19073Result PerformDCWithstandTest(double voltage, double hiLimit, double loLimit,
                                                        double rampTime = 1.0, double testTime = 5.0, 
                                                        double fallTime = 0, double arcLimit = 0, 
                                                        bool inrushOn = false, byte stepIndex = 1,
                                                        CancellationToken cancellationToken = default)
        {
            // 参数校验
            if (voltage < 0 || voltage > 6000)
                throw new ArgumentOutOfRangeException(nameof(voltage), "直流电压必须介于 0~6000V 之间");
            if (hiLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(hiLimit), "高限值不能为负数");
            if (loLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(loLimit), "低限值不能为负数");
            if (loLimit > hiLimit && hiLimit > 0)
                throw new ArgumentException("低限不能大于高限");

            // 单位转换
            ushort srcVoltage = (ushort)Math.Round(voltage);
            ushort ramp = (ushort)Math.Round(rampTime * 10);          // 100ms → 秒
            ushort test = (ushort)Math.Round(testTime * 10);
            ushort fall = (ushort)Math.Round(fallTime * 10);

            uint high = (uint)Math.Round(hiLimit * 10000);              // mA → 100nA
            uint low = (uint)Math.Round(loLimit * 10000);
            uint arc = (uint)Math.Round(arcLimit * 10000);

            byte[] stepData = BuildDCStepData(stepIndex, srcVoltage, ramp, test, fall, high, low, arc, inrushOn);
            SetStepParameters(stepData);

            return PerformTest((int)(rampTime + testTime + fallTime), cancellationToken);
        }

        /// <summary>
        /// 执行绝缘电阻测试（使用 0x24 命令设置参数）
        /// </summary>
        /// <param name="voltage">测试电压 (V)，范围 50~1000 或 0</param>
        /// <param name="hiLimit">电阻高限 (MΩ) – 通常绝缘电阻测试只有高限，低限可设为 0 表示忽略</param>
        /// <param name="loLimit">电阻低限 (MΩ)，一般设为 0 或与高限相同</param>
        /// <param name="rampTime">上升时间 (秒)</param>
        /// <param name="testTime">测试时间 (秒)，0 表示连续</param>
        /// <param name="fallTime">下降时间 (秒) – 绝缘测试可能不需要，但保留参数以保持接口一致性</param>
        /// <param name="stepIndex">步骤索引，默认为 1</param>
        /// <returns>测试结果</returns>
        public Chroma19073Result PerformInsulationTest(double voltage, double hiLimit, double loLimit,
                                                       double rampTime, double testTime,
                                                       double fallTime, byte stepIndex,
                                                       CancellationToken cancellationToken = default)
        {
            // 参数校验
            if (voltage < 0 || voltage > 1000) // 假设 IR 最大电压 1000V
                throw new ArgumentOutOfRangeException(nameof(voltage), "绝缘电阻测试电压必须介于 0~1000V 之间");
            if (hiLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(hiLimit), "电阻高限不能为负数");
            if (loLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(loLimit), "电阻低限不能为负数");

            // 单位转换
            ushort srcVoltage = (ushort)Math.Round(voltage);
            ushort ramp = (ushort)Math.Round(rampTime * 10);          // 100ms → 秒
            ushort test = (ushort)Math.Round(testTime * 10);
            ushort fall = (ushort)Math.Round(fallTime * 10);

            // 电阻转换：MΩ → 100kΩ
            uint high = (uint)Math.Round(hiLimit * 10.0);
            uint low = (uint)Math.Round(loLimit * 10.0);

            // 构造 IR 步骤数据
            byte[] stepData = BuildIRStepData(stepIndex, srcVoltage, ramp, test, fall, high, low);
            SetStepParameters(stepData);

            return PerformTest((int)(rampTime + testTime + fallTime), cancellationToken);
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

        ~Chroma19073_RS232Communicator() => Dispose(false);

        #endregion
    }

    /// <summary>
    /// 测试结果类
    /// </summary>
    public class Chroma19073Result
    {
        public bool Passed { get; set; }                     // 是否通过（需由调用者根据限值判断）
        public double ActualVoltage { get; set; }            // 实测电压 (V)
        public double ActualCurrent { get; set; }            // 实测电流 (A) – 用于 AC/DC 模式
        public double InsulationResistance { get; set; }     // 绝缘电阻 (Ω) – 用于 IR 模式
        public string FailReason { get; set; } = string.Empty;

        // 新增属性（从 0x1B 响应中解析）
        public string Mode { get; set; } = "Unknown";        // 测试模式："AC", "DC", "IR"
        public double RampTime { get; set; } = double.NaN;   // 上升时间 (秒)
        public double DwellTime { get; set; } = double.NaN;  // Dwell 时间 (秒) – DC 模式专用
        public double TestTime { get; set; } = double.NaN;   // 测试时间 (秒)
        public double FailTime { get; set; } = double.NaN;   // 失效时间 (秒)
        public double InrushCurrent { get; set; } = double.NaN; // 冲击电流 (A) – DC 模式专用
    }
}