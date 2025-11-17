using System;
using System.Collections.Generic;
using System.Linq;
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
            // 步骤 1: 初始化 ArcGIS 许可
            if (!InitializeLicense())
            {
                MessageBox.Show(
                    "无法获取 ArcGIS Engine 许可！\n\n请确保：\n1. 已安装 ArcGIS Engine 10.x\n2. 许可管理器中有有效的 Engine 许可",
                    "许可错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return; // 退出程序
            }

            // 步骤 2: 启动 Windows 窗体应用程序
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm()); // 这里要与你的窗体类名一致
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
                // 步骤 3: 关闭许可（无论程序如何退出都会执行）
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
                // 尝试初始化 Engine 许可
                // 优先级顺序：Engine -> ArcView -> ArcEditor -> ArcInfo
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

            // 尝试按优先级顺序初始化许可
            foreach (esriLicenseProductCode productCode in productCodes)
            {
                licenseStatus = m_AoInitialize.Initialize(productCode);
                if (licenseStatus == esriLicenseStatus.esriLicenseCheckedOut)
                {
                    break; // 成功获取许可，跳出循环
                }
            }

            // 可选：检出扩展模块许可（如 Spatial Analyst）
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
