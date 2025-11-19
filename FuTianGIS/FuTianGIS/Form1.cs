using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.SystemUI;
using ESRI.ArcGIS.Display;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.esriSystem;

namespace FuTianGIS
{
    public partial class MainForm : Form
    {
        private void ApplyUniqueValueRenderer(IFeatureLayer featureLayer, string fieldName)
        {
            if (featureLayer == null || featureLayer.FeatureClass == null)
                return;

            IFeatureClass featureClass = featureLayer.FeatureClass;

            // 找到字段索引
            int fieldIndex = featureClass.FindField(fieldName);
            if (fieldIndex < 0)
            {
                MessageBox.Show(
                    "图层中不存在字段：" + fieldName,
                    "渲染错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            // 创建唯一值渲染器
            IUniqueValueRenderer uniqueValueRenderer = new UniqueValueRendererClass();
            uniqueValueRenderer.FieldCount = 1;
            uniqueValueRenderer.set_Field(0, fieldName);

            // 随机数生成器，用来生成随机颜色
            Random rand = new Random();

            // 遍历所有要素，收集唯一值，并为每个值创建符号
            IFeatureCursor cursor = featureClass.Search(null, true);
            IFeature feature = cursor.NextFeature();

            System.Collections.Generic.Dictionary<string, bool> valueDict =
                new System.Collections.Generic.Dictionary<string, bool>();

            while (feature != null)
            {
                object valObj = feature.get_Value(fieldIndex);
                if (valObj != null && valObj != DBNull.Value)
                {
                    string value = valObj.ToString();

                    if (!valueDict.ContainsKey(value))
                    {
                        valueDict[value] = true;

                        // 创建随机颜色
                        IRgbColor color = new RgbColorClass();
                        color.Red = rand.Next(0, 256);
                        color.Green = rand.Next(0, 256);
                        color.Blue = rand.Next(0, 256);
                        color.Transparency = 255;

                        // 面要素：使用填充符号
                        ISimpleFillSymbol fillSymbol = new SimpleFillSymbolClass();
                        fillSymbol.Color = color;
                        fillSymbol.Style = esriSimpleFillStyle.esriSFSSolid;

                        ISymbol symbol = (ISymbol)fillSymbol;

                        // 添加到唯一值渲染器
                        uniqueValueRenderer.AddValue(value, fieldName, symbol);
                        uniqueValueRenderer.set_Label(value, value);
                    }
                }

                feature = cursor.NextFeature();
            }

            // 设置到图层上
            IGeoFeatureLayer geoFeatureLayer = featureLayer as IGeoFeatureLayer;
            if (geoFeatureLayer != null)
            {
                geoFeatureLayer.Renderer = (IFeatureRenderer)uniqueValueRenderer;
            }

            // 刷新地图
            axMapControl1.ActiveView.PartialRefresh(
                esriViewDrawPhase.esriViewGeography, null, axMapControl1.ActiveView.Extent);
        }
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 1. 绑定 TOC 和 Toolbar 到 MapControl
            axTOCControl1.SetBuddyControl(axMapControl1);
            axToolbarControl1.SetBuddyControl(axMapControl1);

            axToolbarControl1.AddItem("esriControls.ControlsOpenDocCommand", 0, -1, false, 0, esriCommandStyles.esriCommandStyleIconOnly);
            axToolbarControl1.AddItem("esriControls.ControlsAddDataCommand", 0, -1, false, 0, esriCommandStyles.esriCommandStyleIconOnly);
            axToolbarControl1.AddItem("esriControls.ControlsMapZoomInTool", 0, -1, true, 0, esriCommandStyles.esriCommandStyleIconOnly);
            axToolbarControl1.AddItem("esriControls.ControlsMapZoomOutTool", 0, -1, false, 0, esriCommandStyles.esriCommandStyleIconOnly);
            axToolbarControl1.AddItem("esriControls.ControlsMapPanTool", 0, -1, false, 0, esriCommandStyles.esriCommandStyleIconOnly);
            //axToolbarControl1.AddItem("esriControls.ControlsFullExtentCommand", 0, -1, false, 0, esriCommandStyles.esriCommandStyleIconOnly);

            // 2. 自动加载项目自带的福田地图 Futian.mxd（位于 Resources 文件夹）
            try
            {
                // exe 所在目录（bin\Debug 等）
                string resourcesFolder = System.IO.Path.Combine(Application.StartupPath, "Resources");
                string mxdPath = System.IO.Path.Combine(resourcesFolder, "Futian.mxd");

                if (!File.Exists(mxdPath))
                {
                    MessageBox.Show(
                        "未找到默认地图文件：\n" + mxdPath +
                        "\n\n请确认 Futian.mxd 是否已正确放置在 Resources 目录下，并设置为“Content/始终复制”。",
                        "地图文件缺失",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                // 使用 MapControl 加载 MXD
                axMapControl1.LoadMxFile(mxdPath, null, Type.Missing);

                // 全图显示
                axMapControl1.Extent = axMapControl1.FullExtent;
                axMapControl1.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "加载默认福田地图时发生错误：\n" + ex.Message,
                    "地图加载错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
        /// <summary>
        /// 获取当前要操作的要素图层：
        /// 1）优先使用 TOC 中当前选中的图层；
        /// 2）如果 TOC 没有选中，则返回最上面的可见要素图层。
        /// </summary>
        private IFeatureLayer GetCurrentFeatureLayer()
        {
            IMap map = axMapControl1.Map;
            if (map == null)
                return null;

            // ---------- 1. 尝试通过 TOC 当前选中项获取图层 ----------
            try
            {
                esriTOCControlItem itemType = esriTOCControlItem.esriTOCControlItemNone;
                object tocItem = null;
                object tocLayer = null;
                object tocOther = null;

                // 我们不知道当前版本 GetSelectedItem 是 3 个参数还是 4 个参数，
                // 所以用反射逐个尝试，避免编译期出错。
                var method = typeof(AxTOCControl).GetMethod("GetSelectedItem");
                if (method != null)
                {
                    var parameters = method.GetParameters();
                    // 4 个参数版本：GetSelectedItem(ref itemType, ref tocItem, ref tocLayer, ref tocOther)
                    if (parameters.Length == 4)
                    {
                        object[] args = new object[] { itemType, tocItem, tocLayer, tocOther };
                        method.Invoke(axTOCControl1, args);
                        itemType = (esriTOCControlItem)args[0];
                        tocItem = args[1];
                        tocLayer = args[2];
                        tocOther = args[3];
                    }
                    // 3 个参数版本：GetSelectedItem(ref itemType, ref tocItem, ref tocLayer)
                    else if (parameters.Length == 3)
                    {
                        object[] args = new object[] { itemType, tocItem, tocLayer };
                        method.Invoke(axTOCControl1, args);
                        itemType = (esriTOCControlItem)args[0];
                        tocItem = args[1];
                        tocLayer = args[2];
                    }
                    // 其它参数个数就不管了，直接跳过
                }

                if (itemType == esriTOCControlItem.esriTOCControlItemLayer && tocLayer is IFeatureLayer)
                {
                    return (IFeatureLayer)tocLayer;
                }
            }
            catch
            {
                // 任何异常都忽略，继续用 fallback 方式
            }

            // ---------- 2. 如果 TOC 没有选中合适图层，则返回最上面的可见要素图层 ----------
            for (int i = 0; i < map.LayerCount; i++)
            {
                ILayer layer = map.get_Layer(i);
                if (layer is IFeatureLayer && layer.Visible)
                {
                    return (IFeatureLayer)layer;
                }
            }

            return null;
        }
        private void btnLoadMap_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要加载的矢量数据";
                dlg.Filter =
                    "Shapefile (*.shp)|*.shp|" +
                    "File GDB (*.gdb)|*.gdb|" +
                    "Personal GDB (*.mdb)|*.mdb|" +
                    "所有支持类型|*.shp;*.gdb;*.mdb";
                dlg.CheckFileExists = true;
                dlg.Multiselect = false;

                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                string filePath = dlg.FileName;
                string extension = System.IO.Path.GetExtension(filePath).ToLower();

                try
                {
                    IFeatureClass featureClass = null;

                    if (extension == ".shp")
                    {
                        // ---------- 加载 Shapefile ----------
                        string folder = System.IO.Path.GetDirectoryName(filePath);
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

                        IWorkspaceFactory wsFactory = new ShapefileWorkspaceFactoryClass();
                        IFeatureWorkspace featureWorkspace = (IFeatureWorkspace)wsFactory.OpenFromFile(folder, 0);
                        featureClass = featureWorkspace.OpenFeatureClass(fileName);
                    }
                    else if (extension == ".gdb" || extension == ".mdb")
                    {
                        // ---------- 加载 GDB / MDB 中的要素类 ----------
                        IWorkspaceFactory wsFactory = null;

                        if (extension == ".gdb")
                            wsFactory = new FileGDBWorkspaceFactoryClass();
                        else
                            wsFactory = new AccessWorkspaceFactoryClass();

                        IWorkspace ws = wsFactory.OpenFromFile(filePath, 0);
                        IEnumDataset enumDataset = ws.get_Datasets(esriDatasetType.esriDTFeatureClass);

                        List<string> fcNames = new List<string>();
                        IDataset dataset = enumDataset.Next();
                        while (dataset != null)
                        {
                            fcNames.Add(dataset.Name);
                            dataset = enumDataset.Next();
                        }

                        if (fcNames.Count == 0)
                        {
                            MessageBox.Show("该 GDB/MDB 中没有要素类。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }

                        // 先简单地：如果只有一个要素类，就用它；如果有多个，用第一个
                        string chosenName = fcNames[0];

                        IFeatureWorkspace featureWorkspace = (IFeatureWorkspace)ws;
                        featureClass = featureWorkspace.OpenFeatureClass(chosenName);
                    }
                    else
                    {
                        MessageBox.Show("暂不支持的文件类型。", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (featureClass == null)
                    {
                        MessageBox.Show("未能打开要素类。", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // ---------- 创建图层并添加到 MapControl ----------
                    IFeatureLayer featureLayer = new FeatureLayerClass();
                    featureLayer.FeatureClass = featureClass;
                    featureLayer.Name = featureClass.AliasName;

                    axMapControl1.Map.AddLayer(featureLayer);

                    // ---------- 收集可用于渲染的字段 ----------
                    List<string> fieldNames = new List<string>();
                    IFields fields = featureClass.Fields;

                    for (int i = 0; i < fields.FieldCount; i++)
                    {
                        IField field = fields.get_Field(i);

                        // 只提供字符串/整型字段供选择，排除几何和 OID 字段
                        if ((field.Type == esriFieldType.esriFieldTypeString ||
                             field.Type == esriFieldType.esriFieldTypeInteger ||
                             field.Type == esriFieldType.esriFieldTypeSmallInteger) &&
                            !field.Name.Equals(featureClass.ShapeFieldName, StringComparison.OrdinalIgnoreCase) &&
                            !field.Name.Equals(featureClass.OIDFieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            fieldNames.Add(field.Name);
                        }
                    }

                    if (fieldNames.Count > 0)
                    {
                        using (FieldSelectForm dlgField = new FieldSelectForm(fieldNames))
                        {
                            if (dlgField.ShowDialog(this) == DialogResult.OK)
                            {
                                string selectedField = dlgField.SelectedFieldName;
                                if (!string.IsNullOrEmpty(selectedField))
                                {
                                    // ★ 调用你写的唯一值渲染方法
                                    ApplyUniqueValueRenderer(featureLayer, selectedField);
                                }
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "当前图层没有合适的字段用于唯一值渲染。",
                            "提示",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }

                    // 缩放到图层范围
                    axMapControl1.ActiveView.Extent = featureLayer.AreaOfInterest;
                    axMapControl1.ActiveView.Refresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "加载或渲染数据时出错：\n" + ex.Message,
                        "错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private void axMapControl1_OnMouseDown(object sender, IMapControlEvents2_OnMouseDownEvent e)
        {
            try
            {
                // 如果当前正在用 Toolbar 上的工具，则不打扰（比如正在放大、平移）
                if (axToolbarControl1.CurrentTool != null)
                    return;

                // 只响应鼠标左键
                if (e.button != 1)
                    return;

                // 当前鼠标点击位置（屏幕坐标）
                int x = e.x;
                int y = e.y;

                // 将屏幕坐标转换为地图坐标
                IPoint mapPoint = axMapControl1.ActiveView.ScreenDisplay.DisplayTransformation.ToMapPoint(x, y);

                // 找到最上面的可见 FeatureLayer
                IMap map = axMapControl1.Map;
                IFeatureLayer topFeatureLayer = null;

                for (int iLayer = 0; iLayer < map.LayerCount; iLayer++)
                {
                    ILayer layer = map.get_Layer(iLayer);
                    if (layer is IFeatureLayer && layer.Visible)
                    {
                        topFeatureLayer = (IFeatureLayer)layer;
                        break;
                    }
                }

                if (topFeatureLayer == null)
                {
                    MessageBox.Show("当前地图中没有可见的要素图层。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 用 IIdentify 在点击位置识别要素
                IIdentify identify = topFeatureLayer as IIdentify;
                if (identify == null)
                {
                    MessageBox.Show("该图层不支持识别。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                IArray idResultArray = identify.Identify(mapPoint);
                if (idResultArray == null || idResultArray.Count == 0)
                {
                    MessageBox.Show("点击位置没有找到要素。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 取第一个识别结果
                IIdentifyObj idObj = idResultArray.get_Element(0) as IIdentifyObj;
                IFeatureIdentifyObj featIdObj = idObj as IFeatureIdentifyObj;
                IRowIdentifyObject rowIdObj = featIdObj as IRowIdentifyObject;
                IFeature feature = rowIdObj.Row as IFeature;

                if (feature == null)
                    return;

                // 找到第一个字符串字段，并显示它的值
                IFields fields = feature.Fields;
                string info = null;

                for (int i = 0; i < fields.FieldCount; i++)
                {
                    IField field = fields.get_Field(i);
                    if (field.Type == esriFieldType.esriFieldTypeString)
                    {
                        object val = feature.get_Value(i);
                        info = field.AliasName + " = " + (val == null ? "" : val.ToString());
                        break;
                    }
                }

                if (string.IsNullOrEmpty(info))
                {
                    info = "没有找到字符串类型的属性字段。";
                }

                MessageBox.Show(
                    info,
                    "属性识别 (" + topFeatureLayer.Name + ")",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "识别时发生错误：\n" + ex.Message,
                    "识别错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            string keyword = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                MessageBox.Show("请输入要搜索的关键字。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 1. 获取当前要操作的图层
            IFeatureLayer targetLayer = GetCurrentFeatureLayer();
            if (targetLayer == null)
            {
                MessageBox.Show("当前没有可用的要素图层。请在图层目录中选择一个图层。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            IFeatureClass fc = targetLayer.FeatureClass;
            if (fc == null)
            {
                MessageBox.Show("该图层没有要素类。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. 这里先暂时固定使用字段 "Name" 做查询（后面可以改成弹出字段选择）
            string fieldName = "Name";
            int idx = fc.FindField(fieldName);
            if (idx < 0)
            {
                MessageBox.Show("图层中不存在字段 \"" + fieldName + "\"。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3. 构造 SQL 条件：[字段名] LIKE '%关键字%'
            string whereClause = "[" + fieldName + "] LIKE '%" + keyword.Replace("'", "''") + "%'";

            IQueryFilter queryFilter = new QueryFilterClass();
            queryFilter.WhereClause = whereClause;

            // 4. 执行选择
            IFeatureSelection featureSelection = targetLayer as IFeatureSelection;
            if (featureSelection == null)
            {
                MessageBox.Show("该图层不支持要素选择。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            featureSelection.Clear();

            featureSelection.SelectFeatures(
                queryFilter,
                esriSelectionResultEnum.esriSelectionResultNew,
                false);

            // 5. 获取选择范围并缩放
            ISelectionSet selectionSet = featureSelection.SelectionSet;
            if (selectionSet == null || selectionSet.Count == 0)
            {
                axMapControl1.Refresh();
                MessageBox.Show("没有找到匹配的要素。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IEnvelope env = new EnvelopeClass();
            bool first = true;

            ICursor cursor;
            selectionSet.Search(null, true, out cursor);
            IFeatureCursor featCursor = cursor as IFeatureCursor;
            IFeature feature = featCursor.NextFeature();

            while (feature != null)
            {
                if (first)
                {
                    env = feature.Extent;
                    first = false;
                }
                else
                {
                    env.Union(feature.Extent);
                }

                feature = featCursor.NextFeature();
            }

            if (!env.IsEmpty)
            {
                env.Expand(1.2, 1.2, true);
                axMapControl1.ActiveView.Extent = env;
            }

            axMapControl1.ActiveView.PartialRefresh(
                esriViewDrawPhase.esriViewGeography, null, null);
            axMapControl1.ActiveView.PartialRefresh(
                esriViewDrawPhase.esriViewGeoSelection, null, null);
        }

        private void btnBoxSelect_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. 获取当前要操作的图层
                IFeatureLayer targetLayer = GetCurrentFeatureLayer();
                if (targetLayer == null)
                {
                    MessageBox.Show("当前没有可用的要素图层。请在图层目录中选择一个图层。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IFeatureClass fc = targetLayer.FeatureClass;
                if (fc == null)
                {
                    MessageBox.Show("该图层没有要素类。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 2. 在地图上画矩形框
                IEnvelope env = axMapControl1.TrackRectangle() as IEnvelope;
                if (env == null || env.IsEmpty)
                    return;

                // 3. 用 ISpatialFilter 做空间查询（Intersects）
                ISpatialFilter spatialFilter = new SpatialFilterClass();
                spatialFilter.Geometry = env;
                spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelIntersects;
                spatialFilter.GeometryField = fc.ShapeFieldName;

                IFeatureSelection featureSelection = targetLayer as IFeatureSelection;
                if (featureSelection == null)
                {
                    MessageBox.Show("该图层不支持要素选择。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                featureSelection.Clear();

                featureSelection.SelectFeatures(
                    spatialFilter,
                    esriSelectionResultEnum.esriSelectionResultNew,
                    false);

                // 4. 没有选到要素则提示
                ISelectionSet selSet = featureSelection.SelectionSet;
                if (selSet == null || selSet.Count == 0)
                {
                    axMapControl1.Refresh();
                    MessageBox.Show("框选区域内没有要素。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 5. 刷新显示选中的要素
                axMapControl1.ActiveView.PartialRefresh(
                    esriViewDrawPhase.esriViewGeography, null, null);
                axMapControl1.ActiveView.PartialRefresh(
                    esriViewDrawPhase.esriViewGeoSelection, null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "框选查询时发生错误：\n" + ex.Message,
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }



    }
    // 简单的字段选择对话框
    internal class FieldSelectForm : Form
    {
        private ComboBox comboFields;
        private Button btnOk;
        private Button btnCancel;

        public string SelectedFieldName
        {
            get
            {
                return comboFields.SelectedItem as string;
            }
        }

        public FieldSelectForm(System.Collections.Generic.IEnumerable<string> fieldNames)
        {
            this.Text = "选择渲染字段";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Width = 320;
            this.Height = 150;

            comboFields = new ComboBox();
            comboFields.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFields.Left = 15;
            comboFields.Top = 15;
            comboFields.Width = 270;

            foreach (var name in fieldNames)
            {
                comboFields.Items.Add(name);
            }
            if (comboFields.Items.Count > 0)
                comboFields.SelectedIndex = 0;

            btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Left = 70;
            btnOk.Top = 60;
            btnOk.Width = 75;

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Left = 165;
            btnCancel.Top = 60;
            btnCancel.Width = 75;

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Controls.Add(comboFields);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);
        }

    }
}
