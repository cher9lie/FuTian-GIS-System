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
            // 第 0 步：绑定 ArcGIS 产品类型
            if (!RuntimeManager.Bind(ProductCode.Engine))
            {
                // 如果绑定 Engine 失败，尝试 Desktop
                if (!RuntimeManager.Bind(ProductCode.Desktop))
                {
                    MessageBox.Show(
                        "无法绑定到 ArcGIS Engine 或 Desktop 运行环境！\n" +
                        "请检查是否已正确安装 ArcGIS Engine Runtime 或 ArcGIS Desktop。",
                        "绑定错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }
            }

            // 第 1 步：初始化 ArcGIS 许可
            if (!InitializeLicense())
            {
                MessageBox.Show(
                    "无法获取 ArcGIS 许可！\n\n请确保：\n" +
                    "1. 已安装 ArcGIS Engine Runtime 或 ArcGIS Desktop 10.x\n" +
                    "2. 许可管理器中有有效的许可（Engine 或 Desktop）",
                    "许可错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return; // 退出程序
            }

            // 第 2 步：启动 Windows 窗体应用程序
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 先显示登录窗体
                LoginForm loginForm = new LoginForm();
                if (loginForm.ShowDialog() == DialogResult.OK)
                {
                    Application.Run(new MainForm());
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "程序运行时出错：\n" + ex.Message,
                    "运行时错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                // 第 3 步：关闭许可
                ShutdownLicense();
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