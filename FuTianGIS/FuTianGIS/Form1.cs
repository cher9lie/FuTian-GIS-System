using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms.DataVisualization.Charting;
using System.Runtime.InteropServices;
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
using ESRI.ArcGIS.Output;
using ESRI.ArcGIS.NetworkAnalystTools;
using ESRI.ArcGIS.NetworkAnalyst;
using ESRI.ArcGIS.Geoprocessing;
using ESRI.ArcGIS.GeoDatabaseExtensions;

namespace FuTianGIS
{
    public partial class MainForm : Form
    {
        // ====== 路径分析相关字段 ======
        private INAContext _naContext;              // Network Analyst 上下文
        private bool _routeStartSet = false;        // 是否已经设置起点
        private IPoint _routeStartPoint;            // 起点坐标（用户第一次点击）
        private IPoint _routeEndPoint;              // 终点坐标（用户第二次点击）
        private IElement _routeStartElement;        // 地图上显示起点的图形元素
        private IElement _routeEndElement;          // 地图上显示终点的图形元素
        private IElement _routePathElement;         // 地图上显示路径的线要素
        private bool _isSettingRoutePoints = false; // 是否处在“设置起终点模式”
        // 是否处于“新增点”模式
        private bool _isAddingPoint = false;
        // 最近一次生成的缓冲几何，供功能 10 复用
        private IGeometry _lastBufferGeometry;
        // ... 你已有的字段 ...
        private string _lastStatsFieldName = null;
        // 统计窗口引用（选中变化时重用）
        private StatsChartForm _statsForm;
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
                // 0. 路径分析：设置起点/终点模式优先
                if (_isSettingRoutePoints)
                {
                    if (e.button != 1)
                        return;

                    IPoint clickPoint = axMapControl1.ActiveView.ScreenDisplay.DisplayTransformation.ToMapPoint(e.x, e.y);

                    IMap map = axMapControl1.Map;
                    IGraphicsContainer gc = map as IGraphicsContainer;
                    IActiveView av = map as IActiveView;

                    if (!_routeStartSet)
                    {
                        // 设置起点
                        _routeStartPoint = clickPoint;
                        _routeStartSet = true;

                        // 画一个绿色圆点作为起点
                        IRgbColor color = new RgbColorClass();
                        color.Red = 0; color.Green = 255; color.Blue = 0;

                        ISimpleMarkerSymbol sms = new SimpleMarkerSymbolClass();
                        sms.Style = esriSimpleMarkerStyle.esriSMSCircle;
                        sms.Size = 10;
                        sms.Color = color;

                        IMarkerElement me = new MarkerElementClass();
                        me.Symbol = sms;

                        _routeStartElement = me as IElement;
                        _routeStartElement.Geometry = _routeStartPoint;

                        if (gc != null)
                        {
                            gc.AddElement(_routeStartElement, 0);
                            if (av != null)
                                av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);
                        }

                        MessageBox.Show("起点已设置，请点击终点。", "路径分析",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        // 设置终点
                        _routeEndPoint = clickPoint;
                        _isSettingRoutePoints = false; // 结束设置模式

                        // 画一个红色圆点作为终点
                        IRgbColor color = new RgbColorClass();
                        color.Red = 255; color.Green = 0; color.Blue = 0;

                        ISimpleMarkerSymbol sms = new SimpleMarkerSymbolClass();
                        sms.Style = esriSimpleMarkerStyle.esriSMSCircle;
                        sms.Size = 10;
                        sms.Color = color;

                        IMarkerElement me = new MarkerElementClass();
                        me.Symbol = sms;

                        _routeEndElement = me as IElement;
                        _routeEndElement.Geometry = _routeEndPoint;

                        if (gc != null)
                        {
                            gc.AddElement(_routeEndElement, 0);
                            if (av != null)
                                av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);
                        }

                        // 起终点都有了，开始求最短路径
                        SolveRoute();
                    }

