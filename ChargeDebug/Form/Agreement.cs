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
    /// <summary>
    /// 协议配置界面 - 支持多种协议类型（CAN总线、Modbus等）的动态显示和配置
    /// </summary>
    public partial class Agreement : XtraUserControl
    {
        #region 字段和属性
        private TreeList treeList;                          // 树形列表控件，用于显示协议结构
        private string dbcPath = "";                        // 数据库文件路径
        private static uint DBCHandle = 0;                  // DBC文件句柄
        private long _currentDbcFileId = -1;                // 当前选中的DBC文件ID
        private GridControl gridControl;                    // 网格控件，用于显示文件列表
        private GridView gridView;                          // 网格视图，用于显示文件列表
        private BindingList<ReuseSignal> _currentReuseSignals; // 当前选中的复用信号列表
        private TreeListNode _currentSignalNode;            // 当前选中的信号节点
        private List<EquipmentModel> agreementList;         // 设备模型列表
        private Dictionary<long, List<ReuseSignal>> _reuseSignalsCache = new Dictionary<long, List<ReuseSignal>>(); // 复用信号缓存

        // 协议处理器 - 根据协议类型动态处理数据
        private IProtocolHandler _currentProtocolHandler;

        // 上下文菜单组件
        private ContextMenuStrip gridContextMenu;
        private ToolStripMenuItem newlyItem;
        private ToolStripMenuItem deleteItem;
        private ToolStripMenuItem copyItem;
        private ToolStripMenuItem pasteItem;

        // 事件 - 当配置更新时触发
        public event EventHandler ConfigUpdated;
        #endregion

        #region 初始化
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbPath">数据库文件路径</param>
        /// <param name="equipmentList">设备列表</param>
        public Agreement(string dbPath, List<EquipmentModel> equipmentList)
        {
            dbcPath = dbPath;
            agreementList = new List<EquipmentModel>(equipmentList);
            InitializeComponent();
            InitializeUI();
            this.Load += Agreement_Load;
        }

        /// <summary>
        /// 窗体加载事件处理
        /// </summary>
        private void Agreement_Load(object? sender, EventArgs e)
        {
            LoadDbcFilesFromDatabase();
            ConfigureGridSelection();
            AutoSelectFirstRow();
        }

        /// <summary>
        /// 自动选择第一行数据
        /// </summary>
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
        /// <summary>
        /// 从数据库加载DBC文件列表到Grid
        /// </summary>
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

        /// <summary>
        /// 绑定数据到Grid
        /// </summary>
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

        /// <summary>
        /// 加载选中DBC的报文信号数据
        /// </summary>
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
                    _currentDbcFileId = SQLite_Service.GetDbcFileId(conn, row["文件名称"].ToString());

                    // 获取协议类型
                    string protocolType = SQLite_Service.GetProtocolType(conn, _currentDbcFileId);

                    // 根据协议类型选择处理器
                    SetProtocolHandler(protocolType);

                    // 使用协议处理器加载数据
                    _currentProtocolHandler.LoadData(conn, _currentDbcFileId, treeList);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"加载树数据失败: {ex.Message}");
            }
            finally
            {
                treeList.EndUnboundLoad();
            }
        }

        /// <summary>
        /// 根据协议类型设置对应的处理器
        /// </summary>
        private void SetProtocolHandler(string protocolType)
        {
            // 根据协议类型选择处理器并初始化UI
            if (protocolType == "CAN总线")
            {
                _currentProtocolHandler = new CanProtocolHandler();
            }
            else if (protocolType == "Modbus")
            {
                _currentProtocolHandler = new ModbusProtocolHandler();
            }
            else
            {
                // 默认使用CAN处理器
                _currentProtocolHandler = new CanProtocolHandler();
            }

            // 初始化编辑器
            _currentProtocolHandler.InitializeEditors();

            // 初始化对应的UI列
            _currentProtocolHandler.InitializeTreeColumns(treeList);

            // 将编辑器添加到TreeList
            InitializeTreeListEditors(protocolType);
        }

        private void InitializeTreeListEditors(string protocolType)
        {
            treeList.RepositoryItems.Clear();

            if (protocolType == "CAN总线" && _currentProtocolHandler is CanProtocolHandler canHandler)
            {
                treeList.RepositoryItems.AddRange(new RepositoryItem[] {
                    canHandler.RepoFrameType,
                    canHandler.RepoMultiplexSignals,
                    canHandler.RepoByteOrder,
                    canHandler.RepoSigned,
                    canHandler.RepositoryTextEdit
                });
            }
            else if (protocolType == "Modbus" && _currentProtocolHandler is ModbusProtocolHandler modbusHandler)
            {
                // 类似处理Modbus编辑器
                //treeList.RepositoryItems.AddRange(new RepositoryItem[] {
                //    modbusHandler.RepoRegisterType,
                //    modbusHandler.RepoDataType,
                //    modbusHandler.RepositoryTextEdit
                //});
            }
        }

        #endregion

        #region UI事件处理
        /// <summary>
        /// 配置Grid选择事件
        /// </summary>
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

                //newlyItem.Enabled = hasSelection;
                deleteItem.Enabled = hasSelection;
                copyItem.Enabled = hasSelection;
                pasteItem.Enabled = clipValid;
            };
        }

        /// <summary>
        /// 删除菜单项点击事件
        /// </summary>
        private void DeleteMenuItem_Click(object? sender, EventArgs e)
        {
            int rowHandle = gridView.FocusedRowHandle;
            if (rowHandle >= 0)
            {
                DataRow row = gridView.GetDataRow(rowHandle);
                if (row != null)
                {
                    string? fileName = row["文件名称"].ToString();
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

        /// <summary>
        /// 复制菜单项点击事件
        /// </summary>
        private void CopyMenuItem_Click(object? sender, EventArgs e)
        {
            int rowHandle = gridView.FocusedRowHandle;
            if (rowHandle >= 0)
            {
                DataRow row = gridView.GetDataRow(rowHandle);
                if (row != null)
                {
                    string? fileName = row["文件名称"].ToString();
                    if (fileName != null)
                        Clipboard.SetText(fileName);
                }
            }
        }

        /// <summary>
        /// 粘贴菜单项点击事件
        /// </summary>
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
                                    long newFileId = SQLite_Service.UpsertDbcFile(conn, sfd.FileName, sfd.ProtocolType);

                                    // 使用协议处理器复制数据
                                    _currentProtocolHandler.CopyData(conn, sourceFileId, newFileId, transaction);

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

        /// <summary>
        /// TreeList单元格内容改变时触发事件
        /// </summary>
        private void TreeList_CustomNodeCellEdit(object sender, GetCustomNodeCellEditEventArgs e)
        {
            // 使用协议处理器处理编辑器选择
            _currentProtocolHandler.CustomNodeCellEdit(e);
        }

        /// <summary>
        /// TreeList单元格编辑触发事件
        /// </summary>
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

            // 使用协议处理器决定是否允许编辑
            e.Cancel = !_currentProtocolHandler.AllowEdit(focusedNode, focusedColumn);
        }

        /// <summary>
        /// TreeList复选框勾选触发事件
        /// </summary>
        private void TreeList_AfterCheckNode(object sender, NodeEventArgs e)
        {
            // 仅处理父节点的勾选状态变化
            if (e.Node.ParentNode != null) return;

            // 获取当前节点的勾选状态
            CheckState parentState = e.Node.CheckState;

            // 递归设置所有子节点状态
            SetChildrenCheckState(e.Node, parentState);
        }

        /// <summary>
        /// TreeList双击单元格触发事件
        /// </summary>
        private void TreeList_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            TreeListHitInfo hitInfo = treeList.CalcHitInfo(e.Location);
            if (hitInfo.HitInfoType != HitInfoType.Cell || hitInfo.Node == null) return;

            // 使用协议处理器处理双击事件
            _currentProtocolHandler.HandleDoubleClick(hitInfo, treeList, dbcPath, _currentDbcFileId);
        }

        /// <summary>
        /// 全部展开按钮触发事件
        /// </summary>
        private void Expand()
        {
            treeList.ExpandAll();
        }

        /// <summary>
        /// 全部折叠按钮触发事件
        /// </summary>
        private void Fold()
        {
            treeList.CollapseAll();
        }

        /// <summary>
        /// 导入DBC按钮触发事件
        /// </summary>
        private void ImportDBC()
        {
            ClearAllNodes();                // 清空所有节点，从新打开DBC
            LoadDBCFile();                  // 加载DBC文件
            ReadDBCFileMessages();          // 打印DBC文件信息
        }

        /// <summary>
        /// 添加报文按钮触发事件
        /// </summary>
        private void AddNewMessage()
        {
            // 使用协议处理器添加新项
            _currentProtocolHandler.AddNewItem(treeList, dbcPath);
        }

        /// <summary>
        /// 删除报文按钮触发事件
        /// </summary>
        private void DeleteSelectedMessages()
        {
            // 使用协议处理器删除选中项
            _currentProtocolHandler.DeleteSelectedItems(treeList, dbcPath, () => {
                if (AgreementUse())
                    ConfigUpdated?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// 添加信号按钮触发事件
        /// </summary>
        private void AddNewSignal()
        {
            // 使用协议处理器添加新子项
            _currentProtocolHandler.AddNewChildItem(treeList, dbcPath);
        }

        /// <summary>
        /// 删除信号按钮触发事件
        /// </summary>
        private void DeleteSelectedSignal()
        {
            // 使用协议处理器删除选中子项
            _currentProtocolHandler.DeleteSelectedChildItems(treeList, dbcPath, () => {
                if (AgreementUse())
                    ConfigUpdated?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// 导入Excel按钮触发事件
        /// </summary>
        private void ImportExcelDBC()
        {
            ClearAllNodes();
            _reuseSignalsCache.Clear(); // 清空缓存

            using (var ofd = new OpenFileDialog { Filter = "Excel文件|*.xlsx;*.xls" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    // 使用协议处理器导入Excel
                    _currentProtocolHandler.ImportExcel(treeList, ofd.FileName, ref _reuseSignalsCache);
                    treeList.ExpandAll();
                    XtraMessageBox.Show($"成功导入 {treeList.Nodes.Count} 条数据！");
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"导入失败: {ex.Message}");
                }
                finally
                {
                    treeList.EndUnboundLoad();
                }
            }
        }

        /// <summary>
        /// 导出Excel按钮触发事件
        /// </summary>
        private void ExportExcel()
        {
            using (var sfd = new SaveFileDialog { Filter = "Excel文件|*.xlsx" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // 使用协议处理器导出Excel
                        _currentProtocolHandler.ExportExcel(treeList, sfd.FileName, dbcPath);
                        XtraMessageBox.Show("导出成功！");
                    }
                    catch (Exception ex)
                    {
                        XtraMessageBox.Show($"导出失败:{ex.Message}！");
                    }
                }
            }
        }

        /// <summary>
        /// 上移按钮触发事件
        /// </summary>
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

        /// <summary>
        /// 下移按钮触发事件
        /// </summary>
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

        /// <summary>
        /// 更新节点集合中的Orders值
        /// </summary>
        private void UpdateNodeOrders(TreeListNodes nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].SetValue("Orders", i);
            }
        }
        #endregion

        #region 数据库操作
        /// <summary>
        /// 保存整个DBC配置
        /// </summary>
        private void SaveDBC()
        {
            if (_currentDbcFileId == -1)
            {
                // 检查是否有选中的文件
                if (gridView.FocusedRowHandle < 0)
                {
                    XtraMessageBox.Show("请先在左侧列表中选择要覆盖的文件");
                    return;
                }

                DataRow row = gridView.GetDataRow(gridView.FocusedRowHandle);
                if (row == null) return;

                // 获取文件名
                string fileName = row["文件名称"].ToString();

                // 弹出对话框询问用户（显示文件名）
                DialogResult result = XtraMessageBox.Show(
                    $"是否覆盖现有文件 '{fileName}'？",  // 添加文件名到提示信息
                    "保存选项",
                    MessageBoxButtons.YesNoCancel
                );

                switch (result)
                {
                    case DialogResult.Yes:
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            // 获取选中文件的ID
                            _currentDbcFileId = SQLite_Service.GetDbcFileId(conn, fileName);

                            // 执行覆盖操作
                            SQLite_Service.DeleteDbcFileMessages(conn, _currentDbcFileId);
                            UpdateExistingDBC();
                            RefreshTreeListData();
                        }
                        break;

                    case DialogResult.No:
                        // 另存为新文件
                        SaveAsNewDBC();
                        break;

                    case DialogResult.Cancel:
                        // 取消操作
                        break;
                }
            }
            else
            {
                UpdateExistingDBC();
                RefreshTreeListData();
            }
        }

        /// <summary>
        /// 另存为新DBC文件
        /// </summary>
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
                            _currentDbcFileId = SQLite_Service.UpsertDbcFile(conn, sfd.FileName, sfd.ProtocolType);

                            // 使用协议处理器保存数据
                            _currentProtocolHandler.SaveData(conn, _currentDbcFileId, treeList, transaction, ref _reuseSignalsCache);

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

        /// <summary>
        /// 更新现有DBC文件
        /// </summary>
        private void UpdateExistingDBC()
        {
            using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 使用协议处理器更新数据
                        _currentProtocolHandler.SaveData(conn, _currentDbcFileId, treeList, transaction, ref _reuseSignalsCache);

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

        /// <summary>
        /// 更新数据库顺序
        /// </summary>
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

                            // 使用协议处理器更新排序
                            _currentProtocolHandler.UpdateOrder(conn, node, order, transaction);
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
        #endregion

        #region 其他方法
        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUI()
        {
            // 初始化Grid
            gridControl = new GridControl { Dock = DockStyle.Left, Width = 450 };
            gridView = new GridView();
            gridControl.MainView = gridView;
            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            gridView.Columns.AddRange(new[]
            {
                new GridColumn { FieldName = "文件名称", Caption = "文件名称", Width = 100, Visible = true,OptionsColumn = {AllowEdit = false}},
                new GridColumn { FieldName = "协议类型", Caption = "协议类型", Width = 100, Visible = true,OptionsColumn = {AllowEdit = false}},
                new GridColumn { FieldName = "创建时间", Caption = "创建时间", Width = 220, Visible = true,OptionsColumn = {AllowEdit = false}}
            });

            // 初始化TreeList
            treeList = new TreeList
            {
                Parent = this,
                Location = new Point(455, 0),
                Size = new Size(1340, 850)
            };

            // 启用默认复选框功能
            treeList.OptionsView.ShowCheckBoxes = true;
            treeList.OptionsView.CheckBoxStyle = DefaultNodeCheckBoxStyle.Check;

            // 注册事件
            treeList.CustomNodeCellEdit += TreeList_CustomNodeCellEdit;
            treeList.ShowingEditor += TreeList_ShowingEditor;
            treeList.AfterCheckNode += TreeList_AfterCheckNode;
            treeList.MouseDoubleClick += TreeList_MouseDoubleClick;

            // 添加右击菜单
            InitializeContextMenu();

            // 添加操作按钮
            var btnPanel = CreateButtonPanel();
            Controls.AddRange(new Control[] { gridControl, btnPanel, treeList });
        }

        /// <summary>
        /// 创建右击菜单栏
        /// </summary>
        private void InitializeContextMenu()
        {
            // 添加右键菜单
            gridContextMenu = new ContextMenuStrip();
            newlyItem = new ToolStripMenuItem("新建文件");
            deleteItem = new ToolStripMenuItem("删除文件");
            copyItem = new ToolStripMenuItem("复制文件");
            pasteItem = new ToolStripMenuItem("粘贴文件");
            newlyItem.Click += NewMenuItem_Click;
            deleteItem.Click += DeleteMenuItem_Click;
            copyItem.Click += CopyMenuItem_Click;
            pasteItem.Click += PasteMenuItem_Click;

            gridContextMenu.Items.AddRange(new ToolStripItem[] { newlyItem, deleteItem, copyItem, pasteItem });
        }

        /// <summary>
        /// 新建菜单项点击事件
        /// </summary>
        private void NewMenuItem_Click(object? sender, EventArgs e)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();

                    // 使用SaveDialogForm获取新文件名
                    using (var sfd = new SaveDialogForm(dbcPath))
                    {
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            // 在数据库中创建新DBC文件记录
                            long newFileId = SQLite_Service.UpsertDbcFile(conn, sfd.FileName, sfd.ProtocolType);

                            // 清空当前TreeList，准备编辑新文件
                            treeList.BeginUnboundLoad();
                            treeList.ClearNodes();
                            treeList.EndUnboundLoad();

                            // 设置当前文件ID
                            _currentDbcFileId = newFileId;

                            // 刷新左侧文件列表
                            LoadDbcFilesFromDatabase();

                            // 选中新创建的文件
                            SelectNewlyCreatedFile(sfd.FileName);

                            XtraMessageBox.Show($"已成功创建新文件：{sfd.FileName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"创建新文件失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 辅助方法：选中新创建的文件
        /// </summary>
        private void SelectNewlyCreatedFile(string fileName)
        {
            for (int i = 0; i < gridView.RowCount; i++)
            {
                DataRow row = gridView.GetDataRow(i);
                if (row != null && row["文件名称"].ToString() == fileName)
                {
                    gridView.FocusedRowHandle = i;
                    break;
                }
            }
        }

        /// <summary>
        /// 创建操作按钮面板
        /// </summary>
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
                Text = "添加项",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnImportExcel.Bottom + 20)
            };
            var btnDeleteMessage = new SimpleButton
            {
                Text = "删除项",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnAddMessage.Bottom + 20)
            };
            var btnAddSignal = new SimpleButton
            {
                Text = "添加子项",
                Width = 100,
                Height = 30,
                Location = new Point(10, btnDeleteMessage.Bottom + 20)
            };
            var btnDeleteSignal = new SimpleButton
            {
                Text = "删除子项",
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
            btnImportExcel.Click += (s, e) => ImportExcelDBC();
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

        /// <summary>
        /// 递归写入节点数据
        /// </summary>
        private void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex)
        {
            // 使用协议处理器写入Excel数据
            _currentProtocolHandler.WriteNodeToExcel(ws, node, ref rowIndex, dbcPath);
        }

        /// <summary>
        /// 解析复用信号
        /// </summary>
        private List<ReuseSignal> ParseReuseSignals(string input)
        {
            var signals = new List<ReuseSignal>();

            if (string.IsNullOrWhiteSpace(input))
                return signals;

            // 格式：描述1=值1;描述2=值2
            var entries = input.Split('\n');
            foreach (var entry in entries)
            {
                var parts = entry.Split('=');
                if (parts.Length == 2)
                {
                    signals.Add(new ReuseSignal
                    {
                        Description = parts[0].Trim(),
                        Value = parts[1].Trim()
                    });
                }
            }

            return signals;
        }

        /// <summary>
        /// 辅助方法：安全转换整数
        /// </summary>
        private int TryParseInt(string value)
        {
            return int.TryParse(value, out int result) ? result : 0;
        }

        /// <summary>
        /// 辅助方法：安全转换小数
        /// </summary>
        private decimal TryParseDecimal(string value)
        {
            return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 显示DBC内容
        /// </summary>
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

        /// <summary>
        /// 解析DBC文件信息
        /// </summary>
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

        /// <summary>
        /// 加载DBC文件
        /// </summary>
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

        /// <summary>
        /// 删除所有节点
        /// </summary>
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

        /// <summary>
        /// 刷新当前选择
        /// </summary>
        private void RefreshCurrentSelection()
        {
            int currentRow = gridView.FocusedRowHandle;
            LoadDbcFilesFromDatabase();
            gridView.FocusedRowHandle = currentRow;
            RefreshTreeListData();
        }

        /// <summary>
        /// 刷新树形数据
        /// </summary>
        private void RefreshTreeListData()
        {
            treeList.BeginUpdate();
            try
            {
                treeList.ClearNodes();
                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    // 使用协议处理器加载数据
                    _currentProtocolHandler.LoadData(conn, _currentDbcFileId, treeList);
                }
            }
            finally
            {
                treeList.EndUpdate();
            }
        }

        /// <summary>
        /// 递归设置子节点勾选状态
        /// </summary>
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

        /// <summary>
        /// 检查当前协议是否被使用
        /// </summary>
        private bool AgreementUse()
        {
            DataRow row = gridView.GetDataRow(gridView.FocusedRowHandle);
            string dbcFileName = row["文件名称"].ToString();
            return agreementList.Exists(e => e.CommunicationProtocols == dbcFileName);
        }

        /// <summary>
        /// 更新协议列表
        /// </summary>
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

        #region 协议处理器接口和实现

        /// <summary>
        /// 协议处理器接口 - 定义不同协议的处理方式
        /// </summary>
        public interface IProtocolHandler
        {
            void InitializeEditors();
            void LoadData(SQLiteConnection conn, long dbcFileId, TreeList treeList);
            void InitializeTreeColumns(TreeList treeList);
            void CustomNodeCellEdit(GetCustomNodeCellEditEventArgs e);
            bool AllowEdit(TreeListNode focusedNode, TreeListColumn focusedColumn);
            void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath, long currentDbcFileId);
            void AddNewItem(TreeList treeList, string dbcPath);
            void DeleteSelectedItems(TreeList treeList, string dbcPath, Action callback);
            void AddNewChildItem(TreeList treeList, string dbcPath);
            void DeleteSelectedChildItems(TreeList treeList, string dbcPath, Action callback);
            void ImportExcel(TreeList treeList, string fileName, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache);
            void ExportExcel(TreeList treeList, string fileName, string dbcPath);
            void SaveData(SQLiteConnection conn, long dbcFileId, TreeList treeList, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache);
            void UpdateOrder(SQLiteConnection conn, TreeListNode node, int order, SQLiteTransaction transaction);
            void CopyData(SQLiteConnection conn, long sourceFileId, long newFileId, SQLiteTransaction transaction);
            void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath);
        }

        /// <summary>
        /// CAN总线协议处理器
        /// </summary>
        public class CanProtocolHandler : IProtocolHandler
        {
            // 声明自己的编辑器实例
            private RepositoryItemComboBox _repoFrameType;
            private RepositoryItemComboBox _repoMultiplexSignals;
            private RepositoryItemComboBox _repoByteOrder;
            private RepositoryItemComboBox _repoSigned;
            private RepositoryItemTextEdit _repositoryTextEdit;

            // 提供对编辑器的访问属性
            public RepositoryItemComboBox RepoFrameType => _repoFrameType;
            public RepositoryItemComboBox RepoMultiplexSignals => _repoMultiplexSignals;
            public RepositoryItemComboBox RepoByteOrder => _repoByteOrder;
            public RepositoryItemComboBox RepoSigned => _repoSigned;
            public RepositoryItemTextEdit RepositoryTextEdit => _repositoryTextEdit;

            public void InitializeEditors()
            {
                _repoFrameType = new RepositoryItemComboBox
                {
                    TextEditStyle = TextEditStyles.DisableTextEditor,
                    Items = { "标准帧", "扩展帧" }
                };

                _repoMultiplexSignals = new RepositoryItemComboBox
                {
                    TextEditStyle = TextEditStyles.DisableTextEditor,
                    Items = { "是", "否" }
                };

                _repoByteOrder = new RepositoryItemComboBox
                {
                    TextEditStyle = TextEditStyles.DisableTextEditor,
                    Items = { "Motorola", "Inter" }
                };

                _repoSigned = new RepositoryItemComboBox
                {
                    TextEditStyle = TextEditStyles.DisableTextEditor,
                    Items = { "Signed", "Unsigned" }
                };

                _repositoryTextEdit = new RepositoryItemTextEdit();
            }

            public void LoadData(SQLiteConnection conn, long dbcFileId, TreeList treeList)
            {
                var messages = SQLite_Service.GetMessagesByDbc(conn, dbcFileId);

                foreach (var msg in messages)
                {
                    var parentNode = CreateMessageNode(treeList, msg);
                    var signals = SQLite_Service.GetSignalsByMessage(conn, msg.MessageID);
                    CreateSignalNodes(treeList, parentNode, signals);
                }
            }

            private TreeListNode CreateMessageNode(TreeList treeList, MessageInfo msg)
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

            private void CreateSignalNodes(TreeList treeList, TreeListNode parent, List<SignalInfo> signals)
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

            public void InitializeTreeColumns(TreeList treeList)
            {
                treeList.Columns.Clear();
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
                    new TreeListColumn { Caption = "Orders", VisibleIndex = treeList.Columns.Count, Visible = false }
                });
            }

            public void CustomNodeCellEdit(GetCustomNodeCellEditEventArgs e) 
            {
                if (e.Node.Level == 0) // 消息节点
                {
                    switch (e.Column.Caption)
                    {
                        case "帧类型":
                            e.RepositoryItem = _repoFrameType;
                            break;
                        default:
                            e.RepositoryItem = _repositoryTextEdit;
                            break;
                    }
                }
                else // 信号节点
                {
                    switch (e.Column.Caption)
                    {
                        case "是否复用信号":
                            e.RepositoryItem = _repoMultiplexSignals;
                            break;
                        case "字节顺序":
                            e.RepositoryItem = _repoByteOrder;
                            break;
                        case "符号":
                            e.RepositoryItem = _repoSigned;
                            break;
                        default:
                            e.RepositoryItem = _repositoryTextEdit;
                            break;
                    }
                }
            }

            public bool AllowEdit(TreeListNode focusedNode, TreeListColumn focusedColumn) 
            {
                // 不允许编辑Orders列
                if (focusedColumn.Caption == "Orders") return false;

                // 父节点编辑规则
                if (focusedNode.Level == 0) // 消息节点
                {
                    switch (focusedColumn.Caption)
                    {
                        case "CAN ID":
                        case "帧类型":
                        case "消息名称":
                        case "数据长度":
                            return true; // 允许编辑
                        default:
                            return false; // 禁止编辑其他列
                    }
                }
                else // 信号节点
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
                            return true; // 允许编辑

                        case "信号名称":
                            // 获取"是否复用信号"列的值
                            var multiplexColumn = focusedNode.TreeList.Columns["是否复用信号"];
                            if (multiplexColumn != null)
                            {
                                string multiplexValue = focusedNode.GetValue(multiplexColumn)?.ToString() ?? "";
                                return (multiplexValue != "是"); // 如果复用信号为"是"，则不允许编辑信号名称
                            }
                            return true; // 默认允许编辑

                        default:
                            return false; // 禁止编辑其他列
                    }
                }
            }

            public void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath, long currentDbcFileId) 
            {
            }

            public void AddNewItem(TreeList treeList, string dbcPath) 
            {

            }

            public void DeleteSelectedItems(TreeList treeList, string dbcPath, Action callback) 
            {

            }

            public void AddNewChildItem(TreeList treeList, string dbcPath) 
            {

            }

            public void DeleteSelectedChildItems(TreeList treeList, string dbcPath, Action callback) 
            {

            }

            public void ImportExcel(TreeList treeList, string fileName, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache) 
            {
                treeList.BeginUnboundLoad();

                try
                {
                    using (var workbook = new XLWorkbook(fileName))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var rows = worksheet.RowsUsed().Skip(1); // 跳过标题行

                        TreeListNode currentMessageNode = null;

                        foreach (var row in rows)
                        {
                            // 检查是否是消息行（有CAN ID）
                            var canIdCell = row.Cell(1);
                            if (!canIdCell.IsEmpty() && canIdCell.Value.ToString() != "")
                            {
                                // 创建新消息节点
                                currentMessageNode = treeList.AppendNode(new object[]
                                {
                            row.Cell(1).Value.ToString(),
                            row.Cell(2).Value.ToString(),
                            row.Cell(3).Value.ToString(),
                            Convert.ToInt32(row.Cell(4).Value),
                            "", "", "", "", "", "", "", "", "", ""
                                }, null);
                            }
                            else if (currentMessageNode != null)
                            {
                                // 创建信号节点
                                treeList.AppendNode(new object[]
                                {
                                    "", "", "", "",
                                    row.Cell(5).Value.ToString(),
                                    row.Cell(6).Value.ToString(),
                                    row.Cell(7).Value.ToString(),
                                    row.Cell(8).Value.ToString(),
                                    Convert.ToInt32(row.Cell(9).Value),
                                    Convert.ToInt32(row.Cell(10).Value),
                                    row.Cell(11).Value.ToString(),
                                    row.Cell(12).Value.ToString(),
                                    row.Cell(13).Value.ToString(),
                                    row.Cell(14).Value.ToString(),
                                    row.Cell(15).Value.ToString()
                                }, currentMessageNode);
                            }
                        }
                    }
                }
                finally
                {
                    treeList.EndUnboundLoad();
                }
            }
            
            public void ExportExcel(TreeList treeList, string fileName, string dbcPath) 
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("CAN协议");

                    // 添加标题行
                    worksheet.Cell(1, 1).Value = "CAN ID";
                    worksheet.Cell(1, 2).Value = "帧类型";
                    worksheet.Cell(1, 3).Value = "消息名称";
                    worksheet.Cell(1, 4).Value = "数据长度";
                    worksheet.Cell(1, 5).Value = "信号名称";
                    worksheet.Cell(1, 6).Value = "是否复用信号";
                    worksheet.Cell(1, 7).Value = "关联系统变量名称";
                    worksheet.Cell(1, 8).Value = "单位";
                    worksheet.Cell(1, 9).Value = "起始位";
                    worksheet.Cell(1, 10).Value = "长度";
                    worksheet.Cell(1, 11).Value = "字节顺序";
                    worksheet.Cell(1, 12).Value = "符号";
                    worksheet.Cell(1, 13).Value = "系数";
                    worksheet.Cell(1, 14).Value = "偏移";
                    worksheet.Cell(1, 15).Value = "范围";

                    int rowIndex = 2;

                    // 递归写入所有节点
                    foreach (TreeListNode node in treeList.Nodes)
                    {
                        WriteNodeToExcel(worksheet, node, ref rowIndex, dbcPath);
                    }

                    workbook.SaveAs(fileName);
                }
            }

            public void SaveData(SQLiteConnection conn, long dbcFileId, TreeList treeList, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache) 
            {

            }

            public void UpdateOrder(SQLiteConnection conn, TreeListNode node, int order, SQLiteTransaction transaction) 
            {
                if (node.Level == 0) // 消息节点
                {
                    long messageId = (long)node.Tag;
                    SQLite_Service.UpdateMessageOrder(conn, messageId, order, transaction);
                }
                else // 信号节点
                {
                    long signalId = (long)node.Tag;
                    SQLite_Service.UpdateSignalOrder(conn, signalId, order, transaction);
                }
            }

            public void CopyData(SQLiteConnection conn, long sourceFileId, long newFileId, SQLiteTransaction transaction) 
            {

            }
            
            public void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath) 
            {
                if (node.Level == 0) // 消息节点
                {
                    ws.Cell(rowIndex, 1).Value = node.GetValue("CAN ID")?.ToString() ?? "";
                    ws.Cell(rowIndex, 2).Value = node.GetValue("帧类型")?.ToString() ?? "";
                    ws.Cell(rowIndex, 3).Value = node.GetValue("消息名称")?.ToString() ?? "";
                    ws.Cell(rowIndex, 4).Value = node.GetValue("数据长度")?.ToString() ?? "";
                    rowIndex++;
                }
                else // 信号节点
                {
                    ws.Cell(rowIndex, 5).Value = node.GetValue("信号名称")?.ToString() ?? "";
                    ws.Cell(rowIndex, 6).Value = node.GetValue("是否复用信号")?.ToString() ?? "";
                    ws.Cell(rowIndex, 7).Value = node.GetValue("关联系统变量名称")?.ToString() ?? "";
                    ws.Cell(rowIndex, 8).Value = node.GetValue("单位")?.ToString() ?? "";
                    ws.Cell(rowIndex, 9).Value = node.GetValue("起始位")?.ToString() ?? "";
                    ws.Cell(rowIndex, 10).Value = node.GetValue("长度")?.ToString() ?? "";
                    ws.Cell(rowIndex, 11).Value = node.GetValue("字节顺序")?.ToString() ?? "";
                    ws.Cell(rowIndex, 12).Value = node.GetValue("符号")?.ToString() ?? "";
                    ws.Cell(rowIndex, 13).Value = node.GetValue("系数")?.ToString() ?? "";
                    ws.Cell(rowIndex, 14).Value = node.GetValue("偏移")?.ToString() ?? "";
                    ws.Cell(rowIndex, 15).Value = node.GetValue("范围")?.ToString() ?? "";
                    rowIndex++;
                }

                // 递归处理子节点
                foreach (TreeListNode childNode in node.Nodes)
                {
                    WriteNodeToExcel(ws, childNode, ref rowIndex, dbcPath);
                }
            }
        }

        /// <summary>
        /// Modbus协议处理器
        /// </summary>
        public class ModbusProtocolHandler : IProtocolHandler
        {
            public void InitializeEditors()
            {

            }
            public void LoadData(SQLiteConnection conn, long dbcFileId, TreeList treeList)
            {
                var registers = SQLite_Service.GetModbusRegistersByFile(conn, dbcFileId);

                // 根据寄存器类型分组
                var groupedRegisters = registers.GroupBy(r => r.RegisterType);

                foreach (var group in groupedRegisters)
                {
                    // 创建寄存器类型节点
                    var typeNode = treeList.AppendNode(new object[]
                    {
                        group.Key, // 寄存器类型
                        "", "", "", "", "", "", "", "", "", "", "", "", "", ""
                    }, null);

                    typeNode.Tag = $"TYPE_{group.Key}";

                    // 添加寄存器节点
                    foreach (var register in group.OrderBy(r => r.Orders))
                    {
                        var registerNode = treeList.AppendNode(new object[]
                        {
                            "", // 寄存器类型已在父节点显示
                            register.Address.ToString(),
                            register.Name,
                            register.DataType,
                            register.ByteOrder ?? "",
                            register.ScalingFactor.ToString(),
                            register.Offset.ToString(),
                            register.MinValue?.ToString() ?? "",
                            register.MaxValue?.ToString() ?? "",
                            register.Unit ?? "",
                            register.Description ?? "",
                            "", "", "", ""
                        }, typeNode);

                        registerNode.Tag = register.RegisterID;
                        registerNode.SetValue("Orders", register.Orders);
                    }
                }
            }

            public void InitializeTreeColumns(TreeList treeList)
            {
                treeList.Columns.Clear();
                treeList.Columns.AddRange(new[] {
                    new TreeListColumn { Caption = "寄存器类型", VisibleIndex = 0, Width = 100 },
                    new TreeListColumn { Caption = "地址", VisibleIndex = 1, Width = 80 },
                    new TreeListColumn { Caption = "名称", VisibleIndex = 2, Width = 120 },
                    new TreeListColumn { Caption = "数据类型", VisibleIndex = 3, Width = 80 },
                    new TreeListColumn { Caption = "字节顺序", VisibleIndex = 4, Width = 80 },
                    new TreeListColumn { Caption = "缩放因子", VisibleIndex = 5, Width = 80 },
                    new TreeListColumn { Caption = "偏移量", VisibleIndex = 6, Width = 80 },
                    new TreeListColumn { Caption = "最小值", VisibleIndex = 7, Width = 80 },
                    new TreeListColumn { Caption = "最大值", VisibleIndex = 8, Width = 80 },
                    new TreeListColumn { Caption = "单位", VisibleIndex = 9, Width = 60 },
                    new TreeListColumn { Caption = "描述", VisibleIndex = 10, Width = 150 },
                    new TreeListColumn { Caption = "Orders", VisibleIndex = treeList.Columns.Count, Visible = false }
                });
            }

            // 其他方法实现...
            public void CustomNodeCellEdit(GetCustomNodeCellEditEventArgs e) { /* 实现 */ }
            public bool AllowEdit(TreeListNode focusedNode, TreeListColumn focusedColumn) { /* 实现 */ return true; }
            public void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath, long currentDbcFileId) { /* 实现 */ }
            public void AddNewItem(TreeList treeList, string dbcPath) { /* 实现 */ }
            public void DeleteSelectedItems(TreeList treeList, string dbcPath, Action callback) { /* 实现 */ }
            public void AddNewChildItem(TreeList treeList, string dbcPath) { /* 实现 */ }
            public void DeleteSelectedChildItems(TreeList treeList, string dbcPath, Action callback) { /* 实现 */ }
            public void ImportExcel(TreeList treeList, string fileName, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache) { /* 实现 */ }
            public void ExportExcel(TreeList treeList, string fileName, string dbcPath) { /* 实现 */ }
            public void SaveData(SQLiteConnection conn, long dbcFileId, TreeList treeList, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache) { /* 实现 */ }
            public void UpdateOrder(SQLiteConnection conn, TreeListNode node, int order, SQLiteTransaction transaction) { /* 实现 */ }
            public void CopyData(SQLiteConnection conn, long sourceFileId, long newFileId, SQLiteTransaction transaction) { /* 实现 */ }
            public void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath) { /* 实现 */ }
        }

        #endregion
    }
}