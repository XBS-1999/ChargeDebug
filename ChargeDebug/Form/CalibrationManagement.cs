using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraLayout;
using DevExpress.Utils;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraEditors.Controls;
using System.Data.SQLite;
using ChargeDebug.Service;
using DataModel;
using Log;
using CommunicationProtocols;
using DevExpress.XtraTreeList.Nodes;
using Aspose.Pdf.Operators;
using System.Text;
using DevExpress.XtraCharts.Native;
using DbcParserLib.Model;
using DevExpress.DataProcessing;
using DevExpress.Charts.Native;

namespace ChargeDebug.Form
{
    /// <summary>
    /// 校准管理用户控件
    /// 负责管理校准信号、执行电压/电流校准操作
    /// </summary>
    public partial class CalibrationManagement : XtraUserControl
    {
        #region 字段声明

        private string sqladdress = "";
        private TreeList treeList;
        private List<EquipmentModel> equipmentList;

        // 组合框控件
        private ComboBoxEdit cbVoltageSource;
        private ComboBoxEdit cbVoltmeter;
        private ComboBoxEdit cbAmmeter;

        private SimpleButton btnVoltageCalibration;
        private SimpleButton btnStopCalibration;

        // 协议列表
        private List<ModbusSignal> voltageSourceProtocols = new List<ModbusSignal>();
        private List<ModbusSignal> voltmeterProtocols = new List<ModbusSignal>();
        private List<SignalInfo> treeSignalProtocols = new List<SignalInfo>();

        // 进度条控件
        private ProgressBarControl progressBar;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="sqladdress">SQLite数据库地址</param>
        /// <param name="equipmentList">设备列表</param>
        public CalibrationManagement(string sqladdress, List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            this.sqladdress = sqladdress;
            InitializeComponent();
            InitializeUI();
            this.Load += CalibrationManagement_Load;

            // 订阅数据接收事件
            //RS485Manager.Instance.DataReceived += RS485_DataReceived;
        }

        //private void RS485_DataReceived(object? sender, DataReceivedEventArgs e)
        //{
        //    // 处理接收到的数据，例如记录日志或解析响应
        //    LogService.Log($"接收到来自 {e.PortName} 的数据: {BitConverter.ToString(e.Data)}");

        //    // 这里可以根据需要处理数据，例如匹配请求与响应
        //}

        /// <summary>
        /// 窗体加载事件处理
        /// </summary>
        private void CalibrationManagement_Load(object? sender, EventArgs e)
        {
            LoadData();
        }

        #endregion

        #region 数据管理方法

