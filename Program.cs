using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UDPNetworkClock
{
    public class UdpTimeServer
    {
        private UdpClient udpServer;
        private bool isRunning;
        private int port;

        public event Action<string> StatusChanged;
        public event Action<string> ClientRequested;

        public bool IsRunning => isRunning;

        public UdpTimeServer(int port = 11000)
        {
            this.port = port;
            isRunning = false;
        }

        public void Start()
        {
            try
            {
                udpServer = new UdpClient(port);
                isRunning = true;
                StatusChanged?.Invoke($"Сервер запущено на порту {port}. Очікування запитів...");
                Task.Run(() => ListenForClients());
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Помилка запуску сервера: {ex.Message}");
            }
        }

        private async void ListenForClients()
        {
            try
            {
                while (isRunning)
                {
                    UdpReceiveResult result = await udpServer.ReceiveAsync();
                    string request = Encoding.UTF8.GetString(result.Buffer);
                    IPEndPoint clientEndpoint = result.RemoteEndPoint;

                    ClientRequested?.Invoke($"Запит від {clientEndpoint.Address}:{clientEndpoint.Port}");

                    string response = "";
                    if (request.Trim().ToLower() == "time" || request.Trim().ToLower() == "час")
                    {
                        response = DateTime.Now.ToString("HH:mm:ss");
                    }
                    else if (request.Trim().ToLower() == "date" || request.Trim().ToLower() == "дата")
                    {
                        response = DateTime.Now.ToString("dd.MM.yyyy");
                    }
                    else if (request.Trim().ToLower() == "datetime" || request.Trim().ToLower() == "датачас")
                    {
                        response = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    }
                    else
                    {
                        response = DateTime.Now.ToString("HH:mm:ss");
                    }

                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    await udpServer.SendAsync(responseData, responseData.Length, clientEndpoint);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (isRunning)
                    StatusChanged?.Invoke($"Помилка сервера: {ex.Message}");
            }
        }

        public void Stop()
        {
            isRunning = false;
            udpServer?.Close();
            StatusChanged?.Invoke("Сервер зупинено.");
        }
    }

    public class UdpTimeClient
    {
        private UdpClient udpClient;

        public event Action<string> StatusChanged;

        public async Task<string> RequestTimeAsync(string serverIP, int port = 11000, string requestType = "time")
        {
            try
            {
                udpClient = new UdpClient();
                StatusChanged?.Invoke($"Підключення до {serverIP}:{port}...");

                byte[] requestData = Encoding.UTF8.GetBytes(requestType);
                await udpClient.SendAsync(requestData, requestData.Length, serverIP, port);

                UdpReceiveResult result = await udpClient.ReceiveAsync();
                string response = Encoding.UTF8.GetString(result.Buffer);

                udpClient.Close();
                StatusChanged?.Invoke("Час отримано.");
                return response;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Помилка клієнта: {ex.Message}");
                return $"Помилка: {ex.Message}";
            }
        }

        public void Close()
        {
            udpClient?.Close();
        }
    }

    public class ServerForm : Form
    {
        private UdpTimeServer server;
        private TextBox txtPort;
        private Button btnStart;
        private Button btnStop;
        private TextBox txtLog;
        private Label lblStatus;

        public ServerForm()
        {
            InitializeComponents();
            this.Text = "UDP Сервер часу";
            this.FormClosing += (s, e) => server?.Stop();
        }

        private void InitializeComponents()
        {
            this.Width = 600;
            this.Height = 450;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            var lblPort = new Label() { Text = "Порт:", Left = 15, Top = 20, Width = 50 };
            txtPort = new TextBox() { Text = "11000", Left = 70, Top = 18, Width = 80 };

            btnStart = new Button() { Text = "Запустити сервер", Left = 160, Top = 16, Width = 130 };
            btnStart.Click += (s, e) =>
            {
                if (int.TryParse(txtPort.Text, out int port))
                {
                    server = new UdpTimeServer(port);
                    server.StatusChanged += OnStatusChanged;
                    server.ClientRequested += OnClientRequested;
                    server.Start();
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                }
                else
                {
                    MessageBox.Show("Невірний порт!");
                }
            };

            btnStop = new Button() { Text = "Зупинити сервер", Left = 300, Top = 16, Width = 130 };
            btnStop.Enabled = false;
            btnStop.Click += (s, e) =>
            {
                server?.Stop();
                btnStart.Enabled = true;
                btnStop.Enabled = false;
            };

            lblStatus = new Label()
            {
                Left = 15, Top = 55, Width = 550, Height = 25,
                Text = "Сервер не запущено", ForeColor = Color.Red
            };

            txtLog = new TextBox()
            {
                Left = 15, Top = 90, Width = 550, Height = 300,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
            };

            this.Controls.AddRange(new Control[] { lblPort, txtPort, btnStart, btnStop, lblStatus, txtLog });
        }

        private void OnStatusChanged(string status)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action<string>(OnStatusChanged), status);
                return;
            }
            lblStatus.Text = status;
            lblStatus.ForeColor = server != null && server.IsRunning ? Color.Green : Color.Red;
        }

        private void OnClientRequested(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(OnClientRequested), message);
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }
    }

    public class ClientForm : Form
    {
        private UdpTimeClient client;
        private TextBox txtIP;
        private TextBox txtPort;
        private ComboBox cmbRequestType;
        private Button btnGetTime;
        private Label lblTime;
        private Label lblStatus;
        private System.Windows.Forms.Timer autoUpdateTimer;
        private CheckBox chkAutoUpdate;
        private TextBox txtInterval;

        public ClientForm()
        {
            client = new UdpTimeClient();
            InitializeComponents();
            this.Text = "UDP Клієнт часу";
            this.FormClosing += (s, e) => { autoUpdateTimer?.Stop(); client?.Close(); };
        }

        private void InitializeComponents()
        {
            this.Width = 500;
            this.Height = 350;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            var lblIP = new Label() { Text = "IP сервера:", Left = 15, Top = 20, Width = 80 };
            txtIP = new TextBox() { Text = "127.0.0.1", Left = 100, Top = 18, Width = 130 };

            var lblPort = new Label() { Text = "Порт:", Left = 240, Top = 20, Width = 40 };
            txtPort = new TextBox() { Text = "11000", Left = 285, Top = 18, Width = 80 };

            var lblRequestType = new Label() { Text = "Тип запиту:", Left = 15, Top = 55, Width = 80 };
            cmbRequestType = new ComboBox()
            {
                Left = 100, Top = 53, Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbRequestType.Items.AddRange(new object[] { "Час", "Дата", "Дата і час" });
            cmbRequestType.SelectedIndex = 0;

            btnGetTime = new Button() { Text = "Отримати час", Left = 15, Top = 90, Width = 130 };
            btnGetTime.Click += async (s, e) => await GetTimeAsync();

            lblStatus = new Label()
            {
                Left = 15, Top = 130, Width = 450, Height = 25,
                Text = "Готовий до запиту", ForeColor = Color.Blue
            };

            var lblTimeLabel = new Label()
            {
                Left = 15, Top = 170, Width = 150, Height = 30,
                Text = "Поточний час:", Font = new Font("Arial", 10, FontStyle.Bold)
            };

            lblTime = new Label()
            {
                Left = 15, Top = 210, Width = 450, Height = 30,
                Text = "--:--:--",
                Font = new Font("Arial", 16, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle
            };

            chkAutoUpdate = new CheckBox()
            {
                Text = "Автооновлення", Left = 15, Top = 255, Width = 120
            };
            chkAutoUpdate.CheckedChanged += (s, e) =>
            {
                if (chkAutoUpdate.Checked)
                {
                    if (int.TryParse(txtInterval.Text, out int interval) && interval >= 1)
                    {
                        autoUpdateTimer.Interval = interval * 1000;
                        autoUpdateTimer.Start();
                    }
                    else
                    {
                        autoUpdateTimer.Interval = 1000;
                        autoUpdateTimer.Start();
                    }
                }
                else
                {
                    autoUpdateTimer.Stop();
                }
            };

            var lblInterval = new Label() { Text = "Інтервал (сек):", Left = 140, Top = 255, Width = 100 };
            txtInterval = new TextBox() { Text = "1", Left = 245, Top = 253, Width = 50 };

            autoUpdateTimer = new System.Windows.Forms.Timer();
            autoUpdateTimer.Tick += async (s, e) => await GetTimeAsync();

            this.Controls.AddRange(new Control[] { lblIP, txtIP, lblPort, txtPort, lblRequestType, cmbRequestType,
                btnGetTime, lblStatus, lblTimeLabel, lblTime, chkAutoUpdate, lblInterval, txtInterval });
        }

        private async Task GetTimeAsync()
        {
            btnGetTime.Enabled = false;
            lblStatus.Text = "Запит часу...";
            lblStatus.ForeColor = Color.Blue;

            string requestType = "time";
            switch (cmbRequestType.SelectedItem.ToString())
            {
                case "Дата":
                    requestType = "date";
                    break;
                case "Дата і час":
                    requestType = "datetime";
                    break;
                default:
                    requestType = "time";
                    break;
            }

            string result = await client.RequestTimeAsync(txtIP.Text, int.Parse(txtPort.Text), requestType);
            lblTime.Text = result;

            if (result.StartsWith("Помилка"))
            {
                lblStatus.Text = result;
                lblStatus.ForeColor = Color.Red;
            }
            else
            {
                lblStatus.Text = $"Час отримано о {DateTime.Now:HH:mm:ss}";
                lblStatus.ForeColor = Color.Green;
            }

            btnGetTime.Enabled = true;
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Task.Run(() => Application.Run(new ServerForm()));
            Application.Run(new ClientForm());
        }
    }
}