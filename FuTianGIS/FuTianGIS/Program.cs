using System;
using System.Windows.Forms;
using ESRI.ArcGIS;
using ESRI.ArcGIS.esriSystem;

namespace FuTianGIS
{
    static class Program
    {
        // 声明许可初始化器
        private static LicenseInitializer m_AOLicenseInitializer = new LicenseInitializer();

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 1. 绑定 ArcGIS Engine/Runtime
            ESRI.ArcGIS.RuntimeManager.Bind(ESRI.ArcGIS.ProductCode.Engine);

            // 2. 初始化许可：Engine + Network Analyst 扩展
            IAoInitialize aoInit = new AoInitializeClass();

            // 先初始化 Engine 许可（如果你用的是 Desktop Advanced 之类，这里可以换成 Desktop）
            esriLicenseStatus status = aoInit.Initialize(esriLicenseProductCode.esriLicenseProductCodeEngine);
            if (status != esriLicenseStatus.esriLicenseCheckedOut)
            {
                MessageBox.Show("无法获取 ArcGIS Engine 许可，程序将退出。", "许可错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 再启用 Network Analyst 扩展
            esriLicenseStatus naStatus = aoInit.CheckOutExtension(esriLicenseExtensionCode.esriLicenseExtensionCodeNetwork);
            if (naStatus != esriLicenseStatus.esriLicenseCheckedOut)
            {
                MessageBox.Show("无法获取 Network Analyst 扩展许可，路径分析功能将不可用。", "许可错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // 这里可以选择继续运行（只是不能用路径分析），也可以直接退出
                // return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());

            // 3. 程序退出前释放扩展许可
            if (naStatus == esriLicenseStatus.esriLicenseCheckedOut)
            {
                aoInit.CheckInExtension(esriLicenseExtensionCode.esriLicenseExtensionCodeNetwork);
            }
            if (status == esriLicenseStatus.esriLicenseCheckedOut)
            {
                aoInit.Shutdown();
            }
        }

        /// <summary>
        /// 初始化 ArcGIS 许可
        /// </summary>
        private static bool InitializeLicense()
        {
            try
            {
                esriLicenseStatus status = m_AOLicenseInitializer.InitializeApplication(
                    new esriLicenseProductCode[]
                    {
                        esriLicenseProductCode.esriLicenseProductCodeEngine,
                        esriLicenseProductCode.esriLicenseProductCodeBasic,
                        esriLicenseProductCode.esriLicenseProductCodeStandard,
                        esriLicenseProductCode.esriLicenseProductCodeAdvanced
                    },
                    new esriLicenseExtensionCode[] { }
                );

                if (status == esriLicenseStatus.esriLicenseCheckedOut)
                {
                    return true;
                }
                else
                {
                    Console.WriteLine("许可状态: " + status.ToString());
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "许可初始化异常：\n" + ex.Message,
                    "许可异常",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }
        }

        /// <summary>
        /// 关闭并释放 ArcGIS 许可
        /// </summary>
        private static void ShutdownLicense()
        {
            try
            {
                m_AOLicenseInitializer.ShutdownApplication();
            }
            catch (Exception ex)
            {
                Console.WriteLine("关闭许可时出错: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// ArcGIS 许可初始化器类
    /// </summary>
    internal class LicenseInitializer
    {
        private IAoInitialize m_AoInitialize = null;

        public esriLicenseStatus InitializeApplication(
            esriLicenseProductCode[] productCodes,
            esriLicenseExtensionCode[] extensionCodes)
        {
            m_AoInitialize = new AoInitializeClass();
            esriLicenseStatus licenseStatus = esriLicenseStatus.esriLicenseUnavailable;

            // 按优先级顺序初始化许可
            foreach (esriLicenseProductCode productCode in productCodes)
            {
                licenseStatus = m_AoInitialize.Initialize(productCode);
                if (licenseStatus == esriLicenseStatus.esriLicenseCheckedOut)
                {
                    break;
                }
            }

            // 可选：检出扩展模块许可
            foreach (esriLicenseExtensionCode extensionCode in extensionCodes)
            {
                m_AoInitialize.CheckOutExtension(extensionCode);
            }

            return licenseStatus;
        }

        public void ShutdownApplication()
        {
            if (m_AoInitialize != null)
            {
                m_AoInitialize.Shutdown();
                m_AoInitialize = null;
            }
        }
    }
}