        /// <summary>
        /// 从数据库加载校准信号数据
        /// </summary>
        private void LoadData()
        {
            treeList.BeginUpdate();
            treeList.ClearNodes();
            try
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    var calibrationsignals = SQLite_Service.GetCalibrationSignals(conn);

                    foreach (var calibrationsignal in calibrationsignals)
                    {
                        var node = treeList.AppendNode(new object[]
                        {
                            calibrationsignal.DeviceName,
                            calibrationsignal.SignalName,
                            calibrationsignal.SignalType,
                            calibrationsignal.ReadTime,
                            calibrationsignal.RatingVoltageCurrent,
                            calibrationsignal.CalibrationNumber,
                            calibrationsignal.ScaleFactor,
                            calibrationsignal.ZeroFactor,
                            "", // 设备电压采样值
                            "", // 实际电压测量值
                            "", // 设备电流采样值
                            "", // 实际电流测量值
                            "", // 校准精度
                            ""  // 校准结果
                        }, null);

                        // 设置节点的 Tag 为信号 ID，以便后续操作
                        node.Tag = calibrationsignal.SignalID;
                        // 设置排序值
                        node.SetValue("Orders", calibrationsignal.Orders);

                        // 默认勾选所有节点
                        node.Checked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载校准管理数据失败: {ex.Message}");
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

        /// <summary>
        /// 更新设备列表
        /// </summary>
        /// <param name="equipmentList">新的设备列表</param>
        public void UpdateDcNumber(List<EquipmentModel> equipmentList)
        {
            this.equipmentList = equipmentList;
            // 清除所有旧布局
            this.Controls.Clear();
            InitializeUI();
            // 加载数据
            LoadData();
        }

        #endregion

        #region 事件处理方法

        /// <summary>
        /// 添加信号按钮点击事件
        /// </summary>
        private void BtnAddSignal_Click(object? sender, EventArgs e)
        {
            try
            {
                // 创建并显示添加信号的对话框
                using (var addForm = new AddEditCalibrationSignalForm(null, sqladdress, equipmentList))
                {
                    if (addForm.ShowDialog() == DialogResult.OK)
                    {
                        // 刷新数据
                        LoadData();
                        XtraMessageBox.Show("信号添加成功!");
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"添加信号失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 编辑信号按钮点击事件
        /// </summary>
        private void BtnEditSignal_Click(object? sender, EventArgs e)
        {
            if (treeList.FocusedNode == null)
            {
                XtraMessageBox.Show("请先选择一个信号进行编辑!");
                return;
            }

            try
            {
                // 获取选中信号的ID
                long signalId = (long)treeList.FocusedNode.Tag;

                // 创建并显示编辑信号的对话框
                using (var editForm = new AddEditCalibrationSignalForm(signalId, sqladdress, equipmentList))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        // 刷新数据
                        LoadData();
                        XtraMessageBox.Show("信号编辑成功!");
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"编辑信号失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除信号按钮点击事件
        /// </summary>
        private void BtnDeleteSignal_Click(object? sender, EventArgs e)
        {
            // 获取所有选中的节点
            var selectedNodes = treeList.Selection;
            int num = selectedNodes.Count;

            if (selectedNodes.Count == 0)
            {
                XtraMessageBox.Show("请先选择要删除的信号!");
                return;
            }

            // 构建确认消息
            string message = selectedNodes.Count == 1
                ? $"确定要删除设备 '{selectedNodes[0].GetValue("DeviceName")}' 的信号 '{selectedNodes[0].GetValue("SignalName")}' 吗？"
                : $"确定要删除选中的 {num} 个信号吗？";

            // 确认删除对话框
            if (XtraMessageBox.Show(message,
                                  "确认删除",
                                  MessageBoxButtons.YesNo,
                                  MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 获取要删除的信号ID列表
                            var signalIds = selectedNodes
                                .Select(node => (long)node.Tag)
                                .ToList();

                            // 执行批量删除操作
                            bool deleteSuccess = SQLite_Service.DeleteCalibrationSignals(conn, signalIds);

                            if (deleteSuccess)
                            {
                                // 重新排序剩余的信号
                                bool reorderSuccess = SQLite_Service.ReorderCalibrationSignals(conn);

                                if (reorderSuccess)
                                {
                                    transaction.Commit();

                                    // 刷新数据
                                    LoadData();
                                    XtraMessageBox.Show($"成功删除 {num} 个信号!");
                                }
                                else
                                {
                                    transaction.Rollback();
                                    XtraMessageBox.Show("重新排序失败，操作已回滚!");
                                }
                            }
                            else
                            {
                                transaction.Rollback();
                                XtraMessageBox.Show("删除失败，操作已回滚!");
                            }
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"删除信号失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 电压校准按钮点击事件
        /// </summary>
        private async void BtnVoltageCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 禁用按钮，防止重复点击
                btnVoltageCalibration.Enabled = false;
                btnStopCalibration.Enabled = true;

                // 执行电压校准
                bool success = await ExecuteVoltageCalibration();

                if (success)
                {
                    XtraMessageBox.Show("电压校准完成!");
                }
                else
                {
                    XtraMessageBox.Show("电压校准失败，请查看日志!");
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"电压校准失败: {ex.Message}");
            }
            finally
            {
                // 重新启用按钮
                btnVoltageCalibration.Enabled = true;
                btnStopCalibration.Enabled = false;
            }
        }

        /// <summary>
        /// 停止校准按钮点击事件
        /// </summary>
        private void BtnStopCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 发送停止校准命令
                byte[] calibrationCommand = new byte[] { 0x63, 0x10, 0x00, 0x02, 0x00, 0x01, 0x02, 0x00, 0x00 };
                bool sendSuccess = RS485Manager.Instance.SendData("COM3", calibrationCommand);

                if (sendSuccess)
                {
                    XtraMessageBox.Show("已发送停止校准命令!");
                }
                else
                {
                    XtraMessageBox.Show("发送停止校准命令失败!");
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"停止校准失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 充电电流校准按钮点击事件
        /// </summary>
        private void BtnChargingCurrentCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 实现充电电流校准逻辑
                XtraMessageBox.Show("充电电流校准功能尚未实现");
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"充电电流校准失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 放电电流校准按钮点击事件
        /// </summary>
        private void BtnDischargingCurrentCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 实现放电电流校准逻辑
                XtraMessageBox.Show("放电电流校准功能尚未实现");
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"放电电流校准失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 导出数据按钮点击事件
        /// </summary>
        private void BtnExportData_Click(object? sender, EventArgs e)
        {
            try
            {
                // 实现数据导出逻辑
                XtraMessageBox.Show("数据导出功能尚未实现");
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"数据导出失败: {ex.Message}");
            }
        }

        #endregion

        #region 设备通信和校准方法

        /// <summary>
        /// 加载设备协议
        /// </summary>
        /// <param name="voltageSource">电压源设备</param>
        /// <param name="voltmeter">电压表设备</param>
        /// <returns>协议列表</returns>
        private async Task<List<ModbusSignal>> LoadProtocolsAsync(EquipmentModel voltageSource, EquipmentModel voltmeter)
        {
            try
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // 1. 加载设备协议（电压源设备和电压表设备）
                    var equipmentProtocols = new List<EquipmentModel> { voltageSource, voltmeter };
                    var allModbusSignals = new List<ModbusSignal>();

                    foreach (var equipment in equipmentProtocols)
                    {
                        if (!string.IsNullOrEmpty(equipment.CommunicationProtocols))
                        {
                            // 查询DbcFile表获取DbcFileID
                            long fileId = SQLite_Service.GetDbcFileId(conn, equipment.CommunicationProtocols);

                            if (equipment.CanType == "CANET-2E-U")
                            {
                                // 加载CAN协议信号
                                //var canSignals = SQLite_Service.GetCanSignalsByDbc(conn, fileId);
                                // 将CAN信号转换为Modbus信号（如果需要统一接口）
                                // 这里可以根据需要将CAN信号转换为Modbus信号格式
                                // var convertedSignals = ConvertCanToModbusSignals(canSignals);
                                // allModbusSignals.AddRange(convertedSignals);
                            }
                            else if (equipment.CanType == "RS485-MODBUS")
                            {
                                // 查询ModbusSignals表所有信息
                                var modbusSignals = SQLite_Service.GetModbusSignalsByDbc(conn, fileId);

                                // 为每个信号添加设备名称
                                foreach (var signal in modbusSignals)
                                {
                                    signal.DeviceName = equipment.DeviceName;
                                }

                                allModbusSignals.AddRange(modbusSignals);
                            }
                            else if (equipment.CanType == "USB-SCPI")
                            {
                                // 加载SCPI协议命令
                                //var scpiCommands = SQLite_Service.GetScpiCommandsByDevice(conn, equipment.DeviceName);
                                // 将SCPI命令转换为Modbus信号格式（如果需要统一接口）
                                // var convertedSignals = ConvertScpiToModbusSignals(scpiCommands);
                                // allModbusSignals.AddRange(convertedSignals);
                            }
                        }
                    }

                    return allModbusSignals;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"加载设备协议失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 加载树形图信号协议
        /// </summary>
        /// <returns>信号信息列表</returns>
        private async Task<List<SignalInfo>> LoadTreeSignalsProtocolAsync()
        {
            try
            {
                // 获取所有树节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();
                string connectionString = $"Data Source={sqladdress};Version=3;";
                var signalInfos = new List<SignalInfo>();

                using (var conn = new SQLiteConnection(connectionString))
                {
                    await conn.OpenAsync();

                    foreach (var node in messageNodes)
                    {
                        // 只处理被勾选的节点
                        if (!node.Checked)
                            continue;

                        // 获取设备名称和信号名称
                        string deviceName = node.GetValue("DeviceName")?.ToString().Split('-')[0] ?? "";
                        string channel = node.GetValue("DeviceName")?.ToString().Split('-')[1] ?? "";
                        string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                        string signaltype = node.GetValue("SignalType")?.ToString() ?? "";
                        string readtime = node.GetValue("ReadTime")?.ToString() ?? "";
                        string ratingVoltageCurrent = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                        string calibrationNumber = node.GetValue("CalibrationNumber")?.ToString() ?? "";
                        //string debugsignal1 = signalName + "比例系数";
                        //string debugsignal2 = signalName + "零点系数";

                        if (!string.IsNullOrEmpty(deviceName) && !string.IsNullOrEmpty(signalName))
                        {
                            // 根据设备名称获取协议名称
                            string protocolName = SQLite_Service.GetProtocolNameByDeviceName(conn, deviceName);

                            if (!string.IsNullOrEmpty(protocolName))
                            {
                                // 获取协议文件ID
                                long fileId = SQLite_Service.GetDbcFileId(conn, protocolName);

                                // 获取所有相关的MessageID
                                var messages = SQLite_Service.GetMessagesByDbc(conn, fileId);

                                // 遍历所有MessageID，查找匹配的信号
                                foreach (var message in messages)
                                {
                                    // 根据MessageID和信号名称查询信号信息
                                    var signalInfo = SQLite_Service.GetSignalByMessageAndSystemName(conn, message.MessageID, signalName);

                                    if (signalInfo != null)
                                    {
                                        // 设置CANID
                                        if (signalInfo.CANID.Contains("AX"))
                                        {
                                            int acnum = Convert.ToInt32(channel.Substring(2,channel.Length - 2));
                                            signalInfo.CANID = signalInfo.CANID?.Replace("AX", "A" + (acnum - 1));
                                        }
                                        else if (signalInfo.CANID.Contains("2X"))
                                        {
                                            int dcnum = Convert.ToInt32(channel.Substring(2,channel.Length - 2));
                                            signalInfo.CANID = signalInfo.CANID?.Replace("2X", "2" + (dcnum - 1));
                                        }

                                        signalInfos.Add(signalInfo);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                return signalInfos;
            }
            catch (Exception ex)
            {
                LogService.Log($"加载树形图信号协议失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动设备
        /// </summary>
        /// <param name="equipment">设备模型</param>
        /// <returns>启动是否成功</returns>
        private static Task<bool> StartEquipment(EquipmentModel equipment)
        {
            try
            {
                // 根据设备通讯类型调用不同的启动方法
                switch (equipment.CanType)
                {
                    //case "CANET-2E-U":
                    //    // 使用CAN管理器启动CAN设备
                    //    CANManager.Instance.RegisterChannel(equipment);
                    //    // 等待设备连接确认
                    //    await Task.Delay(500); // 给设备一些时间连接
                    //    // 检查设备是否成功连接
                    //    string key = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);
                    //    return CANManager.Instance.IsChannelConnected(key);

                    case "RS485-MODBUS":
                        // 使用RS485管理器启动设备
                        bool rs485modbus = RS485Manager.Instance.RegisterChannel(equipment);
                        if (!rs485modbus)
                        {
                            return Task.FromResult(false);
                        }
                        return Task.FromResult(true);

                    case "USB-SCPI":
                        // 使用USB-SCPI管理器启动设备
                        bool usbscpi = Keysight34465A_Communicator.Instance.Connect();
                        if (!usbscpi)
                        {
                            return Task.FromResult(false);
                        }
                        return Task.FromResult(true);

                    default:
                        XtraMessageBox.Show($"不支持的通讯类型: {equipment.CanType}");
                        return Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"启动设备 {equipment.DeviceName} 失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 发送设备命令
        /// </summary>
        /// <param name="commandType">命令类型</param>
        /// <param name="equipment">设备</param>
        /// <param name="protocols">协议</param>
        /// <param name="parameters">参数</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendEquipmentCommand(CommandType commandType, EquipmentModel equipment, List<ModbusSignal> protocols, params object[] parameters)
        {
            try
            {
                byte[] command = null;
                bool isModbus = equipment.CanType == "RS485-MODBUS";

                switch (commandType)
                {
                    case CommandType.SetMode:
                        int setmode = (int)parameters[0];
                        if (isModbus)
                        {
                            // 查找模式设置指令的信号
                            var modeSettingSignal = protocols.FirstOrDefault(s => s.SystemVariableName == "模式设置指令");
                            if (modeSettingSignal == null) return false;

                            // 构建设置模式的Modbus命令
                            command = new byte[]
                            {
                                HexStringToByte(modeSettingSignal.CorrespondenceAddress),
                                HexStringToByte(modeSettingSignal.FunctionCode),
                                HexStringToByteArray(modeSettingSignal.RegisterAddress)[0],
                                HexStringToByteArray(modeSettingSignal.RegisterAddress)[1],
                                (byte)(modeSettingSignal.RegisterCount >> 8),
                                (byte)(modeSettingSignal.RegisterCount & 0xFF),
                                (byte)(2 * modeSettingSignal.RegisterCount),
                                (byte)(setmode >> 8),
                                (byte)(setmode & 0xFF)  // 设置为恒压模式
                            };
                        }
                        // 可以添加其他设备类型的处理
                        break;

                    case CommandType.SetVoltage:
                        double voltageValue = (double)parameters[0];
                        if (isModbus)
                        {
                            // 查找电压设置指令的信号
                            var voltageSettingSignal = protocols.FirstOrDefault(s => s.SystemVariableName == "电压设置指令");
                            if (voltageSettingSignal == null) return false;

                            // 将电压值转换为IEEE 754单精度浮点数字节数组
                            byte[] floatBytes = BitConverter.GetBytes((float)voltageValue);

                            // 从 0x4175C28F (小端序) 转换为 0xC2, 0x8F, 0x41, 0x75
                            byte[] reorderedBytes = new byte[4];
                            reorderedBytes[0] = floatBytes[1]; // 0xC2
                            reorderedBytes[1] = floatBytes[0]; // 0x8F
                            reorderedBytes[2] = floatBytes[3]; // 0x41
                            reorderedBytes[3] = floatBytes[2]; // 0x75

                            command = new byte[]
                            {
                                HexStringToByte(voltageSettingSignal.CorrespondenceAddress),
                                HexStringToByte(voltageSettingSignal.FunctionCode),
                                HexStringToByteArray(voltageSettingSignal.RegisterAddress)[0],
                                HexStringToByteArray(voltageSettingSignal.RegisterAddress)[1],
                                (byte)(voltageSettingSignal.RegisterCount >> 8),
                                (byte)(voltageSettingSignal.RegisterCount & 0xFF),
                                (byte)(2 * voltageSettingSignal.RegisterCount),
                                reorderedBytes[0],reorderedBytes[1],reorderedBytes[2],reorderedBytes[3]
                            };
                        }
                        // 可以添加其他设备类型的处理
                        break;

                    case CommandType.EnableOutput:
                        int enableoutput = (int)parameters[0];
                        if (isModbus)
                        {
                            // 查找输出控制指令的信号
                            var outputControlSignal = protocols.FirstOrDefault(s => s.SystemVariableName == "输出控制指令");
                            if (outputControlSignal == null) return false;

                            command = new byte[]
                            {
                                HexStringToByte(outputControlSignal.CorrespondenceAddress),
                                HexStringToByte(outputControlSignal.FunctionCode),
                                HexStringToByteArray(outputControlSignal.RegisterAddress)[0],
                                HexStringToByteArray(outputControlSignal.RegisterAddress)[1],
                                (byte)(outputControlSignal.RegisterCount >> 8),
                                (byte)(outputControlSignal.RegisterCount & 0xFF),
                                (byte)(2 * outputControlSignal.RegisterCount),
                                (byte)(enableoutput >> 8),
                                (byte)(enableoutput & 0xFF)  // 开机
                            };
                        }
                        // 可以添加其他设备类型的处理
                        break;
                }

                if (command == null)
                {
                    LogService.Log($"不支持的设备类型或命令: {equipment.CanType}, {commandType}");
                    return false;
                }

                // 发送命令
                bool sendSuccess = false;
                switch (equipment.CanType)
                {
                    case "RS485-MODBUS":
                        //RS485Manager.Instance.ClearBuffer(equipment.ComPort);
                        sendSuccess = RS485Manager.Instance.SendData(equipment.ComPort, command);
                        break;
                        // 可以添加其他设备类型的发送逻辑
                }

                if (!sendSuccess)
                {
                    LogService.Log("发送命令失败");
                    return false;
                }

                // 等待设备响应
                await Task.Delay(500);

                // 读取并验证响应
                byte[] response = null;
                switch (equipment.CanType)
                {
                    case "RS485-MODBUS":
                        //int num = RS485Manager.Instance.ReadData(equipment.ComPort)
                        response = RS485Manager.Instance.ReadBuffer(equipment.ComPort);
                        break;
                        // 可以添加其他设备类型的响应读取逻辑
                }

                return ValidateResponse(response, command, commandType);
            }
            catch (Exception ex)
            {
                LogService.Log($"发送命令时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证设备响应
        /// </summary>
        /// <param name="response">设备响应</param>
        /// <param name="sentCommand">发送的命令</param>
        /// <param name="commandType">命令类型</param>
        /// <returns>响应是否有效</returns>
        private bool ValidateResponse(byte[] response, byte[] sentCommand, CommandType commandType)
        {
            if (response == null || response.Length == 0)
            {
                LogService.Log("未收到设备响应");
                return false;
            }

            return response.Length >= 6 &&
                           response[0] == sentCommand[0] &&
                           response[1] == sentCommand[1] &&
                           response[2] == sentCommand[2] &&
                           response[3] == sentCommand[3] &&
                           response[4] == sentCommand[4] &&
                           response[5] == sentCommand[5];

            // 根据命令类型和设备协议进行响应验证
            // 这里需要根据实际协议实现具体的验证逻辑
            // 以下是一个简单的示例，实际应用中需要根据设备协议调整
            switch (commandType)
            {
                case CommandType.SetMode:
                    // 验证模式设置响应
                    return response.Length >= 8 &&
                           response[0] == sentCommand[0] &&
                           response[1] == sentCommand[1];

                case CommandType.SetVoltage:
                    // 验证电压设置响应
                    

                case CommandType.EnableOutput:
                    // 验证输出控制响应
                    return response.Length >= 6 &&
                           response[0] == sentCommand[0] &&
                           response[1] == sentCommand[1];

                default:
                    return false;
            }
        }

        /// <summary>
        /// 从树形信号列表中获取校准点信息
        /// </summary>
        /// <param name="treeSignals">树形信号列表</param>
        /// <returns>校准点字典，键为信号名称，值为校准点列表</returns>
        private async Task<Dictionary<string, List<CalibrationPoint>>> GetCalibrationPoints(List<SignalInfo> treeSignals)
        {
            var calibrationPoints = new Dictionary<string, List<CalibrationPoint>>();

            try
            {
                // 获取所有树节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                foreach (var node in messageNodes)
                {
                    // 获取设备名称和信号名称
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                    string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                    string signalType = node.GetValue("SignalType")?.ToString() ?? "";

                    if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(signalName))
                        continue;

                    // 只处理电压信号
                    if (!signalType.Contains("电压", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 获取校准点个数
                    string calibrationNumberStr = node.GetValue("CalibrationNumber")?.ToString() ?? "";
                    if (!int.TryParse(calibrationNumberStr, out int calibrationNumber) || calibrationNumber <= 0)
                        continue;

                    // 获取额定电压值
                    string ratingVoltageStr = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                    if (!double.TryParse(ratingVoltageStr, out double ratingVoltage) || ratingVoltage <= 0)
                        continue;

                    // 获取稳定读取时间
                    string readTimeStr = node.GetValue("ReadTime")?.ToString() ?? "";
                    if (!int.TryParse(readTimeStr, out int readTimeMs))
                        readTimeMs = 1000; // 默认1秒

                    // 查找对应的信号信息
                    var signalInfo = treeSignals.FirstOrDefault(s =>
                        s.SystemName.Equals(signalName, StringComparison.OrdinalIgnoreCase));

                    if (signalInfo == null)
                        continue;

                    // 生成校准点
                    var points = new List<CalibrationPoint>();

                    // 如果是线性校准，生成等分点
                    for (int i = 0; i < calibrationNumber; i++)
                    {
                        double voltageValue = (ratingVoltage / calibrationNumber) * (i + 1);

                        points.Add(new CalibrationPoint
                        {
                            Voltage = voltageValue,
                            ReadTimeMs = readTimeMs,
                            SignalInfo = signalInfo,
                            DeviceName = deviceName,
                            SignalName = signalName
                        });
                    }

                    // 添加到字典
                    string key = $"{deviceName}_{signalName}";
                    calibrationPoints[key] = points;
                }

                return calibrationPoints;
            }
            catch (Exception ex)
            {
                LogService.Log($"获取校准点失败: {ex.Message}");
                return new Dictionary<string, List<CalibrationPoint>>();
            }
        }

        #endregion

        #region 校准流程封装方法

        /// <summary>
        /// 执行完整的电压校准流程
        /// </summary>
        public async Task<bool> ExecuteVoltageCalibration()
        {
            try
            {
                // 1. 获取选中的设备
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                string? voltmeterName = cbVoltmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(voltageSourceName) || string.IsNullOrEmpty(voltmeterName))
                {
                    XtraMessageBox.Show("请先选择所有必要的校准设备!");
                    return false;
                }

                // 2. 从设备列表中查找设备信息
                var voltageSource = equipmentList.FirstOrDefault(e => e.DeviceName == voltageSourceName);
                var voltmeter = equipmentList.FirstOrDefault(e => e.DeviceName == voltmeterName);
                if (voltageSource == null || voltmeter == null)
                {
                    XtraMessageBox.Show("未找到选定的设备配置信息!");
                    return false;
                }

                // 3. 加载校准设备指令协议
                var protocols = await LoadProtocolsAsync(voltageSource, voltmeter);

                // 分离电压源和电压表的协议
                voltageSourceProtocols = protocols.Where(p => p.DeviceName == voltageSource.DeviceName).ToList();
                voltmeterProtocols = protocols.Where(p => p.DeviceName == voltmeter.DeviceName).ToList();

                // 4.加载树形图信号名称协议
                treeSignalProtocols = await LoadTreeSignalsProtocolAsync();
                // 按 SystemName 分组
                var debugSignals = treeSignalProtocols
                        .Where(s => s.MessageName.Substring(0, 2) == "调试")
                        .GroupBy(s => s.SignalName.Substring(0, s.SignalName.Length - 4))
                        .ToDictionary(g => g.Key, g => g.ToList());

                // 5.加载调试协议
                //debugProtocols = await LoadDebugProtocolsAsync();

                // 5. 根据设备通讯类型启动设备
                bool voltageSourceStarted = await StartEquipment(voltageSource);
                bool voltmeterStarted = await StartEquipment(voltmeter);
                if (!voltageSourceStarted || !voltmeterStarted)
                {
                    LogService.Log("设备启动失败，请检查设备连接!");
                    return false;
                }
                LogService.Log("所有校准设备启动成功，开始电压校准流程!");

                // 6. 设置设备模式
                LogService.Log("设置电压源为程控模式...");
                bool modeSet = await SendEquipmentCommand(CommandType.SetMode, voltageSource, voltageSourceProtocols, 0x01);
                if (!modeSet)
                {
                    XtraMessageBox.Show("设置设备模式失败!");
                    return false;
                }

                // 7. 设置初始电压值（从0开始）
                LogService.Log("设置初始电压值...");
                bool voltageSet = await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceProtocols, 0.0);
                if (!voltageSet)
                {
                    XtraMessageBox.Show("设置初始电压失败!");
                    return false;
                }

                // 8. 开机/启用输出
                LogService.Log("启用电压源输出...");
                bool outputEnabled = await SendEquipmentCommand(CommandType.EnableOutput, voltageSource, voltageSourceProtocols, 0x01);
                if (!outputEnabled)
                {
                    XtraMessageBox.Show("启用输出失败!");
                    return false;
                }

                // 9. 获取校准点信息
                var calibrationPoints = await GetCalibrationPoints(treeSignalProtocols);

                // 10. 遍历每个校准点进行校准
                bool calibrationSuccess = await ProcessCalibrationPoints(
                    calibrationPoints,
                    voltageSource,
                    voltmeter,
                    voltageSourceProtocols,
                    voltmeterProtocols,
                    treeSignalProtocols,
                    debugSignals);

                // 11. 完成校准后关闭输出
                LogService.Log("校准完成，关闭输出...");
                await SendEquipmentCommand(CommandType.EnableOutput, voltageSource, voltageSourceProtocols, 0x00);

                if (calibrationSuccess)
                {
                    //XtraMessageBox.Show("电压校准完成!");
                    return true;
                }
                else
                {
                    //XtraMessageBox.Show("电压校准过程中出现错误，请查看日志!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"电压校准失败: {ex.Message}");
                XtraMessageBox.Show($"电压校准失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 处理所有校准点
        /// </summary>
        private async Task<bool> ProcessCalibrationPoints(
            Dictionary<string, List<CalibrationPoint>> calibrationPoints,
            EquipmentModel voltageSource,
            EquipmentModel voltmeter,
            List<ModbusSignal> voltageSourceSignals,
            List<ModbusSignal> voltmeterSignals,
            List<SignalInfo> treeSignals,
            Dictionary<string, List<SignalInfo>> debugSignals)
        {
            try
            {
                LogService.Log("开始遍历所有校准点进行校准...");
                int totalPoints = calibrationPoints.Values.Sum(points => points.Count);
                int currentPoint = 0;

                foreach (var signalKey in calibrationPoints.Keys)
                {
                    var points = calibrationPoints[signalKey];
                    var firstPoint = points.FirstOrDefault();

                    if (firstPoint == null)
                        continue;

                    LogService.Log($"开始校准信号: {firstPoint.DeviceName} - {firstPoint.SignalName}, 共 {points.Count} 个校准点");

                    // 为每个信号准备数据收集
                    var measuredValues = new List<double>();
                    var actualValues = new List<double>();

                    SignalInfo scaleFactorSignal = null;
                    SignalInfo zeroFactorSignal = null;

                    if (debugSignals.TryGetValue(firstPoint.SignalName, out var scaleSignals) && scaleSignals.Count > 0)
                    {
                        scaleFactorSignal = scaleSignals[0];
                        zeroFactorSignal = scaleSignals[1];
                    }

                    // 读取原有的比例系数和零点系数
                    double originalScaleFactor = 1.0;
                    double originalZeroFactor = 0.0;

                    if (scaleFactorSignal != null && zeroFactorSignal != null)
                    {
                        var (scale, zero) = await ReadCalibrationFactors(
                                            firstPoint.DeviceName,
                                            scaleFactorSignal,
                                            zeroFactorSignal);

                        originalScaleFactor = scale;
                        originalZeroFactor = zero;

                        LogService.Log($"读取原有校准系数 - 比例系数: {originalScaleFactor}, 零点系数: {originalZeroFactor}");
                    }

                    foreach (var point in points)
                    {
                        currentPoint++;
                        UpdateProgressBar(currentPoint, totalPoints);

                        try
                        {
                            LogService.Log($"设置校准点 {currentPoint}/{totalPoints}: {point.Voltage}V");

                            // 设置电压源输出到当前校准点电压
                            bool voltageSet = await SendEquipmentCommand(
                                CommandType.SetVoltage,
                                voltageSource,
                                voltageSourceSignals,
                                point.Voltage);

                            if (!voltageSet)
                            {
                                LogService.Log($"设置电压 {point.Voltage}V 失败，跳过此校准点");
                                continue;
                            }

                            // 等待电压稳定
                            LogService.Log($"等待 {point.ReadTimeMs}ms 使电压稳定...");
                            await Task.Delay(point.ReadTimeMs);

                            var (actualVoltage, deviceVoltage) = await ReadVoltageValuesSync(
                                voltmeter,
                                voltmeterSignals,
                                firstPoint.DeviceName,
                                firstPoint.SignalInfo,
                                treeSignals);

                            LogService.Log($"电压表测量值: {actualVoltage}V, 设备电压采样值: {deviceVoltage}V");

                            // 保存测量数据
                            measuredValues.Add(deviceVoltage);
                            actualValues.Add(actualVoltage);

                            // 为当前校准点创建子节点并更新值
                            UpdateTreeNodeValue(firstPoint.DeviceName, firstPoint.SignalName,
                                actualVoltage, deviceVoltage, currentPoint);
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"校准点 {point.Voltage}V 处理失败: {ex.Message}");
                        }
                    }

                    //关闭输出0.0
                    bool voltageset = await SendEquipmentCommand(
                                CommandType.SetVoltage,
                                voltageSource,
                                voltageSourceSignals,
                                0.0);

                    // 计算校准系数 (使用线性回归 y = kx + b)
                    if (measuredValues.Count >= 2)
                    {
                        try
                        {
                            // 计算比例系数(k)和截距(b)
                            var (scaleFactor, zeroFactor) = CalculateCalibrationFactors(measuredValues, actualValues);

                            LogService.Log($"计算完成 - 比例系数: {scaleFactor}, 零点系数: {zeroFactor}");

                            // 更新数据库和UI中的校准系数
                            //await UpdateCalibrationFactors(firstPoint.DeviceName, firstPoint.SignalName,
                            //    scaleFactor, zeroFactor);

                            // 将新的校准系数写入设备
                            if (scaleFactorSignal != null && zeroFactorSignal != null)
                            {
                                bool writeSuccess = await WriteCalibrationFactors(
                                    firstPoint.DeviceName,
                                    scaleFactorSignal, zeroFactorSignal,
                                    scaleFactor, zeroFactor);

                                if (writeSuccess)
                                {
                                    LogService.Log("校准系数写入设备成功");
                                }
                                else
                                {
                                    LogService.Log("校准系数写入设备失败");
                                }
                            }

                            // 验证校准结果
                            bool calibrationValid = await VerifyCalibration(
                                firstPoint.DeviceName, firstPoint.SignalInfo,
                                scaleFactor, zeroFactor, voltageSource, voltmeter,
                                voltageSourceSignals, voltmeterSignals, treeSignals);

                            string resultText = calibrationValid ? "成功" : "失败";

                            LogService.Log($"校准验证: {resultText}");
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"计算校准系数失败: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogService.Log("有效数据点不足，无法计算校准系数");
                    }

                    // 短暂暂停，确保设备稳定
                    await Task.Delay(3000);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"处理校准点时发生错误: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 同步读取方法

        /// <summary>
        /// 优化版的同步读取方法
        /// </summary>
        private async Task<(double actualVoltage, double deviceVoltage)> ReadVoltageValuesSync(
            EquipmentModel voltmeter,
            List<ModbusSignal> voltmeterSignals,
            string deviceName,
            SignalInfo signalInfo,
            List<SignalInfo> treeSignals)
        {
            try
            {
                // 记录开始时间
                //DateTime startTime = DateTime.Now;

                // 并行读取电压表值和设备采样值
                var voltmeterTask = ReadVoltmeterValue(voltmeter, voltmeterSignals);
                var deviceTask = ReadDeviceVoltageSample(deviceName, signalInfo, treeSignals);

                // 等待两个任务完成
                await Task.WhenAll(voltmeterTask, deviceTask);

                // 记录结束时间并计算耗时
                //TimeSpan duration = DateTime.Now - startTime;
                //LogService.Log($"电压读取完成，耗时: {duration.TotalMilliseconds}ms");

                return (voltmeterTask.Result, deviceTask.Result);
            }
            catch (OperationCanceledException)
            {
                LogService.Log("电压读取超时，使用最后一次有效值");
                // 返回最后一次有效值或默认值
                return (0.0, 0.0);
            }
            catch (Exception ex)
            {
                LogService.Log($"电压读取失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 读取电压表值
        /// </summary>
        private async Task<double> ReadVoltmeterValue(EquipmentModel voltmeter, List<ModbusSignal> voltmeterSignals)
        {
            try
            {
                // 根据电压表类型选择不同的读取方式
                switch (voltmeter.CanType)
                {
                    case "USB-SCPI":
                        return await ReadScpiVoltmeterValue(voltmeter);

                    case "RS485-MODBUS":
                        // 实现Modbus电压表读取逻辑
                        throw new Exception($"不支持的电压表类型: {voltmeter.CanType}");

                    default:
                        throw new Exception($"不支持的电压表类型: {voltmeter.CanType}");
                }
            }
            catch (Exception ex)
            {
                //LogService.Log($"读取电压表值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 读取设备电压采样值
        /// </summary>
        private async Task<double> ReadDeviceVoltageSample(string deviceName, SignalInfo signalInfo, List<SignalInfo> treeSignals)
        {
            try
            {
                // 根据设备类型选择不同的读取方式
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName.Split('-')[0]);
                if (equipment == null)
                    throw new Exception($"未找到设备: {deviceName}");

                // 记录读取开始时间
                //DateTime startTime = DateTime.Now;

                double result;

                switch (equipment.CanType)
                {
                    case "RS485-MODBUS":
                        //result = await ReadModbusDeviceValue(equipment, signalInfo);
                        throw new Exception($"不支持的设备类型: {equipment.CanType}");

                    case "CANET-2E-U":
                        result = await ReadCanDeviceValue(equipment, signalInfo);
                        break;

                    default:
                        throw new Exception($"不支持的设备类型: {equipment.CanType}");
                }

                // 记录读取耗时
                //TimeSpan duration = DateTime.Now - startTime;
                //if (duration.TotalMilliseconds > 100)
                //{
                //    LogService.Log($"设备 {equipment.DeviceName} 读取耗时: {duration.TotalMilliseconds}ms");
                //}

                return result;
            }
            catch (Exception)
            {
                //LogService.Log($"读取设备电压采样值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 读取CAN设备电压采样值
        /// </summary>
        private async Task<double> ReadCanDeviceValue(EquipmentModel equipment, SignalInfo signalInfo)
        {
            try
            {
                // 记录开始时间
                //DateTime startTime = DateTime.Now;

                await Task.Delay(600);//延时600ms

                // 构建通道键
                string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);

                // 从SignalInfo中获取CAN ID
                uint canId = uint.Parse(signalInfo.CANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);


                // 接收指定CAN ID的帧
                var frame = await CANManager.Instance.ReceiveFrameAsync(channelKey, canId, 100);

                if (frame.IsEmpty())
                {
                    throw new Exception($"接收CAN帧超时，CAN ID: 0x{canId:X}");
                }

                // 解析帧中的数据
                ulong rawValue = CANManager.Instance.ExtractRawValue(frame.data, signalInfo);
                double physicalValue = CANManager.Instance.ConvertToPhysicalValue(rawValue, signalInfo);

                // 记录结束时间并计算耗时
                //TimeSpan duration = DateTime.Now - startTime;

                LogService.Log($"电压采样值读取完成");

                return physicalValue;
            }
            catch (Exception)
            {
                //LogService.Log($"读取CAN设备值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 读取SCPI电压表值
        /// </summary>
        private async Task<double> ReadScpiVoltmeterValue(EquipmentModel voltmeter)
        {
            try
            {
                // 记录开始时间
                //DateTime startTime = DateTime.Now;

                // 发送查询命令并读取响应
                Keysight34465A_Communicator.Instance.SendCommand("CONF:VOLT:DC AUTO");

                // 等待设置生效
                await Task.Delay(50);

                // 发送查询命令（必须以问号结尾）
                string response = Keysight34465A_Communicator.Instance.Query("READ?");

                if (double.TryParse(response.Trim(), out double voltageValue))
                {
                    // 记录结束时间并计算耗时
                    //TimeSpan duration = DateTime.Now - startTime;
                    LogService.Log($"电压表读取完成");

                    return voltageValue;
                }

                throw new Exception($"无效的电压值响应: {response}");
            }
            catch (Exception)
            {
                //LogService.Log($"读取SCPI电压表值失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 辅助方法实现

        /// <summary>
        /// 读取设备的校准系数
        /// </summary>
        private async Task<(double scaleFactor, double zeroFactor)> ReadCalibrationFactors(
            string deviceName,SignalInfo scaleFactorSignal, SignalInfo zeroFactorSignal)
        {
            try
            {
                // 从设备列表中查找设备
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName.Split('-')[0]);
                string channel = deviceName.Split('-')[1];
                if (equipment == null)
                    return (1.0, 0.0);

                // 发送读取请求帧
                await SendReadCalibrationRequest(equipment, channel,scaleFactorSignal);

                // 接收响应帧并解析系数
                return await ReceiveAndParseCalibrationResponse(
                    equipment, scaleFactorSignal, zeroFactorSignal, channel);
            }
            catch (Exception ex)
            {
                LogService.Log($"读取校准系数失败: {ex.Message}");
                return (1.0, 0.0);
            }
        }

        /// <summary>
        /// 写入设备的校准系数
        /// </summary>
        private async Task<bool> WriteCalibrationFactors(
            string deviceName,
            SignalInfo scaleFactorSignal,
            SignalInfo zeroFactorSignal,
            double scaleFactor,
            double zeroFactor)
        {
            try
            {
                // 从设备列表中查找设备
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName.Split('-')[0]);
                string channel = deviceName.Split('-')[1];
                if (equipment == null)
                    return false;

                // 发送写入请求帧
                await SendWriteCalibrationRequest(equipment, channel, scaleFactorSignal, zeroFactorSignal, scaleFactor, zeroFactor);

                // 等待100ms
                await Task.Delay(100);

                // 发送读取请求帧
                await SendReadCalibrationRequest(equipment, channel, scaleFactorSignal);

                // 接收响应帧并解析系数
                var(scale, zero) = await ReceiveAndParseCalibrationResponse(
                                  equipment, scaleFactorSignal, zeroFactorSignal, channel);

                if ((scale == scaleFactor) && (zero == zeroFactor))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"读取校准系数失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送写入校准系数的请求
        /// </summary>
        private Task SendWriteCalibrationRequest(
            EquipmentModel equipment, string channel,
            SignalInfo scaleFactorSignal,
            SignalInfo zeroFactorSignal,
            double scaleFactor,
            double zeroFactor)
        {
            try
            {
                // 构建通道键
                string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);

                // 提取通道数字（最后一位字符）
                char channelNumberChar = channel[channel.Length - 1];
                if (!char.IsDigit(channelNumberChar))
                {
                    throw new ArgumentException($"通道格式错误: {channel}");
                }
                int channelNumber = int.Parse(channelNumberChar.ToString());

                // 根据通道类型生成基础CAN ID
                uint baseCanId;
                if (channel.Contains("AC"))
                {
                    baseCanId = 0x0400A0CC; // AC通道基础CAN ID
                }
                else if (channel.Contains("DC"))
                {
                    baseCanId = 0x040020CC; // DC通道基础CAN ID
                }
                else
                {
                    throw new ArgumentException($"不支持的通道类型: {channel}");
                }

                uint canId = baseCanId & 0xFFFFF0FF; // 清除第3个字节的低4位
                canId |= (uint)((channelNumber - 1) << 8); // 将通道号-1左移8位并设置到CAN ID中

                // 构建写入请求数据帧
                byte[] requestData = new byte[8];

                // 第一个字节是功能码 (SystemName前2个字符)
                string functionCode = scaleFactorSignal.SystemName.Length >= 2 ?
                    scaleFactorSignal.SystemName.Substring(0, 2) : scaleFactorSignal.SystemName;

                // 将功能码转换为字节
                byte functionByte = Encoding.ASCII.GetBytes(functionCode)[0];
                requestData[0] = functionByte;

                // 将比例系数和零点系数转换为设备原始值
                decimal rawscaleFactor = ((decimal)scaleFactor - scaleFactorSignal.Offset) / scaleFactorSignal.Factor;
                decimal rawzeroFactor = ((decimal)zeroFactor - zeroFactorSignal.Offset) / zeroFactorSignal.Factor;
                long rawscaleValue = (long)rawscaleFactor;
                long rawzeroValue = (long)rawzeroFactor;

                requestData = CANManager.Instance.SetRawValue(requestData, scaleFactorSignal, rawscaleValue);
                requestData = CANManager.Instance.SetRawValue(requestData, zeroFactorSignal, rawzeroValue);

                // 发送请求帧
                CANManager.Instance.SendCommand(equipment.DeviceIndex, equipment.CanIndex, canId, requestData);

                LogService.Log($"已发送写入校准系数请求: 比例系数={scaleFactor}, 零点系数={zeroFactor}");
            }
            catch (Exception ex)
            {
                LogService.Log($"发送写入校准系数请求失败: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 发送读取校准系数的请求
        /// </summary>
        private Task SendReadCalibrationRequest(EquipmentModel equipment, string channel, SignalInfo signalInfo)
        {
            try
            {
                // 构建通道键
                string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);

                // 提取通道数字（最后一位字符）
                char channelNumberChar = channel[channel.Length - 1];
                if (!char.IsDigit(channelNumberChar))
                {
                    throw new ArgumentException($"通道格式错误: {channel}");
                }
                int channelNumber = int.Parse(channelNumberChar.ToString());

                // 根据通道类型生成基础CAN ID
                uint baseCanId;
                if (channel.Contains("AC"))
                {
                    baseCanId = 0x0800A0CC; // AC通道基础CAN ID
                }
                else if (channel.Contains("DC"))
                {
                    baseCanId = 0x080020CC; // DC通道基础CAN ID
                }
                else
                {
                    throw new ArgumentException($"不支持的通道类型: {channel}");
                }

                uint canId = baseCanId & 0xFFFFF0FF; // 清除第3个字节的低4位
                canId |= (uint)((channelNumber - 1) << 8); // 将通道号-1左移8位并设置到CAN ID中

                // 构建请求数据帧
                byte[] requestData = new byte[8];

                // 第一个字节是功能码 (SystemName前2个字符)
                string functionCode = signalInfo.SystemName.Length >= 2 ?
                    signalInfo.SystemName.Substring(0, 2) : signalInfo.SystemName;

                // 将功能码转换为字节
                byte functionByte = Encoding.ASCII.GetBytes(functionCode)[0];
                requestData[0] = functionByte;

                // 其余字节为0
                for (int i = 1; i < 8; i++)
                {
                    requestData[i] = 0;
                }

                // 发送请求帧
                CANManager.Instance.SendCommand(equipment.DeviceIndex, equipment.CanIndex, canId, requestData);

                LogService.Log($"已发送读取校准系数请求: 功能码 {functionByte:X2}");
            }
            catch (Exception ex)
            {
                LogService.Log($"发送读取校准系数请求失败: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 接收并解析校准系数响应
        /// </summary>
        private async Task<(double scaleFactor, double zeroFactor)> ReceiveAndParseCalibrationResponse(
            EquipmentModel equipment, SignalInfo scaleFactorSignal, SignalInfo zeroFactorSignal, string channel)
        {
            try
            {
                // 构建通道键
                string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);

                // 提取通道数字（最后一位字符）
                char channelNumberChar = channel[channel.Length - 1];
                if (!char.IsDigit(channelNumberChar))
                {
                    throw new ArgumentException($"通道格式错误: {channel}");
                }
                int channelNumber = int.Parse(channelNumberChar.ToString());

                // 根据通道类型生成基础CAN ID
                uint baseCanId;
                if (channel.Contains("AC"))
                {
                    baseCanId = 0x0800CCA0 + (uint)(channelNumber - 1); // AC通道基础CAN ID
                }
                else if (channel.Contains("DC"))
                {
                    baseCanId = 0x0800CC20 + (uint)(channelNumber - 1); // DC通道基础CAN ID
                }
                else
                {
                    throw new ArgumentException($"不支持的通道类型: {channel}");
                }

                // 等待并接收响应帧
                var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, baseCanId, 500);

                if (response.IsEmpty())
                {
                    LogService.Log("接收校准系数响应超时");
                    return (1.0, 0.0);
                }

                // 从响应帧中提取比例系数和零点系数
                double scaleFactor = ExtractCalibrationValue(response.data, scaleFactorSignal);
                double zeroFactor = ExtractCalibrationValue(response.data, zeroFactorSignal);

                LogService.Log($"从响应帧中解析出比例系数: {scaleFactor}, 零点系数: {zeroFactor}");

                return (scaleFactor, zeroFactor);
            }
            catch (Exception ex)
            {
                LogService.Log($"解析校准系数响应失败: {ex.Message}");
                return (1.0, 0.0);
            }
        }

        /// <summary>
        /// 从数据帧中提取校准值
        /// </summary>
        private double ExtractCalibrationValue(byte[] data, SignalInfo signal)
        {
            try
            {
                // 提取原始值
                ulong rawValue = CANManager.Instance.ExtractRawValue(data, signal);

                // 转换为物理值
                return CANManager.Instance.ConvertToPhysicalValue(rawValue, signal);
            }
            catch (Exception ex)
            {
                LogService.Log($"提取校准值失败: {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>
        /// 计算校准系数 (线性回归)
        /// </summary>
        private (double scaleFactor, double zeroFactor) CalculateCalibrationFactors(List<double> xValues, List<double> yValues)
        {
            // 线性回归计算 y = kx + b
            int n = xValues.Count;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += xValues[i];
                sumY += yValues[i];
                sumXY += xValues[i] * yValues[i];
                sumX2 += xValues[i] * xValues[i];
            }

            // 计算斜率k和截距b
            double k = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double b = (sumY - k * sumX) / n;

            return (k, b);
        }

        /// <summary>
        /// 更新树节点值
        /// </summary>
        private void UpdateTreeNodeValue(string deviceName, string signalName, 
            double actualVoltage, double deviceVoltage, int calibrationPointIndex)
        {
            // 在主线程上更新UI
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateTreeNodeValue(deviceName, signalName, actualVoltage, deviceVoltage, calibrationPointIndex)));
                return;
            }

            // 查找匹配的父节点
            foreach (TreeListNode parentNode in treeList.Nodes)
            {
                string nodeDeviceName = parentNode.GetValue("DeviceName")?.ToString() ?? "";
                string nodeSignalName = parentNode.GetValue("SignalName")?.ToString() ?? "";

                if (nodeDeviceName == deviceName && nodeSignalName == signalName)
                {
                    // 创建子节点名称
                    string childNodeName = $"校准点 {calibrationPointIndex}";

                    // 检查是否已经存在该校准点的子节点
                    TreeListNode calibrationNode = null;
                    if (parentNode.HasChildren)
                    {
                        foreach (TreeListNode childNode in parentNode.Nodes)
                        {
                            if (childNode.GetValue("DeviceName")?.ToString() == childNodeName)
                            {
                                calibrationNode = childNode;
                                break;
                            }
                        }
                    }
                    // 如果不存在，创建新的子节点
                    if (calibrationNode == null)
                    {
                        calibrationNode = parentNode.Nodes.Add(new object[]
                        {
                            "", // 设备名称列显示校准点名称
                            "", "", "", "", "", "", "",
                            deviceVoltage.ToString(""), // 设备电压采样值
                            actualVoltage.ToString(""), // 实际电压测量值
                            "", "", "", ""
                        });

                        // 设置子节点的Tag为校准点信息，方便后续查找
                        calibrationNode.Tag = $"CalibrationPoint_{calibrationPointIndex}";

                        // 展开父节点以显示子节点
                        parentNode.Expanded = true;
                    }
                    else
                    {
                        // 如果已存在，更新值
                        calibrationNode.SetValue("DeviceVoltageSample", deviceVoltage.ToString("F3"));
                        calibrationNode.SetValue("ActualVoltageMeasurement", actualVoltage.ToString("F3"));
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// 更新进度条
        /// </summary>
        private void UpdateProgressBar(int current, int total)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateProgressBar(current, total)));
                return;
            }

            int percentage = (int)Math.Round((double)current / total * 100);
            progressBar.Position = percentage;
            progressBar.Update();
        }

        /// <summary>
        /// 更新数据库中的校准系数
        /// </summary>
        private async Task UpdateCalibrationFactors(string deviceName, string signalName, double scaleFactor, double zeroFactor)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 更新数据库中的校准系数
                    string query = "UPDATE CalibrationSignals SET ScaleFactor = @scale, ZeroFactor = @zero " +
                                  "WHERE DeviceName = @device AND SignalName = @signal";

                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@scale", scaleFactor);
                        cmd.Parameters.AddWithValue("@zero", zeroFactor);
                        cmd.Parameters.AddWithValue("@device", deviceName);
                        cmd.Parameters.AddWithValue("@signal", signalName);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"更新数据库校准系数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证校准结果
        /// </summary>
        private async Task<bool> VerifyCalibration(string deviceName, SignalInfo signalInfo,
                                                 double scaleFactor, double zeroFactor,
                                                 EquipmentModel voltageSource, EquipmentModel voltmeter,
                                                 List<ModbusSignal> voltageSourceSignals,
                                                 List<ModbusSignal> voltmeterSignals,
                                                 List<SignalInfo> treeSignals)
        {
            try
            {
                // 选择一个测试点进行验证 (例如中间值)
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName);
                if (equipment == null)
                    return false;

                // 设置一个测试电压
                double testVoltage = 2.5; // 中间值
                await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceSignals, testVoltage);
                await Task.Delay(1000); // 等待稳定

                // 读取实际电压值
                double actualVoltage = 0.0;

                //// 读取设备采样值
                double deviceSample = 0.0;

                // 应用校准公式: 校准值 = 采样值 * scaleFactor + zeroFactor
                double calibratedValue = deviceSample * scaleFactor + zeroFactor;

                // 计算误差
                double error = Math.Abs(calibratedValue - actualVoltage);
                double errorPercentage = (error / actualVoltage) * 100;

                // 判断是否通过验证 (例如误差小于1%)
                return errorPercentage < 1.0;
            }
            catch (Exception ex)
            {
                LogService.Log($"验证校准结果失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region UI创建方法

        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 主布局控件，填充整个用户控件
            LayoutControl layoutControl = new LayoutControl
            {
                Parent = this,
                Dock = DockStyle.Fill
            };

            // 4. 创建根布局组（垂直方向）
            LayoutControlGroup rootGroup = new LayoutControlGroup();
            rootGroup.TextVisible = false;
            rootGroup.GroupBordersVisible = false;
            rootGroup.DefaultLayoutType = LayoutType.Vertical; //垂直排列
            layoutControl.Root.Add(rootGroup);

            // 5. 添加按钮组到根组
            LayoutControlItem buttonGroupItem = rootGroup.AddItem();
            buttonGroupItem.Control = CreateButtonContainer();
            buttonGroupItem.TextVisible = false;
            buttonGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            buttonGroupItem.MinSize = new Size(0, 60);
            buttonGroupItem.MaxSize = new Size(0, 60);

            // 6. 添加TreeList到根组
            LayoutControlItem layoutControlItemForTreeList = rootGroup.AddItem();
            layoutControlItemForTreeList.Control = CreateTreeList();
            layoutControlItemForTreeList.TextVisible = false;
            layoutControlItemForTreeList.SizeConstraintsType = SizeConstraintsType.Custom;
            layoutControlItemForTreeList.MinSize = new Size(0, 0);
            layoutControlItemForTreeList.MaxSize = new Size(0, 800);

            // 7. 添加底部进度条面板到根组
            LayoutControlItem progressGroupItem = rootGroup.AddItem();
            progressGroupItem.Control = CreateProgressContainer();
            progressGroupItem.TextVisible = false;
            progressGroupItem.SizeConstraintsType = SizeConstraintsType.Custom;
            progressGroupItem.MinSize = new Size(0, 60);
            progressGroupItem.MaxSize = new Size(0, 60);
        }

        /// <summary>
        /// 创建按钮容器
        /// </summary>
        /// <returns>按钮容器控件</returns>
        private Control CreateButtonContainer()
        {
            // 创建按钮容器面板
            PanelControl buttonPanel = new PanelControl
            {
                Padding = new System.Windows.Forms.Padding(0),
                Height = 60,
                BorderStyle = BorderStyles.NoBorder // 隐藏边框
            };

            // 电压源型号
            LabelControl lblVoltageSource = new LabelControl
            {
                Text = "电压源型号:",
                Size = new Size(100, 30),
                Location = new Point(10, 14)
            };
            buttonPanel.Controls.Add(lblVoltageSource);

            cbVoltageSource = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltageSource.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压源型号
            var voltageSources = equipmentList
                .Where(e => e.DeviceType == "电压源")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltageSource.Properties.Items.AddRange(voltageSources);
            cbVoltageSource.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltageSource);

            // 电压表型号
            LabelControl lblVoltmeter = new LabelControl
            {
                Text = "电压表型号:",
                Location = new Point(cbVoltageSource.Right + 10, 14),
                AutoSize = true
            };
            buttonPanel.Controls.Add(lblVoltmeter);

            cbVoltmeter = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblVoltmeter.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电压表型号
            var voltmeters = equipmentList
                .Where(e => e.DeviceType == "电压表")
                .Select(e => e.DeviceName)
                .ToList();
            cbVoltmeter.Properties.Items.AddRange(voltmeters);
            cbVoltmeter.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbVoltmeter);

            // 电流表型号
            LabelControl lblAmmeter = new LabelControl
            {
                Text = "电流表型号:",
                Location = new Point(cbVoltmeter.Right + 10, 14)
            };
            buttonPanel.Controls.Add(lblAmmeter);

            cbAmmeter = new ComboBoxEdit
            {
                Size = new Size(160, 30),
                Location = new Point(lblAmmeter.Right + 10, 12),
                Properties = { TextEditStyle = TextEditStyles.DisableTextEditor }
            };

            // 从equipmentList中筛选电流表型号
            var ammeters = equipmentList
                .Where(e => e.DeviceType == "电流表")
                .Select(e => e.DeviceName)
                .ToList();
            cbAmmeter.Properties.Items.AddRange(ammeters);
            cbAmmeter.SelectedIndex = 0;
            buttonPanel.Controls.Add(cbAmmeter);

            // 添加按钮到面板
            SimpleButton btnAddSignal = new SimpleButton
            {
                Text = "添加信号",
                Size = new Size(80, 30),
                Location = new Point(cbAmmeter.Right + 50, 10)
            };
            btnAddSignal.Click += BtnAddSignal_Click;
            buttonPanel.Controls.Add(btnAddSignal);

            SimpleButton btnEditSignal = new SimpleButton
            {
                Text = "编辑信号",
                Size = new Size(80, 30),
                Location = new Point(btnAddSignal.Right + 10, btnAddSignal.Location.Y)
            };
            btnEditSignal.Click += BtnEditSignal_Click;
            buttonPanel.Controls.Add(btnEditSignal);

            SimpleButton btnDeleteSignal = new SimpleButton
            {
                Text = "删除信号",
                Size = new Size(80, 30),
                Location = new Point(btnEditSignal.Right + 10, btnEditSignal.Location.Y)
            };
            btnDeleteSignal.Click += BtnDeleteSignal_Click;
            buttonPanel.Controls.Add(btnDeleteSignal);

            btnVoltageCalibration = new SimpleButton
            {
                Text = "开始电压校准",
                Size = new Size(110, 30),
                Location = new Point(btnDeleteSignal.Right + 10, 10)
            };
            btnVoltageCalibration.Click += BtnVoltageCalibration_Click;
            buttonPanel.Controls.Add(btnVoltageCalibration);

            SimpleButton btnChargingCurrentCalibration = new SimpleButton
            {
                Text = "开始充电电流校准",
                Size = new Size(140, 30),
                Location = new Point(btnVoltageCalibration.Right + 10, 10)
            };
            btnChargingCurrentCalibration.Click += BtnChargingCurrentCalibration_Click;
            buttonPanel.Controls.Add(btnChargingCurrentCalibration);

            SimpleButton btnDischargingCurrentCalibration = new SimpleButton
            {
                Text = "开始放电电流校准",
                Size = new Size(140, 30),
                Location = new Point(btnChargingCurrentCalibration.Right + 10, 10)
            };
            btnDischargingCurrentCalibration.Click += BtnDischargingCurrentCalibration_Click;
            buttonPanel.Controls.Add(btnDischargingCurrentCalibration);

            btnStopCalibration = new SimpleButton
            {
                Text = "停止校准",
                Size = new Size(80, 30),
                Location = new Point(btnDischargingCurrentCalibration.Right + 10, 10)
            };
            btnStopCalibration.Click += BtnStopCalibration_Click;
            buttonPanel.Controls.Add(btnStopCalibration);

            SimpleButton btnExportData = new SimpleButton
            {
                Text = "导出数据",
                Size = new Size(80, 30),
                Location = new Point(btnStopCalibration.Right + 10, 10)
            };
            btnExportData.Click += BtnExportData_Click;
            buttonPanel.Controls.Add(btnExportData);

            return buttonPanel;
        }

        /// <summary>
        /// 创建树形列表
        /// </summary>
        /// <returns>树形列表控件</returns>
        private Control CreateTreeList()
        {
            treeList = new TreeList();

            // 启用多选
            treeList.OptionsSelection.MultiSelect = true;
            treeList.OptionsSelection.UseIndicatorForSelection = true;
            treeList.OptionsBehavior.Editable = false;

            // 启用复选框
            treeList.OptionsView.ShowCheckBoxes = true;
            treeList.OptionsSelection.EnableAppearanceFocusedCell = false;
            treeList.OptionsSelection.EnableAppearanceFocusedRow = false;

            // 添加列
            TreeListColumn column;

            // 设备名称列
            column = treeList.Columns.Add();
            column.Caption = "设备名称及通道号";
            column.FieldName = "DeviceName";
            column.VisibleIndex = 0;
            column.Width = 120;

            // 信号名称列
            column = treeList.Columns.Add();
            column.Caption = "信号名称";
            column.FieldName = "SignalName";
            column.VisibleIndex = 1;
            column.Width = 120;

            // 信号类型列
            column = treeList.Columns.Add();
            column.Caption = "信号类型";
            column.FieldName = "SignalType";
            column.VisibleIndex = 2;
            column.Width = 60;

            column = treeList.Columns.Add();
            column.Caption = "稳定读取时间";
            column.FieldName = "ReadTime";
            column.VisibleIndex = 3;
            column.Width = 100;

            // 额定电压/电流
            column = treeList.Columns.Add();
            column.Caption = "额定电压/电流";
            column.FieldName = "RatingVoltageCurrent";
            column.VisibleIndex = 4;
            column.Width = 100;

            column = treeList.Columns.Add();
            column.Caption = "校准点个数";
            column.FieldName = "CalibrationNumber";
            column.VisibleIndex = 5;
            column.Width = 80;

            // 比例系数列
            column = treeList.Columns.Add();
            column.Caption = "比例系数";
            column.FieldName = "ScaleFactor";
            column.VisibleIndex = 6;
            column.Width = 100;

            // 零点系数列
            column = treeList.Columns.Add();
            column.Caption = "零点系数";
            column.FieldName = "ZeroFactor";
            column.VisibleIndex = 7;
            column.Width = 100;

            // 设备电压采样值列
            column = treeList.Columns.Add();
            column.Caption = "设备电压采样值";
            column.FieldName = "DeviceVoltageSample";
            column.VisibleIndex = 8;
            column.Width = 120;

            // 实际电压测量值列
            column = treeList.Columns.Add();
            column.Caption = "实际电压测量值";
            column.FieldName = "ActualVoltageMeasurement";
            column.VisibleIndex = 9;
            column.Width = 120;

            // 设备电流采样值列
            column = treeList.Columns.Add();
            column.Caption = "设备电流采样值";
            column.FieldName = "DeviceCurrentSample";
            column.VisibleIndex = 10;
            column.Width = 120;

            // 实际电流测量值列
            column = treeList.Columns.Add();
            column.Caption = "实际电流测量值";
            column.FieldName = "ActualCurrentMeasurement";
            column.VisibleIndex = 11;
            column.Width = 120;

            // 校准精度列
            column = treeList.Columns.Add();
            column.Caption = "校准精度";
            column.FieldName = "CalibrationAccuracy";
            column.VisibleIndex = 12;
            column.Width = 80;

            // 校准结果列
            column = treeList.Columns.Add();
            column.Caption = "校准结果";
            column.FieldName = "CalibrationResult";
            column.VisibleIndex = 13;
            column.Width = 80;

            // 设置所有列的内容和标题居中显示
            foreach (TreeListColumn col in treeList.Columns)
            {
                // 设置列内容居中
                col.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Center;

                // 设置列标题居中
                col.AppearanceHeader.TextOptions.HAlignment = HorzAlignment.Center;

                // 可选：设置标题字体样式
                //col.AppearanceHeader.Font = new Font(treeList.Font, FontStyle.Bold);
            }

            // 启用水平滚动条
            treeList.HorzScrollVisibility = DevExpress.XtraTreeList.ScrollVisibility.Auto;

            // 展开所有节点
            //treeList.ExpandAll();

            return treeList;
        }

        /// <summary>
        /// 创建进度条容器
        /// </summary>
        /// <returns>进度条容器控件</returns>
        private Control CreateProgressContainer()
        {
            // 创建进度条容器面板
            PanelControl progressPanel = new PanelControl
            {
                Padding = new System.Windows.Forms.Padding(0),
                Height = 60,
                BorderStyle = BorderStyles.NoBorder // 隐藏边框
            };

            LabelControl label = new LabelControl
            {
                Text = "校准进度:",
                AutoSizeMode = LabelAutoSizeMode.None, // 设置为None以便自定义大小
                Size = new Size(80, 40),
                Location = new Point(0, (progressPanel.Height - 40) / 2), // 计算垂直居中位置
            };

            // 添加进度条
            progressBar = new ProgressBarControl
            {
                Size = new Size(2000, 40),
                Location = new Point(label.Left + 10, (progressPanel.Height - 40) / 2),
            };
            progressBar.Properties.ShowTitle = true;
            progressBar.Properties.PercentView = true;
            progressPanel.Controls.Add(label);
            progressPanel.Controls.Add(progressBar);

            return progressPanel;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 将十六进制字符串转换为字节数组
        /// </summary>
        /// <param name="hexString">十六进制字符串（可以包含"0x"前缀）</param>
        /// <returns>转换后的字节数组</returns>
        /// <exception cref="ArgumentException">当输入字符串不是有效的十六进制格式时抛出</exception>
        public static byte[] HexStringToByteArray(string? hexString)
        {
            if (string.IsNullOrEmpty(hexString))
            {
                throw new ArgumentException("输入字符串不能为空", nameof(hexString));
            }

            // 去掉"0x"前缀（如果存在）
            string cleanHex = hexString;
            if (cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                cleanHex = cleanHex.Substring(2);
            }

            // 确保字符串长度为偶数（每两个字符表示一个字节）
            if (cleanHex.Length % 2 != 0)
            {
                cleanHex = "0" + cleanHex; // 在前面补0
            }

            // 将十六进制字符串转换为字节数组
            byte[] bytes = new byte[cleanHex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                try
                {
                    bytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                }
                catch (FormatException)
                {
                    throw new ArgumentException($"字符串 '{hexString}' 不是有效的十六进制格式", nameof(hexString));
                }
                catch (OverflowException)
                {
                    throw new ArgumentException($"字符串 '{hexString}' 表示的数值超出了字节的范围 (0-255)", nameof(hexString));
                }
            }

            return bytes;
        }

        /// <summary>
        /// 将十六进制字符串转换为字节
        /// </summary>
        /// <param name="hexString">十六进制字符串</param>
        /// <returns>转换后的字节</returns>
        private static byte HexStringToByte(string? hexString)
        {
            if (string.IsNullOrEmpty(hexString))
                throw new ArgumentException("十六进制字符串不能为空");

            // 去掉0x前缀
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexString = hexString.Substring(2);

            return Convert.ToByte(hexString, 16);
        }

        //protected override void Dispose(bool disposing)
        //{
        //    if (disposing)
        //    {
        //        //RS485Manager.Instance.DataReceived -= RS485_DataReceived;
        //    }
        //    base.Dispose(disposing);
        //}

        #endregion

        #region 枚举定义

        /// <summary>
        /// 命令类型枚举
        /// </summary>
        private enum CommandType
        {
            SetMode,
            SetVoltage,
            EnableOutput
        }

        #endregion
    }
}