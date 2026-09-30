using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace Vcrmb.Desktop
{
    internal static class Program
    {
        internal static string DataDirectory;
        internal static bool UiTest;

        [STAThread]
        private static int Main(string[] args)
        {
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vcrmb");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data-dir" && i + 1 < args.Length) DataDirectory = Path.GetFullPath(args[++i]);
                else if (args[i] == "--ui-test") UiTest = true;
                else { MessageBox.Show("不支持的启动参数。", "小词窗"); return 1; }
            }
            string suffix;
            using (SHA256 hash = SHA256.Create()) suffix = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(DataDirectory.ToLowerInvariant()))).Replace("-", "").Substring(0, 20);
            bool created;
            // 安装器只检查此标记是否存在，不获取所有权；独立数据目录仍可并行运行。
            using (Mutex installationGuard = new Mutex(false, "Local\\Vcrmb.InstallationGuard"))
            using (Mutex mutex = new Mutex(true, "Local\\Vcrmb-" + suffix, out created))
            {
                if (!created)
                {
                    try { using (EventWaitHandle show = EventWaitHandle.OpenExisting("Local\\VcrmbShow-" + suffix)) show.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("程序正在启动，请稍后再试。", "小词窗"); }
                    return 0;
                }
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        Log(e.Exception); MessageBox.Show("程序遇到错误，已保存的记录仍在本地。\n" + e.Exception.Message, "小词窗");
                        e.Handled = true; app.Shutdown(1);
                    };
                    using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VcrmbShow-" + suffix))
                    {
                        MainWindow main = new MainWindow(DataDirectory);
                        app.MainWindow = main;
                        RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(signal, delegate
                        {
                            app.Dispatcher.BeginInvoke((Action)delegate { main.RevealWindow(); });
                        }, null, Timeout.Infinite, false);
                        app.Exit += delegate { wait.Unregister(null); main.DisposeResources(); };
                        main.Show();
                        return app.Run();
                    }
                }
                catch (Exception ex)
                {
                    Log(ex); MessageBox.Show("无法启动小词窗：\n" + ex.Message + "\n\n数据目录：" + DataDirectory, "小词窗", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        internal static void Log(Exception exception)
        {
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "error.log"), DateTime.Now.ToString("s") + " " + exception + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { /* 主错误仍通过窗口报告；日志失败不覆盖原始异常。 */ }
            catch (UnauthorizedAccessException) { /* 目录不可写时由主错误窗口说明。 */ }
        }
    }
}
