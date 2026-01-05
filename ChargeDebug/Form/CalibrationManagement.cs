using ChargeDebug.Service;
using ClosedXML.Excel;
using CommunicationProtocols;
using DataModel;
using DevExpress.DataProcessing;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraTreeList.Nodes;
using DocumentFormat.OpenXml.Bibliography;
using Log;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Threading;

#pragma warning disable
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
        private string deviceNumber = "";
        private TreeList treeList;
        private List<EquipmentModel> equipmentList;
        private StartupManager startupManager;

        // 组合框控件
        private ComboBoxEdit cbVoltageSource;
        private ComboBoxEdit cbVoltmeter;
        private ComboBoxEdit cbAmmeter;

        private SimpleButton btnVoltageCalibration;
        private SimpleButton btnCurrentCalibration;
        private SimpleButton btnVoltageMeasurement;
        private SimpleButton btnCurrentMeasurement;
        private SimpleButton btnStopCalibration;
        private SimpleButton btnQueryData;
        private SimpleButton btnClearQuery;

        // 协议列表
        private List<ModbusSignal> voltageSourceProtocols = new List<ModbusSignal>();
        private List<ModbusSignal> voltmeterProtocols = new List<ModbusSignal>();
        private List<SignalInfo> treeSignalProtocols = new List<SignalInfo>();
        Dictionary<string, List<SignalInfo>> debugProtocols = new Dictionary<string, List<SignalInfo>>();
        private ConfigurationData _protectionParameters;

        // 进度条控件
        private ProgressBarControl progressBar;

        // 取消校准相关字段
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _calibrationCancellationRequested = false;
        private ProgressStage currentStage;

        // 校准状态标志
        private bool _isCalibrating = false;

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
                            calibrationsignal.CalibrationSignal,
                            calibrationsignal.ReadTime,
                            calibrationsignal.RatingVoltageCurrent,
                            calibrationsignal.CalibrationNumber,
                            calibrationsignal.CalibrationAccuracy,
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
                        //node.Checked = true;
                        // 确保在加载常规数据时显示复选框
                        treeList.OptionsView.ShowCheckBoxes = true;
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
                // 确保所有信号可见
                ShowAllSignals();

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
            // 确保所有信号可见
            ShowAllSignals();

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
            // 确保所有信号可见
            ShowAllSignals();

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
            // 先过滤出电压信号
            FilterSignalsByType("电压");

            // 1. 弹出设备编号输入对话框
            using (var inputForm = new DeviceNumberInputForm())
            {
                if (inputForm.ShowDialog() != DialogResult.OK)
                {
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                deviceNumber = inputForm.DeviceNumber;

                // 1. 获取当前勾选的信号名称列表
                var selectedDeviceSignals = treeList.Nodes
                    .Cast<TreeListNode>()
                    .Where(node => node.Checked)
                    .Select(node => (
                        DeviceName: node.GetValue("DeviceName")?.ToString() ?? "",
                        SignalName: node.GetValue("SignalName")?.ToString() ?? ""
                    ))
                    .Where(x => !string.IsNullOrEmpty(x.DeviceName) && !string.IsNullOrEmpty(x.SignalName))
                    .ToList();

                if (selectedDeviceSignals.Count == 0)
                {
                    XtraMessageBox.Show("请先勾选至少一个信号进行校准!");
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                // 2. 检查设备编号下特定信号名称的数据是否存在，并获取实际存在的信号列表
                var existingSignals = await GetExistingDeviceSignalData(deviceNumber, selectedDeviceSignals);
                if (existingSignals.Count > 0)
                {
                    // 构建实际存在的信号名称列表
                    string signalNames = string.Join("\n", existingSignals.Select(x => $"{x.DeviceName}-{x.SignalName}"));

                    // 提示是否覆盖实际存在的信号
                    if (XtraMessageBox.Show($"设备编号 {deviceNumber} 下已存在以下信号的校准记录：\n\n{signalNames}\n\n是否覆盖这些信号的校准记录?",
                                          "确认覆盖",
                                          MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        // 恢复所有信号显示
                        ShowAllSignals();
                        return;
                    }

                    // 覆盖时先删除特定信号的原有校准记录
                    bool deleteSuccess = await DeleteDeviceSignalCalibrationRecords(deviceNumber, selectedDeviceSignals);
                    if (!deleteSuccess)
                    {
                        XtraMessageBox.Show("删除原有校准记录失败，操作已取消!");
                        // 恢复所有信号显示
                        ShowAllSignals();
                        return;
                    }
                }

                // 检查是否正在进行校准
                if (_isCalibrating)
                {
                    XtraMessageBox.Show("当前正在进行校准，请先完成或停止当前校准操作!");
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                try
                {
                    // 设置校准状态
                    _isCalibrating = true;

                    // 禁用电流校准按钮
                    btnCurrentCalibration.Enabled = false;

                    // 重置进度条和标签
                    ResetProgress();

                    // 初始化取消令牌
                    _cancellationTokenSource = new CancellationTokenSource();
                    var cancellationToken = _cancellationTokenSource.Token;
                    _calibrationCancellationRequested = false;

                    // 禁用按钮，防止重复点击
                    btnVoltageCalibration.Enabled = false;
                    btnStopCalibration.Enabled = true;

                    // 执行电压校准
                    bool success = await ExecuteVoltageCalibration(cancellationToken);

                    if (success)
                    {
                        // 4. 校准完成后保存数据
                        bool saveSuccess = await SaveCalibrationDataToDatabase(deviceNumber);
                        if (saveSuccess)
                        {
                            // 5. 自动导出数据
                            //string exportPath = GetDefaultExportPath(deviceNumber);
                            //ExportToExcel(exportPath);

                            XtraMessageBox.Show($"校准完成，数据已保存！");
                        }
                        else
                        {
                            XtraMessageBox.Show($"校准完成，数据保存失败！");
                        }
                    }
                    else if (_calibrationCancellationRequested)
                    {
                        XtraMessageBox.Show("电压校准已取消!");
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
                    btnCurrentCalibration.Enabled = true;

                    // 重置校准状态
                    _isCalibrating = false;

                    // 清理取消令牌
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    // 恢复所有信号显示
                    ShowAllSignals();
                }
            }
        }

        /// <summary>
        /// 停止校准按钮点击事件
        /// </summary>
        private void BtnStopCalibration_Click(object? sender, EventArgs e)
        {
            try
            {
                // 请求取消校准操作
                RequestCalibrationCancellation();

                // 更新UI状态
                UpdateUIForCancellation();

                LogService.Log("用户请求停止校准操作");
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"停止校准失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 电流校准按钮点击事件
        /// </summary>
        private async void BtnCurrentCalibration_Click(object? sender, EventArgs e)
        {
            // 先过滤出电流信号
            FilterSignalsByType("电流");

            // 1. 弹出设备编号输入对话框
            using (var inputForm = new DeviceNumberInputForm())
            {
                if (inputForm.ShowDialog() != DialogResult.OK)
                {
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                string deviceNumber = inputForm.DeviceNumber;

                // 1. 获取当前勾选的信号名称列表
                var selectedDeviceSignals = treeList.Nodes
                    .Cast<TreeListNode>()
                    .Where(node => node.Checked)
                    .Select(node => (
                        DeviceName: node.GetValue("DeviceName")?.ToString() ?? "",
                        SignalName: node.GetValue("SignalName")?.ToString() ?? ""
                    ))
                    .Where(x => !string.IsNullOrEmpty(x.DeviceName) && !string.IsNullOrEmpty(x.SignalName))
                    .ToList();

                if (selectedDeviceSignals.Count == 0)
                {
                    XtraMessageBox.Show("请先勾选至少一个信号进行校准!");
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                // 2. 检查设备编号下特定信号名称的数据是否存在，并获取实际存在的信号列表
                var existingSignals = await GetExistingDeviceSignalData(deviceNumber, selectedDeviceSignals);
                if (existingSignals.Count > 0)
                {
                    // 构建实际存在的信号名称列表
                    string signalNames = string.Join("\n", existingSignals.Select(x => $"{x.DeviceName}-{x.SignalName}"));

                    // 提示是否覆盖实际存在的信号
                    if (XtraMessageBox.Show($"设备编号 {deviceNumber} 下已存在以下信号的校准记录：\n\n{signalNames}\n\n是否覆盖这些信号的校准记录?",
                                          "确认覆盖",
                                          MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        // 恢复所有信号显示
                        ShowAllSignals();
                        return;
                    }

                    // 覆盖时先删除特定信号的原有校准记录
                    bool deleteSuccess = await DeleteDeviceSignalCalibrationRecords(deviceNumber, selectedDeviceSignals);
                    if (!deleteSuccess)
                    {
                        XtraMessageBox.Show("删除原有校准记录失败，操作已取消!");
                        // 恢复所有信号显示
                        ShowAllSignals();
                        return;
                    }
                }

                // 检查是否正在进行校准
                if (_isCalibrating)
                {
                    XtraMessageBox.Show("当前正在进行校准，请先完成或停止当前校准操作!");
                    // 恢复所有信号显示
                    ShowAllSignals();
                    return;
                }

                try
                {
                    // 设置校准状态
                    _isCalibrating = true;

                    // 禁用电压校准按钮
                    btnVoltageCalibration.Enabled = false;

                    // 重置进度条和标签
                    ResetProgress();

                    // 初始化取消令牌
                    _cancellationTokenSource = new CancellationTokenSource();
                    var cancellationToken = _cancellationTokenSource.Token;
                    _calibrationCancellationRequested = false;

                    // 禁用按钮，防止重复点击
                    btnCurrentCalibration.Enabled = false;
                    btnStopCalibration.Enabled = true;

                    // 执行电流校准
                    bool success = await ExecuteCurrentCalibration(cancellationToken);

                    if (success)
                    {
                        // 4. 校准完成后保存数据
                        bool saveSuccess = await SaveCalibrationDataToDatabase(deviceNumber);
                        if (saveSuccess)
                        {
                            // 5. 自动导出数据
                            //string exportPath = GetDefaultExportPath(deviceNumber);
                            //ExportToExcel(exportPath);

                            XtraMessageBox.Show($"校准完成，数据已保存！");
                        }
                        else
                        {
                            XtraMessageBox.Show($"校准完成，数据保存失败！");
                        }
                    }
                    else if (_calibrationCancellationRequested)
                    {
                        XtraMessageBox.Show("电流校准已取消!");
                    }
                    else
                    {
                        XtraMessageBox.Show("电流校准失败，请查看日志!");
                    }

                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"电流校准失败: {ex.Message}");
                }
                finally
                {
                    // 重新启用按钮
                    btnCurrentCalibration.Enabled = true;
                    btnStopCalibration.Enabled = false;
                    btnVoltageCalibration.Enabled = true;

                    // 重置校准状态
                    _isCalibrating = false;

                    // 清理取消令牌
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    // 恢复所有信号显示
                    ShowAllSignals();
                }
            }
        }

        /// <summary>
        /// 电压计量按钮点击事件
        /// </summary>
        private async void BtnVoltageMeasurement_Click(object? sender, EventArgs e)
        {
            try
            {
                // 先过滤出电压信号
                FilterSignalsByType("电压");

                // 检查是否正在进行校准
                if (_isCalibrating)
                {
                    XtraMessageBox.Show("当前正在进行校准，请先完成或停止当前校准操作!");
                    ShowAllSignals();
                    return;
                }

                // 获取选中的设备
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                string? voltmeterName = cbVoltmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(voltageSourceName) || string.IsNullOrEmpty(voltmeterName))
                {
                    XtraMessageBox.Show("请先选择电压源和电压表设备!");
                    ShowAllSignals();
                    return;
                }

                // 获取选中的信号
                var selectedSignals = GetSelectedVoltageSignals();
                if (selectedSignals.Count == 0)
                {
                    XtraMessageBox.Show("请先勾选至少一个电压信号进行计量!");
                    ShowAllSignals();
                    return;
                }

                // 确认是否开始电压计量
                if (XtraMessageBox.Show("确认开始电压计量？", "电压计量确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    ShowAllSignals();
                    return;
                }

                // 设置计量状态
                _isCalibrating = true;

                // 禁用相关按钮
                btnVoltageMeasurement.Enabled = false;
                btnStopCalibration.Enabled = true;

                try
                {
                    // 执行电压计量
                    bool success = await ExecuteVoltageMeasurement(selectedSignals);

                    if (success)
                    {
                        XtraMessageBox.Show("电压计量完成!");
                    }
                    else
                    {
                        XtraMessageBox.Show("电压计量失败，请查看日志!");
                    }
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"电压计量失败: {ex.Message}");
                }
                finally
                {
                    // 恢复按钮状态
                    btnVoltageMeasurement.Enabled = true;
                    btnStopCalibration.Enabled = false;
                    _isCalibrating = false;

                    // 恢复所有信号显示
                    ShowAllSignals();
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"电压计量失败: {ex.Message}");
                ShowAllSignals();
            }
        }

        /// <summary>
        /// 电流计量按钮点击事件
        /// </summary>
        private async void BtnCurrentMeasurement_Click(object? sender, EventArgs e)
        {
            try
            {
                // 先过滤出电流信号
                FilterSignalsByType("电流");

                // 检查是否正在进行校准
                if (_isCalibrating)
                {
                    XtraMessageBox.Show("当前正在进行校准，请先完成或停止当前校准操作!");
                    ShowAllSignals();
                    return;
                }

                // 获取选中的设备
                string? currentSourceName = InitializeCurrentSourceModule();
                string? ammeterName = cbAmmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(currentSourceName) || string.IsNullOrEmpty(ammeterName))
                {
                    XtraMessageBox.Show("请先选择电流源和电流表设备!");
                    ShowAllSignals();
                    return;
                }

                // 获取选中的信号
                var selectedSignals = GetSelectedCurrentSignals();
                if (selectedSignals.Count == 0)
                {
                    XtraMessageBox.Show("请先勾选至少一个电流信号进行计量!");
                    ShowAllSignals();
                    return;
                }

                // 确认是否开始电流计量
                if (XtraMessageBox.Show("确认开始电流计量？", "电流计量确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    ShowAllSignals();
                    return;
                }

                // 设置计量状态
                _isCalibrating = true;

                // 禁用相关按钮
                btnCurrentMeasurement.Enabled = false;
                btnStopCalibration.Enabled = true;

                try
                {
                    // 执行电流计量
                    bool success = await ExecuteCurrentMeasurement(selectedSignals, currentSourceName);

                    if (success)
                    {
                        XtraMessageBox.Show("电流计量完成!");
                    }
                    else
                    {
                        XtraMessageBox.Show("电流计量失败，请查看日志!");
                    }
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"电流计量失败: {ex.Message}");
                }
                finally
                {
                    // 恢复按钮状态
                    btnCurrentMeasurement.Enabled = true;
                    btnStopCalibration.Enabled = false;
                    _isCalibrating = false;

                    // 恢复所有信号显示
                    ShowAllSignals();
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"电流计量失败: {ex.Message}");
                ShowAllSignals();
            }
        }

        /// <summary>
        /// 导入数据按钮点击事件
        /// </summary>
        private void BtnImportData_Click(object? sender, EventArgs e)
        {
            try
            {
                // 创建打开文件对话框
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Excel 文件|*.xlsx|Excel 97-2003 文件|*.xls";
                    openFileDialog.Title = "导入校准数据";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // 解析Excel数据
                        var importData = ParseExcelDataFromFile(openFileDialog.FileName);

                        if (importData.Count == 0)
                        {
                            XtraMessageBox.Show("Excel文件中没有找到有效的校准数据!");
                            return;
                        }

                        // 弹出设备编号输入对话框
                        using (var inputForm = new DeviceNumberInputForm())
                        {
                            if (inputForm.ShowDialog() == DialogResult.OK)
                            {
                                deviceNumber = inputForm.DeviceNumber;

                                // 检查信号是否存在并确认替换
                                if (CheckAndConfirmSignalReplacement(importData, deviceNumber))
                                {
                                    // 执行导入操作
                                    bool importSuccess = SaveImportDataToDatabase(importData, deviceNumber);

                                    if (importSuccess)
                                    {
                                        XtraMessageBox.Show("数据导入成功!");
                                        // 刷新显示导入的数据
                                        QueryAndDisplayCalibrationData(deviceNumber);
                                    }
                                    else
                                    {
                                        XtraMessageBox.Show("数据导入失败!");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"数据导入失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 导出数据按钮点击事件
        /// </summary>
        private void BtnExportData_Click(object? sender, EventArgs e)
        {
            try
            {
                // 创建保存文件对话框
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "Excel 文件|*.xlsx|CSV 文件|*.csv";
                    saveFileDialog.Title = "导出校准数据";
                    saveFileDialog.FileName = $"{deviceNumber}_校准数据_{DateTime.Now:yyyyMMdd_HHmmss}";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // 根据文件类型选择导出方式
                        if (Path.GetExtension(saveFileDialog.FileName).ToLower() == ".xlsx")
                        {
                            ExportToExcel(saveFileDialog.FileName);
                        }
                        else
                        {
                            //ExportToCsv(saveFileDialog.FileName);
                        }

                        XtraMessageBox.Show("数据导出成功!");
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"数据导出失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 查询数据按钮点击事件
        /// </summary>
        private void BtnQueryData_Click(object? sender, EventArgs e)
        {
            try
            {
                // 弹出设备编号输入对话框
                using (var inputForm = new DeviceNumberInputForm())
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        deviceNumber = inputForm.DeviceNumber;

                        // 查询并显示数据
                        QueryAndDisplayCalibrationData(deviceNumber);
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"查询数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清空查询按钮点击事件
        /// </summary>
        private void BtnClearQuery_Click(object? sender, EventArgs e)
        {
            // 加载数据
            LoadData();
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

                            if (equipment.CanType == "ZCAN_CANETTCP")
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
        private async Task<List<SignalInfo>> LoadTreeSignalsProtocolAsync(string type = null)
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
                        string calibrationsignal = node.GetValue("CalibrationSignal")?.ToString() ?? "";
                        string readtime = node.GetValue("ReadTime")?.ToString() ?? "";
                        string ratingVoltageCurrent = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                        string calibrationNumber = node.GetValue("CalibrationNumber")?.ToString() ?? "";

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
                                    var signalInfo = SQLite_Service.GetSignalByMessageAndSystemName(conn, message.MessageID, signalName);

                                    if (signalInfo != null)
                                    {
                                        // 设置CANID
                                        if (signalInfo.CANID.Contains("AX"))
                                        {
                                            int acnum = Convert.ToInt32(channel.Substring(2, channel.Length - 2));
                                            signalInfo.CANID = signalInfo.CANID?.Replace("AX", "A" + (acnum - 1));
                                        }
                                        else if (signalInfo.CANID.Contains("2X"))
                                        {
                                            int dcnum = Convert.ToInt32(channel.Substring(2, channel.Length - 2));
                                            signalInfo.CANID = signalInfo.CANID?.Replace("2X", "2" + (dcnum - 1));
                                        }

                                        signalInfo.SignalName = $"{deviceName}-{channel}-{signalInfo.SignalName}";

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
        /// 加载调试协议信号
        /// </summary>
        /// <returns>按信号名称分组的信号信息字典</returns>
        private async Task<Dictionary<string, List<SignalInfo>>> LoadDebugProtocolsAsync()
        {
            try
            {
                // 获取所有树节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();
                string connectionString = $"Data Source={sqladdress};Version=3;";
                Dictionary<string, List<SignalInfo>> allSignalInfos = new Dictionary<string, List<SignalInfo>>();

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
                        string signalType = node.GetValue("SignalType")?.ToString() ?? "";
                        string calibrationSignal = node.GetValue("CalibrationSignal")?.ToString() ?? "";
                        string readTime = node.GetValue("ReadTime")?.ToString() ?? "";
                        string ratingVoltageCurrent = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                        string calibrationNumber = node.GetValue("CalibrationNumber")?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(deviceName))
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
                                    if (message.MessageName.Contains(channel.Substring(0, 2)))
                                    {
                                        var signalInfo = SQLite_Service.GetSignalByMessageAndSystemNameList(conn, message.MessageID, calibrationSignal);
                                        if (signalInfo.Count != 0)
                                        {
                                            allSignalInfos.Add($"{deviceName}-{channel}-{signalName}-{calibrationSignal}", signalInfo);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return allSignalInfos;
            }
            catch (Exception ex)
            {
                LogService.Log($"加载调试校准协议失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动设备
        /// </summary>
        /// <param name="equipment">设备模型</param>
        /// <returns>启动是否成功</returns>
        private async Task<bool> StartEquipment(EquipmentModel equipment, string title)
        {
            try
            {
                // 根据设备通讯类型调用不同的启动方法
                switch (equipment.CanType)
                {
                    case "ZCAN_CANETTCP":
                        // 使用CAN启动设备
                        bool can = await StartDeviceAsync(equipment, title);
                        if (!can)
                        {
                            return false;
                        }
                        return true;

                    case "RS485-MODBUS":
                        // 使用RS485管理器启动设备
                        bool rs485modbus = RS485Manager.Instance.RegisterChannel(equipment);
                        if (!rs485modbus)
                        {
                            return false;
                        }
                        return true;

                    case "USB-SCPI":
                        // 使用USB-SCPI管理器启动设备
                        if (equipment.DeviceName == "GVM-9102")
                        {
                            bool usbscpi = GwinstekGVM9102_Communicator.Instance.Connect();
                            if (!usbscpi)
                            {
                                return false;
                            }
                            return true;
                        }
                        else if (equipment.DeviceName == "KEYSIGHT-34465A")
                        {
                            bool usbscpi = Keysight34465A_Communicator.Instance.Connect();
                            if (!usbscpi)
                            {
                                return false;
                            }
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                        
                    case "RS232":
                        bool rs232 = RS232Manager.Instance.RegisterChannel(equipment);
                        if (!rs232)
                        {
                            return false;
                        }
                        return true;

                    default:
                        XtraMessageBox.Show($"不支持的通讯类型: {equipment.CanType}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"启动设备 {equipment.DeviceName} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置电流表参数
        /// </summary>
        /// <param name="equipment">设备模型</param>
        /// <returns>启动是否成功</returns>
        private async Task<bool> SetPparameters(string type, EquipmentModel equipment, params object[] parameters)
        {
            try
            {
                byte[] command = new byte[] { };

                if (type == "设置远程控制帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xF0,0x01,0x01,0x00,0x00
                    };
                }
                else if (type == "设置交直流帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xA4,0x01,0x00,0x00,0x00
                    };
                }
                else if (type == "设置采样速度帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xA6,0x01,0x00,0x00,0x00
                    };
                }
                else if (type == "设置显示位数帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xA7,0x02,0x00,0x00,0x00
                    };
                }
                else if (type == "设置 NULL 开关帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xAA,0x01,0x00,0x00,0x00
                    };
                }
                else if (type == "读取电流数据帧")
                {
                    command = new byte[]
                    {
                        0xAA,0xAB,0x05,0xA2,0x00,0x00,0x00,0x00
                    };
                }

                bool sendSuccess = RS232Manager.Instance.SendData(equipment.ComPort, command, true);
                if (sendSuccess)
                {
                    await Task.Delay(200);
                    byte[] readBuffer = RS232Manager.Instance.ReadBuffer(equipment.ComPort);
                    if (readBuffer[4] == 0x01)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    LogService.Log($"发送命令失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"发送命令时发生错误: {ex.Message}");
                return false;
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
                byte[]? command = null;

                switch (commandType)
                {
                    case CommandType.SetMode:
                        int setmode = (int)parameters[0];
                        if (equipment.CanType == "RS485-MODBUS")
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
                        else if (equipment.CanType == "RS232")
                        {

                        }
                        // 可以添加其他设备类型的处理
                        break;

                    case CommandType.SetVoltage:
                        double voltageValue = (double)parameters[0];
                        if (equipment.CanType == "RS485-MODBUS")
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

                    case CommandType.SetCurrent:

                        break;

                    case CommandType.EnableOutput:
                        int enableoutput = (int)parameters[0];
                        if (equipment.CanType == "RS485-MODBUS")
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
                        sendSuccess = RS485Manager.Instance.SendData(equipment.ComPort, command);
                        break;

                    case "RS232":
                        //sendSuccess = RS232Manager.Instance.SendData(equipment.ComPort, command);
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
        }

        /// <summary>
        /// 从树形信号列表中获取校准点信息
        /// </summary>
        /// <param name="treeSignals">树形信号列表</param>
        /// <returns>校准点字典，键为信号名称，值为校准点列表</returns>
        private async Task<Dictionary<string, List<CalibrationPoint>>> GetCalibrationPoints(string type, List<SignalInfo> treeSignals)
        {
            var calibrationPoints = new Dictionary<string, List<CalibrationPoint>>();

            try
            {
                // 获取所有树节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                foreach (var node in messageNodes)
                {
                    // 只处理被勾选的节点
                    if (!node.Checked)
                        continue;

                    // 获取设备名称和信号名称
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                    string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                    string signalType = node.GetValue("SignalType")?.ToString() ?? "";
                    string calibrationSignal = node.GetValue("CalibrationSignal")?.ToString() ?? "";

                    if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(signalName))
                        continue;

                    // 只处理指定类型的信号
                    if (!signalType.Contains(type, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 获取校准点个数
                    string calibrationNumberStr = node.GetValue("CalibrationNumber")?.ToString() ?? "";
                    if (!int.TryParse(calibrationNumberStr, out int calibrationNumber) || calibrationNumber <= 0)
                        continue;

                    // 获取校准范围值（格式：0-100）
                    string calibrationRangeStr = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                    if (string.IsNullOrEmpty(calibrationRangeStr))
                        continue;

                    // 解析校准范围
                    var rangeParts = calibrationRangeStr.Split('-');
                    if (rangeParts.Length != 2 ||
                        !double.TryParse(rangeParts[0], out double minValue) ||
                        !double.TryParse(rangeParts[1], out double maxValue) ||
                        minValue >= maxValue)
                    {
                        continue;
                    }

                    // 获取精度范围
                    //string precisionRangeStr = node.GetValue("PrecisionRange")?.ToString() ?? "";
                    //if (!double.TryParse(precisionRangeStr, out double precisionRange) || ratingVoltage <= 0)
                    //    continue;

                    // 获取稳定读取时间
                    string readTimeStr = node.GetValue("ReadTime")?.ToString() ?? "";
                    if (!int.TryParse(readTimeStr, out int readTimeMs))
                        readTimeMs = 1000; // 默认1秒

                    // 查找对应的信号信息
                    var signalInfo = treeSignals.FirstOrDefault(s =>
                        s.SignalName.Equals($"{deviceName}-{signalName}", StringComparison.OrdinalIgnoreCase));

                    if (signalInfo == null)
                        continue;

                    // 生成校准点
                    var points = GenerateCalibrationPoints(signalType, minValue, maxValue, calibrationNumber, readTimeMs, signalInfo, deviceName, signalName, calibrationSignal);

                    // 添加到字典（支持同一个信号的多个校准范围）
                    string key = $"{deviceName}-{signalName}-{calibrationSignal}";
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

        /// <summary>
        /// 根据范围和点数生成校准点
        /// </summary>
        private List<CalibrationPoint> GenerateCalibrationPoints(string signalType, double minValue, double maxValue, int calibrationNumber,
            int readTimeMs, SignalInfo signalInfo, string deviceName, string signalName, string calibrationsignal)
        {
            var points = new List<CalibrationPoint>();

            if (calibrationNumber <= 1)
            {
                // 如果只有1个点，取中间值
                double value = (minValue + maxValue) / 2;
                points.Add(CreateCalibrationPoint(value, readTimeMs, signalInfo, deviceName, signalName, calibrationsignal));
            }
            else
            {
                if (signalType == "电压")
                {
                    // 计算步长
                    double step = (maxValue - minValue) / calibrationNumber;

                    // 生成等分点
                    for (int i = 0; i < calibrationNumber; i++)
                    {
                        double value = minValue + (step * (i + 1));
                        points.Add(CreateCalibrationPoint(value, readTimeMs, signalInfo, deviceName, signalName, calibrationsignal));
                    }
                }
                else
                {
                    // 计算步长
                    double step = (maxValue - minValue) / (calibrationNumber / 2);

                    // 生成负值等分点
                    for (int i = 0; i < calibrationNumber / 2; i++)
                    {
                        double value = minValue + (-step * (i + 1));
                        points.Add(CreateCalibrationPoint(value, readTimeMs, signalInfo, deviceName, signalName, calibrationsignal));
                    }

                    // 生成负值等分点
                    for (int i = 0; i < calibrationNumber / 2; i++)
                    {
                        double value = minValue + (step * (i + 1));
                        points.Add(CreateCalibrationPoint(value, readTimeMs, signalInfo, deviceName, signalName, calibrationsignal));
                    }
                }
            }

            return points;
        }

        /// <summary>
        /// 创建校准点对象
        /// </summary>
        private CalibrationPoint CreateCalibrationPoint(double value, int readTimeMs, SignalInfo signalInfo,
            string deviceName, string signalName, string calibrationSignal)
        {
            return new CalibrationPoint
            {
                Voltage = value,
                ReadTimeMs = readTimeMs,
                SignalInfo = signalInfo,
                DeviceName = deviceName,
                SignalName = signalName,
                CalibrationSignal = calibrationSignal
            };
        }

        #endregion

        #region 校准流程封装方法

        /// <summary>
        /// 执行完整的电压校准流程
        /// </summary>
        public async Task<bool> ExecuteVoltageCalibration(CancellationToken cancellationToken)
        {
            EquipmentModel voltageSource = null;
            EquipmentModel voltmeter = null;

            try
            {
                // 在关键位置添加取消检查
                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(0, "开始初始化设备:");

                // 1. 获取选中的设备
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                string? voltmeterName = cbVoltmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(voltageSourceName) || string.IsNullOrEmpty(voltmeterName))
                {
                    //XtraMessageBox.Show("请先选择所有必要的校准设备!");
                    LogService.Log("请先选择所有必要的校准设备!");
                    return false;
                }

                // 2. 从设备列表中查找设备信息
                voltageSource = equipmentList.FirstOrDefault(e => e.DeviceName == voltageSourceName);
                voltmeter = equipmentList.FirstOrDefault(e => e.DeviceName == voltmeterName);
                if (voltageSource == null || voltmeter == null)
                {
                    //XtraMessageBox.Show("未找到选定的设备配置信息!");
                    LogService.Log("未找到选定的设备配置信息!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.InitializeDevice, "开始加载协议:");

                // 3. 加载校准设备指令协议
                var protocols = await LoadProtocolsAsync(voltageSource, voltmeter);

                // 分离电压源和电压表的协议
                voltageSourceProtocols = protocols.Where(p => p.DeviceName == voltageSource.DeviceName).ToList();
                voltmeterProtocols = protocols.Where(p => p.DeviceName == voltmeter.DeviceName).ToList();

                // 4.加载树形图信号名称协议
                treeSignalProtocols = await LoadTreeSignalsProtocolAsync();
                if (treeSignalProtocols.Count == 0)
                {
                    LogService.Log("没有选择电压信号!");
                    return false;
                }

                // 5.加载调试协议
                debugProtocols = await LoadDebugProtocolsAsync();
                if (debugProtocols.Count == 0)
                {
                    LogService.Log("加载校准信号协议失败!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.LoadProtocol, "开始启动设备:");

                // 5. 根据设备通讯类型启动设备
                bool voltageSourceStarted = await StartEquipment(voltageSource, "");
                bool voltmeterStarted = await StartEquipment(voltmeter, "");
                if (!voltageSourceStarted || !voltmeterStarted)
                {
                    LogService.Log("设备启动失败，请检查设备连接!");
                    return false;
                }
                LogService.Log("所有校准设备启动成功，开始电压校准流程!");

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.StartingEquipment, "开始设置参数:");

                // 6. 设置设备模式
                LogService.Log("设置电压源为程控模式...");
                bool modeSet = await SendEquipmentCommand(CommandType.SetMode, voltageSource, voltageSourceProtocols, 0x01);
                if (!modeSet)
                {
                    //XtraMessageBox.Show("设置设备模式失败!");
                    LogService.Log("设置设备模式失败!");
                    return false;
                }

                // 7. 设置初始电压值（从0开始）
                LogService.Log("设置初始电压值...");
                bool voltageSet = await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceProtocols, 0.0);
                if (!voltageSet)
                {
                    //XtraMessageBox.Show("设置初始电压失败!");
                    LogService.Log("设置初始电压失败!");
                    return false;
                }

                // 8. 开机/启用输出
                LogService.Log("启用电压源输出...");
                bool outputEnabled = await SendEquipmentCommand(CommandType.EnableOutput, voltageSource, voltageSourceProtocols, 0x01);
                if (!outputEnabled)
                {
                    //XtraMessageBox.Show("启用输出失败!");
                    LogService.Log("启用输出失败!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.SetParameters, "开始获取校准点:");

                // 9. 获取校准点信息
                var calibrationPoints = await GetCalibrationPoints("电压", treeSignalProtocols);

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.HandlePoint, "开始校准电压:");

                // 10. 遍历每个校准点进行校准
                bool calibrationSuccess = await ProcessCalibrationPoints(
                    calibrationPoints,
                    voltageSource,
                    voltmeter,
                    treeSignalProtocols,
                    debugProtocols,
                    cancellationToken, "电压", "",
                    voltageSourceProtocols,
                    voltmeterProtocols);
                if (!calibrationSuccess)
                {
                    //XtraMessageBox.Show("校准电压失败!");
                    LogService.Log("电压校准失败!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.BeforeCalibration, "开始验证电压:");

                // 11. 进行校准后验证
                bool verificationSuccess = await PerformPostCalibrationVerification(
                    calibrationPoints,
                    voltageSource,
                    voltmeter,
                    treeSignalProtocols,
                    debugProtocols,
                    cancellationToken, "电压", "",
                    voltageSourceProtocols,
                    voltmeterProtocols);
                if (!verificationSuccess)
                {
                    //XtraMessageBox.Show("校准电压失败!");
                    LogService.Log("电压校准后验证失败!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.AfterCalibration, "开始断开校准仪器:");

                return true;
            }
            catch (OperationCanceledException)
            {
                LogService.Log("校准操作已被用户取消");
                return false;
            }
            catch (Exception ex)
            {
                LogService.Log($"电压校准失败: {ex.Message}");
                XtraMessageBox.Show($"电压校准失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 无论校准成功与否，都执行关闭操作
                LogService.Log("关闭电压源输出...");
                await SafeShutdownEquipment(voltageSource, voltmeter);

                if (_calibrationCancellationRequested)
                {
                    UpdateUIForCancellation();
                }
                else
                {
                    UpdateProgress(ProgressStage.Completed, "电压校准完成!");
                }
            }
        }

        /// <summary>
        /// 执行完整的电流校准流程
        /// </summary>
        public async Task<bool> ExecuteCurrentCalibration(CancellationToken cancellationToken)
        {
            EquipmentModel currentSource = null;
            EquipmentModel ammeter = null;

            try
            {
                // 在关键位置添加取消检查
                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(0, "开始初始化设备:");

                // 1. 获取选中的设备
                string? currentSourceName = InitializeCurrentSourceModule();
                string? ammeterName = cbAmmeter.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(currentSourceName) || string.IsNullOrEmpty(ammeterName))
                {
                    XtraMessageBox.Show("请先选择所有必要的校准设备!");
                    return false;
                }

                // 查找对应的启动管理器
                startupManager = FindStartupManager(currentSourceName);
                if (startupManager == null)
                {
                    LogService.Log($"找不到设备 {currentSourceName} 的启动管理器");
                    return false;
                }

                // 2. 从设备列表中查找设备信息
                currentSource = equipmentList.FirstOrDefault(e => e.DeviceName == currentSourceName.Split("-")[0]);
                ammeter = equipmentList.FirstOrDefault(e => e.DeviceName == ammeterName);
                if (currentSource == null || ammeter == null)
                {
                    XtraMessageBox.Show("未找到选定的设备配置信息!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.InitializeDevice, "开始加载协议:");

                // 3. 加载校准设备指令协议
                //var protocols = await LoadProtocolsAsync(voltageSource, voltmeter);

                //// 分离电压源和电压表的协议
                //voltageSourceProtocols = protocols.Where(p => p.DeviceName == voltageSource.DeviceName).ToList();
                //voltmeterProtocols = protocols.Where(p => p.DeviceName == voltmeter.DeviceName).ToList();

                // 4.加载树形图信号名称协议
                treeSignalProtocols = await LoadTreeSignalsProtocolAsync();
                if (treeSignalProtocols.Count == 0)
                {
                    LogService.Log("没有选择电流信号!");
                    return false;
                }

                // 5.加载调试协议
                debugProtocols = await LoadDebugProtocolsAsync();
                if (debugProtocols.Count == 0)
                {
                    LogService.Log("加载校准信号协议失败!");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.LoadProtocol, "开始启动设备:");

                // 5. 根据设备通讯类型启动通讯接口
                string channelName = currentSourceName.Split("-")[1];
                bool currentSourceStarted = true;
                bool ammeterStarted = await StartEquipment(ammeter, "");
                if (!currentSourceStarted || !ammeterStarted)
                {
                    LogService.Log("设备启动失败，请检查设备连接!");
                    return false;
                }
                LogService.Log("所有校准设备启动成功，开始电压校准流程!");

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.StartingEquipment, "开始设置参数:");

                // 6. 设置设备模式
                LogService.Log("设置电流源参数...");
                bool setpparameters1 = await SetPparameters("设置远程控制帧", ammeter);
                bool setpparameters2 = await SetPparameters("设置交直流帧", ammeter);
                bool setpparameters3 = await SetPparameters("设置采样速度帧", ammeter);
                bool setpparameters4 = await SetPparameters("设置显示位数帧", ammeter);
                bool setpparameters5 = await SetPparameters("设置 NULL 开关帧", ammeter);
                if (!setpparameters1 || !setpparameters2 || !setpparameters3 || !setpparameters4 || !setpparameters5)
                {
                    XtraMessageBox.Show("设置设备模式失败!");
                    return false;
                }

                // 7. 设置初始电流值（从0开始）
                //LogService.Log("设置初始电流值...");
                //bool voltageSet = await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceProtocols, 0.0);
                //if (!voltageSet)
                //{
                //    XtraMessageBox.Show("设置初始电压失败!");
                //    return false;
                //}

                // 8. 开机/启用输出
                //LogService.Log("启用电流源输出..."); 
                //bool outputEnabled = await StartEquipment(currentSource, currentSourceName);
                //if (!outputEnabled)
                //{
                //    XtraMessageBox.Show("启用输出失败!");
                //    return false;
                //}

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.SetParameters, "开始获取校准点:");

                // 9. 获取校准点信息
                var calibrationPoints = await GetCalibrationPoints("电流", treeSignalProtocols);

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.HandlePoint, "开始校准电流:");

                // 10. 遍历每个校准点进行校准
                bool calibrationSuccess = await ProcessCalibrationPoints(
                    calibrationPoints,
                    currentSource,
                    ammeter,
                    treeSignalProtocols,
                    debugProtocols,
                    cancellationToken, "电流", currentSourceName);
                if (!calibrationSuccess)
                {
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                UpdateProgress(ProgressStage.BeforeCalibration, "开始验证电流:");

                // 11. 进行校准后验证
                bool verificationSuccess = await PerformPostCalibrationVerification(
                    calibrationPoints,
                    currentSource,
                    ammeter,
                    treeSignalProtocols,
                    debugProtocols,
                    cancellationToken, "电流", currentSourceName);
                if (!verificationSuccess)
                {
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                LogService.Log("校准操作已被用户取消");
                return false;
            }
            catch (Exception ex)
            {
                LogService.Log($"电流校准失败: {ex.Message}");
                //XtraMessageBox.Show($"电流校准失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 停止电流源设备
                LogService.Log("关闭电流源输出...");
                await startupManager.SetParameters(0x00, 0.0, 0.0);

                if (_calibrationCancellationRequested)
                {
                    UpdateUIForCancellation();
                }
                else
                {
                    UpdateProgress(ProgressStage.Completed, "电压校准完成!");
                }
            }
        }

        /// <summary>
        /// 设置电流源参数
        /// </summary>
        private async Task<bool> SetCurrentSourceParameters()
        {
            try
            {
                // 使用Module实例设置电流源参数
                // 这里需要根据实际的电流源设备协议实现参数设置

                // 示例：设置电流源为恒流模式，量程等
                // 具体实现取决于电流源设备的通信协议

                LogService.Log("设置电流源参数成功");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"设置电流源参数失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartDeviceAsync(EquipmentModel equipment, string _title)
        {
            try
            {
                // 显示启动配置对话框
                using (var configForm = new StartConfiguration(_title, "启动配置", false))
                {
                    if (configForm.ShowDialog() == DialogResult.OK)
                    {
                        // 获取用户设置的配置数据StartCurrent
                        _protectionParameters = configForm.Configuration;

                        bool success = await startupManager.StartCurrent(_protectionParameters);
                        if (!success)
                        {
                            LogService.Log("设备启动失败!");
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
                throw new Exception($"设备启动失败: {ex.Message}");
            }

        }

        /// <summary>
        /// 从树形图中获取勾选的电流信号设备
        /// </summary>
        /// <returns>勾选的设备名称列表</returns>
        private List<string> GetSelectedCurrentDevices()
        {
            var selectedDevices = new List<string>();

            // 遍历树形图所有节点
            foreach (TreeListNode node in treeList.Nodes)
            {
                // 只处理被勾选的节点
                if (!node.Checked)
                    continue;

                // 获取信号类型
                string signalType = node.GetValue("SignalType")?.ToString() ?? "";

                // 只处理电流信号
                if (signalType.Contains("电流", StringComparison.OrdinalIgnoreCase))
                {
                    // 获取设备名称
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(deviceName) && !selectedDevices.Contains(deviceName))
                    {
                        selectedDevices.Add(deviceName);
                    }
                }
            }

            return selectedDevices;
        }

        /// <summary>
        /// 安全关闭设备输出并断开连接
        /// </summary>
        private async Task SafeShutdownEquipment(EquipmentModel voltageSource, EquipmentModel voltmeter)
        {
            try
            {
                // 关闭电压源输出
                if (voltageSource != null)
                {
                    await CloseVoltageSourceOutput(voltageSource);
                }

                // 断开设备连接
                if (voltageSource != null)
                {
                    await DisconnectEquipment(voltageSource);
                }

                if (voltmeter != null)
                {
                    await DisconnectEquipment(voltmeter);
                }

                LogService.Log("设备已安全关闭并断开连接");
            }
            catch (Exception ex)
            {
                LogService.Log($"关闭设备时发生错误: {ex.Message}");
                // 不抛出异常，确保主流程不受影响
            }
        }

        /// <summary>
        /// 处理所有校准点
        /// </summary>
        private async Task<bool> ProcessCalibrationPoints(
            Dictionary<string, List<CalibrationPoint>> calibrationPoints,
            EquipmentModel voltageSource,
            EquipmentModel voltmeter,
            List<SignalInfo> treeSignals,
            Dictionary<string, List<SignalInfo>> debugSignals,
            CancellationToken cancellationToken, string type, string currentSourceName,
            List<ModbusSignal> voltageSourceSignals = null,
            List<ModbusSignal> voltmeterSignals = null)
        {
            try
            {
                LogService.Log("开始遍历所有校准点进行校准...");
                // 计算总点数
                int totalPoints = calibrationPoints.Values.Sum(points => points.Count);
                int globalPointIndex = 0; // 全局点数索引（仅用于进度计算）

                foreach (var signalKey in calibrationPoints.Keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var points = calibrationPoints[signalKey];
                    var firstPoint = points.FirstOrDefault();

                    if (firstPoint == null)
                        continue;

                    LogService.Log($"开始校准信号: {firstPoint.DeviceName} - {firstPoint.SignalName}, 共 {points.Count} 个校准点");

                    // 为当前信号重置校准点索引（从1开始）
                    int signalPointIndex = 0;

                    // 为每个信号准备数据收集
                    var measuredValues = new List<double>();
                    var actualValues = new List<double>();

                    SignalInfo scaleFactorSignal = null;
                    SignalInfo zeroFactorSignal = null;

                    if (debugSignals.TryGetValue($"{firstPoint.DeviceName}-{firstPoint.SignalName}-{firstPoint.CalibrationSignal}", out var scaleSignals) && scaleSignals.Count > 0)
                    {
                        scaleFactorSignal = scaleSignals[0];
                        zeroFactorSignal = scaleSignals[1];
                    }

                    // 读取原有的比例系数和零点系数
                    double originalScaleFactor = 0.0;
                    double originalZeroFactor = 0.0;

                    if (scaleFactorSignal != null && zeroFactorSignal != null)
                    {
                        int num = 0;
                        while (num < 3)
                        {
                            var (scale, zero) = await ReadCalibrationFactors(
                                            firstPoint.DeviceName,
                                            scaleFactorSignal,
                                            zeroFactorSignal);

                            if (scale == 0.0 && zero == 0.0)
                            {
                                num++;
                            }
                            else
                            {
                                originalScaleFactor = scale;
                                originalZeroFactor = zero;
                                num = 0;
                                LogService.Log($"读取原有校准系数 - 比例系数: {originalScaleFactor}, 零点系数: {originalZeroFactor}");
                                break;
                            }
                        }

                        if (num >= 3)
                        {
                            LogService.Log($"读取原有校准系数失败");
                            return false;
                        }
                    }

                    // 对于电流校准，按照特定顺序处理校准点
                    if (type == "电流")
                    {
                        LogService.Log("启用电流源输出...");
                        bool outputEnabled = await StartEquipment(voltageSource, currentSourceName);
                        if (!outputEnabled)
                        {
                            //XtraMessageBox.Show("启用输出失败!");
                            return false;
                        }

                        // 分离负电流和正电流校准点
                        var negativePoints = points.Where(p => p.Voltage < 0).OrderByDescending(p => p.Voltage).ToList();
                        var positivePoints = points.Where(p => p.Voltage > 0).OrderBy(p => p.Voltage).ToList();

                        double currentVoltage = 0;

                        // 处理负电流部分：从0到-额定值，步进20A
                        LogService.Log("开始负电流上升阶段: 0A -> -额定值 (步进20A)");
                        foreach (var point in negativePoints)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            try
                            {
                                // 逐步增加到目标负电流值，步进20A
                                while (currentVoltage > point.Voltage)
                                {
                                    double stepVoltage = Math.Max(currentVoltage - 20, point.Voltage);

                                    LogService.Log($"设置电流 {stepVoltage}A");
                                    bool setcurrent = await startupManager.SetParameters(0x23, stepVoltage, 0.0);

                                    if (!setcurrent)
                                    {
                                        LogService.Log($"设置电流 {stepVoltage}A 失败，跳过此校准点");
                                        return false;
                                    }

                                    currentVoltage = stepVoltage;
                                    await Task.Delay(1000, cancellationToken);
                                }

                                // 等待电流稳定
                                LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                                await Task.Delay(point.ReadTimeMs, cancellationToken);

                                var currentvalue = await ReadCurrentValue(voltmeter);

                                LogService.Log($"电流表测量值: {currentvalue}A, 设备电流采样值: {point.Voltage}A");

                                // 保存测量数据
                                measuredValues.Add(point.Voltage);
                                actualValues.Add(currentvalue);

                                // 为当前校准点创建子节点并更新值
                                UpdateTreeNodeValue(firstPoint.DeviceName, firstPoint.SignalName,
                                    currentvalue, point.Voltage, signalPointIndex, "电流");
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"校准点 {point.Voltage}A 处理失败: {ex.Message}");
                                return false;
                            }

                            // 更新进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.HandlePoint,
                                          $"处理校准点 {globalPointIndex}/{totalPoints}", progress);
                        }

                        // 负电流下降阶段：从-额定值到0，步进50A
                        LogService.Log("开始负电流下降阶段: -额定值 -> 0A (步进50A)");
                        while (currentVoltage < 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            double stepVoltage = Math.Min(currentVoltage + 20, 0);

                            LogService.Log($"设置电流 {stepVoltage}A");
                            bool setcurrent = await startupManager.SetParameters(0x23, stepVoltage, 0.0);

                            if (!setcurrent)
                            {
                                LogService.Log($"设置电流 {stepVoltage}A 失败");
                                return false;
                            }

                            currentVoltage = stepVoltage;
                            await Task.Delay(1000, cancellationToken);
                        }

                        // 处理正电流部分：从0到额定值，步进20A
                        LogService.Log("开始正电流上升阶段: 0A -> 额定值 (步进20A)");
                        foreach (var point in positivePoints)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            try
                            {
                                // 逐步增加到目标正电流值，步进20A
                                while (currentVoltage < point.Voltage)
                                {
                                    double stepVoltage = Math.Min(currentVoltage + 20, point.Voltage);

                                    LogService.Log($"设置电流 {stepVoltage}A");

                                    bool setcurrent = await startupManager.SetParameters(0x03, stepVoltage, 0.0);

                                    if (!setcurrent)
                                    {
                                        LogService.Log($"设置电流 {stepVoltage}A 失败，跳过此校准点");
                                        return false;
                                    }

                                    currentVoltage = stepVoltage;
                                    await Task.Delay(1000, cancellationToken);
                                }

                                // 等待电流稳定
                                LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                                await Task.Delay(point.ReadTimeMs, cancellationToken);

                                var currentvalue = await ReadCurrentValue(voltmeter);

                                LogService.Log($"电流表测量值: {currentvalue}A, 设备电流采样值: {point.Voltage}A");

                                // 保存测量数据
                                measuredValues.Add(point.Voltage);
                                actualValues.Add(currentvalue);

                                // 为当前校准点创建子节点并更新值
                                UpdateTreeNodeValue(firstPoint.DeviceName, firstPoint.SignalName,
                                    currentvalue, point.Voltage, signalPointIndex, "电流");
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"校准点 {point.Voltage}A 处理失败: {ex.Message}");
                                return false;
                            }

                            // 更新进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.HandlePoint,
                                          $"处理校准点 {globalPointIndex}/{totalPoints}",
                                          progress);
                        }

                        // 正电流下降阶段：从额定值到0，步进50A
                        LogService.Log("开始正电流下降阶段: 额定值 -> 0A (步进50A)");
                        while (currentVoltage > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            double stepVoltage = Math.Max(currentVoltage - 20, 0);

                            LogService.Log($"设置电流 {stepVoltage}A");

                            bool setcurrent = await startupManager.SetParameters(0x03, stepVoltage, 0.0);

                            if (!setcurrent)
                            {
                                LogService.Log($"设置电流 {stepVoltage}A 失败");
                                return false;
                            }

                            currentVoltage = stepVoltage;
                            await Task.Delay(1000, cancellationToken); // 短暂等待
                        }

                        LogService.Log("关闭电流源输出...");

                        await startupManager.SetParameters(0x00, 0.0, 0.0);

                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        foreach (var point in points)
                        {
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            LogService.Log($"设置校准点 {globalPointIndex}/{totalPoints}: {point.Voltage}V");

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
                            await Task.Delay(point.ReadTimeMs, cancellationToken);

                            var (actualVoltage, deviceVoltage) = await ReadVoltageValuesSync(
                                voltmeter,
                                voltmeterSignals,
                                firstPoint.DeviceName,
                                firstPoint.SignalInfo,
                                treeSignals);

                            //LogService.Log($"电压表测量值: {actualVoltage}V, 设备电压采样值: {deviceVoltage}V");

                            // 保存测量数据
                            measuredValues.Add(deviceVoltage);
                            actualValues.Add(actualVoltage);

                            // 为当前校准点创建子节点并更新值
                            UpdateTreeNodeValue(firstPoint.DeviceName, firstPoint.SignalName,
                                actualVoltage, deviceVoltage, signalPointIndex, "电压");

                            // 更新进度 - 使用全局点数计算进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.HandlePoint,
                                          $"处理校准点 {globalPointIndex}/{totalPoints}",
                                          progress);
                        }

                        //关闭输出0.0
                        LogService.Log("关闭电压源输出...");
                        bool outpower = await SendEquipmentCommand(
                                    CommandType.SetVoltage,
                                    voltageSource,
                                    voltageSourceSignals,
                                    0.0);
                    }

                    // 计算校准系数 (使用线性回归 y = kx + b)
                    if (measuredValues.Count >= 2)
                    {
                        try
                        {
                            // 计算比例系数(k)和截距(b)
                            var (scaleFactor, zeroFactor) = CalculateCalibrationFactors(measuredValues, actualValues);
                            int scalePlaces = CANManager.Instance.GetNumberOfDecimalPlaces(scaleFactorSignal.Factor);
                            int zeroPlaces = CANManager.Instance.GetNumberOfDecimalPlaces(zeroFactorSignal.Factor);
                            double newScaleFactor = Math.Round(scaleFactor * originalScaleFactor, scalePlaces);
                            double newZeroFactor = Math.Round(zeroFactor + originalZeroFactor, zeroPlaces);

                            LogService.Log($"计算完成 - 比例系数: {newScaleFactor}, 零点系数: {newZeroFactor}");

                            // 更新数据库的校准系数
                            //await UpdateCalibrationFactors(firstPoint.DeviceName, firstPoint.SignalName,
                            //    newScaleFactor, newZeroFactor);

                            // 更新UI页面的校准系数
                            // 更新UI页面的校准系数
                            UpdateTreeNodeCalibrationFactors(firstPoint.DeviceName, firstPoint.SignalName,
                                newScaleFactor, newZeroFactor);

                            // 将新的校准系数写入设备
                            if (scaleFactorSignal != null && zeroFactorSignal != null)
                            {
                                //if (0.9 > newScaleFactor || newScaleFactor > 1.1)
                                //{
                                //    LogService.Log($"比例系数{newScaleFactor}超出设置(0.9—1.1)范围");
                                //    return false;
                                //}

                                //if (-5.0 > newZeroFactor || newZeroFactor > 5.0)
                                //{
                                //    LogService.Log($"零点系数{newZeroFactor}超出设置(-10.0—10.0)范围");
                                //    return false;
                                //}

                                bool writeSuccess = await WriteCalibrationFactors(
                                    firstPoint.DeviceName,
                                    scaleFactorSignal, zeroFactorSignal,
                                    newScaleFactor, newZeroFactor);

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
                            //bool calibrationValid = await VerifyCalibration(
                            //    firstPoint.DeviceName, firstPoint.SignalInfo,
                            //    scaleFactor, zeroFactor, voltageSource, voltmeter,
                            //    voltageSourceSignals, voltmeterSignals, treeSignals);

                            //string resultText = calibrationValid ? "成功" : "失败";

                            //LogService.Log($"校准验证: {resultText}");
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
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"处理校准点时发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task<double> ReadCurrentValue(EquipmentModel ammeter)
        {
            try
            {
                byte[] command = new byte[]
                { 0xAA, 0xAB, 0x05, 0xA2, 0x00, 0x00, 0x00, 0x00 };

                bool sendSuccess = RS232Manager.Instance.SendData(ammeter.ComPort, command, true);
                if (sendSuccess)
                {
                    await Task.Delay(200);
                    byte[] readBuffer = RS232Manager.Instance.ReadBuffer(ammeter.ComPort);

                    // 基本帧检查
                    if (readBuffer.Length < 16 ||
                        readBuffer[0] != 0xAA ||
                        readBuffer[1] != 0xAB ||
                        readBuffer[3] != 0xA2 ||
                        readBuffer[15] != 0x55)
                    {
                        LogService.Log("接收到的数据帧格式错误");
                        return double.NaN;
                    }

                    // 校验和检查
                    byte checksum = 0;
                    for (int i = 2; i <= 13; i++)
                    {
                        checksum += readBuffer[i];
                    }

                    if (checksum != readBuffer[14])
                    {
                        LogService.Log("校验和验证失败");
                        return double.NaN;
                    }

                    // 解析符号
                    bool isPositive = readBuffer[4] == 0x2B;

                    if (_protectionParameters.Directionammeter == "反方向")
                    {
                        isPositive = !isPositive;
                    }

                    // 解析数值部分（索引5到12共8个字节，包括小数点）
                    string valueStr = "";
                    for (int i = 5; i <= 12; i++)
                    {
                        // 将字节转换为对应的ASCII字符
                        char c = (char)readBuffer[i];

                        // 处理小数点
                        if (c == '.') // 0x2E对应ASCII的小数点
                        {
                            valueStr += ".";
                        }
                        else if (char.IsDigit(c)) // 数字字符
                        {
                            valueStr += c;
                        }
                        else
                        {
                            // 如果有非数字字符且不是小数点，记录警告但继续处理
                            LogService.Log($"警告：数据部分包含非数字字符: 0x{readBuffer[i]:X2}");
                            valueStr += c; // 仍然添加到字符串中，让TryParse处理
                        }
                    }

                    // 转换为数字
                    if (double.TryParse(valueStr, out double result))
                    {
                        // 应用符号
                        result = isPositive ? result : -result;

                        // 单位转换
                        switch (readBuffer[13])
                        {
                            case 0x01: // uA
                                result *= 1e-6;
                                break;
                            case 0x02: // mA
                                result *= 1e-3;
                                break;
                            case 0x03: // A (不需要转换)
                                break;
                            case 0x04: // kA
                                result *= 1e3;
                                break;
                            default:
                                LogService.Log("未知单位");
                                return double.NaN;
                        }

                        return result;
                    }
                    else
                    {
                        LogService.Log($"数值解析失败: {valueStr}");
                        return double.NaN;
                    }
                }
                else
                {
                    LogService.Log("发送命令失败");
                    return double.NaN;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"读取电流数据帧操作失败{ex.Message}！");
                return double.NaN;
            }
        }

        /// <summary>
        /// 执行校准后验证
        /// </summary>
        private async Task<bool> PerformPostCalibrationVerification(
            Dictionary<string, List<CalibrationPoint>> calibrationPoints,
            EquipmentModel voltageSource,
            EquipmentModel voltmeter,
            List<SignalInfo> treeSignals,
            Dictionary<string, List<SignalInfo>> debugSignals,
            CancellationToken cancellationToken, string type, string currentSourceName,
            List<ModbusSignal> voltageSourceSignals = null,
            List<ModbusSignal> voltmeterSignals = null)
        {
            try
            {
                LogService.Log("开始校准后验证...");

                int totalPoints = calibrationPoints.Values.Sum(points => points.Count);
                int globalPointIndex = 0; // 全局点数索引（仅用于进度计算）

                foreach (var signalKey in calibrationPoints.Keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var points = calibrationPoints[signalKey];
                    var firstPoint = points.FirstOrDefault();

                    if (firstPoint == null)
                        continue;

                    LogService.Log($"开始验证信号: {firstPoint.DeviceName} - {firstPoint.SignalName}, 共 {points.Count} 个校准点");

                    // 为当前信号重置校准点索引（从1开始）
                    int signalPointIndex = 0;

                    // 获取最大电压和精度范围
                    var (ratingVoltage, precisionRange) = GetRatingVoltageAndPrecision(
                        firstPoint.DeviceName, firstPoint.SignalName);

                    if (type == "电流")
                    {
                        LogService.Log("启用电流源输出...");
                        bool outputEnabled = await StartEquipment(voltageSource, currentSourceName);
                        if (!outputEnabled)
                        {
                            XtraMessageBox.Show("启用输出失败!");
                            return false;
                        }

                        // 分离负电流和正电流校准点
                        var negativePoints = points.Where(p => p.Voltage < 0).OrderByDescending(p => p.Voltage).ToList();
                        var positivePoints = points.Where(p => p.Voltage > 0).OrderBy(p => p.Voltage).ToList();

                        double currentVoltage = 0;

                        foreach (var point in negativePoints)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            try
                            {
                                // 逐步增加到目标负电流值，步进20A
                                while (currentVoltage > point.Voltage)
                                {
                                    double stepVoltage = Math.Max(currentVoltage - 20, point.Voltage);

                                    LogService.Log($"设置电流 {stepVoltage}A");

                                    bool setcurrent = await startupManager.SetParameters(0x23, stepVoltage, 0.0);

                                    if (!setcurrent)
                                    {
                                        LogService.Log($"设置电流 {stepVoltage}A 失败，跳过此校准点");
                                        return false;
                                    }

                                    currentVoltage = stepVoltage;
                                    await Task.Delay(1000, cancellationToken);
                                }

                                // 等待电流稳定
                                LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                                await Task.Delay(point.ReadTimeMs, cancellationToken);

                                var currentvalue = await ReadCurrentValue(voltmeter);

                                LogService.Log($"电流表测量值: {currentvalue}A, 设备电流采样值: {point.Voltage}A");

                                // 计算精度: (采样电压 - 测量电压) / 额定电压
                                double accuracy = (point.Voltage - currentvalue) / ratingVoltage;
                                double accuracyPercentage = accuracy * 100; // 转换为百分比

                                // 判断是否在精度范围内
                                bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                                LogService.Log($"校准精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                                // 更新UI：校准后电压采样值、测量值、校准精度、是否合格
                                UpdateTreeNodePostCalibrationValues(
                                    firstPoint.DeviceName,
                                    firstPoint.SignalName,
                                    signalPointIndex,
                                    point.Voltage,
                                    currentvalue,
                                    accuracyPercentage,
                                    withinPrecision, "电流");
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"校准点 {point.Voltage}A 处理失败: {ex.Message}");
                            }

                            // 更新进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.BeforeCalibration,
                                          $"处理校准点 {globalPointIndex}/{totalPoints}", progress);
                        }

                        // 负电流下降阶段：从-额定值到0，步进50A
                        LogService.Log("开始负电流下降阶段: -额定值 -> 0A (步进50A)");
                        while (currentVoltage < 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            double stepVoltage = Math.Min(currentVoltage + 20, 0);

                            LogService.Log($"设置电流 {stepVoltage}A");

                            bool setcurrent = await startupManager.SetParameters(0x23, stepVoltage, 0.0);

                            if (!setcurrent)
                            {
                                LogService.Log($"设置电流 {stepVoltage}A 失败");
                                return false;
                            }

                            currentVoltage = stepVoltage;
                            await Task.Delay(1000, cancellationToken); // 短暂等待
                        }

                        // 处理正电流部分：从0到额定值，步进20A
                        LogService.Log("开始正电流上升阶段: 0A -> 额定值 (步进20A)");
                        foreach (var point in positivePoints)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            try
                            {
                                // 逐步增加到目标正电流值，步进20A
                                while (currentVoltage < point.Voltage)
                                {
                                    double stepVoltage = Math.Min(currentVoltage + 20, point.Voltage);

                                    LogService.Log($"设置电流 {stepVoltage}A");

                                    bool setcurrent = await startupManager.SetParameters(0x03, stepVoltage, 0.0);

                                    if (!setcurrent)
                                    {
                                        LogService.Log($"设置电流 {stepVoltage}A 失败");
                                        return false;
                                    }

                                    currentVoltage = stepVoltage;
                                    await Task.Delay(1000, cancellationToken);
                                }

                                // 等待电流稳定
                                LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                                await Task.Delay(point.ReadTimeMs, cancellationToken);

                                var currentvalue = await ReadCurrentValue(voltmeter);

                                LogService.Log($"电流表测量值: {currentvalue}A, 设备电流采样值: {point.Voltage}A");

                                // 计算精度: (采样电压 - 测量电压) / 额定电压
                                double accuracy = (point.Voltage - currentvalue) / ratingVoltage;
                                double accuracyPercentage = accuracy * 100; // 转换为百分比

                                // 判断是否在精度范围内
                                bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                                LogService.Log($"校准精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                                // 更新UI：校准后电压采样值、测量值、校准精度、是否合格
                                UpdateTreeNodePostCalibrationValues(
                                    firstPoint.DeviceName,
                                    firstPoint.SignalName,
                                    signalPointIndex,
                                    point.Voltage,
                                    currentvalue,
                                    accuracyPercentage,
                                    withinPrecision, "电流");
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"校准点 {point.Voltage}A 处理失败: {ex.Message}");
                            }

                            // 更新进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.BeforeCalibration,
                                          $"验证校准点 {globalPointIndex}/{totalPoints}",
                                          progress);
                        }

                        // 正电流下降阶段：从额定值到0，步进50A
                        LogService.Log("开始正电流下降阶段: 额定值 -> 0A (步进50A)");
                        while (currentVoltage > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            double stepVoltage = Math.Max(currentVoltage - 20, 0);

                            LogService.Log($"设置电流 {stepVoltage}A");

                            bool setcurrent = await startupManager.SetParameters(0x03, stepVoltage, 0.0);

                            if (!setcurrent)
                            {
                                LogService.Log($"设置电流 {stepVoltage}A 失败");
                                return false;
                            }

                            currentVoltage = stepVoltage;
                            await Task.Delay(1000, cancellationToken); // 短暂等待
                        }
                    }
                    else
                    {
                        foreach (var point in points)
                        {
                            globalPointIndex++; // 全局索引递增（用于进度计算）
                            signalPointIndex++; // 信号局部索引递增（用于显示）

                            LogService.Log($"设置校准点 {globalPointIndex}/{totalPoints}: {point.Voltage}V");

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
                            await Task.Delay(point.ReadTimeMs, cancellationToken);

                            var (actualVoltage, deviceVoltage) = await ReadVoltageValuesSync(
                                voltmeter,
                                voltmeterSignals,
                                firstPoint.DeviceName,
                                firstPoint.SignalInfo,
                                treeSignals);

                            //LogService.Log($"电压表测量值: {actualVoltage}V, 设备电压采样值: {deviceVoltage}V");

                            // 计算精度: (采样电压 - 测量电压) / 额定电压
                            double accuracy = (deviceVoltage - actualVoltage) / ratingVoltage;
                            double accuracyPercentage = accuracy * 100; // 转换为百分比

                            // 判断是否在精度范围内
                            bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                            LogService.Log($"校准精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                            // 更新UI：校准后电压采样值、测量值、校准精度、是否合格
                            UpdateTreeNodePostCalibrationValues(
                                firstPoint.DeviceName,
                                firstPoint.SignalName,
                                signalPointIndex,
                                deviceVoltage,
                                actualVoltage,
                                accuracyPercentage,
                                withinPrecision, "电压");

                            // 更新进度 - 使用全局点数计算进度
                            int progress = (int)((double)globalPointIndex / totalPoints * 100);
                            UpdateProgress(ProgressStage.BeforeCalibration,
                                          $"验证校准点 {globalPointIndex}/{totalPoints}",
                                          progress);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"校准后验证失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 计量流程封装方法

        /// <summary>
        /// 获取选中的电压信号
        /// </summary>
        private List<(string DeviceName, string SignalName, string SignalType)> GetSelectedVoltageSignals()
        {
            var selectedSignals = new List<(string, string, string)>();

            foreach (TreeListNode node in treeList.Nodes)
            {
                if (node.Checked && node.Visible)
                {
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                    string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                    string signalType = node.GetValue("SignalType")?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(deviceName) && !string.IsNullOrEmpty(signalName) &&
                        signalType.Contains("电压"))
                    {
                        selectedSignals.Add((deviceName, signalName, signalType));
                    }
                }
            }

            return selectedSignals;
        }

        /// <summary>
        /// 获取选中的电流信号
        /// </summary>
        private List<(string DeviceName, string SignalName, string SignalType)> GetSelectedCurrentSignals()
        {
            var selectedSignals = new List<(string, string, string)>();

            foreach (TreeListNode node in treeList.Nodes)
            {
                if (node.Checked && node.Visible)
                {
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                    string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                    string signalType = node.GetValue("SignalType")?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(deviceName) && !string.IsNullOrEmpty(signalName) &&
                        signalType.Contains("电流"))
                    {
                        selectedSignals.Add((deviceName, signalName, signalType));
                    }
                }
            }

            return selectedSignals;
        }

        /// <summary>
        /// 执行电压计量流程
        /// </summary>
        private async Task<bool> ExecuteVoltageMeasurement(List<(string DeviceName, string SignalName, string SignalType)> selectedSignals)
        {
            EquipmentModel voltageSource = null;
            EquipmentModel voltmeter = null;

            try
            {
                UpdateProgress(0, "开始电压计量:");

                // 1. 从设备列表中查找设备信息
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                string? voltmeterName = cbVoltmeter.SelectedItem?.ToString();

                voltageSource = equipmentList.FirstOrDefault(e => e.DeviceName == voltageSourceName);
                voltmeter = equipmentList.FirstOrDefault(e => e.DeviceName == voltmeterName);
                if (voltageSource == null || voltmeter == null)
                {
                    XtraMessageBox.Show("未找到选定的设备配置信息!");
                    return false;
                }

                // 2. 加载校准设备指令协议
                var protocols = await LoadProtocolsAsync(voltageSource, voltmeter);
                voltageSourceProtocols = protocols.Where(p => p.DeviceName == voltageSource.DeviceName).ToList();
                voltmeterProtocols = protocols.Where(p => p.DeviceName == voltmeter.DeviceName).ToList();

                // 3. 加载树形图信号名称协议
                treeSignalProtocols = await LoadTreeSignalsProtocolAsync();
                if (treeSignalProtocols.Count == 0)
                {
                    XtraMessageBox.Show("没有找到选中的电压信号协议!");
                    return false;
                }

                // 4. 启动设备
                bool voltageSourceStarted = await StartEquipment(voltageSource, "");
                bool voltmeterStarted = await StartEquipment(voltmeter, "");
                if (!voltageSourceStarted || !voltmeterStarted)
                {
                    XtraMessageBox.Show("设备启动失败，请检查设备连接!");
                    return false;
                }

                LogService.Log("所有计量设备启动成功，开始电压计量流程!");

                // 5. 设置设备参数
                LogService.Log("设置电压源为程控模式...");
                bool modeSet = await SendEquipmentCommand(CommandType.SetMode, voltageSource, voltageSourceProtocols, 0x01);
                if (!modeSet)
                {
                    XtraMessageBox.Show("设置设备模式失败!");
                    return false;
                }

                // 6. 设置初始电压值为0
                LogService.Log("设置初始电压值...");
                bool voltageSet = await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceProtocols, 0.0);
                if (!voltageSet)
                {
                    XtraMessageBox.Show("设置初始电压失败!");
                    return false;
                }

                // 7. 启用输出
                LogService.Log("启用电压源输出...");
                bool outputEnabled = await SendEquipmentCommand(CommandType.EnableOutput, voltageSource, voltageSourceProtocols, 0x01);
                if (!outputEnabled)
                {
                    XtraMessageBox.Show("启用输出失败!");
                    return false;
                }

                // 8. 获取校准点信息（只获取选中信号的校准点）
                var calibrationPoints = await GetCalibrationPointsForMeasurement("电压", selectedSignals);

                // 9. 执行计量操作
                bool measurementSuccess = await ProcessVoltageMeasurement(
                    calibrationPoints,
                    voltageSource,
                    voltmeter,
                    treeSignalProtocols,
                    voltageSourceProtocols,
                    voltmeterProtocols);

                if (!measurementSuccess)
                {
                    XtraMessageBox.Show("电压计量失败!");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"电压计量失败: {ex.Message}");
                XtraMessageBox.Show($"电压计量失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 关闭设备输出
                if (voltageSource != null)
                {
                    LogService.Log("关闭电压源输出...");
                    bool voltageSet = await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceProtocols, 0.0);
                    bool outputDisabled = await SendEquipmentCommand(CommandType.EnableOutput, voltageSource, voltageSourceProtocols, 0x00);

                    await SafeShutdownEquipment(voltageSource, voltmeter);
                }

                UpdateProgress(ProgressStage.Completed, "电压计量完成!");
            }
        }

        /// <summary>
        /// 执行电流计量流程
        /// </summary>
        private async Task<bool> ExecuteCurrentMeasurement(
            List<(string DeviceName, string SignalName, string SignalType)> selectedSignals,
            string currentSourceName)
        {
            EquipmentModel currentSource = null;
            EquipmentModel ammeter = null;

            try
            {
                UpdateProgress(0, "开始电流计量:");

                // 1. 查找启动管理器
                startupManager = FindStartupManager(currentSourceName);
                if (startupManager == null)
                {
                    LogService.Log($"找不到设备 {currentSourceName} 的启动管理器");
                    return false;
                }

                // 2. 从设备列表中查找设备信息
                string? ammeterName = cbAmmeter.SelectedItem?.ToString();
                currentSource = equipmentList.FirstOrDefault(e => e.DeviceName == currentSourceName.Split("-")[0]);
                ammeter = equipmentList.FirstOrDefault(e => e.DeviceName == ammeterName);
                if (currentSource == null || ammeter == null)
                {
                    XtraMessageBox.Show("未找到选定的设备配置信息!");
                    return false;
                }

                // 3. 加载树形图信号名称协议
                treeSignalProtocols = await LoadTreeSignalsProtocolAsync();
                if (treeSignalProtocols.Count == 0)
                {
                    XtraMessageBox.Show("没有找到选中的电流信号协议!");
                    return false;
                }

                // 4. 启动设备
                bool ammeterStarted = await StartEquipment(ammeter, "");
                if (!ammeterStarted)
                {
                    LogService.Log("电流表启动失败，请检查设备连接!");
                    return false;
                }

                LogService.Log("所有计量设备启动成功，开始电流计量流程!");

                // 5. 设置电流表参数
                LogService.Log("设置电流表参数...");
                bool setParam1 = await SetPparameters("设置远程控制帧", ammeter);
                bool setParam2 = await SetPparameters("设置交直流帧", ammeter);
                bool setParam3 = await SetPparameters("设置采样速度帧", ammeter);
                bool setParam4 = await SetPparameters("设置显示位数帧", ammeter);
                bool setParam5 = await SetPparameters("设置 NULL 开关帧", ammeter);

                if (!setParam1 || !setParam2 || !setParam3 || !setParam4 || !setParam5)
                {
                    XtraMessageBox.Show("设置电流表参数失败!");
                    return false;
                }

                // 6. 获取校准点信息（只获取选中信号的校准点）
                var calibrationPoints = await GetCalibrationPointsForMeasurement("电流", selectedSignals);

                // 7. 执行计量操作
                bool measurementSuccess = await ProcessCurrentMeasurement(
                    calibrationPoints,
                    currentSource,
                    ammeter,
                    treeSignalProtocols,
                    currentSourceName);

                if (!measurementSuccess)
                {
                    XtraMessageBox.Show("电流计量失败!");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"电流计量失败: {ex.Message}");
                XtraMessageBox.Show($"电流计量失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 关闭电流源输出
                if (startupManager != null)
                {
                    LogService.Log("关闭电流源输出...");
                    await startupManager.SetParameters(0x00, 0.0, 0.0);
                }

                UpdateProgress(ProgressStage.Completed, "电流计量完成!");
            }
        }

        /// <summary>
        /// 获取选中的信号的校准点信息
        /// </summary>
        private async Task<Dictionary<string, List<CalibrationPoint>>> GetCalibrationPointsForMeasurement(
            string type,
            List<(string DeviceName, string SignalName, string SignalType)> selectedSignals)
        {
            var calibrationPoints = new Dictionary<string, List<CalibrationPoint>>();

            try
            {
                // 获取所有树节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                foreach (var node in messageNodes)
                {
                    // 只处理被勾选的节点
                    if (!node.Checked || !node.Visible)
                        continue;

                    // 获取设备名称和信号名称
                    string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                    string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                    string signalType = node.GetValue("SignalType")?.ToString() ?? "";

                    // 检查是否在选中的信号列表中
                    if (!selectedSignals.Any(s => s.DeviceName == deviceName && s.SignalName == signalName))
                        continue;

                    // 只处理指定类型的信号
                    if (!signalType.Contains(type, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 获取校准点个数
                    string calibrationNumberStr = node.GetValue("CalibrationNumber")?.ToString() ?? "";
                    if (!int.TryParse(calibrationNumberStr, out int calibrationNumber) || calibrationNumber <= 0)
                        continue;

                    // 获取校准范围值
                    string calibrationRangeStr = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                    if (string.IsNullOrEmpty(calibrationRangeStr))
                        continue;

                    // 解析校准范围
                    var rangeParts = calibrationRangeStr.Split('-');
                    if (rangeParts.Length != 2 ||
                        !double.TryParse(rangeParts[0], out double minValue) ||
                        !double.TryParse(rangeParts[1], out double maxValue) ||
                        minValue >= maxValue)
                    {
                        continue;
                    }

                    // 获取稳定读取时间
                    string readTimeStr = node.GetValue("ReadTime")?.ToString() ?? "";
                    if (!int.TryParse(readTimeStr, out int readTimeMs))
                        readTimeMs = 1000;

                    // 查找对应的信号信息
                    var signalInfo = treeSignalProtocols.FirstOrDefault(s =>
                        s.SignalName.Equals($"{deviceName}-{signalName}", StringComparison.OrdinalIgnoreCase));

                    if (signalInfo == null)
                        continue;

                    // 获取校准信号名称
                    string calibrationSignal = node.GetValue("CalibrationSignal")?.ToString() ?? "";

                    // 生成校准点
                    var points = GenerateCalibrationPoints(signalType, minValue, maxValue, calibrationNumber,
                        readTimeMs, signalInfo, deviceName, signalName, calibrationSignal);

                    // 添加到字典
                    string key = $"{deviceName}-{signalName}";
                    calibrationPoints[key] = points;
                }

                return calibrationPoints;
            }
            catch (Exception ex)
            {
                LogService.Log($"获取计量校准点失败: {ex.Message}");
                return new Dictionary<string, List<CalibrationPoint>>();
            }
        }

        /// <summary>
        /// 处理电压计量
        /// </summary>
        private async Task<bool> ProcessVoltageMeasurement(
            Dictionary<string, List<CalibrationPoint>> calibrationPoints,
            EquipmentModel voltageSource,
            EquipmentModel voltmeter,
            List<SignalInfo> treeSignals,
            List<ModbusSignal> voltageSourceSignals,
            List<ModbusSignal> voltmeterSignals)
        {
            try
            {
                LogService.Log("开始电压计量...");

                // 创建计量结果集合
                var measurementResults = new List<MeasurementResult>();

                int totalPoints = calibrationPoints.Values.Sum(points => points.Count);
                int globalPointIndex = 0;

                foreach (var signalKey in calibrationPoints.Keys)
                {
                    var points = calibrationPoints[signalKey];
                    var firstPoint = points.FirstOrDefault();

                    if (firstPoint == null)
                        continue;

                    LogService.Log($"开始计量信号: {firstPoint.DeviceName} - {firstPoint.SignalName}, 共 {points.Count} 个计量点");

                    // 获取额定电压和精度范围
                    var (ratingVoltage, precisionRange) = GetRatingVoltageAndPrecision(
                        firstPoint.DeviceName, firstPoint.SignalName);

                    int signalPointIndex = 0;

                    foreach (var point in points)
                    {
                        globalPointIndex++;
                        signalPointIndex++;

                        LogService.Log($"设置计量点 {globalPointIndex}/{totalPoints}: {point.Voltage}V");

                        // 设置电压源输出到当前点电压
                        bool voltageSet = await SendEquipmentCommand(
                            CommandType.SetVoltage,
                            voltageSource,
                            voltageSourceSignals,
                            point.Voltage);

                        if (!voltageSet)
                        {
                            LogService.Log($"设置电压 {point.Voltage}V 失败，跳过此计量点");
                            continue;
                        }

                        // 等待电压稳定
                        LogService.Log($"等待 {point.ReadTimeMs}ms 使电压稳定...");
                        await Task.Delay(point.ReadTimeMs);

                        // 读取电压表测量值和设备采样值
                        var (actualVoltage, deviceVoltage) = await ReadVoltageValuesSync(
                            voltmeter,
                            voltmeterSignals,
                            firstPoint.DeviceName,
                            firstPoint.SignalInfo,
                            treeSignals);

                        LogService.Log($"电压表测量值: {actualVoltage}V, 设备电压采样值: {deviceVoltage}V");

                        // 计算精度
                        double accuracy = (deviceVoltage - actualVoltage) / ratingVoltage;
                        double accuracyPercentage = accuracy * 100;

                        // 判断是否在精度范围内
                        bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                        LogService.Log($"计量精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                        // 创建测量结果
                        var result = new MeasurementResult
                        {
                            DeviceName = firstPoint.DeviceName,
                            SignalName = firstPoint.SignalName,
                            SignalType = "电压",
                            PointIndex = signalPointIndex,
                            TargetValue = point.Voltage,
                            DeviceValue = deviceVoltage,
                            ActualValue = actualVoltage,
                            AccuracyPercentage = accuracyPercentage,
                            IsQualified = withinPrecision,
                            RatingValue = ratingVoltage,
                            PrecisionRange = precisionRange,
                            MeasurementTime = DateTime.Now
                        };

                        measurementResults.Add(result);

                        // 更新UI显示计量值和精度
                        UpdateTreeNodeMeasurementValue(firstPoint.DeviceName, firstPoint.SignalName,
                            actualVoltage, deviceVoltage, signalPointIndex,
                            accuracyPercentage, withinPrecision, "电压");

                        // 更新进度
                        int progress = (int)((double)globalPointIndex / totalPoints * 100);
                        UpdateProgress(ProgressStage.HandlePoint,
                            $"处理计量点 {globalPointIndex}/{totalPoints}",
                            progress);
                    }

                    // 设置回零电压
                    LogService.Log("设置电压回零...");
                    await SendEquipmentCommand(CommandType.SetVoltage, voltageSource, voltageSourceSignals, 0.0);
                    await Task.Delay(500);
                }

                // 生成计量报告
                if (measurementResults.Count > 0)
                {
                    bool reportGenerated = GenerateMeasurementReport(measurementResults, "电压计量");
                    if (reportGenerated)
                    {
                        LogService.Log("电压计量报告生成成功");
                    }
                    else
                    {
                        LogService.Log("电压计量报告生成失败");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"处理电压计量点时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 处理电流计量
        /// </summary>
        private async Task<bool> ProcessCurrentMeasurement(
            Dictionary<string, List<CalibrationPoint>> calibrationPoints,
            EquipmentModel currentSource,
            EquipmentModel ammeter,
            List<SignalInfo> treeSignals,
            string currentSourceName)
        {
            try
            {
                LogService.Log("开始电流计量...");

                // 创建计量结果集合
                var measurementResults = new List<MeasurementResult>();

                int totalPoints = calibrationPoints.Values.Sum(points => points.Count);
                int globalPointIndex = 0;

                // 启动电流源
                LogService.Log("启用电流源输出...");
                bool outputEnabled = await StartEquipment(currentSource, currentSourceName);
                if (!outputEnabled)
                {
                    LogService.Log("启用电流源输出失败!");
                    return false;
                }

                foreach (var signalKey in calibrationPoints.Keys)
                {
                    var points = calibrationPoints[signalKey];
                    var firstPoint = points.FirstOrDefault();

                    if (firstPoint == null)
                        continue;

                    LogService.Log($"开始计量信号: {firstPoint.DeviceName} - {firstPoint.SignalName}, 共 {points.Count} 个计量点");

                    // 获取额定电流和精度范围
                    var (ratingCurrent, precisionRange) = GetRatingVoltageAndPrecision(
                        firstPoint.DeviceName, firstPoint.SignalName);

                    // 分离负电流和正电流点
                    var negativePoints = points.Where(p => p.Voltage < 0).OrderByDescending(p => p.Voltage).ToList();
                    var positivePoints = points.Where(p => p.Voltage > 0).OrderBy(p => p.Voltage).ToList();

                    int signalPointIndex = 0;
                    double currentValue = 0;

                    // 处理负电流部分
                    LogService.Log("开始负电流计量...");
                    foreach (var point in negativePoints)
                    {
                        globalPointIndex++;
                        signalPointIndex++;

                        // 逐步增加到目标负电流值
                        while (currentValue > point.Voltage)
                        {
                            double stepValue = Math.Max(currentValue - 20, point.Voltage);
                            LogService.Log($"设置电流 {stepValue}A");

                            bool setCurrent = await startupManager.SetParameters(0x23, stepValue, 0.0);
                            if (!setCurrent)
                            {
                                LogService.Log($"设置电流 {stepValue}A 失败，跳过此计量点");
                                break;
                            }

                            currentValue = stepValue;
                            await Task.Delay(1000);
                        }

                        // 等待电流稳定
                        LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                        await Task.Delay(point.ReadTimeMs);

                        // 读取电流表测量值
                        var actualCurrent = await ReadCurrentValue(ammeter);
                        LogService.Log($"电流表测量值: {actualCurrent}A, 设备电流采样值: {point.Voltage}A");

                        // 计算精度
                        double accuracy = (point.Voltage - actualCurrent) / ratingCurrent;
                        double accuracyPercentage = accuracy * 100;

                        // 判断是否在精度范围内
                        bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                        LogService.Log($"计量精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                        // 创建测量结果
                        var result = new MeasurementResult
                        {
                            DeviceName = firstPoint.DeviceName,
                            SignalName = firstPoint.SignalName,
                            SignalType = "电流",
                            PointIndex = signalPointIndex,
                            TargetValue = point.Voltage,
                            DeviceValue = point.Voltage,
                            ActualValue = actualCurrent,
                            AccuracyPercentage = accuracyPercentage,
                            IsQualified = withinPrecision,
                            RatingValue = ratingCurrent,
                            PrecisionRange = precisionRange,
                            MeasurementTime = DateTime.Now
                        };

                        measurementResults.Add(result);

                        // 更新UI显示计量值和精度
                        UpdateTreeNodeMeasurementValue(firstPoint.DeviceName, firstPoint.SignalName,
                            actualCurrent, point.Voltage, signalPointIndex,
                            accuracyPercentage, withinPrecision, "电流");

                        // 更新进度
                        int progress = (int)((double)globalPointIndex / totalPoints * 100);
                        UpdateProgress(ProgressStage.HandlePoint,
                            $"处理计量点 {globalPointIndex}/{totalPoints}",
                            progress);
                    }

                    // 负电流回零
                    LogService.Log("负电流回零...");
                    while (currentValue < 0)
                    {
                        double stepValue = Math.Min(currentValue + 20, 0);
                        await startupManager.SetParameters(0x23, stepValue, 0.0);
                        currentValue = stepValue;
                        await Task.Delay(500);
                    }

                    // 处理正电流部分
                    LogService.Log("开始正电流计量...");
                    foreach (var point in positivePoints)
                    {
                        globalPointIndex++;
                        signalPointIndex++;

                        // 逐步增加到目标正电流值
                        while (currentValue < point.Voltage)
                        {
                            double stepValue = Math.Min(currentValue + 20, point.Voltage);
                            LogService.Log($"设置电流 {stepValue}A");

                            bool setCurrent = await startupManager.SetParameters(0x03, stepValue, 0.0);
                            if (!setCurrent)
                            {
                                LogService.Log($"设置电流 {stepValue}A 失败，跳过此计量点");
                                break;
                            }

                            currentValue = stepValue;
                            await Task.Delay(1000);
                        }

                        // 等待电流稳定
                        LogService.Log($"等待 {point.ReadTimeMs}ms 使电流稳定...");
                        await Task.Delay(point.ReadTimeMs);

                        // 读取电流表测量值
                        var actualCurrent = await ReadCurrentValue(ammeter);
                        LogService.Log($"电流表测量值: {actualCurrent}A, 设备电流采样值: {point.Voltage}A");

                        // 计算精度
                        double accuracy = (point.Voltage - actualCurrent) / ratingCurrent;
                        double accuracyPercentage = accuracy * 100;

                        // 判断是否在精度范围内
                        bool withinPrecision = Math.Abs(accuracyPercentage) <= precisionRange;

                        LogService.Log($"计量精度: {accuracyPercentage:F4}%, 精度范围: ±{precisionRange}%, 是否合格: {(withinPrecision ? "是" : "否")}");

                        // 创建测量结果
                        var result = new MeasurementResult
                        {
                            DeviceName = firstPoint.DeviceName,
                            SignalName = firstPoint.SignalName,
                            SignalType = "电流",
                            PointIndex = signalPointIndex,
                            TargetValue = point.Voltage,
                            DeviceValue = point.Voltage,
                            ActualValue = actualCurrent,
                            AccuracyPercentage = accuracyPercentage,
                            IsQualified = withinPrecision,
                            RatingValue = ratingCurrent,
                            PrecisionRange = precisionRange,
                            MeasurementTime = DateTime.Now
                        };

                        measurementResults.Add(result);

                        // 更新UI显示计量值和精度
                        UpdateTreeNodeMeasurementValue(firstPoint.DeviceName, firstPoint.SignalName,
                            actualCurrent, point.Voltage, signalPointIndex,
                            accuracyPercentage, withinPrecision, "电流");

                        // 更新进度
                        int progress = (int)((double)globalPointIndex / totalPoints * 100);
                        UpdateProgress(ProgressStage.HandlePoint,
                            $"处理计量点 {globalPointIndex}/{totalPoints}",
                            progress);
                    }

                    // 正电流回零
                    LogService.Log("正电流回零...");
                    while (currentValue > 0)
                    {
                        double stepValue = Math.Max(currentValue - 20, 0);
                        await startupManager.SetParameters(0x03, stepValue, 0.0);
                        currentValue = stepValue;
                        await Task.Delay(500);
                    }
                }

                // 关闭电流源输出
                LogService.Log("关闭电流源输出...");
                await startupManager.SetParameters(0x00, 0.0, 0.0);

                // 生成计量报告
                if (measurementResults.Count > 0)
                {
                    bool reportGenerated = GenerateMeasurementReport(measurementResults, "电流计量");
                    if (reportGenerated)
                    {
                        LogService.Log("电流计量报告生成成功");
                    }
                    else
                    {
                        LogService.Log("电流计量报告生成失败");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"处理电流计量点时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新树节点的计量值（添加精度显示）
        /// </summary>
        private void UpdateTreeNodeMeasurementValue(string deviceName, string signalName,
            double actualValue, double deviceValue, int pointIndex,
            double accuracy, bool isQualified, string type)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateTreeNodeMeasurementValue(
                    deviceName, signalName, actualValue, deviceValue, pointIndex,
                    accuracy, isQualified, type)));
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
                    string childNodeName = $"计量点 {pointIndex}";

                    // 格式化值为"采样值/测量值"格式
                    string valueDisplay = $"{deviceValue}/{actualValue}";

                    // 检查是否已经存在该校准点的子节点
                    TreeListNode measurementNode = null;
                    if (parentNode.HasChildren)
                    {
                        foreach (TreeListNode childNode in parentNode.Nodes)
                        {
                            if (childNode.GetValue("DeviceName")?.ToString() == childNodeName)
                            {
                                measurementNode = childNode;
                                break;
                            }
                        }
                    }

                    // 如果不存在，创建新的子节点
                    if (measurementNode == null)
                    {
                        measurementNode = parentNode.Nodes.Add(new object[]
                        {
                            childNodeName, // 设备名称列显示计量点名称
                            "", "", "", "", "", "", "","","",
                            valueDisplay,  // 校准前列显示计量值
                            "", // 校准后列为空
                            $"{Math.Abs(accuracy):F4}%", // 精度
                            isQualified ? "合格" : "不合格" // 结果
                        });

                        // 展开父节点
                        parentNode.Expanded = true;
                    }
                    else
                    {
                        // 更新校准前列的值
                        measurementNode.SetValue("BeforeCalibration", valueDisplay);
                        measurementNode.SetValue("CalibrationAccuracy", $"{Math.Abs(accuracy):F4}%");
                        measurementNode.SetValue("CalibrationResult", isQualified ? "合格" : "不合格");
                    }

                    // 刷新节点显示
                    treeList.RefreshNode(measurementNode);
                    break;
                }
            }
        }

        /// <summary>
        /// 生成计量报告
        /// </summary>
        private bool GenerateMeasurementReport(List<MeasurementResult> results, string reportType)
        {
            try
            {
                // 创建报告目录
                string reportDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Measurement Report");
                if (!Directory.Exists(reportDirectory))
                {
                    Directory.CreateDirectory(reportDirectory);
                }

                // 生成报告文件名
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{reportType}_{timestamp}.xlsx";
                string filePath = Path.Combine(reportDirectory, fileName);

                using (var workbook = new XLWorkbook())
                {
                    // 创建汇总工作表
                    var summaryWorksheet = workbook.Worksheets.Add("计量汇总");

                    // 设置样式
                    summaryWorksheet.Style.Font.FontName = "宋体";
                    summaryWorksheet.Style.Font.FontSize = 11;

                    // 标题
                    summaryWorksheet.Cell(1, 1).Value = $"{reportType}报告";
                    summaryWorksheet.Range(1, 1, 1, 8).Merge();
                    summaryWorksheet.Row(1).Height = 30;
                    summaryWorksheet.Row(1).Style.Font.FontSize = 16;
                    summaryWorksheet.Row(1).Style.Font.Bold = true;
                    summaryWorksheet.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // 报告信息
                    summaryWorksheet.Cell(2, 1).Value = "生成时间：";
                    summaryWorksheet.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    summaryWorksheet.Cell(3, 1).Value = "报告类型：";
                    summaryWorksheet.Cell(3, 2).Value = reportType;

                    // 统计信息
                    int qualifiedCount = results.Count(r => r.IsQualified);
                    int totalCount = results.Count;
                    double qualifiedRate = totalCount > 0 ? (double)qualifiedCount / totalCount * 100 : 0;

                    summaryWorksheet.Cell(4, 1).Value = "总计量点数：";
                    summaryWorksheet.Cell(4, 2).Value = totalCount;
                    summaryWorksheet.Cell(5, 1).Value = "合格点数：";
                    summaryWorksheet.Cell(5, 2).Value = qualifiedCount;
                    summaryWorksheet.Cell(6, 1).Value = "合格率：";
                    summaryWorksheet.Cell(6, 2).Value = $"{qualifiedRate:F2}%";

                    // 表头
                    int row = 8;
                    summaryWorksheet.Cell(row, 1).Value = "序号";
                    summaryWorksheet.Cell(row, 2).Value = "设备名称";
                    summaryWorksheet.Cell(row, 3).Value = "信号名称";
                    summaryWorksheet.Cell(row, 4).Value = "信号类型";
                    summaryWorksheet.Cell(row, 5).Value = "额定值";
                    summaryWorksheet.Cell(row, 6).Value = "精度范围";
                    summaryWorksheet.Cell(row, 7).Value = "合格点数/总点数";
                    summaryWorksheet.Cell(row, 8).Value = "合格率";

                    // 设置表头样式
                    var headerRange = summaryWorksheet.Range(row, 1, row, 8);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // 按设备信号分组统计
                    var groupedResults = results.GroupBy(r => new { r.DeviceName, r.SignalName, r.SignalType });
                    int index = 1;

                    foreach (var group in groupedResults)
                    {
                        row++;
                        var signalResults = group.ToList();
                        int signalQualifiedCount = signalResults.Count(r => r.IsQualified);
                        int signalTotalCount = signalResults.Count;
                        double signalQualifiedRate = signalTotalCount > 0 ? (double)signalQualifiedCount / signalTotalCount * 100 : 0;

                        summaryWorksheet.Cell(row, 1).Value = index++;
                        summaryWorksheet.Cell(row, 2).Value = group.Key.DeviceName;
                        summaryWorksheet.Cell(row, 3).Value = group.Key.SignalName;
                        summaryWorksheet.Cell(row, 4).Value = group.Key.SignalType;
                        summaryWorksheet.Cell(row, 5).Value = signalResults.First().RatingValue;
                        summaryWorksheet.Cell(row, 6).Value = $"±{signalResults.First().PrecisionRange}%";
                        summaryWorksheet.Cell(row, 7).Value = $"{signalQualifiedCount}/{signalTotalCount}";
                        summaryWorksheet.Cell(row, 8).Value = $"{signalQualifiedRate:F2}%";

                        // 根据合格率设置颜色
                        if (signalQualifiedRate >= 95)
                            summaryWorksheet.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.LightGreen;
                        else if (signalQualifiedRate >= 80)
                            summaryWorksheet.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.LightYellow;
                        else
                            summaryWorksheet.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.LightCoral;
                    }

                    // 创建详细信息工作表
                    var detailWorksheet = workbook.Worksheets.Add("计量详情");

                    // 详细信息表头
                    detailWorksheet.Cell(1, 1).Value = "设备名称";
                    detailWorksheet.Cell(1, 2).Value = "信号名称";
                    detailWorksheet.Cell(1, 3).Value = "信号类型";
                    detailWorksheet.Cell(1, 4).Value = "计量点";
                    detailWorksheet.Cell(1, 5).Value = "目标值";
                    detailWorksheet.Cell(1, 6).Value = "设备采样值";
                    detailWorksheet.Cell(1, 7).Value = "实际测量值";
                    detailWorksheet.Cell(1, 8).Value = "精度(%)";
                    detailWorksheet.Cell(1, 9).Value = "是否合格";
                    detailWorksheet.Cell(1, 10).Value = "额定值";
                    detailWorksheet.Cell(1, 11).Value = "精度范围";
                    detailWorksheet.Cell(1, 12).Value = "计量时间";

                    // 设置表头样式
                    var detailHeaderRange = detailWorksheet.Range(1, 1, 1, 12);
                    detailHeaderRange.Style.Font.Bold = true;
                    detailHeaderRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    detailHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // 填充详细信息
                    row = 2;
                    foreach (var result in results)
                    {
                        detailWorksheet.Cell(row, 1).Value = result.DeviceName;
                        detailWorksheet.Cell(row, 2).Value = result.SignalName;
                        detailWorksheet.Cell(row, 3).Value = result.SignalType;
                        detailWorksheet.Cell(row, 4).Value = result.PointIndex;
                        detailWorksheet.Cell(row, 5).Value = result.TargetValue;
                        detailWorksheet.Cell(row, 6).Value = result.DeviceValue;
                        detailWorksheet.Cell(row, 7).Value = result.ActualValue;
                        detailWorksheet.Cell(row, 8).Value = Math.Abs(result.AccuracyPercentage);

                        // 根据精度是否合格设置颜色
                        if (result.IsQualified)
                        {
                            detailWorksheet.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.LightGreen;
                            detailWorksheet.Cell(row, 9).Value = "合格";
                            detailWorksheet.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.LightGreen;
                        }
                        else
                        {
                            detailWorksheet.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.LightCoral;
                            detailWorksheet.Cell(row, 9).Value = "不合格";
                            detailWorksheet.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.LightCoral;
                        }

                        detailWorksheet.Cell(row, 10).Value = result.RatingValue;
                        detailWorksheet.Cell(row, 11).Value = $"±{result.PrecisionRange}%";
                        detailWorksheet.Cell(row, 12).Value = result.MeasurementTime.ToString("yyyy-MM-dd HH:mm:ss");

                        row++;
                    }

                    // 设置列宽
                    detailWorksheet.Columns().AdjustToContents();

                    // 创建趋势分析工作表
                    var trendWorksheet = workbook.Worksheets.Add("趋势分析");

                    // 分析精度分布
                    var accuracyGroups = results
                        .GroupBy(r => Math.Floor(Math.Abs(r.AccuracyPercentage) / 5) * 5)
                        .OrderBy(g => g.Key)
                        .ToList();

                    trendWorksheet.Cell(1, 1).Value = "精度分布分析";
                    trendWorksheet.Range(1, 1, 1, 3).Merge();
                    trendWorksheet.Row(1).Style.Font.Bold = true;
                    trendWorksheet.Row(1).Style.Font.FontSize = 14;

                    trendWorksheet.Cell(2, 1).Value = "精度范围(%)";
                    trendWorksheet.Cell(2, 2).Value = "点数";
                    trendWorksheet.Cell(2, 3).Value = "占比(%)";

                    row = 3;
                    foreach (var group in accuracyGroups)
                    {
                        int count = group.Count();
                        double percentage = (double)count / results.Count * 100;

                        trendWorksheet.Cell(row, 1).Value = $"{group.Key}~{group.Key + 5}";
                        trendWorksheet.Cell(row, 2).Value = count;
                        trendWorksheet.Cell(row, 3).Value = percentage;
                        row++;
                    }

                    // 保存报告
                    workbook.SaveAs(filePath);

                    // 显示报告保存位置
                    string message = $"{reportType}报告已生成！\n\n" +
                                   $"报告保存位置：{filePath}\n\n" +
                                   $"总计量点数：{totalCount}\n" +
                                   $"合格点数：{qualifiedCount}\n" +
                                   $"合格率：{qualifiedRate:F2}%";

                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                            XtraMessageBox.Show(message, "计量报告生成完成",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)));
                    }
                    else
                    {
                        XtraMessageBox.Show(message, "计量报告生成完成",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"生成计量报告失败: {ex.Message}");

                string errorMessage = $"生成计量报告失败：{ex.Message}";
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                        XtraMessageBox.Show(errorMessage, "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                else
                {
                    XtraMessageBox.Show(errorMessage, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return false;
            }
        }

        #endregion

        #region 计量结果数据模型
        /// <summary>
        /// 计量结果数据模型
        /// </summary>
        public class MeasurementResult
        {
            /// <summary>
            /// 设备名称
            /// </summary>
            public string DeviceName { get; set; } = string.Empty;

            /// <summary>
            /// 信号名称
            /// </summary>
            public string SignalName { get; set; } = string.Empty;

            /// <summary>
            /// 信号类型（电压/电流）
            /// </summary>
            public string SignalType { get; set; } = string.Empty;

            /// <summary>
            /// 计量点序号
            /// </summary>
            public int PointIndex { get; set; }

            /// <summary>
            /// 目标值
            /// </summary>
            public double TargetValue { get; set; }

            /// <summary>
            /// 设备采样值
            /// </summary>
            public double DeviceValue { get; set; }

            /// <summary>
            /// 实际测量值
            /// </summary>
            public double ActualValue { get; set; }

            /// <summary>
            /// 精度百分比
            /// </summary>
            public double AccuracyPercentage { get; set; }

            /// <summary>
            /// 是否合格
            /// </summary>
            public bool IsQualified { get; set; }

            /// <summary>
            /// 额定值
            /// </summary>
            public double RatingValue { get; set; }

            /// <summary>
            /// 精度范围
            /// </summary>
            public double PrecisionRange { get; set; }

            /// <summary>
            /// 计量时间
            /// </summary>
            public DateTime MeasurementTime { get; set; }
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
                // 并行读取电压表值和设备采样值 - 确保真正的异步执行
                var voltmeterTask = ReadVoltmeterValueAsync(voltmeter, voltmeterSignals);
                var deviceTask = ReadDeviceVoltageSampleAsync(deviceName, signalInfo, treeSignals);

                // 等待两个任务完成
                await Task.WhenAll(voltmeterTask, deviceTask);

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
        /// 异步读取电压表值
        /// </summary>
        private async Task<double> ReadVoltmeterValueAsync(EquipmentModel voltmeter, List<ModbusSignal> voltmeterSignals)
        {
            //LogService.Log("开始异步执行ReadVoltmeterValue");
            try
            {
                // 根据电压表类型选择不同的读取方式
                switch (voltmeter.CanType)
                {
                    case "USB-SCPI":
                        return await ReadScpiVoltmeterValueAsync(voltmeter);

                    case "RS485-MODBUS":
                        throw new Exception($"不支持的电压表类型: {voltmeter.CanType}");

                    default:
                        throw new Exception($"不支持的电压表类型: {voltmeter.CanType}");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"异步读取电压表值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 异步读取设备电压采样值
        /// </summary>
        private async Task<double> ReadDeviceVoltageSampleAsync(string deviceName, SignalInfo signalInfo, List<SignalInfo> treeSignals)
        {
            //LogService.Log("开始异步执行ReadDeviceVoltageSample");
            try
            {
                // 根据设备类型选择不同的读取方式
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName.Split('-')[0]);
                if (equipment == null)
                    throw new Exception($"未找到设备: {deviceName}");

                double result;

                switch (equipment.CanType)
                {
                    case "RS485-MODBUS":
                        throw new Exception($"不支持的设备类型: {equipment.CanType}");

                    case "ZCAN_CANETTCP":
                        result = await ReadCanDeviceValueAsync(equipment, signalInfo);
                        break;

                    default:
                        throw new Exception($"不支持的设备类型: {equipment.CanType}");
                }

                LogService.Log($"设备采样电压值: {result}");
                return result;
            }
            catch (Exception ex)
            {
                LogService.Log($"异步读取设备电压采样值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 异步读取SCPI电压表值
        /// </summary>
        private async Task<double> ReadScpiVoltmeterValueAsync(EquipmentModel voltmeter)
        {
            try
            {
                // 使用Task.Run将同步方法包装为异步，避免阻塞
                return await Task.Run(() =>
                {
                    double voltageValue = 0.0;
                    if (voltmeter.DeviceName == "GVM-9102")
                    {
                        voltageValue = GwinstekGVM9102_Communicator.Instance.MeasureDCVoltage(measurementTimeMs:2000);
                    }
                    else if (voltmeter.DeviceName == "KEYSIGHT-34465A")
                    {
                        // 发送查询命令并读取响应
                        voltageValue = Keysight34465A_Communicator.Instance.MeasureDCVoltage(measurementTimeMs:2000);
                    }
                    else
                    {
                        throw new Exception($"不支持的电压表类型: {voltmeter.CanType}");
                    }

                    LogService.Log($"电压表测量值: {voltageValue}V");
                    return voltageValue;
                });
            }
            catch (Exception ex)
            {
                LogService.Log($"读取电压表值失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 异步读取CAN设备电压采样值
        /// </summary>
        private async Task<double> ReadCanDeviceValueAsync(EquipmentModel equipment, SignalInfo signalInfo)
        {
            try
            {
                // 构建通道键
                string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);
                CANManager.Instance.ClearQueue(channelKey);

                // 从SignalInfo中获取CAN ID
                uint canId = uint.Parse(signalInfo.CANID.Replace("0x", ""),
                    System.Globalization.NumberStyles.HexNumber);

                List<double> values = new List<double>();

                try
                {
                    // 接收指定CAN ID的帧
                    var frames = await CANManager.Instance.ReceiveAllFramesAsync(channelKey, canId, 2000);

                    foreach (var frame in frames)
                    {
                        // 解析帧中的数据
                        ulong rawValue = CANManager.Instance.ExtractRawValue(frame.data, signalInfo);
                        double physicalValue = CANManager.Instance.ConvertToPhysicalValue(rawValue, signalInfo);
                        
                        values.Add(physicalValue);
                    }
                }
                catch (Exception ex)
                {
                    // 忽略单次读取的异常，继续尝试读取
                    LogService.Log($"读取异常: {ex.Message}");
                }

                // 检查是否读取到数据
                if (values.Count == 0)
                {
                    throw new Exception($"在2000ms内未接收到任何CAN帧，CAN ID: 0x{canId:X}");
                }

                // 计算平均值
                double aveValue = values.Average();
                int decimalPlaces = CANManager.Instance.GetNumberOfDecimalPlaces(signalInfo.Factor);
                double averageValue = Math.Round(aveValue, decimalPlaces);

                return averageValue;
            }
            catch (Exception ex)
            {
                //LogService.Log($"读取CAN设备值失败: {ex.Message}");
                throw new Exception(ex.Message);
            }
        }

        #endregion

        #region 新增进度管理方法

        /// <summary>
        /// 更新进度显示
        /// </summary>
        /// <param name="stage">当前进度阶段</param>
        /// <param name="message">进度描述信息</param>
        /// <param name="subProgress">子进度百分比(0-100)</param>
        private void UpdateProgress(ProgressStage stage, string message = null, int subProgress = 0)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<ProgressStage, string, int>(UpdateProgress), stage, message, subProgress);
                return;
            }

            currentStage = stage;

            // 计算基础进度和下一阶段进度
            int baseProgress = (int)stage;
            int nextStageProgress = GetNextStageProgress();

            // 计算子进度在当前阶段中的贡献
            int stageRange = nextStageProgress - baseProgress;
            int subProgressContribution = (int)(stageRange * subProgress / 100.0);

            // 总进度 = 基础进度 + 子进度贡献
            int totalProgress = baseProgress + subProgressContribution;

            // 确保进度不会减少（防止往回跑）
            if (totalProgress < progressBar.Position)
            {
                totalProgress = progressBar.Position;
            }

            progressBar.Position = totalProgress;

            if (!string.IsNullOrEmpty(message))
            {
                progressLabel.Text = $"[{totalProgress}%] {message}";
            }

            progressBar.Update();
            progressLabel.Update();
        }

        /// <summary>
        /// 获取下一阶段的进度值
        /// </summary>
        private int GetNextStageProgress()
        {
            var stages = Enum.GetValues(typeof(ProgressStage)).Cast<ProgressStage>().ToList();
            int currentIndex = stages.IndexOf(currentStage);

            if (currentIndex < stages.Count - 1)
            {
                return (int)stages[currentIndex + 1];
            }

            return 100;
        }

        #endregion

        #region 辅助方法实现

        private StartupManager FindStartupManager(string deviceName)
        {
            if (Module.StartupManagers.TryGetValue(deviceName, out var manager))
            {
                return manager;
            }
            return null;
        }

        /// <summary>
        /// 初始化电流源设备
        /// </summary>
        private string InitializeCurrentSourceModule()
        {
            try
            {
                // 查找电流源设备
                var selectedCurrentDevices = GetSelectedCurrentDevices();
                if (selectedCurrentDevices.Count == 0)
                {
                    XtraMessageBox.Show("请先勾选至少一个电流信号设备!");
                    return "";
                }

                if (selectedCurrentDevices.Count > 1)
                {
                    XtraMessageBox.Show("只能选择一个电流信号设备进行校准!");
                    return "";
                }

                // 获取选中的设备名称
                string currentSourceName = selectedCurrentDevices.First();

                LogService.Log("电流源设备初始化成功");
                return currentSourceName;
            }
            catch (Exception ex)
            {
                LogService.Log($"初始化电流源设备失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 从Excel文件解析数据
        /// </summary>
        private List<SignalImportData> ParseExcelDataFromFile(string filePath)
        {
            try
            {
                using (var workbook = new XLWorkbook(filePath))
                {
                    // 获取第一个工作表
                    var worksheet = workbook.Worksheet(1);
                    return ParseExcelData(worksheet);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"解析Excel文件失败: {ex.Message}");
                return new List<SignalImportData>();
            }
        }

        /// <summary>
        /// 解析Excel数据
        /// </summary>
        private List<SignalImportData> ParseExcelData(IXLWorksheet worksheet)
        {
            var importData = new List<SignalImportData>();

            int currentRow = 2;
            while (currentRow <= worksheet.RowsUsed().Count())
            {
                // 查找通道标题行 (包含"-"的行)
                var channelCell = worksheet.Cell(currentRow, 1);
                string channelValue = channelCell.Value.ToString();

                if (channelValue.Contains("-"))
                {
                    var signalData = new SignalImportData();

                    string[] parts = channelValue.Split('-');
                    if (parts.Length >= 3)
                    {
                        signalData.DeviceName = $"{parts[0]}-{ parts[1]} "; // 完整设备名称带通道
                        signalData.SignalName = parts[2]; // 信号名称

                        // 判断信号类型（根据信号名称或设备名称）
                        if (signalData.SignalName.Contains("电压") || channelValue.Contains("电压"))
                        {
                            signalData.SignalType = "电压";
                        }
                        else if (signalData.SignalName.Contains("电流") || channelValue.Contains("电流"))
                        {
                            signalData.SignalType = "电流";
                        }
                    }

                    // 解析额定值 (第2行第5列)
                    var ratingCell = worksheet.Cell(currentRow + 1, 5);
                    if (double.TryParse(ratingCell.Value.ToString(), out double rating))
                    {
                        signalData.RatingValue = rating;
                    }

                    // 解析比例系数和零点系数 (第2行第6列，格式: "K:1.0 B:0.0")
                    var factorsCell = worksheet.Cell(currentRow + 1, 6);
                    string factors = factorsCell.Value.ToString();
                    signalData.ScaleFactor = ParseFactor(factors, "K:");
                    signalData.ZeroFactor = ParseFactor(factors, "B:");

                    // 解析校准点数据 (从第4行开始)
                    signalData.CalibrationPoints = new List<CalibrationPointImportData>();
                    int dataRow = currentRow + 3;

                    //总行数
                    int totallines = worksheet.RangeUsed().LastRow().RowNumber();

                    while (dataRow <= totallines)
                    {
                        var point = ParseCalibrationPoint(worksheet, dataRow);
                        if (point != null)
                        {
                            signalData.CalibrationPoints.Add(point);
                            dataRow++;
                        }
                        else
                        {
                            break; // 遇到空行，结束当前信号
                        }
                    }

                    if (signalData.CalibrationPoints.Count != 0)
                    {
                        importData.Add(signalData);
                    }

                    // 移动到下一个信号块
                    currentRow = dataRow + 1; // 跳过空行
                }
                else
                {
                    currentRow++;
                }
            }

            return importData;
        }

        /// <summary>
        /// 解析比例系数或零点系数
        /// </summary>
        private double ParseFactor(string factors, string prefix)
        {
            try
            {
                int startIndex = factors.IndexOf(prefix);
                if (startIndex >= 0)
                {
                    startIndex += prefix.Length;
                    int endIndex = factors.IndexOf(' ', startIndex);
                    if (endIndex == -1) endIndex = factors.Length;

                    string factorStr = factors.Substring(startIndex, endIndex - startIndex);
                    if (double.TryParse(factorStr, out double factor))
                    {
                        return factor;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"解析系数失败: {ex.Message}");
            }

            return 0.0;
        }

        /// <summary>
        /// 解析校准点数据
        /// </summary>
        private CalibrationPointImportData ParseCalibrationPoint(IXLWorksheet worksheet, int row)
        {
            try
            {
                // 检查是否为空行
                var firstCell = worksheet.Cell(row, 1);
                if (string.IsNullOrEmpty(firstCell.Value.ToString()))
                    return null;

                var point = new CalibrationPointImportData();

                // 解析校准前数据
                point.BeforeDeviceValue = ParseValueFromCell(worksheet.Cell(row, 1));
                point.BeforeActualValue = ParseValueFromCell(worksheet.Cell(row, 2));

                // 解析校准后数据
                point.AfterDeviceValue = ParseValueFromCell(worksheet.Cell(row, 3));
                point.AfterActualValue = ParseValueFromCell(worksheet.Cell(row, 4));

                // 解析精度
                var accuracyCell = worksheet.Cell(row, 5);
                string accuracyStr = accuracyCell.Value.ToString().Replace("%", "");
                if (double.TryParse(accuracyStr, out double accuracy))
                {
                    point.Accuracy = accuracy;
                }

                // 解析测试结果
                point.TestResult = worksheet.Cell(row, 6).Value.ToString();

                return point;
            }
            catch (Exception ex)
            {
                LogService.Log($"解析校准点数据失败 (行 {row}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从单元格解析数值 (处理"采样值/测量值"格式)
        /// </summary>
        private double ParseValueFromCell(IXLCell cell)
        {
            try
            {
                string value = cell.Value.ToString();

                // 如果是"采样值/测量值"格式，取采样值
                if (value.Contains("/"))
                {
                    string[] parts = value.Split('/');
                    if (parts.Length >= 1 && double.TryParse(parts[0], out double result))
                    {
                        return result;
                    }
                }

                // 直接解析数值
                if (double.TryParse(value, out double directResult))
                {
                    return directResult;
                }

                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// 导出数据到Excel文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        private void ExportToExcel(string filePath)
        {
            try
            {
                using (var workbook = new XLWorkbook())
                {
                    // 获取所有树节点
                    var channelNodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                    var worksheet = workbook.Worksheets.Add($"电压电流校准数据（电子档）");

                    // 设置整个工作表的默认字体为宋体，11号，内容居中，格式常规
                    worksheet.Style.Font.FontName = "宋体";
                    worksheet.Style.Font.FontSize = 11;
                    worksheet.RowHeight = 20;
                    worksheet.ColumnWidth = 14;
                    worksheet.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    worksheet.Style.NumberFormat.Format = "General"; // 设置格式为常规

                    // 设置标题
                    worksheet.Cell(1, 1).Value = $"蓄电池充放电检测仪--附件：电压电流校准数据";
                    worksheet.Row(1).Height = 40;
                    worksheet.Row(1).Style.Font.FontSize = 20;
                    worksheet.Range("A1:F1").Merge(); // 合并标题行
                    worksheet.Row(1).Style.Font.Bold = true;

                    int currentRow = 2; // 当前行指针
                    int count = 0;
                    foreach (var channelGroup in channelNodes)
                    {
                        count++;
                        // 获取信号信息 - 从第一个节点获取设备名称和信号名称
                        string deviceName = channelGroup.GetValue("DeviceName")?.ToString() ?? "";
                        string signaltype = channelGroup.GetValue("SignalType")?.ToString() ?? "";
                        string signalName = channelGroup.GetValue("SignalName")?.ToString() ?? "";
                        string ratingVoltage = channelGroup.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                        string scalefactor = channelGroup.GetValue("ScaleFactor")?.ToString() ?? "";
                        string zerofactor = channelGroup.GetValue("ZeroFactor")?.ToString() ?? "";

                        // 通道标题
                        worksheet.Cell(currentRow, 1).Value = $"{deviceName}-{signalName}";
                        worksheet.Range(currentRow, 1, currentRow, 4).Merge(); // 合并通道标题

                        worksheet.Cell(currentRow, 5).Value = "额定值";
                        worksheet.Cell(currentRow, 6).Value = "比例零点系数";
                        worksheet.Column(6).Width = 25;

                        // 子标题
                        worksheet.Cell(currentRow + 1, 1).Value = "校准前";
                        worksheet.Range(currentRow + 1, 1, currentRow + 1, 2).Merge(); // 合并校准前

                        worksheet.Cell(currentRow + 1, 3).Value = "校准后";
                        worksheet.Range(currentRow + 1, 3, currentRow + 1, 4).Merge(); // 合并校准后

                        worksheet.Cell(currentRow + 1, 5).Value = ratingVoltage;
                        //worksheet.Range(currentRow + 1, 5, currentRow + 1, 6).Merge(); // 合并额定电压和测试结果

                        worksheet.Cell(currentRow + 1, 6).Value = $"K:{scalefactor} B:{zerofactor}";

                        // 列标题
                        worksheet.Cell(currentRow + 2, 1).Value = "设备采样值";
                        worksheet.Cell(currentRow + 2, 2).Value = "实际输入值";
                        worksheet.Cell(currentRow + 2, 3).Value = "设备采样值";
                        worksheet.Cell(currentRow + 2, 4).Value = "实际输入值";
                        worksheet.Cell(currentRow + 2, 5).Value = "校准精度";
                        worksheet.Cell(currentRow + 2, 6).Value = "测试结果";

                        // 设置表头样式
                        var headerRange = worksheet.Range(currentRow, 1, currentRow + 2, 6);
                        headerRange.Style.Font.Bold = true;

                        // 获取该通道的所有校准点数据
                        int dataStartRow = currentRow + 3;
                        int dataRow = dataStartRow;

                        foreach (TreeListNode node in channelGroup.Nodes)
                        {
                            string[] beforeParts;
                            string[] afterParts;
                            string accuracy = node.GetValue("CalibrationAccuracy")?.ToString() ?? "";
                            string result = node.GetValue("CalibrationResult")?.ToString() ?? "";

                            string beforeData = node.GetValue("BeforeCalibration")?.ToString() ?? "";
                            beforeParts = beforeData.Split('/');

                            string afterData = node.GetValue("AfterCalibration")?.ToString() ?? "";
                            afterParts = afterData.Split('/');

                            if (beforeParts.Length == 2 && afterParts.Length == 2)
                            {
                                worksheet.Cell(dataRow, 1).Value = beforeParts[0];
                                worksheet.Cell(dataRow, 2).Value = beforeParts[1];
                                worksheet.Cell(dataRow, 3).Value = afterParts[0];
                                worksheet.Cell(dataRow, 4).Value = afterParts[1];
                                worksheet.Cell(dataRow, 5).Value = accuracy;
                                worksheet.Cell(dataRow, 6).Value = result;
                                dataRow++;
                            }
                        }

                        // 更新当前行指针，增加空行
                        if (count != channelNodes.Count)
                        {
                            currentRow = dataRow + 1;
                            worksheet.Range(dataRow, 1, dataRow, 6).Merge(); // 合并
                        }

                        // 设置数据区域边框
                        if (dataRow > 1)
                        {
                            var dataRange = worksheet.Range(2, 1, dataRow - 1, 6);
                            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        }
                    }

                    // 保存文件
                    workbook.SaveAs(filePath);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"导出到Excel失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置进度显示
        /// </summary>
        private void ResetProgress()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(ResetProgress));
                return;
            }

            // 重置进度条
            progressBar.Position = 0;
            progressLabel.Text = "准备开始校准...";
            currentStage = ProgressStage.InitializeDevice;

            // 刷新显示
            progressBar.Update();
            progressLabel.Update();
        }

        /// <summary>
        /// 请求取消校准操作
        /// </summary>
        private void RequestCalibrationCancellation()
        {
            // 设置取消标志
            _calibrationCancellationRequested = true;

            // 取消相关的异步任务
            _cancellationTokenSource?.Cancel();

            // 停止所有设备输出
            StopAllEquipmentOutput();
        }

        /// <summary>
        /// 停止所有设备输出
        /// </summary>
        private void StopAllEquipmentOutput()
        {
            try
            {
                // 获取当前选中的设备
                string? voltageSourceName = cbVoltageSource.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(voltageSourceName))
                {
                    var voltageSource = equipmentList.FirstOrDefault(e => e.DeviceName == voltageSourceName);
                    if (voltageSource != null)
                    {
                        // 关闭电压源输出
                        Task.Run(async () => await CloseVoltageSourceOutput(voltageSource)).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"停止设备输出时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新UI状态以反映取消操作
        /// </summary>
        private void UpdateUIForCancellation()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateUIForCancellation));
                return;
            }

            // 更新按钮状态
            btnVoltageCalibration.Enabled = true;
            btnStopCalibration.Enabled = false;

            // 更新进度显示
            progressBar.Position = 0;
            progressLabel.Text = "校准已取消";

            //XtraMessageBox.Show("校准操作已成功取消");
        }

        /// <summary>
        /// 关闭电压源输出
        /// </summary>
        private async Task CloseVoltageSourceOutput(EquipmentModel voltageSource)
        {
            try
            {
                LogService.Log($"关闭 {voltageSource.DeviceName} 输出...");
                // 根据设备类型执行不同的关闭操作
                switch (voltageSource.CanType)
                {
                    case "RS485-MODBUS":
                        // 发送关闭输出命令
                        if (voltageSourceProtocols.Any())
                        {
                            bool outputDisabled = await SendEquipmentCommand(
                                CommandType.EnableOutput,
                                voltageSource,
                                voltageSourceProtocols,
                                0x00); // 关机

                            if (outputDisabled)
                            {
                                LogService.Log($"{voltageSource.DeviceName} 输出已关闭");
                            }
                            else
                            {
                                LogService.Log($"关闭 {voltageSource.DeviceName} 输出失败");
                            }
                        }
                        break;

                    case "ZCAN_CANETTCP":
                        // CAN设备关闭输出逻辑
                        // 实现CAN设备关闭输出的具体逻辑
                        LogService.Log($"CAN设备 {voltageSource.DeviceName} 输出已关闭");
                        break;

                    case "USB-SCPI":
                        // USB-SCPI设备关闭输出逻辑
                        // 实现USB-SCPI设备关闭输出的具体逻辑
                        LogService.Log($"USB-SCPI设备 {voltageSource.DeviceName} 输出已关闭");
                        break;

                    default:
                        LogService.Log($"不支持的设备类型: {voltageSource.CanType}");
                        break;
                }

                // 短暂延迟确保设备完全关闭
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                LogService.Log($"关闭 {voltageSource.DeviceName} 输出时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开设备连接
        /// </summary>
        private async Task DisconnectEquipment(EquipmentModel equipment)
        {
            try
            {
                LogService.Log($"断开 {equipment.DeviceName} 连接...");

                // 根据设备类型执行不同的断开连接操作
                switch (equipment.CanType)
                {
                    case "RS485-MODBUS":
                        // RS485设备通常不需要显式断开连接
                        // 但可以清理缓冲区等
                        RS485Manager.Instance.CloseChannel(equipment.ComPort);
                        LogService.Log($"{equipment.DeviceName} RS485连接已清理");
                        break;

                    case "ZCAN_CANETTCP":
                        // CAN设备断开连接
                        //string channelKey = CANManager.GetChannelKey(equipment.DeviceIndex, equipment.CanIndex);
                        //CANManager.Instance.UnregisterChannel(channelKey);
                        LogService.Log($"{equipment.DeviceName} CAN连接已断开");
                        break;

                    case "USB-SCPI":
                        // USB-SCPI设备断开连接
                        if (equipment.DeviceName == "GVM-9102")
                        {
                            GwinstekGVM9102_Communicator.Instance.Disconnect();
                        }
                        else if (equipment.DeviceName == "KEYSIGHT-34465A")
                        {
                            Keysight34465A_Communicator.Instance.Disconnect();
                        }
                        
                        LogService.Log($"{equipment.DeviceName} USB连接已断开");
                        break;

                    default:
                        LogService.Log($"不支持的设备类型: {equipment.CanType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"断开 {equipment.DeviceName} 连接时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新树节点的校准后值
        /// </summary>
        private void UpdateTreeNodePostCalibrationValues(
            string deviceName,
            string signalName,
            int pointIndex,
            double deviceVoltage,
            double actualVoltage,
            double accuracy,
            bool isQualified, string type)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateTreeNodePostCalibrationValues(
                    deviceName, signalName, pointIndex, deviceVoltage, actualVoltage, accuracy, isQualified, type)));
                return;
            }

            // 查找匹配的父节点
            foreach (TreeListNode parentNode in treeList.Nodes)
            {
                string nodeDeviceName = parentNode.GetValue("DeviceName")?.ToString() ?? "";
                string nodeSignalName = parentNode.GetValue("SignalName")?.ToString() ?? "";

                if (nodeDeviceName == deviceName && nodeSignalName == signalName)
                {
                    // 查找对应的校准点子节点
                    string childNodeName = $"校准点 {pointIndex}";

                    // 格式化电压值为"采样值/测量值"格式，保留4位小数
                    string voltageDisplay = $"{deviceVoltage}/{actualVoltage}";

                    if (parentNode.HasChildren)
                    {
                        foreach (TreeListNode childNode in parentNode.Nodes)
                        {
                            if (childNode.GetValue("DeviceName")?.ToString() == childNodeName)
                            {
                                childNode.SetValue("AfterCalibration", voltageDisplay);
                                childNode.SetValue("CalibrationAccuracy", $"{Math.Abs(accuracy):F4}%");
                                childNode.SetValue("CalibrationResult", isQualified ? "合格" : "不合格");

                                // 刷新节点显示
                                treeList.RefreshNode(childNode);
                                break;
                            }
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 从树节点获取额定电压值和精度范围
        /// </summary>
        /// <returns>元组包含额定电压和精度范围</returns>
        private (double ratingVoltage, double precisionRange) GetRatingVoltageAndPrecision(string deviceName, string signalName)
        {
            // 在主线程上执行
            if (this.InvokeRequired)
            {
                return ((double, double))this.Invoke(new Func<(double, double)>(() =>
                    GetRatingVoltageAndPrecision(deviceName, signalName)));
            }

            // 默认值
            double ratingVoltage = 1.0;
            double precisionRange = 1.0; // 默认1%精度

            // 查找匹配的节点
            foreach (TreeListNode node in treeList.Nodes)
            {
                string nodeDeviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                string nodeSignalName = node.GetValue("SignalName")?.ToString() ?? "";

                if (nodeDeviceName == deviceName && nodeSignalName == signalName)
                {
                    // 获取额定电压值
                    string ratingVoltageStr = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                    ratingVoltage = Convert.ToDouble(ratingVoltageStr.Split('-')[1]);

                    // 获取精度范围
                    string precisionRangeStr = node.GetValue("PrecisionRange")?.ToString() ?? "";

                    // 处理可能包含百分号的精度范围字符串
                    if (!string.IsNullOrEmpty(precisionRangeStr))
                    {
                        // 去除百分号并尝试解析
                        string numericPart = precisionRangeStr.Trim().TrimEnd('%');

                        if (double.TryParse(numericPart, out double parsedPrecision))
                        {
                            precisionRange = parsedPrecision;
                            // 成功解析精度范围
                            //LogService.Log($"成功解析精度范围: {precisionRange}%");
                        }
                        else
                        {
                            // 解析失败，使用默认值或记录错误
                            LogService.Log($"无法解析精度范围: {precisionRangeStr}，使用默认值1.0%");
                            precisionRange = 1.0; // 默认精度范围
                        }
                    }
                    else
                    {
                        // 字符串为空，使用默认值
                        LogService.Log("精度范围为空，使用默认值1.0%");
                        precisionRange = 1.0; // 默认精度范围
                    }

                    break;
                }
            }

            return (ratingVoltage, precisionRange);
        }

        /// <summary>
        /// 读取设备的校准系数
        /// </summary>
        private async Task<(double scaleFactor, double zeroFactor)> ReadCalibrationFactors(
            string deviceName, SignalInfo scaleFactorSignal, SignalInfo zeroFactorSignal)
        {
            try
            {
                // 从设备列表中查找设备
                var equipment = equipmentList.FirstOrDefault(e => e.DeviceName == deviceName.Split('-')[0]);
                string channel = deviceName.Split('-')[1];
                if (equipment == null)
                    return (0.0, 0.0);

                // 发送读取请求帧
                await SendReadCalibrationRequest(equipment, channel, scaleFactorSignal);

                // 接收响应帧并解析系数
                return await ReceiveAndParseCalibrationResponse(
                    equipment, scaleFactorSignal, zeroFactorSignal, channel);
            }
            catch (Exception ex)
            {
                LogService.Log($"读取校准系数失败: {ex.Message}");
                return (0.0, 0.0);
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
                var (scale, zero) = await ReceiveAndParseCalibrationResponse(
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
                byte functionByte = HexStringToByte(functionCode);
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

                //LogService.Log($"已发送写入校准系数请求: 比例系数={scaleFactor}, 零点系数={zeroFactor}");
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
                byte functionByte = HexStringToByte(functionCode);
                requestData[0] = functionByte;

                // 其余字节为0
                for (int i = 1; i < 8; i++)
                {
                    requestData[i] = 0;
                }

                // 发送请求帧
                CANManager.Instance.ClearQueue(channelKey);
                CANManager.Instance.SendCommand(equipment.DeviceIndex, equipment.CanIndex, canId, requestData);

                //LogService.Log($"已发送读取校准系数请求: 功能码 {functionByte:X2}");
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
                var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, baseCanId, 1000);

                if (response.IsEmpty())
                {
                    LogService.Log("接收校准系数响应超时");
                    return (0.0, 0.0);
                }

                // 从响应帧中提取比例系数和零点系数
                double scaleFactor = ExtractCalibrationValue(response.data, scaleFactorSignal);
                double zeroFactor = ExtractCalibrationValue(response.data, zeroFactorSignal);

                return (scaleFactor, zeroFactor);
            }
            catch (Exception ex)
            {
                LogService.Log($"解析校准系数响应失败: {ex.Message}");
                return (0.0, 0.0);
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
        /// 更新树节点的校准系数
        /// </summary>
        private void UpdateTreeNodeCalibrationFactors(string deviceName, string signalName,
            double scaleFactor, double zeroFactor)
        {
            // 在主线程上更新UI
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateTreeNodeCalibrationFactors(deviceName, signalName, scaleFactor, zeroFactor)));
                return;
            }

            // 查找匹配的节点
            foreach (TreeListNode node in treeList.Nodes)
            {
                string nodeDeviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                string nodeSignalName = node.GetValue("SignalName")?.ToString() ?? "";

                if (nodeDeviceName == deviceName && nodeSignalName == signalName)
                {
                    // 更新比例系数和零点系数
                    node.SetValue("ScaleFactor", scaleFactor.ToString());
                    node.SetValue("ZeroFactor", zeroFactor.ToString());

                    // 刷新节点显示
                    treeList.RefreshNode(node);
                    break;
                }
            }
        }

        /// <summary>
        /// 更新树节点的值（支持电压和电流）
        /// </summary>
        private void UpdateTreeNodeValue(string deviceName, string signalName,
            double actualValue, double deviceValue, int calibrationPointIndex, string type = "电压")
        {
            // 在主线程上更新UI
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateTreeNodeValue(
                    deviceName, signalName, actualValue, deviceValue, calibrationPointIndex, type)));
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

                    // 格式化值为"采样值/测量值"格式
                    string valueDisplay = $"{deviceValue}/{actualValue}";

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
                            childNodeName, // 设备名称列显示校准点名称
                            "", "", "", "", "", "", "","","",
                            valueDisplay,// 设备电压采样值/实际电压测量值
                            "", "", ""
                        });

                        // 设置子节点的Tag为校准点信息，方便后续查找
                        calibrationNode.Tag = $"CalibrationPoint_{calibrationPointIndex}";

                        // 展开父节点以显示子节点
                        parentNode.Expanded = true;
                    }
                    else
                    {
                        calibrationNode.SetValue("BeforeCalibration", valueDisplay);
                    }

                    break;
                }
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
                Location = new Point(lblVoltageSource.Right + 10, 10),
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
                Location = new Point(lblVoltmeter.Right + 10, 10),
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
                Location = new Point(lblAmmeter.Right + 10, 10),
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
                Text = "电压校准",
                Size = new Size(80, 30),
                Location = new Point(btnDeleteSignal.Right + 10, 10)
            };
            btnVoltageCalibration.Click += BtnVoltageCalibration_Click;
            buttonPanel.Controls.Add(btnVoltageCalibration);

            btnCurrentCalibration = new SimpleButton
            {
                Text = "电流校准",
                Size = new Size(80, 30),
                Location = new Point(btnVoltageCalibration.Right + 10, 10)
            };
            btnCurrentCalibration.Click += BtnCurrentCalibration_Click;
            buttonPanel.Controls.Add(btnCurrentCalibration);

            btnVoltageMeasurement = new SimpleButton
            {
                Text = "电压计量",
                Size = new Size(80, 30),
                Location = new Point(btnCurrentCalibration.Right + 10, 10)
            };
            btnVoltageMeasurement.Click += BtnVoltageMeasurement_Click;
            buttonPanel.Controls.Add(btnVoltageMeasurement);

            btnCurrentMeasurement = new SimpleButton
            {
                Text = "电流计量",
                Size = new Size(80, 30),
                Location = new Point(btnVoltageMeasurement.Right + 10, 10)
            };
            btnCurrentMeasurement.Click += BtnCurrentMeasurement_Click;
            buttonPanel.Controls.Add(btnCurrentMeasurement);

            btnStopCalibration = new SimpleButton
            {
                Text = "停止校准",
                Size = new Size(80, 30),
                Location = new Point(btnCurrentMeasurement.Right + 10, 10)
            };
            btnStopCalibration.Click += BtnStopCalibration_Click;
            buttonPanel.Controls.Add(btnStopCalibration);

            SimpleButton btnImportData = new SimpleButton
            {
                Text = "导入数据",
                Size = new Size(80, 30),
                Location = new Point(btnStopCalibration.Right + 10, 10)
            };
            btnImportData.Click += BtnImportData_Click;
            buttonPanel.Controls.Add(btnImportData);

            SimpleButton btnExportData = new SimpleButton
            {
                Text = "导出数据",
                Size = new Size(80, 30),
                Location = new Point(btnImportData.Right + 10, 10)
            };
            btnExportData.Click += BtnExportData_Click;
            buttonPanel.Controls.Add(btnExportData);

            // 新增查询数据按钮
            btnQueryData = new SimpleButton
            {
                Text = "查询数据",
                Size = new Size(80, 30),
                Location = new Point(btnExportData.Right + 10, 10)
            };
            btnQueryData.Click += BtnQueryData_Click;
            buttonPanel.Controls.Add(btnQueryData);

            btnClearQuery = new SimpleButton
            {
                Text = "清空查询",
                Size = new Size(80, 30),
                Location = new Point(btnQueryData.Right + 10, 10)
            };
            btnClearQuery.Click += BtnClearQuery_Click;
            buttonPanel.Controls.Add(btnClearQuery);

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

            // 设置整个 TreeList 所有列标题的字体
            //treeList.Appearance.HeaderPanel.Font = new Font("Tahoma", 8F, FontStyle.Regular);
            //treeList.Appearance.HeaderPanel.Options.UseFont = true; // 确保启用自定义字体设置

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
            column.Width = 80;

            // 信号类型列
            column = treeList.Columns.Add();
            column.Caption = "信号类型";
            column.FieldName = "SignalType";
            column.VisibleIndex = 2;
            column.Width = 50;

            column = treeList.Columns.Add();
            column.Caption = "校准信号";
            column.FieldName = "CalibrationSignal";
            column.VisibleIndex = 3;
            column.Width = 80;

            column = treeList.Columns.Add();
            column.Caption = "稳定读取时间(ms)";
            column.FieldName = "ReadTime";
            column.VisibleIndex = 4;
            column.Width = 100;

            // 额定电压/电流
            column = treeList.Columns.Add();
            column.Caption = "校准范围(V/A)";
            column.FieldName = "RatingVoltageCurrent";
            column.VisibleIndex = 5;
            column.Width = 120;

            column = treeList.Columns.Add();
            column.Caption = "校准点个数";
            column.FieldName = "CalibrationNumber";
            column.VisibleIndex = 6;
            column.Width = 60;

            column = treeList.Columns.Add();
            column.Caption = "精度范围";
            column.FieldName = "PrecisionRange";
            column.VisibleIndex = 7;
            column.Width = 50;

            // 比例系数列
            column = treeList.Columns.Add();
            column.Caption = "比例系数";
            column.FieldName = "ScaleFactor";
            column.VisibleIndex = 8;
            column.Width = 50;

            // 零点系数列
            column = treeList.Columns.Add();
            column.Caption = "零点系数";
            column.FieldName = "ZeroFactor";
            column.VisibleIndex = 9;
            column.Width = 50;

            // 校准前采样值列
            column = treeList.Columns.Add();
            column.Caption = "校准前(采样值/测量值)";
            column.FieldName = "BeforeCalibration";
            column.VisibleIndex = 10;
            column.Width = 180;

            // 校准后采样值列
            column = treeList.Columns.Add();
            column.Caption = "校准后(采样值/测量值)";
            column.FieldName = "AfterCalibration";
            column.VisibleIndex = 11;
            column.Width = 180;

            // 校准精度列
            column = treeList.Columns.Add();
            column.Caption = "校准精度";
            column.FieldName = "CalibrationAccuracy";
            column.VisibleIndex = 12;
            column.Width = 50;

            // 校准结果列
            column = treeList.Columns.Add();
            column.Caption = "校准结果";
            column.FieldName = "CalibrationResult";
            column.VisibleIndex = 13;
            column.Width = 50;

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

            // 添加进度标签
            progressLabel = new LabelControl
            {
                Text = "准备开始校准...",
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(180, 40),
                Location = new Point(0, (progressPanel.Height - 40) / 2), // 计算垂直居中位置
                //Appearance = { TextOptions = { HAlignment = HorzAlignment.Near } }
            };
            progressPanel.Controls.Add(progressLabel);

            // 添加进度条
            progressBar = new ProgressBarControl
            {
                Size = new Size(1670, 40),
                Location = new Point(progressLabel.Right + 20, (progressPanel.Height - 40) / 2),
                Properties = {
                    ShowTitle = true,
                    PercentView = true,
                    Minimum = 0,
                    Maximum = 100
                }
            };
            progressPanel.Controls.Add(progressBar);

            return progressPanel;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 根据信号类型过滤树形列表
        /// </summary>
        /// <param name="signalType">信号类型（"电压"或"电流"）</param>
        private void FilterSignalsByType(string signalType)
        {
            treeList.BeginUpdate();

            try
            {
                // 遍历所有节点
                foreach (TreeListNode node in treeList.Nodes)
                {
                    string nodeSignalType = node.GetValue("SignalType")?.ToString() ?? "";

                    // 如果节点类型匹配，则显示；否则隐藏
                    if (nodeSignalType.Contains(signalType))
                    {
                        node.Visible = true;
                    }
                    else
                    {
                        node.Visible = false;
                        node.Checked = false; // 取消勾选隐藏的节点
                    }
                }
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

        /// <summary>
        /// 显示所有信号（取消过滤）
        /// </summary>
        private void ShowAllSignals()
        {
            treeList.BeginUpdate();

            try
            {
                // 遍历所有节点，使其全部可见
                foreach (TreeListNode node in treeList.Nodes)
                {
                    node.Visible = true;
                }
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

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

        #region 操作数据库方法

        /// <summary>
        /// 检查信号是否存在并确认替换
        /// </summary>
        private bool CheckAndConfirmSignalReplacement(List<SignalImportData> importData, string deviceNumber)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    conn.Open();

                    // 获取主记录ID
                    long? masterId = GetMasterIdByDeviceNumber(conn, deviceNumber);
                    if (masterId == null)
                    {
                        // 没有主记录，直接返回true（全部新建）
                        return true;
                    }

                    // 检查每个导入信号在数据库中是否存在
                    var existingSignals = new List<string>();
                    var newSignals = new List<string>();

                    foreach (var signalData in importData)
                    {
                        string deviceName = signalData.DeviceName;
                        string signalName = signalData.SignalName;

                        if (CheckSignalExists(conn, masterId.Value, deviceName, signalName))
                        {
                            existingSignals.Add($"{deviceName} - {signalName}");
                        }
                        else
                        {
                            newSignals.Add($"{deviceName} - {signalName}");
                        }
                    }

                    // 如果没有存在的信号，直接返回true
                    if (existingSignals.Count == 0)
                    {
                        return true;
                    }

                    // 显示替换确认对话框
                    return ShowReplacementConfirmationDialog(existingSignals, newSignals);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查信号存在性失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据设备编号获取主记录ID
        /// </summary>
        private long? GetMasterIdByDeviceNumber(SQLiteConnection conn, string deviceNumber)
        {
            string sql = "SELECT MasterID FROM CalibrationMaster WHERE DeviceNumber = @DeviceNumber";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt64(result) : (long?)null;
            }
        }

        /// <summary>
        /// 检查信号是否存在
        /// </summary>
        private bool CheckSignalExists(SQLiteConnection conn, long masterId, string deviceName, string signalName)
        {
            string sql = @"
            SELECT COUNT(*) FROM CalibrationSignalInfo 
            WHERE MasterID = @MasterID AND DeviceName = @DeviceName AND SignalName = @SignalName";

            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@MasterID", masterId);
                cmd.Parameters.AddWithValue("@DeviceName", deviceName);
                cmd.Parameters.AddWithValue("@SignalName", signalName);

                long count = Convert.ToInt64(cmd.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>
        /// 显示替换确认对话框
        /// </summary>
        private bool ShowReplacementConfirmationDialog(List<string> existingSignals, List<string> newSignals)
        {
            // 构建确认消息
            var message = new System.Text.StringBuilder();
            message.AppendLine("以下信号在数据库中已存在，是否替换？");
            message.AppendLine();

            if (existingSignals.Count > 0)
            {
                message.AppendLine("将替换的信号：");
                foreach (var signal in existingSignals)
                {
                    message.AppendLine($"  • {signal}");
                }
                message.AppendLine();
            }

            if (newSignals.Count > 0)
            {
                message.AppendLine("将新增的信号：");
                foreach (var signal in newSignals)
                {
                    message.AppendLine($"  • {signal}");
                }
                message.AppendLine();
            }

            message.AppendLine("点击'是'替换存在的信号并新增不存在的信号，点击'否'取消导入。");

            DialogResult result = XtraMessageBox.Show(
                message.ToString(),
                "确认信号替换",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            return result == DialogResult.Yes;
        }

        /// <summary>
        /// 保存导入数据到数据库
        /// </summary>
        private bool SaveImportDataToDatabase(List<SignalImportData> importData, string deviceNumber)
        {
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
                            long masterId = GetOrCreateMasterRecord(conn, transaction, deviceNumber);

                            foreach (var signalData in importData)
                            {
                                // 检查信号是否存在
                                bool signalExists = CheckSignalExists(conn, masterId, signalData.DeviceName, signalData.SignalName);

                                if (signalExists)
                                {
                                    // 更新存在的信号
                                    UpdateExistingSignal(conn, transaction, masterId, signalData);
                                }
                                else
                                {
                                    // 插入新信号
                                    InsertNewSignal(conn, transaction, masterId, signalData);
                                }
                            }

                            transaction.Commit();
                            LogService.Log($"成功导入 {importData.Count} 个信号到设备 {deviceNumber}");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            LogService.Log($"保存导入数据失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"保存导入数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新已存在的信号
        /// </summary>
        private void UpdateExistingSignal(SQLiteConnection conn, SQLiteTransaction transaction, long masterId, SignalImportData signalData)
        {
            // 1. 获取信号信息ID
            long infoId = GetSignalInfoId(conn, transaction, masterId, signalData.DeviceName, signalData.SignalName);

            if (infoId == -1) return;

            // 2. 更新信号信息
            UpdateSignalInfo(conn, transaction, infoId, signalData);

            // 3. 删除原有校准点
            DeleteCalibrationPoints(conn, transaction, infoId);

            // 4. 插入新校准点
            InsertCalibrationPoints(conn, transaction, infoId, signalData.CalibrationPoints, signalData.SignalType);
        }

        /// <summary>
        /// 获取信号信息ID
        /// </summary>
        private long GetSignalInfoId(SQLiteConnection conn, SQLiteTransaction transaction, long masterId, string deviceName, string signalName)
        {
            string sql = @"
            SELECT InfoID FROM CalibrationSignalInfo 
            WHERE MasterID = @MasterID AND DeviceName = @DeviceName AND SignalName = @SignalName";

            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@MasterID", masterId);
                cmd.Parameters.AddWithValue("@DeviceName", deviceName);
                cmd.Parameters.AddWithValue("@SignalName", signalName);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt64(result) : -1;
            }
        }

        /// <summary>
        /// 更新信号信息
        /// </summary>
        private void UpdateSignalInfo(SQLiteConnection conn, SQLiteTransaction transaction, long infoId, SignalImportData signalData)
        {
            string sql = @"
                UPDATE CalibrationSignalInfo 
                SET ReadTime = @ReadTime, 
                RatingValue = @RatingValue, 
                CalibrationPoints = @CalibrationPoints, 
                PrecisionRange = @PrecisionRange, 
                ScaleFactor = @ScaleFactor, 
                ZeroFactor = @ZeroFactor,
                SignalType = @SignalType
                WHERE InfoID = @InfoID";

            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@InfoID", infoId);
                cmd.Parameters.AddWithValue("@ReadTime", signalData.ReadTime);
                cmd.Parameters.AddWithValue("@RatingValue", signalData.RatingValue);
                cmd.Parameters.AddWithValue("@CalibrationPoints", signalData.CalibrationPoints?.Count ?? 0);
                cmd.Parameters.AddWithValue("@PrecisionRange", signalData.PrecisionRange);
                cmd.Parameters.AddWithValue("@ScaleFactor", signalData.ScaleFactor);
                cmd.Parameters.AddWithValue("@ZeroFactor", signalData.ZeroFactor);
                cmd.Parameters.AddWithValue("@SignalType", signalData.SignalType);

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 插入新信号
        /// </summary>
        private void InsertNewSignal(SQLiteConnection conn, SQLiteTransaction transaction, long masterId, SignalImportData signalData)
        {
            // 1. 插入信号信息
            long infoId = InsertSignalInfo(conn, transaction, masterId, signalData);

            // 2. 插入校准点
            InsertCalibrationPoints(conn, transaction, infoId, signalData.CalibrationPoints, signalData.SignalType);
        }

        /// <summary>
        /// 插入信号信息
        /// </summary>
        private long InsertSignalInfo(SQLiteConnection conn, SQLiteTransaction transaction, long masterId, SignalImportData signalData)
        {
            string sql = @"
            INSERT INTO CalibrationSignalInfo 
            (MasterID, DeviceName, SignalName, SignalType, ReadTime, RatingValue, CalibrationPoints, PrecisionRange, ScaleFactor, ZeroFactor)
            VALUES (@MasterID, @DeviceName, @SignalName, @SignalType, @ReadTime, @RatingValue, @CalibrationPoints, @PrecisionRange, @ScaleFactor, @ZeroFactor);
            SELECT last_insert_rowid();";

            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@MasterID", masterId);
                cmd.Parameters.AddWithValue("@DeviceName", signalData.DeviceName);
                cmd.Parameters.AddWithValue("@SignalName", signalData.SignalName);
                cmd.Parameters.AddWithValue("@SignalType", signalData.SignalType);
                cmd.Parameters.AddWithValue("@ReadTime", signalData.ReadTime);
                cmd.Parameters.AddWithValue("@RatingValue", signalData.RatingValue);
                cmd.Parameters.AddWithValue("@CalibrationPoints", signalData.CalibrationPoints?.Count ?? 0);
                cmd.Parameters.AddWithValue("@PrecisionRange", signalData.PrecisionRange);
                cmd.Parameters.AddWithValue("@ScaleFactor", signalData.ScaleFactor);
                cmd.Parameters.AddWithValue("@ZeroFactor", signalData.ZeroFactor);

                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 获取或创建主记录
        /// </summary>
        private long GetOrCreateMasterRecord(SQLiteConnection conn, SQLiteTransaction transaction, string deviceNumber)
        {
            // 检查是否已存在
            string checkSql = "SELECT MasterID FROM CalibrationMaster WHERE DeviceNumber = @DeviceNumber";
            using (var cmd = new SQLiteCommand(checkSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                var result = cmd.ExecuteScalar();

                if (result != null)
                {
                    return Convert.ToInt64(result);
                }
            }

            // 创建新记录
            string insertSql = @"
            INSERT INTO CalibrationMaster (DeviceNumber, CalibrationDate, Operator, Comments)
            VALUES (@DeviceNumber, @CalibrationDate, @Operator, @Comments);
            SELECT last_insert_rowid();";

            using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                cmd.Parameters.AddWithValue("@CalibrationDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@Operator", "导入");
                cmd.Parameters.AddWithValue("@Comments", "从Excel导入");

                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 插入或更新信号信息 (使用REPLACE实现upsert功能):cite[4]
        /// </summary>
        private long InsertOrReplaceSignalInfo(SQLiteConnection conn, SQLiteTransaction transaction, long masterId, SignalImportData signalData)
        {
            string sql = @"
            INSERT OR REPLACE INTO CalibrationSignalInfo 
            (InfoID, MasterID, DeviceName, SignalName, SignalType, ReadTime, RatingValue, CalibrationPoints, PrecisionRange, ScaleFactor, ZeroFactor)
            VALUES (
                COALESCE((SELECT InfoID FROM CalibrationSignalInfo WHERE MasterID = @MasterID AND DeviceName = @DeviceName), NULL),
                @MasterID, @DeviceName, @SignalName, @SignalType, @ReadTime, @RatingValue, @CalibrationPoints, @PrecisionRange, @ScaleFactor, @ZeroFactor
            );
            SELECT last_insert_rowid();";

            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@MasterID", masterId);
                cmd.Parameters.AddWithValue("@DeviceName", signalData.DeviceName);
                cmd.Parameters.AddWithValue("@SignalName", signalData.SignalName);
                cmd.Parameters.AddWithValue("@SignalType", "电压"); // 根据实际情况判断
                cmd.Parameters.AddWithValue("@ReadTime", 1000);
                cmd.Parameters.AddWithValue("@RatingValue", signalData.RatingValue);
                cmd.Parameters.AddWithValue("@CalibrationPoints", signalData.CalibrationPoints?.Count ?? 0);
                cmd.Parameters.AddWithValue("@PrecisionRange", 1.0);
                cmd.Parameters.AddWithValue("@ScaleFactor", signalData.ScaleFactor);
                cmd.Parameters.AddWithValue("@ZeroFactor", signalData.ZeroFactor);

                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 删除现有校准点数据
        /// </summary>
        private void DeleteCalibrationPoints(SQLiteConnection conn, SQLiteTransaction transaction, long infoId)
        {
            string sql = "DELETE FROM CalibrationPointDetails WHERE InfoID = @InfoID";
            using (var cmd = new SQLiteCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@InfoID", infoId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 插入校准点数据
        /// </summary>
        private void InsertCalibrationPoints(SQLiteConnection conn, SQLiteTransaction transaction, long infoId, List<CalibrationPointImportData> points, string signalType)
        {
            if (points == null) return;

            string sql = @"
            INSERT INTO CalibrationPointDetails 
            (InfoID, PointIndex, BeforeDeviceCalibration, BeforeActualCalibration, AfterDeviceCalibration, AfterActualCalibration, Accuracy, TestResult)
            VALUES (@InfoID, @PointIndex, @BeforeDeviceCalibration, @BeforeActualCalibration, @AfterDeviceCalibration, @AfterActualCalibration, @Accuracy, @TestResult)";

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                using (var cmd = new SQLiteCommand(sql, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@InfoID", infoId);
                    cmd.Parameters.AddWithValue("@PointIndex", i + 1);

                    cmd.Parameters.AddWithValue("@BeforeDeviceCalibration", point.BeforeDeviceValue);
                    cmd.Parameters.AddWithValue("@BeforeActualCalibration", point.BeforeActualValue);
                    cmd.Parameters.AddWithValue("@AfterDeviceCalibration", point.AfterDeviceValue);
                    cmd.Parameters.AddWithValue("@AfterActualCalibration", point.AfterActualValue);

                    cmd.Parameters.AddWithValue("@Accuracy", point.Accuracy);
                    cmd.Parameters.AddWithValue("@TestResult", point.TestResult);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 保存校准数据到数据库（按照三张表的结构）
        /// </summary>
        private async Task<bool> SaveCalibrationDataToDatabase(string deviceNumber, string operatorName = "")
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            long masterId;

                            // 检查设备编号是否已存在
                            string checkMasterSql = "SELECT MasterID FROM CalibrationMaster WHERE DeviceNumber = @DeviceNumber";
                            using (var checkCmd = new SQLiteCommand(checkMasterSql, conn, transaction))
                            {
                                checkCmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                var result = await checkCmd.ExecuteScalarAsync();

                                if (result != null)
                                {
                                    // 使用现有的 MasterID
                                    masterId = Convert.ToInt64(result);

                                    // 更新主记录信息（如操作员、日期等）
                                    string updateMasterSql = @"
                                            UPDATE CalibrationMaster 
                                            SET CalibrationDate = @CalibrationDate, Operator = @Operator, Comments = @Comments
                                            WHERE MasterID = @MasterID";

                                    using (var updateCmd = new SQLiteCommand(updateMasterSql, conn, transaction))
                                    {
                                        updateCmd.Parameters.AddWithValue("@CalibrationDate", DateTime.Now);
                                        updateCmd.Parameters.AddWithValue("@Operator", operatorName);
                                        updateCmd.Parameters.AddWithValue("@Comments", "自动校准更新");
                                        updateCmd.Parameters.AddWithValue("@MasterID", masterId);
                                        await updateCmd.ExecuteNonQueryAsync();
                                    }
                                }
                                else
                                {
                                    // 插入新的主表记录
                                    string insertMasterSql = @"
                                        INSERT INTO CalibrationMaster (DeviceNumber, CalibrationDate, Operator, Comments)
                                        VALUES (@DeviceNumber, @CalibrationDate, @Operator, @Comments);
                                        SELECT last_insert_rowid();";

                                    using (var insertCmd = new SQLiteCommand(insertMasterSql, conn, transaction))
                                    {
                                        insertCmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                        insertCmd.Parameters.AddWithValue("@CalibrationDate", DateTime.Now);
                                        insertCmd.Parameters.AddWithValue("@Operator", operatorName);
                                        insertCmd.Parameters.AddWithValue("@Comments", "自动校准生成");

                                        masterId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());
                                    }
                                }
                            }

                            // 2. 插入信号信息表记录
                            string insertSignalInfoSql = @"
                                    INSERT INTO CalibrationSignalInfo 
                                    (MasterID, DeviceName, SignalName, SignalType, CalibrationSignal, ReadTime, RatingValue, CalibrationPoints, PrecisionRange, ScaleFactor, ZeroFactor)
                                    VALUES 
                                    (@MasterID, @DeviceName, @SignalName, @SignalType, @CalibrationSignal, @ReadTime, @RatingValue, @CalibrationPoints, @PrecisionRange, @ScaleFactor, @ZeroFactor);
                                    SELECT last_insert_rowid();";

                            // 3. 插入校准点详情表记录
                            string insertPointDetailsSql = @"
                                    INSERT INTO CalibrationPointDetails 
                                    (InfoID, PointIndex, BeforeDeviceCalibration, BeforeActualCalibration, 
                                     AfterDeviceCalibration, AfterActualCalibration, Accuracy, TestResult)
                                    VALUES 
                                    (@InfoID, @PointIndex, @BeforeDeviceCalibration, @BeforeActualCalibration,
                                     @AfterDeviceCalibration, @AfterActualCalibration, @Accuracy, @TestResult)";

                            foreach (TreeListNode node in treeList.Nodes)
                            {
                                // 只处理当前勾选的节点
                                if (!node.Checked) continue;

                                // 确保节点有校准数据（有子节点）
                                if (!node.HasChildren) continue;

                                string deviceName = node.GetValue("DeviceName")?.ToString() ?? "";
                                string signalName = node.GetValue("SignalName")?.ToString() ?? "";
                                string signalType = node.GetValue("SignalType")?.ToString() ?? "";
                                string calibrationsignal = node.GetValue("CalibrationSignal")?.ToString() ?? "";
                                string readTimeStr = node.GetValue("ReadTime")?.ToString() ?? "";
                                string ratingValueStr = node.GetValue("RatingVoltageCurrent")?.ToString() ?? "";
                                string calibrationPointsStr = node.GetValue("CalibrationNumber")?.ToString() ?? "";
                                string precisionRangeStr = node.GetValue("PrecisionRange")?.ToString() ?? "";
                                string scaleFactorStr = node.GetValue("ScaleFactor")?.ToString() ?? "";
                                string zeroFactorStr = node.GetValue("ZeroFactor")?.ToString() ?? "";

                                // 转换数据类型
                                int readTime = int.TryParse(readTimeStr, out int rt) ? rt : 0;
                                int calibrationPoints = int.TryParse(calibrationPointsStr, out int cp) ? cp : 0;
                                double scaleFactor = double.TryParse(scaleFactorStr, out double sf) ? sf : 1.0;
                                double zeroFactor = double.TryParse(zeroFactorStr, out double zf) ? zf : 0.0;

                                long infoId;
                                using (var cmd = new SQLiteCommand(insertSignalInfoSql, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@MasterID", masterId);
                                    cmd.Parameters.AddWithValue("@DeviceName", deviceName);
                                    cmd.Parameters.AddWithValue("@SignalName", signalName);
                                    cmd.Parameters.AddWithValue("@SignalType", signalType);
                                    cmd.Parameters.AddWithValue("@CalibrationSignal", calibrationsignal);
                                    cmd.Parameters.AddWithValue("@ReadTime", readTime);
                                    cmd.Parameters.AddWithValue("@RatingValue", ratingValueStr);
                                    cmd.Parameters.AddWithValue("@CalibrationPoints", calibrationPoints);
                                    cmd.Parameters.AddWithValue("@PrecisionRange", precisionRangeStr);
                                    cmd.Parameters.AddWithValue("@ScaleFactor", scaleFactor);
                                    cmd.Parameters.AddWithValue("@ZeroFactor", zeroFactor);

                                    infoId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                                }

                                // 处理每个校准点的数据
                                int pointIndex = 1;
                                foreach (TreeListNode childNode in node.Nodes)
                                {
                                    // 解析校准数据
                                    ParseCalibrationData(childNode, out double beforeDeviceCalibration, out double beforeActualCalibration,
                                                        out double afterDeviceCalibration, out double afterActualCalibration,
                                                        out double accuracy, out string testResult);

                                    using (var cmd = new SQLiteCommand(insertPointDetailsSql, conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@InfoID", infoId);
                                        cmd.Parameters.AddWithValue("@PointIndex", pointIndex++);
                                        cmd.Parameters.AddWithValue("@BeforeDeviceCalibration", beforeDeviceCalibration);
                                        cmd.Parameters.AddWithValue("@BeforeActualCalibration", beforeActualCalibration);
                                        cmd.Parameters.AddWithValue("@AfterDeviceCalibration", afterDeviceCalibration);
                                        cmd.Parameters.AddWithValue("@AfterActualCalibration", afterActualCalibration);
                                        cmd.Parameters.AddWithValue("@Accuracy", accuracy);
                                        cmd.Parameters.AddWithValue("@TestResult", testResult);

                                        await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                            }

                            transaction.Commit();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            LogService.Log($"保存校准数据失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"保存校准数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析校准数据（支持电压和电流）
        /// </summary>
        private void ParseCalibrationData(TreeListNode node,
            out double beforeDeviceVoltage, out double beforeActualVoltage,
            out double afterDeviceVoltage, out double afterActualVoltage,
            out double accuracy, out string testResult)
        {
            // 初始化输出参数
            beforeDeviceVoltage = 0; beforeActualVoltage = 0;
            afterDeviceVoltage = 0; afterActualVoltage = 0;
            accuracy = 0;
            testResult = "不合格";

            try
            {
                // 解析数据
                string beforeCalibration = node.GetValue("BeforeCalibration")?.ToString() ?? "";
                string afterCalibration = node.GetValue("AfterCalibration")?.ToString() ?? "";

                // 解析校准前数据
                if (!string.IsNullOrEmpty(beforeCalibration) && beforeCalibration.Contains("/"))
                {
                    var parts = beforeCalibration.Split('/');
                    if (parts.Length == 2)
                    {
                        double.TryParse(parts[0], out beforeDeviceVoltage);
                        double.TryParse(parts[1], out beforeActualVoltage);
                    }
                }

                // 解析校准后数据
                if (!string.IsNullOrEmpty(afterCalibration) && afterCalibration.Contains("/"))
                {
                    var parts = afterCalibration.Split('/');
                    if (parts.Length == 2)
                    {
                        double.TryParse(parts[0], out afterDeviceVoltage);
                        double.TryParse(parts[1], out afterActualVoltage);
                    }
                }

                // 解析精度和测试结果
                string accuracyStr = node.GetValue("CalibrationAccuracy")?.ToString() ?? "";
                if (accuracyStr.EndsWith("%"))
                    accuracyStr = accuracyStr.TrimEnd('%');
                double.TryParse(accuracyStr, out accuracy);

                testResult = node.GetValue("CalibrationResult")?.ToString() ?? "不合格";
            }
            catch (Exception ex)
            {
                LogService.Log($"解析校准数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查设备编号下特定设备名称和信号名称的数据是否存在（新增方法）
        /// </summary>
        private async Task<bool> CheckDeviceSignalDataExists(string deviceNumber, List<(string DeviceName, string SignalName)> deviceSignals)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 构建条件列表
                    var conditions = new List<string>();
                    var parameters = new Dictionary<string, object>
                    {
                        ["@DeviceNumber"] = deviceNumber
                    };

                    for (int i = 0; i < deviceSignals.Count; i++)
                    {
                        string deviceNameParam = $"@DeviceName{i}";
                        string signalNameParam = $"@SignalName{i}";

                        // 设备名称和信号名称都在 CalibrationSignalInfo 表中
                        conditions.Add($"(csi.DeviceName = {deviceNameParam} AND csi.SignalName = {signalNameParam})");

                        parameters[deviceNameParam] = deviceSignals[i].DeviceName;
                        parameters[signalNameParam] = deviceSignals[i].SignalName;
                    }

                    if (conditions.Count == 0)
                        return false;

                    string whereClause = string.Join(" OR ", conditions);

                    string sql = $@"
                    SELECT COUNT(*) 
                    FROM CalibrationPointDetails cpd
                    JOIN CalibrationSignalInfo csi ON cpd.InfoID = csi.InfoID
                    JOIN CalibrationMaster cm ON csi.MasterID = cm.MasterID
                    WHERE cm.DeviceNumber = @DeviceNumber 
                    AND ({whereClause})";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        foreach (var param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value);
                        }

                        long count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"检查设备信号数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查询并显示指定设备编号的校准数据
        /// </summary>
        /// <param name="deviceNumber">设备编号</param>
        private void QueryAndDisplayCalibrationData(string deviceNumber)
        {
            treeList.BeginUpdate();
            treeList.ClearNodes();

            try
            {
                string connectionString = $"Data Source={sqladdress};Version=3;";

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    // 1. 查询主记录
                    string masterSql = @"
                        SELECT MasterID, CalibrationDate, Operator, Comments 
                        FROM CalibrationMaster 
                        WHERE DeviceNumber = @DeviceNumber 
                        ORDER BY CalibrationDate DESC 
                        LIMIT 1";

                    long masterId = -1;
                    DateTime calibrationDate = DateTime.Now;
                    string operatorName = "";
                    string comments = "";

                    using (var cmd = new SQLiteCommand(masterSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                masterId = reader.GetInt64(0);
                                calibrationDate = reader.GetDateTime(1);
                                operatorName = reader.GetString(2);
                                comments = reader.GetString(3);
                            }
                            else
                            {
                                XtraMessageBox.Show($"未找到设备编号 '{deviceNumber}' 的校准记录!");
                                return;
                            }
                        }
                    }

                    // 2. 查询信号信息
                    string signalInfoSql = @"
                                SELECT InfoID, DeviceName, SignalName, SignalType, CalibrationSignal, ReadTime, 
                                       RatingValue, CalibrationPoints, PrecisionRange, ScaleFactor, ZeroFactor
                                FROM CalibrationSignalInfo 
                                WHERE MasterID = @MasterID 
                                ORDER BY InfoID";

                    Dictionary<long, TreeListNode> signalNodes = new Dictionary<long, TreeListNode>();

                    using (var cmd = new SQLiteCommand(signalInfoSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MasterID", masterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long infoId = reader.GetInt64(0);
                                string deviceName = reader.GetString(1);
                                string signalName = reader.GetString(2);
                                string signalType = reader.GetString(3);
                                string calibrationSignal = reader.IsDBNull(4) ? null : reader.GetString(4);
                                int readTime = reader.GetInt32(5);
                                string ratingValue = reader.GetString(6);
                                int calibrationPoints = reader.GetInt32(7);
                                string precisionRange = reader.GetString(8);
                                double scaleFactor = reader.GetDouble(9);
                                double zeroFactor = reader.GetDouble(10);

                                // 创建父节点
                                var parentNode = treeList.AppendNode(new object[]
                                {
                                    deviceName,
                                    signalName,
                                    signalType,
                                    calibrationSignal,
                                    readTime.ToString(),
                                    ratingValue.ToString(),
                                    calibrationPoints.ToString(),
                                    precisionRange.ToString(),
                                    scaleFactor.ToString(""),
                                    zeroFactor.ToString(""),
                                    "", // 设备采样值
                                    "", // 实际测量值
                                    "", // 校准精度
                                    ""  // 校准结果
                                }, null);

                                // 存储InfoID与节点的映射关系
                                signalNodes[infoId] = parentNode;
                            }
                        }
                    }

                    // 3. 查询校准点详情
                    string pointDetailsSql = @"
                            SELECT InfoID, PointIndex, 
                                   BeforeDeviceCalibration, BeforeActualCalibration, 
                                   AfterDeviceCalibration, AfterActualCalibration,
                                   Accuracy, TestResult
                            FROM CalibrationPointDetails 
                            WHERE InfoID IN (SELECT InfoID FROM CalibrationSignalInfo WHERE MasterID = @MasterID)
                            ORDER BY InfoID, PointIndex";

                    using (var cmd = new SQLiteCommand(pointDetailsSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MasterID", masterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long infoId = reader.GetInt64(0);
                                int pointIndex = reader.GetInt32(1);

                                // 获取电压值
                                double beforeDeviceCalibration = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                                double beforeActualCalibration = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                                double afterDeviceCalibration = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                                double afterActualCalibration = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);

                                // 获取精度和结果
                                double accuracy = reader.IsDBNull(6) ? 0 : reader.GetDouble(6);
                                string testResult = reader.IsDBNull(7) ? "" : reader.GetString(7);

                                // 获取电流值
                                //double beforeDeviceCalibrationCurrent = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                                //double beforeActualCalibrationCurrent = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                                //double afterDeviceCalibrationCurrent = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                                //double afterActualCalibrationCurrent = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);

                                // 查找对应的父节点
                                if (signalNodes.TryGetValue(infoId, out var parentNode))
                                {
                                    // 确定是电压还是电流信号
                                    //string signalType = parentNode.GetValue("SignalType")?.ToString() ?? "";
                                    //bool isVoltage = signalType.Contains("电压", StringComparison.OrdinalIgnoreCase);
                                    //bool isCurrent = signalType.Contains("电流", StringComparison.OrdinalIgnoreCase);

                                    // 创建子节点
                                    var childNode = parentNode.Nodes.Add(new object[]
                                    {
                                        $"校准点 {pointIndex}", // 设备名称列显示校准点名称
                                        "", "", "", "", "", "", "", "", "",
                                        $"{beforeDeviceCalibration}/{beforeActualCalibration}", // 校准前
                                        $"{afterDeviceCalibration}/{afterActualCalibration}",   // 校准后
                                        $"{accuracy}%",  // 校准精度
                                        testResult          // 校准结果
                                    });

                                    // 设置子节点的Tag为校准点信息
                                    childNode.Tag = $"CalibrationPoint_{pointIndex}";
                                }
                            }
                        }
                    }

                    // 4. 展开所有节点以显示详情
                    treeList.ExpandAll();

                    // 5. 隐藏复选框（查询模式下不需要勾选）
                    treeList.OptionsView.ShowCheckBoxes = false;

                    // 显示查询结果信息
                    string resultInfo = $"设备编号: {deviceNumber}\n" +
                                       $"校准时间: {calibrationDate:yyyy-MM-dd HH:mm:ss}\n" +
                                       $"操作人员: {operatorName}\n" +
                                       $"备注: {comments}";

                    //XtraMessageBox.Show(resultInfo, "查询结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"查询校准数据失败: {ex.Message}");
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

        /// <summary>
        /// 获取设备编号下实际存在的信号数据
        /// </summary>
        private async Task<List<(string DeviceName, string SignalName)>> GetExistingDeviceSignalData(string deviceNumber, List<(string DeviceName, string SignalName)> deviceSignals)
        {
            var existingSignals = new List<(string DeviceName, string SignalName)>();

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 构建条件列表
                    var conditions = new List<string>();
                    var parameters = new Dictionary<string, object>
                    {
                        ["@DeviceNumber"] = deviceNumber
                    };

                    for (int i = 0; i < deviceSignals.Count; i++)
                    {
                        string deviceNameParam = $"@DeviceName{i}";
                        string signalNameParam = $"@SignalName{i}";

                        conditions.Add($"(csi.DeviceName = {deviceNameParam} AND csi.SignalName = {signalNameParam})");

                        parameters[deviceNameParam] = deviceSignals[i].DeviceName;
                        parameters[signalNameParam] = deviceSignals[i].SignalName;
                    }

                    if (conditions.Count == 0)
                        return existingSignals;

                    string whereClause = string.Join(" OR ", conditions);

                    string sql = $@"
                    SELECT DISTINCT csi.DeviceName, csi.SignalName
                    FROM CalibrationPointDetails cpd
                    JOIN CalibrationSignalInfo csi ON cpd.InfoID = csi.InfoID
                    JOIN CalibrationMaster cm ON csi.MasterID = cm.MasterID
                    WHERE cm.DeviceNumber = @DeviceNumber 
                    AND ({whereClause})";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        foreach (var param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value);
                        }

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string deviceName = reader.GetString(0);
                                string signalName = reader.GetString(1);
                                existingSignals.Add((deviceName, signalName));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"获取存在的设备信号数据失败: {ex.Message}");
            }

            return existingSignals;
        }

        /// <summary>
        /// 删除指定设备编号下特定信号名称的校准记录
        /// </summary>
        private async Task<bool> DeleteSignalCalibrationRecordsByName(string deviceNumber, List<string> signalNames)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 构建信号名称的IN条件
                            string signalNameList = string.Join(",", signalNames.Select(name => $"'{name.Replace("'", "''")}'"));

                            // 1. 删除校准点详情
                            string deleteDetailsSql = @"
                        DELETE FROM CalibrationPointDetails 
                        WHERE InfoID IN (
                            SELECT csi.InfoID 
                            FROM CalibrationSignalInfo csi
                            JOIN CalibrationMaster cm ON csi.MasterID = cm.MasterID
                            WHERE cm.DeviceNumber = @DeviceNumber 
                            AND csi.SignalName IN (" + signalNameList + "))";

                            using (var cmd = new SQLiteCommand(deleteDetailsSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            // 2. 删除信号信息
                            string deleteSignalInfoSql = @"
                            DELETE FROM CalibrationSignalInfo 
                            WHERE MasterID IN (
                                SELECT MasterID FROM CalibrationMaster 
                                WHERE DeviceNumber = @DeviceNumber)
                            AND SignalName IN (" + signalNameList + ")";

                            using (var cmd = new SQLiteCommand(deleteSignalInfoSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            LogService.Log($"已删除设备编号 {deviceNumber} 下指定信号的校准记录");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            LogService.Log($"删除设备编号 {deviceNumber} 下指定信号的校准记录失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"删除校准记录时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除设备编号下特定设备名称和信号名称的校准记录（新增方法）
        /// </summary>
        private async Task<bool> DeleteDeviceSignalCalibrationRecords(string deviceNumber, List<(string DeviceName, string SignalName)> deviceSignals)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 构建条件列表
                            var conditions = new List<string>();
                            var parameters = new Dictionary<string, object>
                            {
                                ["@DeviceNumber"] = deviceNumber
                            };

                            for (int i = 0; i < deviceSignals.Count; i++)
                            {
                                string deviceNameParam = $"@DeviceName{i}";
                                string signalNameParam = $"@SignalName{i}";

                                // 设备名称和信号名称都在 CalibrationSignalInfo 表中
                                conditions.Add($"(csi.DeviceName = {deviceNameParam} AND csi.SignalName = {signalNameParam})");

                                parameters[deviceNameParam] = deviceSignals[i].DeviceName;
                                parameters[signalNameParam] = deviceSignals[i].SignalName;
                            }

                            if (conditions.Count == 0)
                                return false;

                            string whereClause = string.Join(" OR ", conditions);

                            // 1. 删除校准点详情
                            string deleteDetailsSql = $@"
                                DELETE FROM CalibrationPointDetails 
                                WHERE InfoID IN (
                                SELECT csi.InfoID 
                                FROM CalibrationSignalInfo csi
                                JOIN CalibrationMaster cm ON csi.MasterID = cm.MasterID
                                WHERE cm.DeviceNumber = @DeviceNumber 
                                AND ({whereClause})
                            )";

                            using (var cmd = new SQLiteCommand(deleteDetailsSql, conn, transaction))
                            {
                                foreach (var param in parameters)
                                {
                                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                                }
                                await cmd.ExecuteNonQueryAsync();
                            }

                            // 2. 删除信号信息
                            string deleteSignalInfoSql = $@"
                                DELETE FROM CalibrationSignalInfo 
                                WHERE MasterID IN (
                                SELECT cm.MasterID 
                                FROM CalibrationSignalInfo csi
                                JOIN CalibrationMaster cm ON csi.MasterID = cm.MasterID
                                WHERE cm.DeviceNumber = @DeviceNumber
                                AND ({whereClause})
                            )";

                            using (var cmd = new SQLiteCommand(deleteSignalInfoSql, conn, transaction))
                            {
                                foreach (var param in parameters)
                                {
                                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                                }
                                await cmd.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            LogService.Log($"已删除设备编号 {deviceNumber} 下指定设备和信号的校准记录");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            LogService.Log($"删除设备编号 {deviceNumber} 下指定设备和信号的校准记录失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"删除校准记录时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除指定设备编号的所有校准记录
        /// </summary>
        /// <param name="deviceNumber">设备编号</param>
        /// <returns>删除是否成功</returns>
        private async Task<bool> DeleteCalibrationRecordsByDeviceNumber(string deviceNumber)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={sqladdress};Version=3;"))
                {
                    await conn.OpenAsync();

                    // 开始事务
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. 首先删除CalibrationDetails表中的相关记录
                            string deleteDetailsSql = @"
                                DELETE FROM CalibrationDetails 
                                WHERE MasterID IN (SELECT MasterID FROM CalibrationMaster WHERE DeviceNumber = @DeviceNumber)";

                            using (var cmd = new SQLiteCommand(deleteDetailsSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            // 2. 然后删除CalibrationMaster表中的记录
                            string deleteMasterSql = "DELETE FROM CalibrationMaster WHERE DeviceNumber = @DeviceNumber";

                            using (var cmd = new SQLiteCommand(deleteMasterSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@DeviceNumber", deviceNumber);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            LogService.Log($"已删除设备编号 {deviceNumber} 的所有校准记录");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            LogService.Log($"删除设备编号 {deviceNumber} 的校准记录失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"删除校准记录时发生错误: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 枚举定义

        /// <summary>
        /// 命令类型枚举
        /// </summary>
        private enum CommandType
        {
            SetMode,
            SetVoltage,
            SetCurrent,
            EnableOutput
        }

        // 进度阶段枚举
        private enum ProgressStage
        {
            InitializeDevice = 2,      // 初始化设备 (0-2%)
            LoadProtocol = 4,          // 加载协议   (2-4%)
            StartingEquipment = 6,     // 启动设备 (4-6%)
            SetParameters = 8,         // 设置设备参数 (6-8%)
            HandlePoint = 10,          // 处理校准点 (8-10%)
            BeforeCalibration = 54,    // 校准前 (10-54%)
            AfterCalibration = 98,     // 验证校准结果 (54-98%)
            Completed = 100            // 完成 (100%)
        }

        private LabelControl progressLabel;

        #endregion

        #region 导入数据模型

        /// <summary>
        /// 信号导入数据
        /// </summary>
        public class SignalImportData
        {
            public string DeviceName { get; set; } = string.Empty;
            public string SignalName { get; set; } = string.Empty;
            public string SignalType { get; set; } = "电压";
            public int ReadTime { get; set; } = 1000;
            public double RatingValue { get; set; }
            public double PrecisionRange { get; set; } = 1.0;
            public double ScaleFactor { get; set; } = 1.0;
            public double ZeroFactor { get; set; }
            public List<CalibrationPointImportData> CalibrationPoints { get; set; } = new List<CalibrationPointImportData>();
        }

        /// <summary>
        /// 校准点导入数据
        /// </summary>
        public class CalibrationPointImportData
        {
            public double BeforeDeviceValue { get; set; }
            public double BeforeActualValue { get; set; }
            public double AfterDeviceValue { get; set; }
            public double AfterActualValue { get; set; }
            public double Accuracy { get; set; }
            public string TestResult { get; set; } = "不合格";
        }

        #endregion
    }
}