                    return; // 在路径模式下不再执行后面的新增点/识别逻辑
                }
                // 处理“新增点模式”
                if (_isAddingPoint)
                {
                    if (e.button != 1)
                        return;

                    // 使用当前图层作为新增目标图层
                    IFeatureLayer layer = GetCurrentFeatureLayer();
                    if (layer == null || layer.FeatureClass == null)
                    {
                        MessageBox.Show("当前没有可用的要素图层，无法新增点。", "新增点",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _isAddingPoint = false;
                        return;
                    }

                    IFeatureClass fc = layer.FeatureClass;
                    if (fc.ShapeType != esriGeometryType.esriGeometryPoint &&
                        fc.ShapeType != esriGeometryType.esriGeometryMultipoint)
                    {
                        MessageBox.Show("当前图层不是点图层，无法新增点。", "新增点",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _isAddingPoint = false;
                        return;
                    }

                    // 1. 屏幕坐标转地图坐标
                    IPoint mapPoint = axMapControl1.ActiveView.ScreenDisplay.DisplayTransformation.ToMapPoint(e.x, e.y);

                    // 2. 启动编辑会话
                    IDataset ds = fc as IDataset;
                    if (ds == null || ds.Workspace == null)
                    {
                        MessageBox.Show("无法获取要素类所在的工作空间，不能编辑。", "新增点",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        _isAddingPoint = false;
                        return;
                    }

                    IWorkspace workspace = ds.Workspace;
                    IWorkspaceEdit workspaceEdit = workspace as IWorkspaceEdit;
                    if (workspaceEdit == null)
                    {
                        MessageBox.Show("该数据源不支持编辑（可能是只读或非可编辑类型）。", "新增点",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _isAddingPoint = false;
                        return;
                    }

                    bool startedHere = false;
                    if (!workspaceEdit.IsBeingEdited())
                    {
                        workspaceEdit.StartEditing(false);
                        workspaceEdit.StartEditOperation();
                        startedHere = true;
                    }
                    else
                    {
                        workspaceEdit.StartEditOperation();
                    }

                    // 3. 创建新要素并设置几何
                    IFeature newFeature = fc.CreateFeature();
                    newFeature.Shape = mapPoint;

                    // 4. 弹出多字段编辑窗体，让用户填写属性
                    using (FeatureAttributeEditForm attrForm = new FeatureAttributeEditForm(fc.Fields))
                    {
                        if (attrForm.ShowDialog(this) == DialogResult.OK)
                        {
                            foreach (var item in attrForm.EditedItems)
                            {
                                int idx = fc.FindField(item.FieldName);
                                if (idx < 0) continue;

                                IField f = fc.Fields.get_Field(idx);
                                string txt = item.Value;

                                if (string.IsNullOrEmpty(txt))
                                {
                                    // 留空就不赋值或给 NULL
                                    continue;
                                }

                                object valObj = null;

                                switch (f.Type)
                                {
                                    case esriFieldType.esriFieldTypeString:
                                        valObj = txt;
                                        break;
                                    case esriFieldType.esriFieldTypeInteger:
                                    case esriFieldType.esriFieldTypeSmallInteger:
                                        int intVal;
                                        if (int.TryParse(txt, out intVal))
                                            valObj = intVal;
                                        break;
                                    case esriFieldType.esriFieldTypeDouble:
                                    case esriFieldType.esriFieldTypeSingle:
                                        double dblVal;
                                        if (double.TryParse(txt, out dblVal))
                                            valObj = dblVal;
                                        break;
                                    case esriFieldType.esriFieldTypeDate:
                                        DateTime dtVal;
                                        if (DateTime.TryParse(txt, out dtVal))
                                            valObj = dtVal;
                                        break;
                                    default:
                                        break;
                                }

                                if (valObj != null)
                                {
                                    newFeature.set_Value(idx, valObj);
                                }
                            }

                            // 5. 保存要素
                            newFeature.Store();

                            workspaceEdit.StopEditOperation();
                            if (startedHere)
                            {
                                workspaceEdit.StopEditing(true);
                            }

                            axMapControl1.ActiveView.PartialRefresh(esriViewDrawPhase.esriViewGeography, null, null);

                            MessageBox.Show("已成功新增一个点要素。", "新增点",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            // 用户取消输入，撤销本次编辑
                            workspaceEdit.AbortEditOperation();
                            if (startedHere)
                            {
                                workspaceEdit.StopEditing(false); // 不保存
                            }
                        }
                    }

                    // 一次点击完成后退出新增模式（如需连续新增，可以不退出）
                    _isAddingPoint = false;

                    return;
                }
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
                IPoint mapPoint2 = axMapControl1.ActiveView.ScreenDisplay.DisplayTransformation.ToMapPoint(x, y);

                // 找到最上面的可见 FeatureLayer
                IMap routemap = axMapControl1.Map;
                IFeatureLayer topFeatureLayer = null;

                for (int iLayer = 0; iLayer < routemap.LayerCount; iLayer++)
                {
                    ILayer layer = routemap.get_Layer(iLayer);
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

                IArray idResultArray = identify.Identify(mapPoint2);
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

        private void axMapControl1_OnSelectionChanged(object sender, EventArgs e)
        {
    try
    {
        // 1. 获取当前要操作的图层
        IFeatureLayer layer = GetCurrentFeatureLayer();
        if (layer == null || layer.FeatureClass == null)
        {
            if (_statsForm != null && !_statsForm.IsDisposed)
                _statsForm.Close();
            return;
        }

        IFeatureSelection featSel = layer as IFeatureSelection;
        if (featSel == null || featSel.SelectionSet == null || featSel.SelectionSet.Count == 0)
        {
            // 没有选中要素，关闭统计窗
            if (_statsForm != null && !_statsForm.IsDisposed)
                _statsForm.Close();
            return;
        }

        IFeatureClass fc = layer.FeatureClass;

        // 2. 每次都重新弹出对话框选择统计字段
        List<string> candidates = new List<string>();
        IFields fields = fc.Fields;

        for (int i = 0; i < fields.FieldCount; i++)
        {
            IField f = fields.get_Field(i);

            // 只把适合做统计的字段加入，例如字符串、整数、小整数
            if ((f.Type == esriFieldType.esriFieldTypeString ||
                 f.Type == esriFieldType.esriFieldTypeInteger ||
                 f.Type == esriFieldType.esriFieldTypeSmallInteger) &&
                !f.Name.Equals(fc.ShapeFieldName, StringComparison.OrdinalIgnoreCase) &&
                !f.Name.Equals(fc.OIDFieldName, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(f.Name);
            }
        }

        if (candidates.Count == 0)
        {
            MessageBox.Show("当前图层没有适合用于统计的字段。", "统计",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (_statsForm != null && !_statsForm.IsDisposed)
                _statsForm.Close();
            return;
        }

        string fieldName = null;
        using (FieldSelectForm dlg = new FieldSelectForm(candidates))
        {
            dlg.Text = "选择统计字段";
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                // 用户取消，不统计
                return;
            }

            fieldName = dlg.SelectedFieldName;
        }

        if (string.IsNullOrEmpty(fieldName))
            return;

        int fieldIndex = fc.FindField(fieldName);
        if (fieldIndex < 0)
            return;

        // 3. 遍历选择集，统计各个字段值的数量
        Dictionary<string, int> counts = new Dictionary<string, int>();

        ISelectionSet selSet = featSel.SelectionSet;
        ICursor cursor;
        selSet.Search(null, true, out cursor);
        IFeatureCursor fCursor = cursor as IFeatureCursor;
        IFeature feature = fCursor.NextFeature();

        while (feature != null)
        {
            object val = feature.get_Value(fieldIndex);
            string key = (val == null || val == DBNull.Value) ? "(空)" : val.ToString();

            if (!counts.ContainsKey(key))
                counts[key] = 0;
            counts[key]++;

            feature = fCursor.NextFeature();
        }

        if (counts.Count == 0)
        {
            if (_statsForm != null && !_statsForm.IsDisposed)
                _statsForm.Close();
            return;
        }

        // 4. 打开或更新统计窗口
        if (_statsForm == null || _statsForm.IsDisposed)
        {
            _statsForm = new StatsChartForm();
            _statsForm.Show(this);   // 非模态窗口
        }

        var chart = _statsForm.ChartControl;
        chart.Series.Clear();

        var series = chart.Series.Add("统计");
        series.ChartType = SeriesChartType.Column;
        series.IsValueShownAsLabel = true;

        foreach (var kv in counts)
        {
            series.Points.AddXY(kv.Key, kv.Value);
        }

        if (chart.ChartAreas.Count > 0)
        {
            chart.ChartAreas[0].AxisX.Interval = 1;
        }

        _statsForm.Text = "统计图表 - 图层：{layer.Name}，字段：{fieldName}";
    }
    catch (Exception ex)
    {
        MessageBox.Show("更新统计图表时出错：\n" + ex.Message,
            "统计错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
   }
        /// <summary>
        /// 统计图表窗口：单独弹出的 Form，内部一个 Chart 控件
        /// </summary>
        internal class StatsChartForm : Form
        {
            public System.Windows.Forms.DataVisualization.Charting.Chart ChartControl { get; private set; }

            public StatsChartForm()
            {
                this.Text = "统计图表";
                this.StartPosition = FormStartPosition.CenterParent;
                this.Width = 600;
                this.Height = 400;

                ChartControl = new System.Windows.Forms.DataVisualization.Charting.Chart();
                ChartControl.Dock = DockStyle.Fill;

                // 基本 ChartArea / Series（后面代码会重建 Series，这里只是初始化）
                var chartArea = new System.Windows.Forms.DataVisualization.Charting.ChartArea("ChartArea1");
                ChartControl.ChartAreas.Add(chartArea);

                this.Controls.Add(ChartControl);
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
        /// <summary>
        /// 简单字符串输入对话框，用于让用户输入属性新值等
        /// </summary>
        internal class InputTextForm : Form
        {
            private TextBox txtValue;
            private Button btnOk;
            private Button btnCancel;

            public string InputText
            {
                get { return txtValue.Text; }
            }

            public InputTextForm(string title, string labelText, string defaultValue = "")
            {
                this.Text = title;
                this.StartPosition = FormStartPosition.CenterParent;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.Width = 360;
                this.Height = 160;

                Label lbl = new Label();
                lbl.Text = labelText;
                lbl.Left = 15;
                lbl.Top = 15;
                lbl.AutoSize = true;

                txtValue = new TextBox();
                txtValue.Left = 15;
                txtValue.Top = 40;
                txtValue.Width = 320;
                txtValue.Text = defaultValue;

                btnOk = new Button();
                btnOk.Text = "确定";
                btnOk.DialogResult = DialogResult.OK;
                btnOk.Left = 80;
                btnOk.Top = 80;
                btnOk.Width = 80;

                btnCancel = new Button();
                btnCancel.Text = "取消";
                btnCancel.DialogResult = DialogResult.Cancel;
                btnCancel.Left = 180;
                btnCancel.Top = 80;
                btnCancel.Width = 80;

                this.AcceptButton = btnOk;
                this.CancelButton = btnCancel;

                this.Controls.Add(lbl);
                this.Controls.Add(txtValue);
                this.Controls.Add(btnOk);
                this.Controls.Add(btnCancel);
            }
        }
        /// <summary>
        /// 多字段属性编辑窗口：列出可编辑字段，让用户逐个填写值
        /// </summary>
        internal class FeatureAttributeEditForm : Form
        {
            private DataGridView dgv;
            private Button btnOk;
            private Button btnCancel;

            public class FieldEditItem
            {
                public string FieldName { get; set; }
                public string FieldAlias { get; set; }
                public string FieldType { get; set; }
                public string Value { get; set; }
            }

            private BindingList<FieldEditItem> _items;

            public IEnumerable<FieldEditItem> EditedItems
            {
                get { return _items; }
            }

            public FeatureAttributeEditForm(IFields fields)
            {
                this.Text = "编辑属性";
                this.StartPosition = FormStartPosition.CenterParent;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.Width = 500;
                this.Height = 400;

                dgv = new DataGridView();
                dgv.Dock = DockStyle.Top;
                dgv.Height = 300;
                dgv.AutoGenerateColumns = false;
                dgv.AllowUserToAddRows = false;
                dgv.AllowUserToDeleteRows = false;

                // 列：字段名（只读）、别名（只读）、类型（只读）、值（可编辑）
                DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn();
                colName.HeaderText = "字段名";
                colName.DataPropertyName = "FieldName";
                colName.ReadOnly = true;
                colName.Width = 120;

                DataGridViewTextBoxColumn colAlias = new DataGridViewTextBoxColumn();
                colAlias.HeaderText = "别名";
                colAlias.DataPropertyName = "FieldAlias";
                colAlias.ReadOnly = true;
                colAlias.Width = 150;

                DataGridViewTextBoxColumn colType = new DataGridViewTextBoxColumn();
                colType.HeaderText = "类型";
                colType.DataPropertyName = "FieldType";
                colType.ReadOnly = true;
                colType.Width = 80;

                DataGridViewTextBoxColumn colValue = new DataGridViewTextBoxColumn();
                colValue.HeaderText = "值";
                colValue.DataPropertyName = "Value";
                colValue.ReadOnly = false;
                colValue.Width = 120;

                dgv.Columns.Add(colName);
                dgv.Columns.Add(colAlias);
                dgv.Columns.Add(colType);
                dgv.Columns.Add(colValue);

                btnOk = new Button();
                btnOk.Text = "确定";
                btnOk.DialogResult = DialogResult.OK;
                btnOk.Left = 150;
                btnOk.Top = 320;
                btnOk.Width = 80;

                btnCancel = new Button();
                btnCancel.Text = "取消";
                btnCancel.DialogResult = DialogResult.Cancel;
                btnCancel.Left = 260;
                btnCancel.Top = 320;
                btnCancel.Width = 80;

                this.AcceptButton = btnOk;
                this.CancelButton = btnCancel;

                this.Controls.Add(dgv);
                this.Controls.Add(btnOk);
                this.Controls.Add(btnCancel);

                // 初始化字段列表：只列出可编辑、非几何、非OID字段
                _items = new BindingList<FieldEditItem>();

                for (int i = 0; i < fields.FieldCount; i++)
                {
                    IField f = fields.get_Field(i);

                    if (!f.Editable) continue;
                    if (f.Type == esriFieldType.esriFieldTypeGeometry ||
                        f.Type == esriFieldType.esriFieldTypeOID)
                        continue;

                    FieldEditItem item = new FieldEditItem();
                    item.FieldName = f.Name;
                    item.FieldAlias = f.AliasName;
                    item.FieldType = f.Type.ToString();
                    item.Value = ""; // 默认空，由用户填写

                    _items.Add(item);
                }

                dgv.DataSource = _items;
            }
        }


        private void btnBuffer_Click(object sender, EventArgs e)
{
    try
    {
        // 1. 获取当前要操作的图层
        IFeatureLayer layer = GetCurrentFeatureLayer();
        if (layer == null || layer.FeatureClass == null)
        {
            MessageBox.Show("当前没有可用的要素图层。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        IFeatureSelection featSel = layer as IFeatureSelection;
        if (featSel == null || featSel.SelectionSet == null || featSel.SelectionSet.Count == 0)
        {
            MessageBox.Show("请先在地图上选择至少一个要素（通过搜索或拉框选择）。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        IFeatureClass fc = layer.FeatureClass;

        // 2. 取第一个选中要素的几何，生成缓冲（以米为单位假设投影坐标）
        ISelectionSet selSet = featSel.SelectionSet;
        ICursor cursor;
        selSet.Search(null, true, out cursor);
        IFeatureCursor fCursor = cursor as IFeatureCursor;
        IFeature feature = fCursor.NextFeature();

        if (feature == null || feature.Shape == null || feature.Shape.IsEmpty)
        {
            MessageBox.Show("选中要素没有有效几何。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        IGeometry geom = feature.ShapeCopy; // 使用副本
        ITopologicalOperator topo = geom as ITopologicalOperator;
        if (topo == null)
        {
            MessageBox.Show("该要素不支持缓冲运算。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 缓冲半径 500（单位取决于地图投影，一般是米）
        double bufferDistance = 500.0;
        IGeometry bufferGeom = topo.Buffer(bufferDistance);

        if (bufferGeom == null || bufferGeom.IsEmpty)
        {
            MessageBox.Show("缓冲区生成失败。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 3. 把缓冲几何画到地图的图形层上（红色半透明面）
        IMap map = axMapControl1.Map;
        IActiveView activeView = map as IActiveView;
        IGraphicsContainer graphicsContainer = map as IGraphicsContainer;

        if (graphicsContainer == null)
        {
            MessageBox.Show("无法获取图形容器。", "缓冲区",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 可选：先清除之前生成的缓冲元素（如果你只想保留最新一个）
        graphicsContainer.DeleteAllElements();

        // 创建符号：红色半透明填充 + 红色边线
        IRgbColor fillColor = new RgbColorClass();
        fillColor.Red = 255;
        fillColor.Green = 0;
        fillColor.Blue = 0;
        fillColor.Transparency = 80;   // 0–255，越小越透明

        IRgbColor outlineColor = new RgbColorClass();
        outlineColor.Red = 255;
        outlineColor.Green = 0;
        outlineColor.Blue = 0;
        outlineColor.Transparency = 255;

        ISimpleLineSymbol outline = new SimpleLineSymbolClass();
        outline.Color = outlineColor;
        outline.Width = 1.5;
        outline.Style = esriSimpleLineStyle.esriSLSSolid;

        ISimpleFillSymbol fillSymbol = new SimpleFillSymbolClass();
        fillSymbol.Color = fillColor;
        fillSymbol.Style = esriSimpleFillStyle.esriSFSSolid;
        fillSymbol.Outline = outline;

        IFillShapeElement fillElement = new PolygonElementClass();
        IElement element = (IElement)fillElement;
        element.Geometry = bufferGeom;
        fillElement.Symbol = fillSymbol;

        graphicsContainer.AddElement(element, 0);

        // 4. 保存缓冲几何，供后续邻近查询使用
        _lastBufferGeometry = bufferGeom;

        // 5. 刷新图形层
        activeView.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

        MessageBox.Show("已在图形层生成 {bufferDistance} 范围的缓冲区。", "缓冲区",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show("缓冲区分析时发生错误：\n" + ex.Message,
            "缓冲区错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

        private void btnSelectByBuffer_Click(object sender, EventArgs e)
        {
            try
            {
                if (_lastBufferGeometry == null || _lastBufferGeometry.IsEmpty)
                {
                    MessageBox.Show("尚未生成缓冲区，请先对选中要素执行“缓冲区”操作。", "缓冲选取",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                IMap map = axMapControl1.Map;
                if (map == null || map.LayerCount == 0)
                {
                    MessageBox.Show("当前地图中没有图层。", "缓冲选取",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<IFeatureLayer> featureLayers = new List<IFeatureLayer>();
                List<string> layerNames = new List<string>();

                for (int i = 0; i < map.LayerCount; i++)
                {
                    ILayer lyr = map.get_Layer(i);
                    IFeatureLayer fl = lyr as IFeatureLayer;
                    if (fl != null)
                    {
                        featureLayers.Add(fl);
                        layerNames.Add(fl.Name);
                    }
                }

                if (featureLayers.Count == 0)
                {
                    MessageBox.Show("当前地图中没有要素图层可供查询。", "缓冲选取",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string targetLayerName = null;
                using (Form dlg = new Form())
                {
                    dlg.Text = "选择邻近查询的目标图层";
                    dlg.StartPosition = FormStartPosition.CenterParent;
                    dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dlg.MaximizeBox = false;
                    dlg.MinimizeBox = false;
                    dlg.Width = 350;
                    dlg.Height = 150;

                    ComboBox cmb = new ComboBox();
                    cmb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cmb.Left = 15;
                    cmb.Top = 15;
                    cmb.Width = 300;

                    foreach (string name in layerNames)
                        cmb.Items.Add(name);
                    if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;

                    Button btnOk = new Button();
                    btnOk.Text = "确定";
                    btnOk.DialogResult = DialogResult.OK;
                    btnOk.Left = 70;
                    btnOk.Top = 60;
                    btnOk.Width = 80;

                    Button btnCancel = new Button();
                    btnCancel.Text = "取消";
                    btnCancel.DialogResult = DialogResult.Cancel;
                    btnCancel.Left = 170;
                    btnCancel.Top = 60;
                    btnCancel.Width = 80;

                    dlg.AcceptButton = btnOk;
                    dlg.CancelButton = btnCancel;
                    dlg.Controls.Add(cmb);
                    dlg.Controls.Add(btnOk);
                    dlg.Controls.Add(btnCancel);

                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;

                    targetLayerName = cmb.SelectedItem as string;
                }

                if (string.IsNullOrEmpty(targetLayerName))
                    return;

                IFeatureLayer targetLayer = null;
                for (int i = 0; i < featureLayers.Count; i++)
                {
                    if (featureLayers[i].Name == targetLayerName)
                    {
                        targetLayer = featureLayers[i];
                        break;
                    }
                }

                if (targetLayer == null || targetLayer.FeatureClass == null)
                {
                    MessageBox.Show("未能获取目标图层。", "缓冲选取",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IFeatureClass targetFc = targetLayer.FeatureClass;
                ISpatialFilter spatialFilter = new SpatialFilterClass();
                spatialFilter.Geometry = _lastBufferGeometry;
                spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelIntersects;
                spatialFilter.GeometryField = targetFc.ShapeFieldName;

                IFeatureSelection targetSelection = targetLayer as IFeatureSelection;
                if (targetSelection == null)
                {
                    MessageBox.Show("目标图层不支持要素选择。", "缓冲选取",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                targetSelection.Clear();
                targetSelection.SelectFeatures(
                    spatialFilter,
                    esriSelectionResultEnum.esriSelectionResultNew,
                    false);

                ISelectionSet selSetRes = targetSelection.SelectionSet;
                int count = (selSetRes == null) ? 0 : selSetRes.Count;

                IActiveView activeView = map as IActiveView;
                activeView.PartialRefresh(esriViewDrawPhase.esriViewGeography, null, null);
                activeView.PartialRefresh(esriViewDrawPhase.esriViewGeoSelection, null, null);

                MessageBox.Show(
                    string.Format("在缓冲区范围内，共找到 {0} 个要素。\n\n目标图层：{1}", count, targetLayer.Name),
                    "缓冲选取结果",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("缓冲选取时发生错误：\n" + ex.Message,
                    "缓冲选取错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            try
            {
                IActiveView activeView = axMapControl1.ActiveView;
                if (activeView == null)
                {
                    MessageBox.Show("当前没有可导出的地图视图。", "导出 PDF",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 1. 选择导出路径（也可以直接写死到程序目录，这里用对话框更友好）
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "导出地图为 PDF";
                    dlg.Filter = "PDF 文件 (*.pdf)|*.pdf";
                    dlg.FileName = "MapOutput.pdf";

                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;

                    string outPath = dlg.FileName;
                    if (string.IsNullOrEmpty(outPath))
                        return;

                    // 2. 创建 PDF 导出对象
                    IExport export = new ExportPDFClass();
                    export.ExportFileName = outPath;

                    // 3. 设置分辨率（DPI）
                    export.Resolution = 300;   // 300 dpi，一般打印质量

                    tagRECT exportRect;
                    exportRect.left = 0;
                    exportRect.top = 0;
                    exportRect.right = activeView.ExportFrame.right;
                    exportRect.bottom = activeView.ExportFrame.bottom;
                    export.PixelBounds = new EnvelopeClass
                    {
                        XMin = exportRect.left,
                        YMin = exportRect.top,
                        XMax = exportRect.right,
                        YMax = exportRect.bottom
                    };

                    // 4. 开始导出
                    int hDC = export.StartExporting();
                    activeView.Output(hDC, (int)export.Resolution, ref exportRect, null, null);
                    export.FinishExporting();
                    export.Cleanup();

                    // 可选：释放 COM
                    Marshal.ReleaseComObject(export);

                    MessageBox.Show("地图已成功导出为 PDF：\n" + outPath,
                        "导出 PDF",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出 PDF 时发生错误：\n" + ex.Message,
                    "导出 PDF 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. 获取当前图层和选中要素
                IFeatureLayer layer = GetCurrentFeatureLayer();
                if (layer == null || layer.FeatureClass == null)
                {
                    MessageBox.Show("当前没有可用的要素图层。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IFeatureSelection featSel = layer as IFeatureSelection;
                if (featSel == null || featSel.SelectionSet == null || featSel.SelectionSet.Count == 0)
                {
                    MessageBox.Show("请先在地图上选择至少一个要素。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                IFeatureClass fc = layer.FeatureClass;
                IFields fields = fc.Fields;

                // 2. 让用户选择要修改的字段（排除几何字段、OID 等）
                List<string> editableFields = new List<string>();
                for (int i = 0; i < fields.FieldCount; i++)
                {
                    IField f = fields.get_Field(i);

                    // 排除几何、OID、只读字段等（这里只做简单判断）
                    if (f.Type == esriFieldType.esriFieldTypeGeometry ||
                        f.Type == esriFieldType.esriFieldTypeOID)
                        continue;

                    if (f.Editable)
                        editableFields.Add(f.Name);
                }

                if (editableFields.Count == 0)
                {
                    MessageBox.Show("当前图层中没有可编辑的属性字段。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string fieldName = null;
                using (FieldSelectForm dlgField = new FieldSelectForm(editableFields))
                {
                    dlgField.Text = "选择要修改的字段";
                    if (dlgField.ShowDialog(this) != DialogResult.OK)
                        return;

                    fieldName = dlgField.SelectedFieldName;
                }

                if (string.IsNullOrEmpty(fieldName))
                    return;

                int fieldIndex = fc.FindField(fieldName);
                if (fieldIndex < 0)
                {
                    MessageBox.Show("无法在要素类中找到字段 \"" + fieldName + "\"。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IField targetField = fields.get_Field(fieldIndex);

                // 3. 让用户输入新的属性值（以字符串形式）
                string newValueStr = null;
                using (InputTextForm dlgInput = new InputTextForm(
                    "输入新属性值",
                    "请输入字段 \"" + fieldName + "\" 的新值：",
                    "已整改"))
                {
                    if (dlgInput.ShowDialog(this) != DialogResult.OK)
                        return;

                    newValueStr = dlgInput.InputText;
                }

                if (newValueStr == null)
                    return;

                // 4. 启动编辑会话
                IDataset ds = fc as IDataset;
                if (ds == null || ds.Workspace == null)
                {
                    MessageBox.Show("无法获取要素类所在的工作空间，不能编辑。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                IWorkspace workspace = ds.Workspace;
                IWorkspaceEdit workspaceEdit = workspace as IWorkspaceEdit;
                if (workspaceEdit == null)
                {
                    MessageBox.Show("该数据源不支持编辑（可能是只读或非可编辑类型）。", "属性编辑",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool startedHere = false;
                if (!workspaceEdit.IsBeingEdited())
                {
                    workspaceEdit.StartEditing(false);
                    workspaceEdit.StartEditOperation();
                    startedHere = true;
                }
                else
                {
                    workspaceEdit.StartEditOperation();
                }

                // 5. 遍历选中要素，按字段类型把字符串转换成合适类型后写入
                ISelectionSet selSet = featSel.SelectionSet;
                ICursor cursor;
                selSet.Search(null, false, out cursor);
                IFeatureCursor fCursor = cursor as IFeatureCursor;
                IFeature feature = fCursor.NextFeature();

                int editedCount = 0;

                while (feature != null)
                {
                    object newValObj = null;

                    switch (targetField.Type)
                    {
                        case esriFieldType.esriFieldTypeString:
                            newValObj = newValueStr;
                            break;
                        case esriFieldType.esriFieldTypeInteger:
                        case esriFieldType.esriFieldTypeSmallInteger:
                            int intVal;
                            if (int.TryParse(newValueStr, out intVal))
                                newValObj = intVal;
                            else
                            {
                                // 如果解析失败，就不修改这个要素
                                feature = fCursor.NextFeature();
                                continue;
                            }
                            break;
                        case esriFieldType.esriFieldTypeDouble:
                        case esriFieldType.esriFieldTypeSingle:
                            double dblVal;
                            if (double.TryParse(newValueStr, out dblVal))
                                newValObj = dblVal;
                            else
                            {
                                feature = fCursor.NextFeature();
                                continue;
                            }
                            break;
                        case esriFieldType.esriFieldTypeDate:
                            DateTime dtVal;
                            if (DateTime.TryParse(newValueStr, out dtVal))
                                newValObj = dtVal;
                            else
                            {
                                feature = fCursor.NextFeature();
                                continue;
                            }
                            break;
                        default:
                            // 其他类型暂不支持，跳过
                            feature = fCursor.NextFeature();
                            continue;
                    }

                    feature.set_Value(fieldIndex, newValObj);
                    feature.Store();
                    editedCount++;

                    feature = fCursor.NextFeature();
                }

                workspaceEdit.StopEditOperation();
                if (startedHere)
                {
                    workspaceEdit.StopEditing(true);
                }

                IActiveView activeView = axMapControl1.ActiveView;
                activeView.PartialRefresh(esriViewDrawPhase.esriViewGeography, null, null);
                activeView.PartialRefresh(esriViewDrawPhase.esriViewGeoSelection, null, null);

                MessageBox.Show(
                    string.Format("已将 {0} 个要素的 \"{1}\" 字段修改为 \"{2}\"。", editedCount, fieldName, newValueStr),
                    "属性编辑",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("属性编辑时发生错误：\n" + ex.Message,
                    "属性编辑错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnAddPoint_Click(object sender, EventArgs e)
        {
            IFeatureLayer layer = GetCurrentFeatureLayer();
            if (layer == null || layer.FeatureClass == null)
            {
                MessageBox.Show("当前没有可用的要素图层。请在图层目录中选择要新增点的图层。", "新增点",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查图层是否为点图层
            IFeatureClass fc = layer.FeatureClass;
            if (fc.ShapeType != esriGeometryType.esriGeometryPoint &&
                fc.ShapeType != esriGeometryType.esriGeometryMultipoint)
            {
                MessageBox.Show("当前选中的图层不是点图层，无法新增点要素。", "新增点",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 标记进入新增模式
            _isAddingPoint = true;

            MessageBox.Show("请在地图上单击位置来创建一个新点要素。", "新增点",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 初始化 Network Analyst 上下文：
        /// 直接从当前地图中已存在的“路线”网络分析图层 (INALayer) 获取 INAContext。
        /// 这样 Stops/Routes 结构和求解参数与 ArcMap 中保持完全一致。
        /// </summary>
        private bool InitNetworkAnalysis()
        {
            try
            {
                if (_naContext != null)
                    return true;

                IMap map = axMapControl1.Map;
                if (map == null || map.LayerCount == 0)
                {
                    MessageBox.Show("当前地图中没有图层，无法初始化路径分析。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                // 1. 在地图中查找 Network Analyst 路径图层 (INALayer)
                INAContext foundContext = null;

                for (int i = 0; i < map.LayerCount; i++)
                {
                    ILayer lyr = map.get_Layer(i);

                    // 只要是 INALayer 就拿它的 Context
                    INALayer naLayer = lyr as INALayer;
                    if (naLayer != null && naLayer.Context != null)
                    {
                        foundContext = naLayer.Context;
                        break;
                    }
                }

                if (foundContext == null)
                {
                    MessageBox.Show(
                        "在当前地图中未找到网络分析“路线”图层。\n\n" +
                        "请确认 Futian.mxd 中已保存一个基于 Roads_Projected_ND 的路线分析图层，" +
                        "并勾选可见后再运行本程序。",
                        "路径分析",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }

                _naContext = foundContext;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化路径分析时发生错误：\n" + ex.ToString(),
                    "路径分析错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void SolveRoute()
        {

            try
            {
                if (_naContext == null)
                {
                    MessageBox.Show("网络分析上下文尚未初始化。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (_routeStartPoint == null || _routeEndPoint == null)
                {
                    MessageBox.Show("起点或终点未设置。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 1. 获取 Stops 和 Routes NAClass
                INAClass stopsNAClass = _naContext.NAClasses.get_ItemByName("Stops") as INAClass;
                INAClass routesNAClass = _naContext.NAClasses.get_ItemByName("Routes") as INAClass;
                if (stopsNAClass == null || routesNAClass == null)
                {
                    MessageBox.Show("在 NAContext 中未找到 \"Stops\" 或 \"Routes\" 类，请检查网络图层配置。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // （可选）调试：确认拿到的 NAClass 名称
                try
                {
                    string stopsName = stopsNAClass.ClassDefinition.Name;
                    string routesName = routesNAClass.ClassDefinition.Name;
                    MessageBox.Show(
                        "调试：获取到 NAClass\nStops = " + stopsName + "\nRoutes = " + routesName,
                        "路径分析调试");
                }
                catch { }

                // 2. 通过 ITable 接口操作 NAClass
                ITable stopsTable = stopsNAClass as ITable;
                ITable routesTable = routesNAClass as ITable;
                if (stopsTable == null || routesTable == null)
                {
                    MessageBox.Show("NAClass 未能转换为 ITable 接口。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 2.1 清空 Stops 表旧记录
                // 使用 DeleteSearchedRows 可以一次性删除 Search 出来的所有行
                IQueryFilter deleteFilter = new QueryFilterClass();
                deleteFilter.WhereClause = "1=1";
                stopsTable.DeleteSearchedRows(deleteFilter);

                // 3. 向 Stops 中添加起点和终点
                int shapeIndex = stopsTable.FindField("Shape");
                if (shapeIndex < 0)
                {
                    MessageBox.Show("Stops 表中未找到 Shape 字段。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 这两个字段在你的 Stops 表里是存在的
                int routeNameIndex = stopsTable.FindField("RouteName");   // 路线名
                int sequenceIndex = stopsTable.FindField("Sequence");    // 停靠点顺序

                string routeNameValue = "Route1";   // 任意非空字符串即可，与你的场景无关

                // 起点（序号 1）
                IRow fromRow = stopsTable.CreateRow();
                fromRow.set_Value(shapeIndex, _routeStartPoint);

                if (routeNameIndex >= 0)
                    fromRow.set_Value(routeNameIndex, routeNameValue);
                if (sequenceIndex >= 0)
                    fromRow.set_Value(sequenceIndex, 1);   // 起点 Sequence = 1

                fromRow.Store();

                // 终点（序号 2）
                IRow toRow = stopsTable.CreateRow();
                toRow.set_Value(shapeIndex, _routeEndPoint);

                if (routeNameIndex >= 0)
                    toRow.set_Value(routeNameIndex, routeNameValue);
                if (sequenceIndex >= 0)
                    toRow.set_Value(sequenceIndex, 2);     // 终点 Sequence = 2

                toRow.Store();

                // ★ 在调用 Solve 之前，加 RowCount 调试
                int stopCount = stopsTable.RowCount(null);
                MessageBox.Show("调试：当前 Stops 表中记录数 = " + stopCount,
                    "路径分析调试");

                // 4. 求解路径
                INASolver solver = _naContext.Solver;
                if (solver == null)
                {
                    MessageBox.Show("NAContext 中的 Solver 为空。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 这里先不再设置搜索容差，使用 Network Dataset 的默认配置
                IGPMessages gpMessages = new GPMessagesClass();
                solver.Solve(_naContext, gpMessages, null);

                // 如果 Solve 抛异常会直接到 catch，这里暂不解析 gpMessages

                // 5. 从 Routes 表读取路径几何
                int routeShapeIndex = routesTable.FindField("Shape");
                if (routeShapeIndex < 0)
                {
                    MessageBox.Show("Routes 表中未找到 Shape 字段。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ICursor routeCursor = routesTable.Search(null, false);
                IRow routeRow = routeCursor.NextRow();
                if (routeRow == null)
                {
                    MessageBox.Show("没有求得路径结果。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                IGeometry routeGeom = routeRow.get_Value(routeShapeIndex) as IGeometry;
                if (routeGeom == null || routeGeom.IsEmpty)
                {
                    MessageBox.Show("路径结果几何为空。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 6. 在地图上绘制路径
                IMap map = axMapControl1.Map;
                IGraphicsContainer gc = map as IGraphicsContainer;
                IActiveView av = map as IActiveView;

                if (gc == null || av == null)
                {
                    MessageBox.Show("无法获取地图图形容器。", "路径分析",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 删除旧路径
                if (_routePathElement != null)
                {
                    gc.DeleteElement(_routePathElement);
                    _routePathElement = null;
                }

                IRgbColor lineColor = new RgbColorClass();
                lineColor.Red = 0;
                lineColor.Green = 0;
                lineColor.Blue = 255;

                ISimpleLineSymbol lineSymbol = new SimpleLineSymbolClass();
                lineSymbol.Color = lineColor;
                lineSymbol.Width = 2.5;
                lineSymbol.Style = esriSimpleLineStyle.esriSLSSolid;

                ILineElement lineElement = new LineElementClass();
                lineElement.Symbol = lineSymbol;

                _routePathElement = lineElement as IElement;
                _routePathElement.Geometry = routeGeom;

                gc.AddElement(_routePathElement, 0);
                av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

                // 缩放到路径范围
                IEnvelope env = routeGeom.Envelope;
                if (env != null && !env.IsEmpty)
                {
                    env.Expand(1.2, 1.2, true);
                    av.Extent = env;
                    av.Refresh();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("求解路径时发生错误：\n" + ex.Message,
                    "路径分析错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 从 NetworkDataset 获取 DENetworkDataset（构建 NAContext 需要）
        /// </summary>
        private IDENetworkDataset GetDENetworkDatasetFromNetworkDataset(INetworkDataset networkDataset)
        {
            IDatasetComponent dsComponent = networkDataset as IDatasetComponent;
            if (dsComponent == null)
                return null;

            return dsComponent.DataElement as IDENetworkDataset;
        }

        private void btnRoute_Click(object sender, EventArgs e)
        {
            // 初始化 NAContext
            if (!InitNetworkAnalysis())
                return;

            // 清空之前的起终点和路径显示
            IMap map = axMapControl1.Map;
            IGraphicsContainer gc = map as IGraphicsContainer;
            IActiveView av = map as IActiveView;
            if (gc != null)
            {
                if (_routeStartElement != null) gc.DeleteElement(_routeStartElement);
                if (_routeEndElement != null) gc.DeleteElement(_routeEndElement);
                if (_routePathElement != null) gc.DeleteElement(_routePathElement);
                _routeStartElement = null;
                _routeEndElement = null;
                _routePathElement = null;
            }
            if (av != null)
                av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

            _routeStartSet = false;
            _routeStartPoint = null;
            _routeEndPoint = null;
            _isSettingRoutePoints = true;

            MessageBox.Show("请在地图上点击起点，然后再点击终点。", "路径分析",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
