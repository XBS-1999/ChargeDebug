using ClosedXML.Excel;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraTreeList.Nodes;
using DevExpress.XtraTreeList;
using System.Runtime.InteropServices;
using ZLGAPI;
using System.Text;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using System.Data.SQLite;
using System.Data;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraEditors.Controls;
using System.ComponentModel;
using ChargeDebug.Service;
using System.Globalization;
using DataModel;

namespace ChargeDebug.Form
{
    public partial class Agreement : XtraUserControl
    {
        #region 字段和属性
        private TreeList treeList;
        private string dbcPath = "";
        private static uint DBCHandle = 0;    // DBC句柄
        private long _currentDbcFileId = -1;
        private GridControl gridControl;
        private GridView gridView;
        private BindingList<ReuseSignal> _currentReuseSignals;
        private TreeListNode _currentSignalNode;
        private List<EquipmentModel> agreementList;

        // 上下文菜单组件
        private ContextMenuStrip gridContextMenu;
        private ToolStripMenuItem deleteItem;
        private ToolStripMenuItem copyItem;
        private ToolStripMenuItem pasteItem;

        // 下拉框组件
        private RepositoryItemComboBox repoFrameType;
        private RepositoryItemComboBox repoByteOrder;
        private RepositoryItemComboBox repoMultiplexSignals;
        private RepositoryItemComboBox repoSigned;
        private RepositoryItemTextEdit repositoryTextEdit;

        public event EventHandler ConfigUpdated;
        #endregion

        #region 初始化
        public Agreement(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            agreementList = new List<EquipmentModel>(equipmentList);
            int a = equipmentList.Count;
            InitializeComponent();
            InitializeUI();
            this.Load += Agreement_Load;
        }

        private void Agreement_Load(object? sender, EventArgs e)
        {
            LoadDbcFilesFromDatabase();
            ConfigureGridSelection();
            AutoSelectFirstRow();
        }

        private void AutoSelectFirstRow()
        {
            if (gridView.RowCount > 0)
            {
                gridView.FocusedRowHandle = 0;
                LoadTreeDataForSelectedRow();
            }
        }
        #endregion

