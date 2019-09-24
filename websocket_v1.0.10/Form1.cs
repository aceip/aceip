using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace websocket
{
    

    public partial class Form1 : Form
    {
        private Thread thMain = null;
        private Thread thFlashServer = null;
        private Thread thClear = null;
        private Dictionary<string, TcpClient> listMainTcp = new Dictionary<string, TcpClient>();
        private List<TcpClient> listFlashTcp = new List<TcpClient>();
        private Dictionary<string, Thread> listths = new Dictionary<string, Thread>();

        private ManualResetEvent tcpClientConnected = new ManualResetEvent(false);
        private ManualResetEvent tcpClientConnectedflash = new ManualResetEvent(false);

        public bool IsOnline(TcpClient c)
        {
            return !((c.Client.Poll(1000, SelectMode.SelectRead) && (c.Client.Available == 0)) || !c.Client.Connected);
        }
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            txtIP.Text = GetLocalIP();
            txtPort.Text = "8000";
        }

        private void WebSocketMain()
        {
            //实例化服务器本机的端点
            IPEndPoint local = new IPEndPoint(IPAddress.Parse(txtIP.Text), Convert.ToInt32(txtPort.Text));
            //定义服务器监听对象
            TcpListener listener = new TcpListener(local);
            //开始监听
            listener.Start();

            thMain = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        tcpClientConnected.Reset();
                        listener.BeginAcceptTcpClient(clientConnect, listener);
                        tcpClientConnected.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        WriteLog("Error", "WebSocketMain " + ex.Message);
                    }
                }
            });

            thMain.Start();
        }

        private void clientConnect(IAsyncResult ar)
        {
            try
            {
                TcpListener listener = (TcpListener)ar.AsyncState;
                //接受客户的连接,得到连接的Socket
                TcpClient client = listener.EndAcceptTcpClient(ar);
                client.ReceiveBufferSize = 524288;
                client.SendBufferSize = 524288;
                Console.WriteLine("t " + client.Available);
                int maxi = 0;
                while (IsOnline(client) && maxi <= 1000)
                {
                    maxi++;
                    Console.WriteLine("max " + maxi);
                    if (client.Available > 0)
                    {
                        BinaryReader reader = new BinaryReader(client.GetStream());
                        BinaryWriter writer = new BinaryWriter(client.GetStream());

                        Console.WriteLine("33");

                        byte[] buffer = new byte[client.Available];
                        reader.Read(buffer, 0, client.Available);
                        String result = Encoding.UTF8.GetString(buffer);
                        if (result.IndexOf("Sec-WebSocket-Key") >= 0)
                        {
                            writer.Write(PackHandShakeData(GetSecKeyAccetp(result)));
                            writer.Flush();

                            lock (listMainTcp)
                            {
                                if (!listMainTcp.ContainsValue(client))
                                {
                                    listMainTcp.Add(client.Client.RemoteEndPoint.ToString(), client);
                                }
                            }

                            cmbClient.Invoke(new Action(() =>
                            {
                                cmbClient.Items.Add(client.Client.RemoteEndPoint.ToString());
                            }));

                            WriteLog("系统提示:", "浏览器"+client.Client.RemoteEndPoint+"连接成功！");
                        }

                        Thread th = new Thread(() =>
                        {
                            while (true)
                            {
                                try
                                {
                                    if (client.Available > 0)
                                    {
                                        byte[] buffers = new byte[client.Available];
                                        reader.Read(buffers, 0, client.Available);
                                        result = AnalyticData(buffers, buffers.Length);
                                        WriteLog("消息", "客户端[" + client.Client.RemoteEndPoint + "]:" + result);
                                        if (check_all_recall.Checked)
                                        {
                                            /*
                                             *全员转发消息
                                             */
                                            WriteLog("IP", "一共有" + listMainTcp.Count.ToString() + "个终端IP");
                                            foreach (KeyValuePair<string, TcpClient> kvp in listMainTcp)
                                            {
                                                TcpClient clients = listMainTcp[kvp.Key];
                                                BinaryWriter writers = new BinaryWriter(clients.GetStream());
                                                byte[] playloadData = Encoding.UTF8.GetBytes(client.Client.RemoteEndPoint + "发来消息:" + result);
                                                var fragment = GetPackageData(OpCode.Text, playloadData, 0, playloadData.Length);
                                                writers.Write(fragment);
                                                //WriteLog("终端","IP是"+kvp.Key);

                                            }
                                        }
                                        else {
                                             // 消息发给本身
                                            byte[] playloadData = Encoding.UTF8.GetBytes(result);
                                            var fragment = GetPackageData(OpCode.Text, playloadData, 0, playloadData.Length);
                                            writer.Write(fragment);
                                         
                                        }


                                        /*
                                        TcpClient clients = listMainTcp[client.Client.RemoteEndPoint.ToString()];
                                        BinaryWriter writers = new BinaryWriter(clients.GetStream());
                                        byte[] playloadData = Encoding.UTF8.GetBytes(result);
                                        var fragment = GetPackageData(OpCode.Text, playloadData, 0, playloadData.Length);
                                        writers.Write(fragment);
                                        
                                        if (result == "\u0003�")
                                        {
                                            lock (listMainTcp)
                                            {
                                                listMainTcp.Remove(client.Client.RemoteEndPoint.ToString());
                                            }
                                            lock (listths)
                                            {
                                                listths.Remove(client.Client.RemoteEndPoint.ToString());
                                            }
                                            client.Close();
                                            break;
                                        }
                                        */
                                    }
                                }
                                catch (ThreadAbortException e)
                                {
                                    e.GetBaseException();
                                }
                                catch (Exception ex)
                                {
                                    WriteLog("Error", "th " + ex.Message);
                                }
                            }
                        })
                        {
                            Name = client.Client.RemoteEndPoint.ToString()
                        };
                        listths.Add(th.Name, th);
                        th.Start();

                        break;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                tcpClientConnected.Set();
            }
        }

        private void FlashSocketServer()
        {
            //实例化服务器本机的端点
            IPEndPoint local = new IPEndPoint(IPAddress.Parse(txtIP.Text), 843);
            //定义服务器监听对象
            TcpListener listener = new TcpListener(local);
            //开始监听
            listener.Start();

            TcpClient client = null;
            BinaryReader reader = null;
            BinaryWriter writer = null;
            thFlashServer = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        client = listener.AcceptTcpClient();
                        Console.WriteLine("1 t" + client.Available);
                        int maxi = 0;
                        while (IsOnline(client) && maxi <= 1000)
                        {
                            maxi++;
                            Console.WriteLine("max2 " + maxi);
                            if (client.Available > 0)
                            {
                                lock (listFlashTcp)
                                {
                                    if (!listFlashTcp.Contains(client))
                                    {
                                        listFlashTcp.Add(client);
                                    }
                                }

                                reader = new BinaryReader(client.GetStream());
                                writer = new BinaryWriter(client.GetStream());


                                byte[] buffer = new byte[client.Available];
                                reader.Read(buffer, 0, client.Available);
                                String result = Encoding.UTF8.GetString(buffer);
                                if (result.IndexOf("<policy-file-request/>") >= 0)
                                {
                                    byte[] datas = System.Text.Encoding.UTF8.GetBytes("<cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"*\" /></cross-domain-policy>\0");
                                    writer.Write(datas);
                                    writer.Flush();
                                    WriteLog("系统提示您", "843端口连接成功！" + client.Client.RemoteEndPoint);
                                }

                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog("Error", "thFlashServer " + ex.Message);
                    }
                }
            });
            thFlashServer.Start();
        }

        private void tClear()
        {
            thClear = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(10000);

                    try
                    {
                        lock (listMainTcp)
                        {
                            if (listMainTcp.Count > 0)
                            {
                                string[] keys = new string[listMainTcp.Count];
                                listMainTcp.Keys.CopyTo(keys, 0);
                                foreach (string key in keys)
                                {
                                    if (listMainTcp[key] != null)
                                    {
                                        if (!IsOnline(listMainTcp[key]))
                                        {
                                            listths[key].Abort();
                                            listths.Remove(key);

                                            cmbClient.Invoke(new Action(() =>
                                            {
                                                cmbClient.Items.Remove(key);
                                            }));

                                            listMainTcp[key].Close();
                                            listMainTcp[key] = null;
                                            listMainTcp.Remove(key);
                                        }
                                    }
                                }
                            }
                        }

                        lock (listFlashTcp)
                        {
                            if (listFlashTcp.Count > 0)
                            {
                                for (int i = 0; i < listFlashTcp.Count; i++)
                                {
                                    if (listFlashTcp[i] != null)
                                    {
                                        if (!IsOnline(listFlashTcp[i]))
                                        {
                                            listFlashTcp[i].Close();
                                            listFlashTcp.Remove(listFlashTcp[i]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog("Error", "tClear " + ex.Message);
                    }
                }
            });
            thClear.Start();
        }
        
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
       
         
        private void WriteLog(string type, string t)
        {
            if (!txtLog.IsDisposed)
            {
                try
                {
                    txtLog.Invoke(new Action(() =>
                    {
                        txtLog.AppendText(Environment.NewLine);
                        txtLog.AppendText(string.Format("[{0}] [{1}] {2}", type, DateTime.Now.ToString(), t));
                    }));
                }
                catch { }
            }
        }

        /// <summary>
        /// 打包握手信息
        /// </summary>
        /// <param name="secKeyAccept">Sec-WebSocket-Accept</param>
        /// <returns>数据包</returns>
        private byte[] PackHandShakeData(string secKeyAccept)
        {
            var responseBuilder = new StringBuilder();
            responseBuilder.Append("HTTP/1.1 101 Switching Protocols" + Environment.NewLine);
            responseBuilder.Append("Upgrade: websocket" + Environment.NewLine);
            responseBuilder.Append("Connection: Upgrade" + Environment.NewLine);
            responseBuilder.Append("Sec-WebSocket-Accept: " + secKeyAccept + Environment.NewLine + Environment.NewLine);
            //如果把上一行换成下面两行，才是thewebsocketprotocol-17协议，但居然握手不成功，目前仍没弄明白！
            //responseBuilder.Append("Sec-WebSocket-Accept: " + secKeyAccept + Environment.NewLine);
            //responseBuilder.Append("Sec-WebSocket-Protocol: chat" + Environment.NewLine);

            return Encoding.UTF8.GetBytes(responseBuilder.ToString());
        }

        /// <summary>
        /// 生成Sec-WebSocket-Accept
        /// </summary>
        /// <param name="handShakeText">客户端握手信息</param>
        /// <returns>Sec-WebSocket-Accept</returns>
        private string GetSecKeyAccetp(String handShakeText)
        {
            string key = string.Empty;
            Regex r = new Regex(@"Sec\-WebSocket\-Key:(.*?)\r\n");
            Match m = r.Match(handShakeText);
            if (m.Groups.Count != 0)
            {
                key = Regex.Replace(m.Value, @"Sec\-WebSocket\-Key:(.*?)\r\n", "$1").Trim();
            }
            byte[] encryptionString = SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
            return Convert.ToBase64String(encryptionString);
        }

        /// <summary>
        /// 解析客户端数据包
        /// </summary>
        /// <param name="recBytes">服务器接收的数据包</param>
        /// <param name="recByteLength">有效数据长度</param>
        /// <returns></returns>
        private static string AnalyticData(byte[] recBytes, int recByteLength)
        {
            if (recByteLength < 2) { return string.Empty; }

            bool fin = (recBytes[0] & 0x80) == 0x80; // 1bit，1表示最后一帧  
            if (!fin)
            {
                return string.Empty;// 超过一帧暂不处理 
            }

            bool mask_flag = (recBytes[1] & 0x80) == 0x80; // 是否包含掩码  
            if (!mask_flag)
            {
                return string.Empty;// 不包含掩码的暂不处理
            }

            int payload_len = recBytes[1] & 0x7F; // 数据长度  

            byte[] masks = new byte[4];
            byte[] payload_data;

            if (payload_len == 126)
            {
                Array.Copy(recBytes, 4, masks, 0, 4);
                payload_len = (UInt16)(recBytes[2] << 8 | recBytes[3]);
                payload_data = new byte[payload_len];
                Array.Copy(recBytes, 8, payload_data, 0, payload_len);

            }
            else if (payload_len == 127)
            {
                Array.Copy(recBytes, 10, masks, 0, 4);
                byte[] uInt64Bytes = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    uInt64Bytes[i] = recBytes[9 - i];
                }
                UInt64 len = BitConverter.ToUInt64(uInt64Bytes, 0);

                payload_data = new byte[len];
                for (UInt64 i = 0; i < len; i++)
                {
                    payload_data[i] = recBytes[i + 14];
                }
            }
            else
            {
                Array.Copy(recBytes, 2, masks, 0, 4);
                payload_data = new byte[payload_len];
                Array.Copy(recBytes, 6, payload_data, 0, payload_len);

            }

            for (var i = 0; i < payload_len; i++)
            {
                payload_data[i] = (byte)(payload_data[i] ^ masks[i % 4]);
            }

            return Encoding.UTF8.GetString(payload_data);
        }

        private string GetLocalIP()
        {
            try
            {
                string HostName = Dns.GetHostName(); //得到主机名  
                IPHostEntry IpEntry = Dns.GetHostEntry(HostName);
                for (int i = 0; i < IpEntry.AddressList.Length; i++)
                {
                    //从IP地址列表中筛选出IPv4类型的IP地址  
                    //AddressFamily.InterNetwork表示此IP为IPv4,  
                    //AddressFamily.InterNetworkV6表示此地址为IPv6类型  
                    if (IpEntry.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        return IpEntry.AddressList[i].ToString();
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                WriteLog("Error", "获取本机IP出错:" + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// 可传输超长数据
        /// </summary>
        /// <param name="opCode"></param>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        private byte[] GetPackageData(int opCode, byte[] data, int offset, int length)
        {
            byte[] fragment;

            if (length < 126)
            {
                fragment = new byte[2 + length];
                fragment[1] = (byte)length;
            }
            else if (length < 65536)
            {
                fragment = new byte[4 + length];
                fragment[1] = (byte)126;
                fragment[2] = (byte)(length / 256);
                fragment[3] = (byte)(length % 256);
            }
            else
            {
                fragment = new byte[10 + length];
                fragment[1] = (byte)127;

                int left = length;
                int unit = 256;

                for (int i = 9; i > 1; i--)
                {
                    fragment[i] = (byte)(left % unit);
                    left = left / unit;

                    if (left == 0)
                        break;
                }
            }

            fragment[0] = (byte)(opCode | 0x80);

            if (length > 0)
            {
                Buffer.BlockCopy(data, offset, fragment, fragment.Length - length, length);
            }

            return fragment;
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            WriteLog("来自系统", "启动 FlashSocketServer");
            FlashSocketServer();

            WriteLog("来自系统", "启动 WebSocketMain");
            WebSocketMain();

            WriteLog("来自系统", "开始监听...");
            tClear();
            ((Button)sender).Enabled = false;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            try
            {
                    if (check_all_to.Checked) {

                        foreach (KeyValuePair<string, TcpClient> kvp in listMainTcp)
                        {
                            TcpClient clients = listMainTcp[kvp.Key];
                            BinaryWriter writers = new BinaryWriter(clients.GetStream());
                            byte[] playloadData = Encoding.UTF8.GetBytes(txtmsg.Text);
                            var fragment = GetPackageData(OpCode.Text, playloadData, 0, playloadData.Length);
                            writers.Write(fragment);
                            //WriteLog("终端","IP是"+kvp.Key);

                        }

                    }
                    else
                    {
                    if (cmbClient.SelectedIndex > -1)

                    {
                        TcpClient client = listMainTcp[cmbClient.SelectedItem.ToString()];
                        if (client != null)
                        {
                            if (IsOnline(client))
                            {
                                BinaryWriter writer = new BinaryWriter(client.GetStream());
                                byte[] playloadData = Encoding.UTF8.GetBytes(txtmsg.Text);
                                var fragment = GetPackageData(OpCode.Text, playloadData, 0, playloadData.Length);
                                writer.Write(fragment);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Error", "send " + ex.Message);
            }
        }

        private void clear_btn_Click(object sender, EventArgs e)
        {
            txtmsg.Text = "";
            txtLog.Text = "";
        }

      


        /*
        private void Closed_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            
            Application.Exit();//是退出整个应用程序
            Application.ExitThread();//强制中止调用线程上的所有消息，同样面临其它线程无法正确退出的问题
            
            //如果你是需要关闭进程的代码，则如下：
            //先确定你的进程 
            Process[] plist = Process.GetProcessesByName("服务");
            Process p = plist[0];
            // 结束进程的方式： 
            p.Kill();  // 就可以强制关掉进程。
            System.Environment.Exit(0);   //这是最彻底的退出方式，不管什么线程都被强制退出，把程序结束的很干净

            this.Close();// This.Close()是关闭当前的窗体
            this.Dispose();//Dispose方法只能释放当前窗体资源，不能强制结束循环
            
        }
        */
    }
    public class OpCode
    {
        public const sbyte Plain = -2; // defined by SuperWebSocket, to support hybi-00
        public const string PlainTag = "-2";

        public const sbyte Handshake = -1; // defined by SuperWebSocket
        public const string HandshakeTag = "-1";

        public const sbyte Continuation = 0;
        public const string ContinuationTag = "0";

        public const sbyte Text = 1;
        public const string TextTag = "1";

        public const sbyte Binary = 2;
        public const string BinaryTag = "2";

        public const sbyte Close = 8;
        public const string CloseTag = "8";

        public const sbyte Ping = 9;
        public const string PingTag = "9";

        public const sbyte Pong = 10;
        public const string PongTag = "10";
    }
}
