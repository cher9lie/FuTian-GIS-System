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
                string resourcesFolder = Path.Combine(Application.StartupPath, "Resources");
                string mxdPath = Path.Combine(resourcesFolder, "Futian.mxd");

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
    }
}
