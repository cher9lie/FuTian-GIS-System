using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FuTianGIS
{
    public partial class LoginForm : Form
    {
        public LoginForm()
        {
            InitializeComponent();
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            string username = txtUser.Text.Trim();
            string password = txtPassword.Text;

            // 简单的硬编码校验：用户名 admin，密码 123
            if (username == "admin" && password == "123")
            {
                // 登录成功，设置对话框结果为 OK，关闭当前窗体
                this.DialogResult = DialogResult.OK;
                this.Hide();     // 可选：先隐藏再关闭
                this.Close();
            }
            else
            {
                // 登录失败，提示错误
                MessageBox.Show(
                    "用户名或密码错误！",
                    "登录失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                // 选中全部文本，方便重新输入
                txtPassword.Clear();
                txtPassword.Focus();
            }
        }
        private void LoginForm_Load(object sender, EventArgs e)
        {
            txtUser.Focus();
        }
    }
}