        #region 数据加载
        /* 加载DBC文件列表到Grid */
        private void LoadDbcFilesFromDatabase()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    // 使用新方法获取数据
                    DataTable dt = SQLite_Service.GetDbcFilesWithFullColumns(conn);
                    if (gridControl.InvokeRequired)
                    {
                        gridControl.BeginInvoke((MethodInvoker)delegate
                        {
                            BindGridData(dt);
                        });
                    }
                    else
                    {
                        BindGridData(dt);
                    }

                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载失败: {ex.Message}");
            }
        }

        private void BindGridData(DataTable dt)
        {
            try
            {
                gridControl.BeginUpdate();
                gridControl.DataSource = null;
                gridControl.DataSource = dt;
                gridView.BestFitColumns();

                var fileIdColumn = gridView.Columns["文件ID"];
                if (fileIdColumn != null)
                {
                    fileIdColumn.Visible = false;
                }
            }
            finally
            {
                gridControl.EndUpdate();
            }
        }

        /* 加载选中DBC的报文信号数据 */
        private void LoadTreeDataForSelectedRow()
        {
            DataRow row = gridView.GetDataRow(gridView.FocusedRowHandle);
            if (row == null) return;

            try
            {
                treeList.BeginUnboundLoad();
                treeList.ClearNodes();


                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    _currentDbcFileId = SQLite_Service.GetDbcFileId(conn, row["DBC文件名称"].ToString());
                    var messages = SQLite_Service.GetMessagesByDbc(conn, _currentDbcFileId);
                    LoadMessagesAndSignals(conn, messages);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载树数据失败: {ex.Message}");
            }
            finally
            {
                //treeList.ExpandAll();
                treeList.EndUnboundLoad();
            }
        }

        /* 递归加载报文和信号 */
        private void LoadMessagesAndSignals(SQLiteConnection conn, List<MessageInfo> messages)
        {
            foreach (var msg in messages)
            {
                var parentNode = CreateMessageNode(msg);
                var signals = SQLite_Service.GetSignalsByMessage(conn, msg.MessageID);
                CreateSignalNodes(parentNode, signals);
            }
        }

        private TreeListNode CreateMessageNode(MessageInfo msg)
        {
            var node = treeList.AppendNode(new object[]
            {
                msg.CANID,
                msg.FrameType,
                msg.MessageName,
                msg.DataLength,
                "", "", "", "", "", "", "", "", "", ""
            }, null);
            node.Tag = msg.MessageID;
            node.SetValue("Orders", msg.Orders);
            return node;
        }

        private void CreateSignalNodes(TreeListNode parent, List<SignalInfo> signals)
        {
            foreach (var signal in signals)
            {
                var node = treeList.AppendNode(new object[]
                {
                    "", "", "", "",
                    signal.SignalName,
                    signal.MultiplexSignals,
                    signal.SystemName,
                    signal.Unit,
                    signal.StartBit,
                    signal.Length,
                    signal.ByteOrder,
                    signal.Signed,
                    signal.Factor,
                    signal.Offset,
                    signal.MinMax
                }, parent);
                node.Tag = signal.SignalID;
                node.SetValue("Orders", signal.Orders);
            }
        }
        #endregion

        #region UI事件处理
        /* 配置Grid选择事件 */
        private void ConfigureGridSelection()
        {
            gridView.FocusedRowChanged += (s, e) =>
            {
                if (e.FocusedRowHandle >= 0)
                {
                    gridControl.BeginInvoke(new Action(() =>
                    {
                        LoadTreeDataForSelectedRow();
                    }));
                }
            };

            /* 右键菜单处理 */
            gridView.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    // 获取点击位置信息
                    GridHitInfo hitInfo = gridView.CalcHitInfo(e.Location);

                    // 无论是否在行上都显示菜单
                    gridContextMenu.Show(gridControl, e.Location);

                    // 如果在行上点击，设置焦点行
                    if (hitInfo.InRow)
                    {
                        gridView.Focus();
                        gridView.FocusedRowHandle = hitInfo.RowHandle;
                    }
                    else
                    {
                        // 清空选择
                        gridView.ClearSelection();
                    }
                }
            };

            gridContextMenu.Opening += (s, e) =>
            {
                bool hasSelection = gridView.SelectedRowsCount > 0;
                bool clipValid = Clipboard.ContainsText();

                deleteItem.Enabled = hasSelection;
                copyItem.Enabled = hasSelection;
                pasteItem.Enabled = clipValid;
            };
        }

        /* 删除菜单项点击事件 */
        private void DeleteMenuItem_Click(object? sender, EventArgs e)
        {
            int rowHandle = gridView.FocusedRowHandle;
            if (rowHandle >= 0)
            {
                DataRow row = gridView.GetDataRow(rowHandle);
                if (row != null)
                {
                    string? fileName = row["DBC文件名称"].ToString();
                    if (XtraMessageBox.Show($"确定要删除'{fileName}'吗？", "确认删除",
                        MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            using (var transaction = conn.BeginTransaction())
                            {
                                try
                                {
                                    long fileId = SQLite_Service.GetDbcFileId(conn, fileName);
                                    SQLite_Service.DeleteDbcFile(conn, fileId);
                                    transaction.Commit();
                                    XtraMessageBox.Show("删除成功！");
                                }
                                catch (Exception ex)
                                {
                                    transaction.Rollback();
                                    XtraMessageBox.Show($"删除失败：{ex.Message}");
                                }
                            }
                        }
                        LoadDbcFilesFromDatabase();
                    }
                }
            }
        }

        /* 复制菜单项点击事件 */
        private void CopyMenuItem_Click(object? sender, EventArgs e)
        {
            int rowHandle = gridView.FocusedRowHandle;
            if (rowHandle >= 0)
            {
                DataRow row = gridView.GetDataRow(rowHandle);
                if (row != null)
                {
                    string? fileName = row["DBC文件名称"].ToString();
                    if (fileName != null)
                        Clipboard.SetText(fileName);
                }
            }
        }

        /* 粘贴菜单项点击事件 */
        private void PasteMenuItem_Click(object? sender, EventArgs e)
        {
            try
            {
                string sourceFileName = Clipboard.GetText();
                if (string.IsNullOrEmpty(sourceFileName))
                {
                    XtraMessageBox.Show("剪贴板中没有有效的DBC文件名");
                    return;
                }

                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();

                    // 获取源文件ID
                    long sourceFileId = SQLite_Service.GetDbcFileId(conn, sourceFileName);
                    if (sourceFileId == -1)
                    {
                        XtraMessageBox.Show("找不到要复制的源文件");
                        return;
                    }

                    using (var sfd = new SaveDialogForm(dbcPath))
                    {
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            using (var transaction = conn.BeginTransaction())
                            {
                                try
                                {
                                    // 创建新DBC文件记录
                                    long newFileId = SQLite_Service.UpsertDbcFile(conn, sfd.FileName);

                                    // 创建消息和信号
                                    var messages = SQLite_Service.GetMessagesByDbc(conn, sourceFileId);
                                    foreach (var message in messages)
                                    {
                                        long newMessageId = SQLite_Service.UpsertMessage(
                                            conn,
                                            messageId: -1,
                                            canId: message.CANID,
                                            frameType: message.FrameType,
                                            messageName: message.MessageName,
                                            dataLength: message.DataLength,
                                            orders: message.Orders,
                                            dbcFileId: newFileId
                                        );
                                        var signals = SQLite_Service.GetSignalsByMessage(conn, message.MessageID);
                                        foreach (var signal in signals)
                                        {
                                            long newsignalId = SQLite_Service.UpsertSignal(
                                                conn,
                                                signalId: -1,
                                                signalName: signal.SignalName,
                                                multiplexSignals: signal.MultiplexSignals,
                                                systemName: signal.SystemName,
                                                unit: signal.Unit,
                                                startBit: signal.StartBit,
                                                length: signal.Length,
                                                byteOrder: signal.ByteOrder,
                                                signed: signal.Signed,
                                                factor: signal.Factor,
                                                offset: signal.Offset,
                                                minMax: signal.MinMax,
                                                orders: signal.Orders,
                                                messageId: newMessageId
                                            );
                                            var reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signal.SignalID);
                                            SQLite_Service.SaveReuseSignals(conn, newsignalId, reuseSignals);
                                        }
                                    }
                                    transaction.Commit();
                                    XtraMessageBox.Show($"已成功创建：{sfd.FileName}");
                                }
                                catch
                                {
                                    transaction.Rollback();
                                    throw;
                                }
                            }
                        }
                    }
                }
                // 刷新列表
                LoadDbcFilesFromDatabase();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"粘贴失败：{ex.Message}");
            }
        }

        /* TreeList单元格内容改变时触发事件 */
        private void TreeList_CustomNodeCellEdit(object sender, GetCustomNodeCellEditEventArgs e)
        {
            // 父节点编辑器配置
            if (e.Node.ParentNode == null)
            {
                switch (e.Column.Caption)
                {
                    case "帧类型":
                        e.RepositoryItem = repoFrameType;
                        break;
                    case "CAN ID":
                    case "消息名称":
                    case "数据长度":
                        e.RepositoryItem = repositoryTextEdit; // 文本编辑器
                        break;
                }
            }
            // 子节点编辑器配置
            else
            {
                switch (e.Column.Caption)
                {
                    case "是否复用信号":
                        e.RepositoryItem = repoMultiplexSignals;
                        break;
                    case "字节顺序":
                        e.RepositoryItem = repoByteOrder;
                        break;
                    case "符号":
                        e.RepositoryItem = repoSigned;
                        break;
                    default:
                        e.RepositoryItem = repositoryTextEdit; // 文本编辑器
                        break;
                }
            }
        }

        /* TreeList单元格编辑触发事件 */
        private void TreeList_ShowingEditor(object? sender, CancelEventArgs e)
        {
            var treeList = sender as TreeList;
            TreeListNode focusedNode = treeList.FocusedNode;
            TreeListColumn focusedColumn = treeList.FocusedColumn;

            if (focusedNode == null || focusedColumn == null)
            {
                e.Cancel = true;
                return;
            }

            // 默认禁止编辑
            e.Cancel = true;

            // 父节点编辑规则
            if (focusedNode.ParentNode == null)
            {
                switch (focusedColumn.Caption)
                {
                    case "CAN ID":
                    case "帧类型":
                    case "消息名称":
                    case "数据长度":
                        e.Cancel = false; // 允许编辑
                        break;
                }
            }
            // 子节点编辑规则
            else
            {
                switch (focusedColumn.Caption)
                {
                    case "关联系统变量名称":
                    case "是否复用信号":
                    case "单位":
                    case "起始位":
                    case "长度":
                    case "系数":
                    case "偏移":
                    case "范围":
                    case "字节顺序":
                    case "符号":
                        e.Cancel = false; // 允许编辑
                        break;

                    case "信号名称":
                        var signalNameColumn = treeList.Columns["是否复用信号"];
                        if (signalNameColumn != null)
                        {
                            string signalName = focusedNode.GetValue(signalNameColumn)?.ToString() ?? "";
                            e.Cancel = (signalName == "是");
                        }
                        break;
                }
            }
        }

        /* TreeList复选框勾选触发事件 */
        private void TreeList_AfterCheckNode(object sender, NodeEventArgs e)
        {
            // 仅处理父节点的勾选状态变化
            if (e.Node.ParentNode != null) return;

            // 获取当前节点的勾选状态
            CheckState parentState = e.Node.CheckState;

            // 递归设置所有子节点状态
            SetChildrenCheckState(e.Node, parentState);
        }

        /* TreeList双击单元格触发事件(TreeList_ShowingEditor事件中禁用单元格编辑才有用?) */
        private void TreeList_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            TreeListHitInfo hitInfo = treeList.CalcHitInfo(e.Location);
            if (hitInfo.HitInfoType != HitInfoType.Cell || hitInfo.Node == null) return;
            TreeListColumn clickedColumn = hitInfo.Column;
            if (clickedColumn?.Caption != "信号名称") return;
            if (hitInfo.Node.ParentNode == null) return;

            _currentSignalNode = hitInfo.Node;
            TreeListNode parentNode = _currentSignalNode.ParentNode;

            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction()) // 开启事务
                {
                    try
                    {
                        //1.获取当前页面值
                        //父节点
                        var canId = parentNode.GetValue("CAN ID").ToString();
                        var frameType = parentNode.GetValue("帧类型").ToString();
                        var messageName = parentNode.GetValue("消息名称").ToString();
                        var dataLength = Convert.ToInt32(parentNode.GetValue("数据长度"));
                        var order = Convert.ToInt32(parentNode.GetValue("Orders"));
                        //子节点
                        var signalName = _currentSignalNode.GetValue("信号名称").ToString();
                        var multiplexSignals = _currentSignalNode.GetValue("是否复用信号").ToString();
                        var systemName = _currentSignalNode.GetValue("关联系统变量名称").ToString();
                        var unit = _currentSignalNode.GetValue("单位").ToString();
                        var startBit = Convert.ToInt32(_currentSignalNode.GetValue("起始位"));
                        var length = Convert.ToInt32(_currentSignalNode.GetValue("长度"));
                        var byteOrder = _currentSignalNode.GetValue("字节顺序").ToString();
                        var signed = _currentSignalNode.GetValue("符号").ToString();
                        var factor = Convert.ToDecimal(_currentSignalNode.GetValue("系数"));
                        var offset = Convert.ToDecimal(_currentSignalNode.GetValue("偏移"));
                        var minMax = _currentSignalNode.GetValue("范围").ToString();
                        var orders = Convert.ToInt32(_currentSignalNode.GetValue("Orders"));

                        // 2. 插入或更新报文
                        long messageID = Convert.ToInt64(parentNode.Tag ?? -1);
                        messageID = SQLite_Service.UpsertMessage(
                            conn,
                            messageId: messageID,
                            canId: canId,
                            frameType: frameType,
                            messageName: messageName,
                            dataLength: dataLength,
                            orders: order,
                            dbcFileId: _currentDbcFileId
                        );
                        parentNode.Tag = messageID; // 更新Tag为新的MessageID

                        // 3. 插入或更新信号
                        long signalID = Convert.ToInt64(_currentSignalNode.Tag ?? -1);
                        signalID = SQLite_Service.UpsertSignal(
                            conn,
                            signalId: signalID,
                            messageId: messageID,
                            signalName: signalName,
                            multiplexSignals: multiplexSignals,
                            systemName: systemName,
                            unit: unit,
                            startBit: startBit,
                            length: length,
                            byteOrder: byteOrder,
                            signed: signed,
                            factor: factor,
                            offset: offset,
                            minMax: minMax,
                            orders: orders
                        );
                        _currentSignalNode.Tag = signalID; // 更新Tag为新的SignalID

                        // 4. 加载复用信号并编辑
                        var reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signalID);
                        _currentReuseSignals = new BindingList<ReuseSignal>(reuseSignals);

                        using (var reuseForm = new ReuseSignalForm(_currentReuseSignals))
                        {
                            if (reuseForm.ShowDialog() == DialogResult.OK)
                            {
                                _currentReuseSignals = reuseForm.reuseSignals;
                                if (_currentReuseSignals.Count == 0)
                                    _currentSignalNode.SetValue("信号名称", "");
                                else
                                    _currentSignalNode.SetValue("信号名称", "已配置");

                                // 4. 保存复用信号
                                SQLite_Service.SaveReuseSignals(conn, signalID, _currentReuseSignals);

                                transaction.Commit(); // 提交事务
                                XtraMessageBox.Show("复用信号保存成功！");
                            }
                            else
                            {
                                transaction.Rollback(); // 用户取消则回滚
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        XtraMessageBox.Show($"复用信号保存失败：{ex.Message}");
                    }
                }
            }
        }
        /* 全部展开按钮触发事件 */
        private void Expand()
        {
            treeList.ExpandAll();
        }
        /* 全部折叠按钮触发事件 */
        private void Fold()
        {
            treeList.CollapseAll();
        }
        /* 导入DBC按钮触发事件 */
        private void ImportDBC()
        {
            ClearAllNodes();                // 清空所有节点，从新打开DBC
            LoadDBCFile();                  // 加载DBC文件
            ReadDBCFileMessages();          // 打印DBC文件信息
        }
        /* 添加报文按钮触发事件 */
        private void AddNewMessage()
        {
            treeList.BeginUnboundLoad();
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();

                    // 获取最大排序值
                    int maxSortOrder = SQLite_Service.GetMaxSortOrder(conn, "Messages") + 1;

                    var newNode = treeList.AppendNode(new object[]
                    {
                        "0x", // CAN ID
                        "扩展帧", // 帧类型
                        "",
                        8,      // 数据长度
                        "", "", "", "", "", "", "", "", "","",""// 信号相关字段
                    }, null);

                    // 设置初始排序值
                    newNode.Tag = -1; // 临时标记为新节点
                    newNode.SetValue("Orders", maxSortOrder);

                    // 设置初始复选框状态
                    newNode.StateImageIndex = 1; // 默认未选中
                    newNode.Expanded = true;
                    treeList.FocusedNode = newNode;
                }
            }
            finally
            {
                treeList.EndUnboundLoad();
            }
        }
        /* 删除报文按钮触发事件 */
        private void DeleteSelectedMessages()
        {
            var parentNodesToDelete = treeList.Nodes.Cast<TreeListNode>()
                .Where(n => n.ParentNode == null && n.CheckState == CheckState.Checked)
                .ToList();

            if (parentNodesToDelete.Count == 0)
            {
                XtraMessageBox.Show("请先选择要删除的报文");
                return;
            }

            if (XtraMessageBox.Show($"确定要删除选中的{parentNodesToDelete.Count}条报文及其所有信号吗？",
                "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 收集要删除的报文ID
                        var messageIdsToDelete = new List<long>();

                        // 先处理数据库删除（倒序防止索引变化）
                        for (int i = parentNodesToDelete.Count - 1; i >= 0; i--)
                        {
                            var node = parentNodesToDelete[i];
                            long messageId = Convert.ToInt64(node.Tag ?? -1);
                            if (messageId == -1) continue;

                            // 记录要删除的报文ID
                            messageIdsToDelete.Add(messageId);

                            // 删除关联数据
                            SQLite_Service.DeleteMessage(conn, messageId);
                        }

                        transaction.Commit();

                        // 再删除界面节点（倒序删除）
                        for (int i = parentNodesToDelete.Count - 1; i >= 0; i--)
                        {
                            treeList.DeleteNode(parentNodesToDelete[i]);
                        }

                        // 重新排序剩余的报文
                        ReorderMessagesAfterDeletion(conn, messageIdsToDelete);

                        XtraMessageBox.Show($"成功删除 {parentNodesToDelete.Count} 条报文及关联信号！");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        XtraMessageBox.Show($"删除失败：{ex.Message}");
                    }
                }
            }
        }
        /* 添加信号按钮触发事件 */
        private void AddNewSignal()
        {
            try
            {
                TreeListNode parentNode = treeList.FocusedNode?.ParentNode ?? treeList.FocusedNode;
                // 验证选中的是父节点
                if (parentNode == null || parentNode.ParentNode != null)
                {
                    XtraMessageBox.Show("请先选择要添加信号的报文节点");
                    return;
                }

                treeList.BeginUnboundLoad();
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    // 获取当前报文ID
                    long messageId = Convert.ToInt64(parentNode.Tag);
                    // 获取最大排序值
                    int maxSortOrder = SQLite_Service.GetMaxSortOrder(conn, "Signals", messageId) + 1;
                    // 创建带默认值的新信号节点
                    var newNode = treeList.AppendNode(new object[]
                    {
                    "", "", "","",
                    "",             // 信号名称
                    "否",
                    "",             // 系统变量名称
                    "",             // 单位
                    0,              // 起始位
                    8,              // 长度
                    "Inter",     // 字节顺序
                    "Unsigned",     // 符号
                    1,            // 系数
                    0,            // 偏移
                    ""        // 范围
                    }, parentNode);

                    // 设置初始排序值
                    newNode.Tag = -1; // 临时标记为新节点
                    newNode.SetValue("Orders", maxSortOrder);

                    parentNode.Expanded = true;
                    treeList.FocusedNode = newNode;
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"添加信号失败：{ex.Message}");
            }
            finally
            {
                treeList.EndUnboundLoad();
            }
        }
        /* 删除信号按钮触发事件 */
        private void DeleteSelectedSignal()
        {
            var nodesToDelete = treeList.GetNodeList()
                .Where(n => n.ParentNode != null && n.CheckState == CheckState.Checked)
                .ToList();

            if (nodesToDelete.Count == 0)
            {
                XtraMessageBox.Show("请先选择要删除的信号节点");
                return;
            }

            // 按父节点分组
            var groupedByParent = nodesToDelete
                .GroupBy(n => n.ParentNode)
                .ToList();

            if (XtraMessageBox.Show($"确定要删除选中的{nodesToDelete.Count}个信号吗？",
                "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 收集要删除的信号ID
                        var signalIdsToDelete = new List<long>();

                        // 先处理数据库删除（倒序）
                        for (int i = nodesToDelete.Count - 1; i >= 0; i--)
                        {
                            var node = nodesToDelete[i];
                            long signalId = Convert.ToInt64(node.Tag ?? -1);
                            if (signalId == -1) continue;

                            // 记录要删除的信号ID
                            signalIdsToDelete.Add(signalId);

                            // 删除复用信号
                            SQLite_Service.DeleteReuseSignals(conn, signalId);

                            // 删除信号
                            SQLite_Service.DeleteSignal(conn, signalId);

                        }

                        transaction.Commit();

                        // 再删除界面节点（倒序）
                        for (int i = nodesToDelete.Count - 1; i >= 0; i--)
                        {
                            treeList.DeleteNode(nodesToDelete[i]);
                        }

                        // 重新排序每个父节点下的信号
                        foreach (var group in groupedByParent)
                        {
                            if (group.Key != null)
                            {
                                ReorderSignalsAfterDeletion(conn, group.Key);
                            }
                        }


                        XtraMessageBox.Show($"成功删除 {nodesToDelete.Count} 个信号！");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        XtraMessageBox.Show($"删除失败：{ex.Message}");
                    }
                }
            }
        }

        /* 导出Excel按钮触发事件 */
        private void ExportExcel()
        {
            using (var sfd = new SaveFileDialog { Filter = "Excel文件|*.xlsx" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var workbook = new XLWorkbook())
                        {
                            var ws = workbook.Worksheets.Add("CAN Messages");

                            // 设置全局样式
                            var style = workbook.Style;
                            style.Font.SetFontName("等线");
                            style.Font.SetFontSize(12);
                            style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                            style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

                            // 表头样式
                            var headerStyle = workbook.Style;
                            headerStyle.Font.Bold = true;
                            headerStyle.Fill.BackgroundColor = XLColor.FromHtml("#F4F4F4");
                            headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                            // 写入表头
                            for (int i = 0; i < treeList.Columns.Count; i++)
                            {
                                ws.Cell(1, i + 1).Value = treeList.Columns[i].Caption;
                            }

                            // 递归写入数据
                            int rowIndex = 2;
                            foreach (TreeListNode node in treeList.Nodes)
                            {
                                WriteNodeToExcel(ws, node, ref rowIndex);
                            }

                            // 格式优化
                            //ws.RangeUsed().Style = style;
                            ws.Columns("A").Width = 12;
                            ws.Columns("B").Width = 10;
                            ws.Columns("C").Width = 25;
                            ws.Columns("D").Width = 10;
                            ws.Columns("E").Width = 25;
                            ws.Columns("F").Width = 15;
                            ws.Columns("G").Width = 25;
                            ws.Columns("H").Width = 10;
                            ws.Columns("I").Width = 10;
                            ws.Columns("J").Width = 10;
                            ws.Columns("K").Width = 10;
                            ws.Columns("L").Width = 10;
                            ws.Columns("M").Width = 10;
                            ws.Columns("N").Width = 10;
                            ws.Columns("O").Width = 20;
                            ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                            workbook.SaveAs(sfd.FileName);
                        }
                        XtraMessageBox.Show("导出成功！");
                    }
                    catch (Exception ex)
                    {
                        XtraMessageBox.Show($"导出失败:{ex.Message}！");
                    }
                }
            }
        }
        /* 上移下移按钮触发事件 */
        private void MoveUp()
        {
            TreeListNode focusedNode = treeList.FocusedNode;
            int LastNodeIndex = treeList.GetNodeIndex(treeList.Nodes.LastNode);
            int targetNodeIndex = treeList.GetNodeIndex(treeList.FocusedNode.PrevNode);

            if (targetNodeIndex == -1)
            {
                treeList.SetNodeIndex(treeList.FocusedNode, LastNodeIndex);
                treeList.MakeNodeVisible(treeList.FocusedNode);
            }
            else
            {
                int nodeIndex = treeList.GetNodeIndex(treeList.FocusedNode);
                treeList.SetNodeIndex(treeList.FocusedNode, targetNodeIndex);
                treeList.MakeNodeVisible(treeList.FocusedNode);
            }
            // 获取节点集合
            TreeListNodes collection = focusedNode.ParentNode?.Nodes ?? treeList.Nodes;

            // 更新所有节点的Orders值
            UpdateNodeOrders(collection);
            UpdateNodeOrderInDatabase(collection);
        }
        private void MoveDown()
        {
            TreeListNode focusedNode = treeList.FocusedNode;
            int targetNodeIndex = treeList.GetNodeIndex(treeList.FocusedNode.NextNode);
            int nodeIndex = treeList.GetNodeIndex(treeList.FocusedNode);
            treeList.SetNodeIndex(treeList.FocusedNode, targetNodeIndex);
            treeList.MakeNodeVisible(treeList.FocusedNode);

            // 获取节点集合
            TreeListNodes collection = focusedNode.ParentNode?.Nodes ?? treeList.Nodes;
            // 更新所有节点的Orders值
            UpdateNodeOrders(collection);
            UpdateNodeOrderInDatabase(collection);
        }

        // 新增方法：更新节点集合中的Orders值
        private void UpdateNodeOrders(TreeListNodes nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].SetValue("Orders", i);
            }
        }

        #endregion

        #region 数据库操作
        /* 保存整个DBC配置 */
        private void SaveDBC()
        {
            if (_currentDbcFileId == -1)
            {
                SaveAsNewDBC();
            }
            else
            {
                UpdateExistingDBC();
                RefreshTreeListData();
                //RefreshCurrentSelection();
            }
        }

        /* 另存为新DBC文件 */
        private void SaveAsNewDBC()
        {
            using (var sfd = new SaveDialogForm(dbcPath))
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;

                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            _currentDbcFileId = SQLite_Service.UpsertDbcFile(conn, sfd.FileName);
                            foreach (TreeListNode messageNode in treeList.Nodes)
                            {
                                //1.保存报文信息
                                long messageId = UpdateMessage(conn, messageNode, transaction);
                                //2.保存信号信息
                                UpdateSignals(conn, messageId, messageNode, transaction);
                            }
                            transaction.Commit();
                            XtraMessageBox.Show("保存成功！");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            XtraMessageBox.Show($"保存失败: {ex.Message}");
                        }
                    }
                }
                LoadDbcFilesFromDatabase();
            }
        }

        /* 更新现有DBC文件 */
        private void UpdateExistingDBC()
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (TreeListNode msgNode in treeList.Nodes)
                        {
                            long msgId = UpdateMessage(conn, msgNode, transaction);
                            UpdateSignals(conn, msgId, msgNode, transaction);
                        }

                        transaction.Commit();
                        if (AgreementUse())
                            ConfigUpdated?.Invoke(this, EventArgs.Empty);
                        XtraMessageBox.Show($"更新成功！");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        XtraMessageBox.Show($"更新失败: {ex.Message}");
                    }
                }
            }
        }

        /* 更新/插入报文 */
        private long UpdateMessage(SQLiteConnection conn, TreeListNode node, SQLiteTransaction transaction)
        {
            var msgInfo = new MessageInfo
            {
                MessageID = node.Tag as long? ?? -1,
                CANID = node.GetValue("CAN ID").ToString(),
                FrameType = node.GetValue("帧类型").ToString(),
                MessageName = node.GetValue("消息名称").ToString(),
                DataLength = Convert.ToInt32(node.GetValue("数据长度")),
                Orders = Convert.ToInt32(node.GetValue("Orders"))
            };

            return SQLite_Service.UpsertMessage(
                conn: conn,
                messageId: msgInfo.MessageID,
                canId: msgInfo.CANID,
                frameType: msgInfo.FrameType,
                messageName: msgInfo.MessageName,
                dataLength: msgInfo.DataLength,
                orders: msgInfo.Orders,
                dbcFileId: _currentDbcFileId,
                transaction: transaction
            );
        }

        /* 更新/插入信号 */
        private void UpdateSignals(SQLiteConnection conn, long msgId, TreeListNode parentNode, SQLiteTransaction transaction)
        {
            foreach (TreeListNode signalNode in parentNode.Nodes)
            {
                var sigInfo = new SignalInfo
                {
                    SignalID = signalNode.Tag as long? ?? -1,
                    SignalName = signalNode.GetValue("信号名称").ToString(),
                    MultiplexSignals = signalNode.GetValue("是否复用信号").ToString(),
                    SystemName = signalNode.GetValue("关联系统变量名称").ToString(),
                    Unit = signalNode.GetValue("单位").ToString(),
                    StartBit = Convert.ToInt32(signalNode.GetValue("起始位")),
                    Length = Convert.ToInt32(signalNode.GetValue("长度")),
                    ByteOrder = signalNode.GetValue("字节顺序").ToString(),
                    Signed = signalNode.GetValue("符号").ToString(),
                    Factor = Convert.ToDecimal(signalNode.GetValue("系数")),
                    Offset = Convert.ToDecimal(signalNode.GetValue("偏移")),
                    MinMax = signalNode.GetValue("范围").ToString(),
                    Orders = Convert.ToInt32(signalNode.GetValue("Orders"))
                };

                long sigId = SQLite_Service.UpsertSignal(
                    conn: conn,
                    signalId: sigInfo.SignalID,
                    messageId: msgId,
                    signalName: sigInfo.SignalName,
                    multiplexSignals: sigInfo.MultiplexSignals,
                    systemName: sigInfo.SystemName,
                    unit: sigInfo.Unit,
                    startBit: sigInfo.StartBit,
                    length: sigInfo.Length,
                    byteOrder: sigInfo.ByteOrder,
                    signed: sigInfo.Signed,
                    factor: sigInfo.Factor,
                    offset: sigInfo.Offset,
                    minMax: sigInfo.MinMax,
                    orders: sigInfo.Orders,
                    transaction: transaction
                );

                signalNode.Tag = sigId;
            }
        }

        /* 更新数据库顺序 */
        private void UpdateNodeOrderInDatabase(TreeListNodes nodes)
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var node = nodes[i];

                            // 使用节点中的Orders值（确保已更新）
                            int order = Convert.ToInt32(node.GetValue("Orders"));

                            // 如果是父节点（报文）
                            if (node.ParentNode == null)
                            {
                                long messageId = Convert.ToInt64(node.Tag);
                                SQLite_Service.UpdateMessageOrder(
                                    conn, messageId, order
                                );

                            }
                            else // 子节点（信号）
                            {
                                long signalId = Convert.ToInt64(node.Tag);
                                SQLite_Service.UpdateSignalOrder(
                                    conn, signalId, i
                                );
                            }
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /* 删除后重新排序报文 */
        private void ReorderMessagesAfterDeletion(SQLiteConnection conn, List<long> deletedMessageIds)
        {
            try
            {
                // 获取所有剩余的报文（按当前排序）
                var remainingMessages = treeList.Nodes
                    .Cast<TreeListNode>()
                    .Where(n => n.ParentNode == null)
                    .ToList();

                // 更新数据库中的排序
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        for (int i = 0; i < remainingMessages.Count; i++)
                        {
                            var node = remainingMessages[i];
                            long messageId = Convert.ToInt64(node.Tag);

                            // 更新排序值
                            SQLite_Service.UpdateMessageSortOrder(conn, messageId, i);

                            // 更新节点中的排序值（可选）
                            node.SetValue("Orders", i);
                        }
                        transaction.Commit();

                        // 刷新配置
                        if (AgreementUse())
                            ConfigUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"重新排序报文失败: {ex.Message}");
            }
        }

        /* 删除后重新排序信号 */
        private void ReorderSignalsAfterDeletion(SQLiteConnection conn, TreeListNode parentNode)
        {
            try
            {
                // 获取父节点下所有剩余的信号节点
                var remainingSignals = parentNode.Nodes
                    .Cast<TreeListNode>()
                    .ToList();

                // 更新数据库中的排序
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        for (int i = 0; i < remainingSignals.Count; i++)
                        {
                            var node = remainingSignals[i];
                            long signalId = Convert.ToInt64(node.Tag);

                            // 更新排序值
                            SQLite_Service.UpdateSignalSortOrder(conn, signalId, i);

                            // 更新节点中的排序值（可选）
                            node.SetValue("Orders", i);
                        }
                        transaction.Commit();

                        // 刷新配置
                        if (AgreementUse())
                            ConfigUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"重新排序信号失败: {ex.Message}");
            }
        }

        #endregion

        #region 其他方法
        /* 初始化UI组件 */
        private void InitializeUI()
        {
            // 初始化Grid
            gridControl = new GridControl { Dock = DockStyle.Left, Width = 350 };
            gridView = new GridView();
            gridControl.MainView = gridView;
            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridView.Columns.AddRange(new[]
            {
                new GridColumn { FieldName = "DBC文件名称", Caption = "DBC文件名称", Width = 100, Visible = true,OptionsColumn = {AllowEdit = false}},
                new GridColumn { FieldName = "创建时间", Caption = "创建时间", Width = 220, Visible = true,OptionsColumn = {AllowEdit = false}}
            });

            // 初始化TreeList
            treeList = new TreeList
            {
                Parent = this,
                Location = new Point(355, 0),
                Size = new Size(1440, 850)
            };
            InitializeTreeColumns();

            // 添加右击菜单
            InitializeContextMenu();

            // 添加操作按钮
            var btnPanel = CreateButtonPanel();
            Controls.AddRange(new Control[] { gridControl, btnPanel, treeList });
        }

        /* 初始化树形列表列 */
        private void InitializeTreeColumns()
        {
            treeList.Columns.AddRange(new[] {
                new TreeListColumn { Caption = "CAN ID", VisibleIndex = 0, Width = 120 },
                new TreeListColumn { Caption = "帧类型", VisibleIndex = 1, Width = 50 },
                new TreeListColumn { Caption = "消息名称", VisibleIndex = 2, Width = 100 },
                new TreeListColumn { Caption = "数据长度", VisibleIndex = 3, Width = 60 },
                new TreeListColumn { Caption = "信号名称", VisibleIndex = 4,Width = 100 },
                new TreeListColumn { Caption = "是否复用信号", VisibleIndex = 5,Width = 100 },
                new TreeListColumn { Caption = "关联系统变量名称", VisibleIndex = 6, Width = 130 },
                new TreeListColumn { Caption = "单位", VisibleIndex = 7, Width = 50 },
                new TreeListColumn { Caption = "起始位", VisibleIndex = 8,Width = 50 },
                new TreeListColumn { Caption = "长度", VisibleIndex = 9, Width = 50 },
                new TreeListColumn { Caption = "字节顺序", VisibleIndex = 10, Width = 80 },
                new TreeListColumn { Caption = "符号", VisibleIndex = 11, Width = 80 },
                new TreeListColumn { Caption = "系数", VisibleIndex = 12, Width = 80 },
                new TreeListColumn { Caption = "偏移", VisibleIndex = 13, Width = 80 },
                new TreeListColumn { Caption = "范围", VisibleIndex = 14, Width = 100 },
                new TreeListColumn { Caption = "Orders", VisibleIndex = treeList.Columns.Count, Visible = false } //顺序列，不显示
            });

            // 初始化下拉框
            repoFrameType = new RepositoryItemComboBox
            {
                TextEditStyle = TextEditStyles.DisableTextEditor,
                Items = { "标准帧", "扩展帧" }
            };
            repoMultiplexSignals = new RepositoryItemComboBox
            {
                TextEditStyle = TextEditStyles.DisableTextEditor,
                Items = { "是", "否" }
            };
            // 字节顺序下拉框
            repoByteOrder = new RepositoryItemComboBox
            {
                TextEditStyle = TextEditStyles.DisableTextEditor,
                Items = { "Motorola", "Inter" }
            };
            // 符号下拉框
            repoSigned = new RepositoryItemComboBox
            {
                TextEditStyle = TextEditStyles.DisableTextEditor,
                Items = { "Signed", "Unsigned" }
            };
            //创建输入文本
            repositoryTextEdit = new RepositoryItemTextEdit();

            // 注册到TreeList
            treeList.RepositoryItems.AddRange(new RepositoryItem[]
            {
                repoFrameType,
                repoMultiplexSignals,
                repoByteOrder,
                repoSigned,
                repositoryTextEdit
            });

            // 启用默认复选框功能
            treeList.OptionsView.ShowCheckBoxes = true;
            treeList.OptionsView.CheckBoxStyle = DefaultNodeCheckBoxStyle.Check;

            // 注册事件
            treeList.CustomNodeCellEdit += TreeList_CustomNodeCellEdit;
            treeList.ShowingEditor += TreeList_ShowingEditor;
            treeList.AfterCheckNode += TreeList_AfterCheckNode;
            treeList.MouseDoubleClick += TreeList_MouseDoubleClick;
        }

        /* 创建右击菜单栏 */
        private void InitializeContextMenu()
        {
            // 添加右键菜单
            gridContextMenu = new ContextMenuStrip();
            deleteItem = new ToolStripMenuItem("删除");
            copyItem = new ToolStripMenuItem("复制");
            pasteItem = new ToolStripMenuItem("粘贴");
            deleteItem.Click += DeleteMenuItem_Click;
            copyItem.Click += CopyMenuItem_Click;
            pasteItem.Click += PasteMenuItem_Click;

            gridContextMenu.Items.AddRange(new ToolStripItem[] { deleteItem, copyItem, pasteItem });
        }

        /* 创建操作按钮面板 */
        private PanelControl CreateButtonPanel()
        {
            var panel = new PanelControl { Dock = DockStyle.Right, Width = 120 };
            var btnexpand = new SimpleButton
            {
                Text = "全部展开",
                Width = 100,
                Height = 30,
                Location = new Point(10, 30)
            };
            var btnfold = new SimpleButton
            {
                Text = "全部折叠",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnexpand.Bottom + 20)
            };
            var btnImport = new SimpleButton
            {
                Text = "导入DBC",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnfold.Bottom + 20)
            };
            var btnImportExcel = new SimpleButton
            {
                Text = "导入Excel",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnImport.Bottom + 20)
            };
            var btnAddMessage = new SimpleButton
            {
                Text = "添加报文",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnImportExcel.Bottom + 20)
            };
            var btnDeleteMessage = new SimpleButton
            {
                Text = "删除报文",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnAddMessage.Bottom + 20)
            };
            var btnAddSignal = new SimpleButton
            {
                Text = "添加信号",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnDeleteMessage.Bottom + 20)
            };
            var btnDeleteSignal = new SimpleButton
            {
                Text = "删除信号",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnAddSignal.Bottom + 20)
            };
            var btnSave = new SimpleButton
            {
                Text = "保存文件",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnDeleteSignal.Bottom + 20)
            };
            var btnExport = new SimpleButton
            {
                Text = "导出Excel",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnSave.Bottom + 20)
            };
            var btnMoveUp = new SimpleButton
            {
                Text = "上移",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnExport.Bottom + 20)
            };
            var btnMoveDown = new SimpleButton
            {
                Text = "下移",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnMoveUp.Bottom + 20)
            };

            //绑定事件
            btnexpand.Click += (s, e) => Expand();
            btnfold.Click += (s, e) => Fold();
            btnImport.Click += (s, e) => ImportDBC();
            //btnImportExcel.Click += (s, e) => ImportExcelDBC();
            btnAddMessage.Click += (s, e) => AddNewMessage();
            btnDeleteMessage.Click += (s, e) => DeleteSelectedMessages();
            btnAddSignal.Click += (s, e) => AddNewSignal();
            btnDeleteSignal.Click += (s, e) => DeleteSelectedSignal();
            btnSave.Click += (s, e) => SaveDBC();
            btnExport.Click += (s, e) => ExportExcel();
            btnMoveUp.Click += (s, e) => MoveUp();
            btnMoveDown.Click += (s, e) => MoveDown(); ;

            panel.Controls.AddRange(new Control[] { btnMoveUp, btnMoveDown, btnexpand, btnfold, btnImport, btnImportExcel, btnAddMessage, btnDeleteMessage, btnAddSignal, btnDeleteSignal, btnSave, btnExport });
            return panel;
        }

        #endregion

        #region 辅助方法

        /* 递归写入节点数据 */
        private void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex)
        {
            // 遍历所有列获取值
            for (int i = 0; i < treeList.Columns.Count; i++)
            {
                var cell = ws.Cell(rowIndex, i + 1);
                var value = node.GetValue(treeList.Columns[i]);

                if (decimal.TryParse(value?.ToString(), out decimal num))
                {
                    cell.Value = num;
                }
                else
                {
                    cell.Value = value?.ToString()?.Trim();
                }
            }

            // 递归处理子节点
            foreach (TreeListNode childNode in node.Nodes)
            {
                rowIndex++;
                WriteNodeToExcel(ws, childNode, ref rowIndex);
            }
        }

        /* 显示DBC内容 */
        private void PrintfDBCMessage(IntPtr ptrMsg)
        {
            // 消息
            ZDBC.DBCMessage msg = new ZDBC.DBCMessage();
            msg = (ZDBC.DBCMessage)Marshal.PtrToStructure(ptrMsg, typeof(ZDBC.DBCMessage));
            string str_name = new string(Encoding.ASCII.GetChars(msg.strName));
            str_name = str_name.Substring(0, str_name.IndexOf('\0'));
            // 添加父节点
            var parentNode = treeList.AppendNode(new object[]
            {
                $"0x{msg.nID.ToString("X")}",
                msg.nExtend != 0 ? "扩展帧" : "标准帧",
                str_name,
                msg.nSize,
                "", "", "", "", "","","","","","",""
            }, null);

            for (int i = 0; i < msg.nSignalCount; i++)
            {
                ZDBC.DBCSignal curSig = msg.vSignals[i];
                string signal_name = new string(Encoding.ASCII.GetChars(curSig.strName));
                signal_name = signal_name.Substring(0, signal_name.IndexOf('\0'));
                string unit = new string(Encoding.ASCII.GetChars(curSig.unit));
                unit = unit.Substring(0, unit.IndexOf('\0'));
                treeList.AppendNode(new object[]
                {
                    "","","","",
                    signal_name,
                    "否",
                    "",
                    unit,
                    msg.vSignals[i].nStartBit,
                    msg.vSignals[i].nLen,
                    msg.vSignals[i].is_motorola != 0 ? "Motorola" : "Inter",
                    msg.vSignals[i].is_signed != 0 ? "Signed" : "Unsigned",
                    ((decimal)msg.vSignals[i].nFactor).ToString(CultureInfo.InvariantCulture),
                    ((decimal)msg.vSignals[i].nOffset).ToString(CultureInfo.InvariantCulture),
                    $"{msg.vSignals[i].nMin + "～" + msg.vSignals[i].nMax}",
                }, parentNode);
            }
            treeList.EndUnboundLoad();
            treeList.ExpandAll(); // 默认展开所有节点
        }

        /* 解析DBC文件信息 */
        private void ReadDBCFileMessages()
        {
            uint count = ZDBC.ZDBC_GetMessageCount(DBCHandle);  //信号数量
            ZDBC.DBCMessage msg = new ZDBC.DBCMessage();
            IntPtr ptrMsg = Marshal.AllocHGlobal(Marshal.SizeOf(msg));

            if (ZDBC.ZDBC_GetFirstMessage(DBCHandle, ptrMsg))
            {
                PrintfDBCMessage(ptrMsg);
            }
            while (ZDBC.ZDBC_GetNextMessage(DBCHandle, ptrMsg))
            {
                PrintfDBCMessage(ptrMsg);
            }

            Marshal.FreeHGlobal(ptrMsg);
        }

        /* 加载DBC文件 */
        private void LoadDBCFile()
        {
            using (var ofd = new OpenFileDialog { Filter = "DBC文件|*.dbc" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // 加载DBC文件
                    DBCHandle = ZDBC.ZDBC_Init();
                    IntPtr P2DBCFileAddress = Marshal.StringToHGlobalAnsi(ofd.FileName);
                    bool Result = ZDBC.ZDBC_LoadFile(DBCHandle, P2DBCFileAddress);
                    Marshal.FreeHGlobal(P2DBCFileAddress);
                }
            }
        }

        /* 删除所有节点 */
        public void ClearAllNodes()
        {
            // 确认控件存在且未被释放
            if (treeList == null || treeList.IsDisposed) return;

            // 开始批量操作（防止界面闪烁）
            treeList.BeginUpdate();
            try
            {
                // 方法一：直接清除所有节点（推荐）
                treeList.ClearNodes();

                // 或者方法二：递归删除（适用于需要额外处理的情况）
                // while(treeList.Nodes.Count > 0)
                // {
                //     treeList.DeleteNode(treeList.Nodes[0]);
                // }

                // 清除关联数据（如果需要）
                treeList.DataSource = null;
                _currentDbcFileId = -1;
            }
            finally
            {
                // 结束批量操作
                treeList.EndUpdate();
            }
        }

        /* 刷新当前选择 */
        private void RefreshCurrentSelection()
        {
            int currentRow = gridView.FocusedRowHandle;
            LoadDbcFilesFromDatabase();
            gridView.FocusedRowHandle = currentRow;
            RefreshTreeListData();
        }

        /* 刷新树形数据 */
        private void RefreshTreeListData()
        {
            treeList.BeginUpdate();
            try
            {
                treeList.ClearNodes();
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    var messages = SQLite_Service.GetMessagesByDbc(conn, _currentDbcFileId);
                    LoadMessagesAndSignals(conn, messages);
                }
            }
            finally
            {
                treeList.EndUpdate();
                //treeList.ExpandAll();
            }
        }

        /* 递归设置子节点勾选状态 */
        private void SetChildrenCheckState(TreeListNode parentNode, CheckState checkState)
        {
            foreach (TreeListNode childNode in parentNode.Nodes)
            {
                childNode.CheckState = checkState;
                // 如果子节点还有子节点，继续递归
                if (childNode.HasChildren)
                {
                    SetChildrenCheckState(childNode, checkState);
                }
            }
        }

        private bool AgreementUse()
        {
            DataRow row = gridView.GetDataRow(gridView.FocusedRowHandle);
            string dbcFileName = row["DBC文件名称"].ToString();
            return
                agreementList.Exists(e => e.CommunicationProtocols == dbcFileName);
        }

        public void UpdateAgreements(List<EquipmentModel> equipmentList)
        {
            agreementList = new List<EquipmentModel>(equipmentList);
            this.Controls.Clear(); // 清除当前控件
            InitializeUI();       // 重新生成界面
            LoadDbcFilesFromDatabase();
            ConfigureGridSelection();
            AutoSelectFirstRow();
        }

        #endregion

    }
}