using ChargeDebug.Service;
using ClosedXML.Excel;
using DataModel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraTreeList.Nodes;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ZLGAPI;

namespace ChargeDebug.Form
{
    /// <summary>
    /// 协议配置界面 - 支持多种协议类型（CAN总线、RS485-Modbus等）的动态显示和配置
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
            else if (protocolType == "MODBUS")
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
            else if (protocolType == "MODBUS" && _currentProtocolHandler is ModbusProtocolHandler modbusHandler)
            {
                treeList.RepositoryItems.AddRange(new RepositoryItem[] {
                    modbusHandler.RepoByteOrder,
                    modbusHandler.RepoSigned,
                    modbusHandler.RepositoryTextEdit
                });
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

            // 使用协议处理器处理双击事件，传递复用信号缓存引用
            _currentProtocolHandler.HandleDoubleClick(hitInfo, treeList, dbcPath,
                                                    _currentDbcFileId, ref _reuseSignalsCache);
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
            _currentProtocolHandler.DeleteSelectedItems(treeList, dbcPath, () =>
            {
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
            _currentProtocolHandler.DeleteSelectedChildItems(treeList, dbcPath, () =>
            {
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
            void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath,
                            long currentDbcFileId, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache);
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

            public void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath,
                            long currentDbcFileId, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                if (hitInfo.Node == null || hitInfo.Column == null) return;

                // 只处理信号节点（Level=1）的"信号名称"列双击
                if (hitInfo.Node.Level != 1 || hitInfo.Column.Caption != "信号名称") return;

                TreeListNode signalNode = hitInfo.Node;
                TreeListNode parentNode = signalNode.ParentNode;

                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 保存原始ID（可能是临时ID）
                            long originalId = Convert.ToInt64(signalNode.Tag ?? -1);

                            // 获取父节点（报文节点）的值
                            string canId = parentNode.GetValue("CAN ID")?.ToString() ?? "";
                            string frameType = parentNode.GetValue("帧类型")?.ToString() ?? "";
                            string messageName = parentNode.GetValue("消息名称")?.ToString() ?? "";
                            int dataLength = Convert.ToInt32(parentNode.GetValue("数据长度") ?? 0);
                            int parentOrder = Convert.ToInt32(parentNode.GetValue("Orders") ?? 0);

                            // 获取信号节点的值
                            string signalName = signalNode.GetValue("信号名称")?.ToString() ?? "";
                            string multiplexSignals = signalNode.GetValue("是否复用信号")?.ToString() ?? "";
                            string systemName = signalNode.GetValue("关联系统变量名称")?.ToString() ?? "";
                            string unit = signalNode.GetValue("单位")?.ToString() ?? "";
                            int startBit = Convert.ToInt32(signalNode.GetValue("起始位") ?? 0);
                            int length = Convert.ToInt32(signalNode.GetValue("长度") ?? 0);
                            string byteOrder = signalNode.GetValue("字节顺序")?.ToString() ?? "";
                            string signed = signalNode.GetValue("符号")?.ToString() ?? "";
                            decimal factor = Convert.ToDecimal(signalNode.GetValue("系数") ?? 1);
                            decimal offset = Convert.ToDecimal(signalNode.GetValue("偏移") ?? 0);
                            string minMax = signalNode.GetValue("范围")?.ToString() ?? "";
                            int order = Convert.ToInt32(signalNode.GetValue("Orders") ?? 0);

                            // 插入或更新报文
                            long messageId = Convert.ToInt64(parentNode.Tag ?? -1);
                            messageId = SQLite_Service.UpsertMessage(
                                conn,
                                messageId: messageId,
                                canId: canId,
                                frameType: frameType,
                                messageName: messageName,
                                dataLength: dataLength,
                                orders: parentOrder,
                                dbcFileId: currentDbcFileId,
                                transaction: transaction
                            );
                            parentNode.Tag = messageId;

                            // 插入或更新信号
                            long signalId = Convert.ToInt64(signalNode.Tag ?? -1);
                            signalId = SQLite_Service.UpsertSignal(
                                conn,
                                signalId: signalId,
                                messageId: messageId,
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
                                orders: order,
                                transaction: transaction
                            );
                            signalNode.Tag = signalId;

                            // 检查原始ID对应的缓存复用信号
                            List<ReuseSignal> reuseSignals;
                            if (originalId < 0 && reuseSignalsCache.ContainsKey(originalId))
                            {
                                reuseSignals = reuseSignalsCache[originalId];
                                SQLite_Service.SaveReuseSignals(
                                    conn,
                                    signalId,
                                    reuseSignals,
                                    transaction
                                );
                                reuseSignalsCache.Remove(originalId);
                            }
                            else
                            {
                                // 从数据库加载复用信号
                                reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signalId);
                            }

                            var reuseSignalsList = new BindingList<ReuseSignal>(reuseSignals);
                            using (var reuseForm = new ReuseSignalForm(reuseSignalsList))
                            {
                                if (reuseForm.ShowDialog() == DialogResult.OK)
                                {
                                    reuseSignalsList = reuseForm.reuseSignals;

                                    // 更新信号名称显示
                                    if (reuseSignalsList.Count == 0)
                                        signalNode.SetValue("信号名称", "");
                                    else
                                        signalNode.SetValue("信号名称", "已配置");

                                    // 保存复用信号到数据库
                                    SQLite_Service.SaveReuseSignals(
                                        conn,
                                        signalId,
                                        reuseSignalsList.ToList(),
                                        transaction
                                    );

                                    transaction.Commit();
                                    XtraMessageBox.Show("复用信号保存成功！");
                                }
                                else
                                {
                                    transaction.Rollback();
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

            public void AddNewItem(TreeList treeList, string dbcPath)
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

            public void DeleteSelectedItems(TreeList treeList, string dbcPath, Action callback)
            {
                // 获取所有选中的父节点（消息节点）
                var parentNodesToDelete = treeList.Nodes.Cast<TreeListNode>()
                    .Where(n => n.ParentNode == null && n.CheckState == CheckState.Checked)
                    .ToList();

                if (parentNodesToDelete.Count == 0)
                {
                    XtraMessageBox.Show("请先选择要删除的报文");
                    return;
                }

                // 确认删除
                if (XtraMessageBox.Show($"确定要删除选中的 {parentNodesToDelete.Count} 条报文及其所有信号吗？",
                                      "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }

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
                            //ReorderMessagesAfterDeletion(conn, messageIdsToDelete);

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

            public void AddNewChildItem(TreeList treeList, string dbcPath)
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

            public void DeleteSelectedChildItems(TreeList treeList, string dbcPath, Action callback)
            {
                // 获取所有选中的子节点（信号节点）
                var nodesToDelete = treeList.GetNodeList()
                    .Where(n => n.ParentNode != null && n.CheckState == CheckState.Checked)
                    .ToList();


                if (nodesToDelete.Count == 0)
                {
                    XtraMessageBox.Show("请先选择要删除的信号");
                    return;
                }

                // 按父节点分组
                var groupedByParent = nodesToDelete
                    .GroupBy(n => n.ParentNode)
                    .ToList();

                // 确认删除
                if (XtraMessageBox.Show($"确定要删除选中的 {nodesToDelete.Count} 个信号吗？",
                                      "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }

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
                                    //ReorderSignalsAfterDeletion(conn, group.Key);
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

            public void ImportExcel(TreeList treeList, string fileName, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                try
                {
                    treeList.BeginUnboundLoad();
                    treeList.ClearNodes();

                    using (var workbook = new XLWorkbook(fileName))
                    {
                        var ws = workbook.Worksheet(1); // 第一个工作表
                        var range = ws.RangeUsed();
                        if (range == null) return;

                        // 创建字典跟踪当前报文节点
                        TreeListNode currentMessageNode = null;
                        Dictionary<string, TreeListNode> signalNodeMap = new Dictionary<string, TreeListNode>();

                        // 从第2行开始（跳过表头）
                        for (int row = 2; row <= range.RowCount(); row++)
                        {
                            // 读取关键列值
                            string canId = ws.Cell(row, 1).GetString().Trim();
                            string frameType = ws.Cell(row, 2).GetString().Trim();
                            string messageName = ws.Cell(row, 3).GetString().Trim();
                            string dataLength = ws.Cell(row, 4).GetString().Trim();
                            string signalName = ws.Cell(row, 5).GetString().Trim();

                            // 如果是报文行
                            if (!string.IsNullOrWhiteSpace(canId))
                            {
                                // 创建新报文节点
                                currentMessageNode = treeList.AppendNode(new object[]
                                {
                                    canId,
                                    frameType,
                                    messageName,
                                    dataLength,
                                    "", "", "", "", "", "", "", "", "", ""
                                }, null);

                                // 设置初始排序值
                                currentMessageNode.SetValue("Orders", row - 1);
                            }
                            else if (!string.IsNullOrWhiteSpace(signalName) && currentMessageNode != null)
                            {
                                // 创建信号节点
                                var signalNode = treeList.AppendNode(new object[]
                                {
                                    "", "", "", "",
                                    signalName,
                                    ws.Cell(row, 6).GetString().Trim(), // 是否复用信号
                                    ws.Cell(row, 7).GetString().Trim(), // 系统变量
                                    ws.Cell(row, 8).GetString().Trim(), // 单位
                                    TryParseInt(ws.Cell(row, 9).GetString()), // 起始位
                                    TryParseInt(ws.Cell(row, 10).GetString()), // 长度
                                    ws.Cell(row, 11).GetString().Trim(), // 字节顺序
                                    ws.Cell(row, 12).GetString().Trim(), // 符号
                                    TryParseDecimal(ws.Cell(row, 13).GetString()), // 系数
                                    TryParseDecimal(ws.Cell(row, 14).GetString()), // 偏移
                                    ws.Cell(row, 15).GetString().Trim() // 范围
                                }, currentMessageNode);

                                // 设置初始排序值
                                signalNode.SetValue("Orders", row - 1);

                                // 处理复用信号
                                string reuseSignals = ws.Cell(row, 6).GetString().Trim();
                                string reuseDetails = ws.Cell(row, 16).GetString().Trim(); // 新增复用信号详情列

                                if (reuseSignals == "是")
                                {
                                    signalNode.SetValue("是否复用信号", "是");
                                    signalNode.SetValue("信号名称", "已配置");

                                    // 解析复用信号并缓存（使用临时ID）
                                    long tempSignalId = -(row + 1000); // 生成临时唯一ID
                                    var parsedSignals = ParseReuseSignals(reuseDetails);
                                    reuseSignalsCache[tempSignalId] = parsedSignals;

                                    signalNode.Tag = tempSignalId;
                                    signalNodeMap[signalName] = signalNode;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    XtraMessageBox.Show($"导入Excel失败: {ex.Message}");
                }
                finally
                {
                    treeList.EndUnboundLoad();
                }
            }

            /// <summary>
            /// 处理Excel中的复用信号数据
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
            /// 从文本解析复用信号
            /// </summary>
            private List<ReuseSignal> ParseReuseSignalsFromText(string text)
            {
                var signals = new List<ReuseSignal>();

                if (string.IsNullOrWhiteSpace(text))
                    return signals;

                try
                {
                    // 支持多种分隔符：换行、分号、逗号
                    var lines = text.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (string.IsNullOrEmpty(trimmedLine))
                            continue;

                        // 解析格式：描述=值
                        var parts = trimmedLine.Split('=');
                        if (parts.Length >= 2)
                        {
                            signals.Add(new ReuseSignal
                            {
                                Description = parts[0].Trim(),
                                Value = parts[1].Trim()
                            });
                        }
                        else if (parts.Length == 1)
                        {
                            // 如果没有等号，使用默认格式
                            signals.Add(new ReuseSignal
                            {
                                Description = trimmedLine,
                                Value = signals.Count.ToString()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"解析复用信号文本失败: {ex.Message}");
                }

                return signals;
            }

            /// <summary>
            /// 安全转换整数
            /// </summary>
            private int TryParseInt(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return 0;

                // 处理十六进制格式
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int result))
                        return result;
                }

                // 处理十进制格式
                if (int.TryParse(value, out int decimalResult))
                    return decimalResult;

                return 0;
            }

            /// <summary>
            /// 安全转换小数
            /// </summary>
            private decimal TryParseDecimal(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return 1.0m;

                // 处理科学计数法和常规小数
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result))
                    return result;

                return 1.0m;
            }

            public void ExportExcel(TreeList treeList, string fileName, string dbcPath)
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("CAN协议");

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
                        WriteNodeToExcel(treeList, ws, node, ref rowIndex, dbcPath);
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

                    // 添加复用信号详情列头
                    ws.Cell(1, 16).Value = "复用信号详情";
                    ws.Column(16).Width = 40;

                    ws.RangeUsed().Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    workbook.SaveAs(fileName);
                }
            }

            public void SaveData(SQLiteConnection conn, long dbcFileId, TreeList treeList, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                // 获取所有消息节点
                var messageNodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                // 保存消息和信号数据
                foreach (var messageNode in messageNodes)
                {
                    // 处理消息节点
                    long messageId = ProcessMessageNode(conn, dbcFileId, messageNode, transaction);

                    // 处理信号节点
                    ProcessSignalNodes(conn, messageId, messageNode, transaction, ref reuseSignalsCache);
                }
            }

            private long ProcessMessageNode(SQLiteConnection conn, long dbcFileId, TreeListNode messageNode, SQLiteTransaction transaction)
            {
                var msgInfo = new MessageInfo
                {
                    MessageID = messageNode.Tag as long? ?? -1,
                    CANID = messageNode.GetValue("CAN ID").ToString(),
                    FrameType = messageNode.GetValue("帧类型").ToString(),
                    MessageName = messageNode.GetValue("消息名称").ToString(),
                    DataLength = Convert.ToInt32(messageNode.GetValue("数据长度")),
                    Orders = Convert.ToInt32(messageNode.GetValue("Orders"))
                };

                return SQLite_Service.UpsertMessage(
                    conn: conn,
                    messageId: msgInfo.MessageID,
                    canId: msgInfo.CANID,
                    frameType: msgInfo.FrameType,
                    messageName: msgInfo.MessageName,
                    dataLength: msgInfo.DataLength,
                    orders: msgInfo.Orders,
                    dbcFileId: dbcFileId,
                    transaction: transaction
                );
            }

            private void ProcessSignalNodes(SQLiteConnection conn, long messageId, TreeListNode messageNode, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                foreach (TreeListNode signalNode in messageNode.Nodes)
                {
                    // 保存原始ID（可能是临时ID）
                    long originalId = Convert.ToInt64(signalNode.Tag ?? -1);

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
                        messageId: messageId,
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

                    // 检查原始ID对应的缓存复用信号
                    if (originalId < 0 && reuseSignalsCache.ContainsKey(originalId))
                    {
                        var reuseSignals = reuseSignalsCache[originalId];
                        SQLite_Service.SaveReuseSignals(
                            conn,
                            sigId,
                            reuseSignals,
                            transaction
                        );
                        // 移除已处理的缓存
                        reuseSignalsCache.Remove(originalId);
                    }
                    // 处理现有信号的复用信号（原逻辑保留）
                    else if (signalNode.Tag is long tempId && tempId < 0)
                    {
                        if (reuseSignalsCache.TryGetValue(tempId, out var reuseSignals))
                        {
                            SQLite_Service.SaveReuseSignals(
                                conn,
                                sigId,
                                reuseSignals,
                                transaction
                            );
                            reuseSignalsCache.Remove(tempId);
                        }
                    }
                }
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
                try
                {
                    // 1. 复制报文数据
                    var sourceMessages = SQLite_Service.GetMessagesByDbc(conn, sourceFileId);
                    var messageMapping = new Dictionary<long, long>(); // 源MessageID -> 新MessageID映射

                    foreach (var sourceMsg in sourceMessages)
                    {
                        // 插入新报文记录
                        long newMessageId = SQLite_Service.UpsertMessage(
                            conn,
                            messageId: -1, // 新记录
                            canId: sourceMsg.CANID,
                            frameType: sourceMsg.FrameType,
                            messageName: sourceMsg.MessageName,
                            dataLength: sourceMsg.DataLength,
                            orders: sourceMsg.Orders,
                            dbcFileId: newFileId,
                            transaction: transaction
                        );

                        // 保存映射关系
                        messageMapping.Add(sourceMsg.MessageID, newMessageId);
                    }

                    // 2. 复制信号数据
                    foreach (var sourceMsg in sourceMessages)
                    {
                        var sourceSignals = SQLite_Service.GetSignalsByMessage(conn, sourceMsg.MessageID);
                        var signalMapping = new Dictionary<long, long>(); // 源SignalID -> 新SignalID映射

                        foreach (var sourceSig in sourceSignals)
                        {
                            // 插入新信号记录
                            long newSignalId = SQLite_Service.UpsertSignal(
                                conn,
                                signalId: -1, // 新记录
                                messageId: messageMapping[sourceMsg.MessageID],
                                signalName: sourceSig.SignalName,
                                multiplexSignals: sourceSig.MultiplexSignals,
                                systemName: sourceSig.SystemName,
                                unit: sourceSig.Unit,
                                startBit: sourceSig.StartBit,
                                length: sourceSig.Length,
                                byteOrder: sourceSig.ByteOrder,
                                signed: sourceSig.Signed,
                                factor: sourceSig.Factor,
                                offset: sourceSig.Offset,
                                minMax: sourceSig.MinMax,
                                orders: sourceSig.Orders,
                                transaction: transaction
                            );

                            // 保存映射关系
                            signalMapping.Add(sourceSig.SignalID, newSignalId);

                            // 3. 复制复用信号
                            var reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, sourceSig.SignalID);
                            if (reuseSignals.Count > 0)
                            {
                                SQLite_Service.SaveReuseSignals(
                                    conn,
                                    newSignalId,
                                    reuseSignals,
                                    transaction
                                );
                            }
                        }
                    }

                    // 4. 复制协议配置信息（如果有的话）
                    // 例如：SQLite_Service.CopyProtocolConfig(conn, sourceFileId, newFileId, transaction);
                }
                catch (Exception ex)
                {
                    throw new Exception($"复制数据失败: {ex.Message}");
                }
            }

            public void WriteNodeToExcel(TreeList treeList, IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath)
            {
                // 写入当前节点所有列的数据
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

                // 检查是否为复用信号
                bool isMultiplexSignal = false;
                List<ReuseSignal> reuseSignals = null;

                if (node.ParentNode != null) // 信号节点
                {
                    string multiplexValue = node.GetValue("是否复用信号")?.ToString();
                    if (multiplexValue == "是")
                    {
                        isMultiplexSignal = true;
                        long signalId = Convert.ToInt64(node.Tag);

                        // 从数据库加载复用信号
                        using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                        {
                            conn.Open();
                            reuseSignals = SQLite_Service.GetReuseSignalsBySignals(conn, signalId);
                        }
                    }
                }

                // 如果是复用信号且有复用信号数据
                if (isMultiplexSignal && reuseSignals != null && reuseSignals.Count > 0)
                {
                    if (reuseSignals == null || reuseSignals.Count == 0)
                    {
                        ws.Cell(rowIndex, 16).Value = "无复用信号配置";
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        foreach (var signal in reuseSignals)
                        {
                            sb.AppendLine($"{signal.Description} = {signal.Value}");
                        }
                        ws.Cell(rowIndex, 16).Value = sb.ToString().TrimEnd();
                    }
                }

                rowIndex++; // 移动到下一行

                // 递归处理子节点
                foreach (TreeListNode childNode in node.Nodes)
                {
                    WriteNodeToExcel(treeList, ws, childNode, ref rowIndex, dbcPath);
                }
            }

            public void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Modbus-RTU 协议处理器
        /// </summary>
        public class ModbusProtocolHandler : IProtocolHandler
        {
            // Modbus寄存器类型编辑器
            private RepositoryItemComboBox _repoByteOrder;
            private RepositoryItemComboBox _repoSigned;
            private RepositoryItemTextEdit _repositoryTextEdit;

            // 提供对编辑器的访问属性
            public RepositoryItemComboBox RepoByteOrder => _repoByteOrder;
            public RepositoryItemComboBox RepoSigned => _repoSigned;
            public RepositoryItemTextEdit RepositoryTextEdit => _repositoryTextEdit;

            // Modbus寄存器定义（根据文档）
            private readonly Dictionary<string, ModbusRegister> _registerDefinitions = new Dictionary<string, ModbusRegister>
            {
                {"0x0001", new ModbusRegister("程控", "u16", "rw", "1-REM/0-LOCAL")},
                {"0x0002", new ModbusRegister("ON/OFF", "u16", "rw", "1-ON/0-OFF")},
                {"0x0003", new ModbusRegister("最大电压设置", "float", "rw", "")},
                {"0x0005", new ModbusRegister("最大电流设置", "float", "rw", "")},
                {"0x0007", new ModbusRegister("电压预设值", "float", "rw", "")},
                {"0x0009", new ModbusRegister("电流预设值", "float", "rw", "")},
                {"0x000B", new ModbusRegister("电压采样回检值", "float", "r", "")},
                {"0x000D", new ModbusRegister("电流采样回检值", "float", "r", "")},
                {"0x000F", new ModbusRegister("过压设置", "float", "rw", "")},
                {"0x0011", new ModbusRegister("过流设置", "float", "rw", "")},
                {"0x0013", new ModbusRegister("电流上升斜率", "float", "rw", "")},
                {"0x0015", new ModbusRegister("电压上升斜率", "float", "rw", "")},
                {"0x0017", new ModbusRegister("电流下降斜率", "float", "rw", "")},
                {"0x0019", new ModbusRegister("电压下降斜率", "float", "rw", "")}
            };

            public void InitializeEditors()
            {
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
                // 开始批量操作（防止界面闪烁）
                treeList.BeginUnboundLoad();
                try
                {
                    // 清空现有节点
                    treeList.ClearNodes();

                    // 从数据库加载 Modbus 信号数据
                    var signals = SQLite_Service.GetModbusSignalsByDbc(conn, dbcFileId);

                    // 将数据添加到 TreeList
                    foreach (var signal in signals)
                    {
                        var node = treeList.AppendNode(new object[]
                        {
                            signal.SignalName,
                            signal.CorrespondenceAddress,
                            signal.FunctionCode,
                            signal.RegisterAddress,
                            signal.RegisterCount,
                            signal.SystemVariableName,
                            signal.Unit,
                            signal.ByteOrder,
                            signal.Signed,
                            signal.Factor,
                            signal.Offset,
                            signal.ValueRange
                        }, null);

                        // 设置节点的 Tag 为信号 ID，以便后续操作
                        node.Tag = signal.SignalID;

                        // 设置排序值
                        node.SetValue("Orders", signal.Orders);
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误日志或显示错误消息
                    XtraMessageBox.Show($"加载 RS485-Modbus 数据失败: {ex.Message}");
                }
                finally
                {
                    // 结束批量操作
                    treeList.EndUnboundLoad();
                }
            }

            public void InitializeTreeColumns(TreeList treeList)
            {
                treeList.Columns.Clear();
                treeList.Columns.AddRange(new[] {
                    new TreeListColumn { Caption = "信号名称", VisibleIndex = 0, Width = 150 },
                    new TreeListColumn { Caption = "通讯地址", VisibleIndex = 1, Width = 100 },
                    new TreeListColumn { Caption = "功能码", VisibleIndex = 2, Width = 100 },
                    new TreeListColumn { Caption = "寄存器地址", VisibleIndex = 3, Width = 100 },
                    new TreeListColumn { Caption = "寄存器个数", VisibleIndex = 4, Width = 100 },
                    //new TreeListColumn { Caption = "字节数", VisibleIndex = 4, Width = 100 },
                    new TreeListColumn { Caption = "关联系统变量名称", VisibleIndex = 5, Width = 150 },
                    new TreeListColumn { Caption = "单位", VisibleIndex = 6, Width = 80 },
                    new TreeListColumn { Caption = "字节顺序", VisibleIndex = 7, Width = 100 },
                    new TreeListColumn { Caption = "符号", VisibleIndex = 8, Width = 100 },
                    new TreeListColumn { Caption = "系数", VisibleIndex = 9, Width = 100 },
                    new TreeListColumn { Caption = "偏移", VisibleIndex = 10, Width = 100 },
                    new TreeListColumn { Caption = "范围", VisibleIndex = 11, Width = 100 },
                    new TreeListColumn { Caption = "Orders", VisibleIndex = treeList.Columns.Count, Visible = false }
                });
            }

            public void CustomNodeCellEdit(GetCustomNodeCellEditEventArgs e)
            {
                switch (e.Column.Caption)
                {
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

            public bool AllowEdit(TreeListNode focusedNode, TreeListColumn focusedColumn)
            {
                // 不允许编辑Orders列
                if (focusedColumn.Caption == "Orders") return false;

                // 所有列都可编辑
                return true;
            }

            public void HandleDoubleClick(TreeListHitInfo hitInfo, TreeList treeList, string dbcPath,
                            long currentDbcFileId, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                // Modbus协议不需要复用信号功能
                XtraMessageBox.Show("RS485-Modbus协议不支持复用信号配置");
            }

            public void AddNewItem(TreeList treeList, string dbcPath)
            {
                treeList.BeginUnboundLoad();
                try
                {
                    using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                    {
                        conn.Open();

                        // 获取最大排序值
                        int maxSortOrder = SQLite_Service.GetMaxSortOrder(conn, "ModbusSignals") + 1;
                        var newNode = treeList.AppendNode(new object[]
                        {
                            "",       // 信号名称
                            "0x00",   // 地址码
                            "0x00",   // 功能码
                            "0x0001", // 寄存器地址
                            "1",      // 寄存器个数
                            "",       // 关联系统变量名称
                            "",       // 单位
                            "Inter",       // 字节顺序
                            "Signed",       // 符号
                            "1",       // 系数
                            "0",       // 偏移
                            "",       // 范围
                            maxSortOrder // 排序
                        }, null);

                        newNode.Tag = -1; // 临时标记为新节点
                        treeList.FocusedNode = newNode;
                    }
                }
                finally
                {
                    treeList.EndUnboundLoad();
                }
            }

            public void DeleteSelectedItems(TreeList treeList, string dbcPath, Action callback)
            {
                // 获取所有选中的节点
                var nodesToDelete = treeList.GetNodeList()
                    .Where(n => n.CheckState == CheckState.Checked)
                    .ToList();

                if (nodesToDelete.Count == 0)
                {
                    XtraMessageBox.Show("请先选择要删除的信号");
                    return;
                }

                // 确认删除
                if (XtraMessageBox.Show($"确定要删除选中的 {nodesToDelete.Count} 个信号吗？",
                                      "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }

                using (var conn = new SQLiteConnection($"Data Source={dbcPath};Version=3;"))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 先处理数据库删除（倒序）
                            for (int i = nodesToDelete.Count - 1; i >= 0; i--)
                            {
                                var node = nodesToDelete[i];
                                long registerId = Convert.ToInt64(node.Tag ?? -1);
                                if (registerId == -1) continue;

                                SQLite_Service.DeleteModbusSignals(conn, registerId);
                            }

                            transaction.Commit();

                            // 再删除界面节点（倒序）
                            for (int i = nodesToDelete.Count - 1; i >= 0; i--)
                            {
                                treeList.DeleteNode(nodesToDelete[i]);
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

            public void AddNewChildItem(TreeList treeList, string dbcPath)
            {
                // Modbus协议没有子项概念
                XtraMessageBox.Show("RS485-Modbus协议不支持添加子项");
            }

            public void DeleteSelectedChildItems(TreeList treeList, string dbcPath, Action callback)
            {
                // Modbus协议没有子项概念
                XtraMessageBox.Show("RS485-Modbus协议不支持删除子项");
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

                        foreach (var row in rows)
                        {
                            treeList.AppendNode(new object[]
                            {
                        row.Cell(1).Value.ToString(), // 地址
                        row.Cell(2).Value.ToString(), // 描述
                        row.Cell(3).Value.ToString(), // 数据类型
                        row.Cell(4).Value.ToString(), // 读写权限
                        row.Cell(5).Value.ToString(), // 值
                        row.Cell(6).Value.ToString(), // 备注
                        0 // 排序
                            }, null);
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
                    var worksheet = workbook.Worksheets.Add("RS485-Modbus协议");

                    // 添加标题行
                    worksheet.Cell(1, 1).Value = "寄存器地址";
                    worksheet.Cell(1, 2).Value = "描述";
                    worksheet.Cell(1, 3).Value = "数据类型";
                    worksheet.Cell(1, 4).Value = "读写权限";
                    worksheet.Cell(1, 5).Value = "值";
                    worksheet.Cell(1, 6).Value = "备注";

                    int rowIndex = 2;

                    // 写入所有节点
                    foreach (TreeListNode node in treeList.Nodes)
                    {
                        worksheet.Cell(rowIndex, 1).Value = node.GetValue("寄存器地址")?.ToString() ?? "";
                        worksheet.Cell(rowIndex, 2).Value = node.GetValue("描述")?.ToString() ?? "";
                        worksheet.Cell(rowIndex, 3).Value = node.GetValue("数据类型")?.ToString() ?? "";
                        worksheet.Cell(rowIndex, 4).Value = node.GetValue("读写权限")?.ToString() ?? "";
                        worksheet.Cell(rowIndex, 5).Value = node.GetValue("值")?.ToString() ?? "";
                        worksheet.Cell(rowIndex, 6).Value = node.GetValue("备注")?.ToString() ?? "";
                        rowIndex++;
                    }

                    workbook.SaveAs(fileName);
                }
            }

            public void SaveData(SQLiteConnection conn, long dbcFileId, TreeList treeList, SQLiteTransaction transaction, ref Dictionary<long, List<ReuseSignal>> reuseSignalsCache)
            {
                // 获取所有节点
                var nodes = treeList.Nodes.Cast<TreeListNode>().ToList();

                // 保存信号数据
                foreach (var node in nodes)
                {
                    ProcessSignalNode(conn, dbcFileId, node, transaction);
                }
            }

            /// <summary>
            /// 处理单个信号节点并保存到数据库
            /// </summary>
            private void ProcessSignalNode(SQLiteConnection conn, long dbcFileId, TreeListNode node, SQLiteTransaction transaction)
            {
                // 保存原始ID（可能是临时ID）
                long originalId = Convert.ToInt64(node.Tag ?? -1);

                // 获取节点的值
                string signalName = node.GetValue("信号名称")?.ToString() ?? "";
                string correspondenceAddress = node.GetValue("通讯地址")?.ToString() ?? "";
                string functionCode = node.GetValue("功能码")?.ToString() ?? "";
                string registerAddress = node.GetValue("寄存器地址")?.ToString() ?? "";
                int registerCount = Convert.ToInt32(node.GetValue("寄存器个数") ?? 1);
                string systemVariableName = node.GetValue("关联系统变量名称")?.ToString() ?? "";
                string unit = node.GetValue("单位")?.ToString() ?? "";
                string byteOrder = node.GetValue("字节顺序")?.ToString() ?? "Inter";
                string signed = node.GetValue("符号")?.ToString() ?? "Unsigned";
                double factor = Convert.ToDouble(node.GetValue("系数") ?? 1.0);
                double offset = Convert.ToDouble(node.GetValue("偏移") ?? 0.0);
                string valueRange = node.GetValue("范围")?.ToString() ?? "";
                int order = Convert.ToInt32(node.GetValue("Orders") ?? 0);

                // 插入或更新信号
                long signalId = SQLite_Service.UpsertModbusSignal(
                    conn: conn,
                    signalId: originalId,
                    dbcFileId: dbcFileId,
                    signalName: signalName,
                    correspondenceAddress: correspondenceAddress,
                    functionCode: functionCode,
                    registerAddress: registerAddress,
                    registerCount: registerCount,
                    systemVariableName: systemVariableName,
                    unit: unit,
                    byteOrder: byteOrder,
                    signed: signed,
                    factor: factor,
                    offset: offset,
                    valueRange: valueRange,
                    orders: order,
                    transaction: transaction
                );

                // 更新节点的Tag为数据库ID
                node.Tag = signalId;
            }

            public void UpdateOrder(SQLiteConnection conn, TreeListNode node, int order, SQLiteTransaction transaction)
            {
                long registerId = (long)node.Tag;
                SQLite_Service.UpdateModbusRegisterOrder(conn, registerId, order, transaction);
            }

            public void CopyData(SQLiteConnection conn, long sourceFileId, long newFileId, SQLiteTransaction transaction)
            {
                try
                {
                    // 复制Modbus寄存器数据
                    var sourceRegisters = SQLite_Service.GetModbusSignalsByDbc(conn, sourceFileId);

                    foreach (var sourceReg in sourceRegisters)
                    {
                        //SQLite_Service.UpsertModbusRegister(
                        //    conn,
                        //    registerId: -1, // 新记录
                        //    address: sourceReg.Address,
                        //    description: sourceReg.Description,
                        //    dataType: sourceReg.DataType,
                        //    accessType: sourceReg.AccessType,
                        //    value: sourceReg.Value,
                        //    notes: sourceReg.Notes,
                        //    orders: sourceReg.Orders,
                        //    dbcFileId: newFileId,
                        //    transaction: transaction
                        //);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"复制RS485-Modbus数据失败: {ex.Message}");
                }
            }

            public void WriteNodeToExcel(IXLWorksheet ws, TreeListNode node, ref int rowIndex, string dbcPath)
            {
                ws.Cell(rowIndex, 1).Value = node.GetValue("寄存器地址")?.ToString() ?? "";
                ws.Cell(rowIndex, 2).Value = node.GetValue("描述")?.ToString() ?? "";
                ws.Cell(rowIndex, 3).Value = node.GetValue("数据类型")?.ToString() ?? "";
                ws.Cell(rowIndex, 4).Value = node.GetValue("读写权限")?.ToString() ?? "";
                ws.Cell(rowIndex, 5).Value = node.GetValue("值")?.ToString() ?? "";
                ws.Cell(rowIndex, 6).Value = node.GetValue("备注")?.ToString() ?? "";
                rowIndex++;
            }

            // Modbus寄存器信息类
            private class ModbusRegister
            {
                public string Description { get; set; }
                public string DataType { get; set; }
                public string AccessType { get; set; }
                public string Notes { get; set; }

                public ModbusRegister(string desc, string type, string access, string notes)
                {
                    Description = desc;
                    DataType = type;
                    AccessType = access;
                    Notes = notes;
                }
            }
        }

        #endregion
    }
}