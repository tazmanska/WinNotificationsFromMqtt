using Microsoft.Toolkit.Uwp.Notifications;

using MQTTnet;
using MQTTnet.Client;
using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinNotificationsFromMqtt.App;

namespace MqttTrayNotifier
{
    static class Program
    {
        static NotifyIcon trayIcon;
        static IMqttClient mqttClient;
        static AppConfiguration configuration;
        static SynchronizationContext uiContext;
        static readonly HttpClient httpClient = new HttpClient();
        static (string title, string message, string imageUrl) lastNotification;
        static MqttClientOptions mqttOptions;
        static readonly SemaphoreSlim connectLock = new SemaphoreSlim(1, 1);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            uiContext = new System.Windows.Forms.WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);

            ToastNotificationManagerCompat.OnActivated += toastArgs => { };

            configuration = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText("AppConfiguration.json"));

            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "MQTT Tray Notifier",
                ContextMenuStrip = new ContextMenuStrip()
                {
                    Items =
                    {
                        new ToolStripMenuItem("Pokaż ostatnie powiadomienie", null, (s, e) =>
                        {
                            var (title, message, imageUrl) = lastNotification;
                            if (title == null)
                                trayIcon.ShowBalloonTip(3000, "Brak powiadomień", "Nie odebrano jeszcze żadnego powiadomienia.", ToolTipIcon.Info);
                            else
                                ShowNotification(title, message, imageUrl);
                        }),
                        new ToolStripMenuItem("Reconnect", null, (s, e) =>
                        {
                            Task.Run(() => ForceReconnect());
                        }),
                        new ToolStripSeparator(),
                        new ToolStripMenuItem("Exit", null, (s, e) =>
                        {
                            trayIcon.Visible = false;
                            mqttClient?.DisconnectAsync().Wait();
                            Application.Exit();
                        })
                    }
                }
            };

            Task.Run(() => ConnectMqtt());

            Application.Run();
        }

        static async Task ConnectMqtt()
        {
            var factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();

            mqttOptions = new MqttClientOptionsBuilder()
                .WithClientId(configuration.ClientId)
                .WithTcpServer(configuration.Url, configuration.Port)
                .WithCredentials(configuration.UserName, configuration.Password)
                .WithCleanSession(false)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .Build();

            mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                try
                {
                    var json = JsonDocument.Parse(payload);
                    string title = json.RootElement.GetProperty("title").GetString();
                    string message = json.RootElement.GetProperty("message").GetString();
                    string imageUrl = json.RootElement.GetProperty("imageUrl").GetString();

                    lastNotification = (title, message, imageUrl);
                    ShowNotification(title, message, imageUrl);
                }
                catch
                {
                    lastNotification = ("MQTT Wiadomość", payload, null);
                    ShowNotification("MQTT Wiadomość", payload, null);
                }

                return Task.CompletedTask;
            };

            mqttClient.ConnectedAsync += async e =>
            {
                SetTrayStatus($"Połączono z {configuration.Url}:{configuration.Port}");
                ShowNotification("Połączono z MQTT", null, null);
                await mqttClient.SubscribeAsync("notifications/global");
            };

            mqttClient.DisconnectedAsync += async e =>
            {
                // Ignoruj rozłączenia wymuszone ręcznie (ForceReconnect sam zarządza reconnectem)
                if (e.ReasonCode == MqttClientDisconnectOptionsReasonCode.NormalDisconnection)
                    return;

                SetTrayStatus("Rozłączono");
                ShowNotification("Rozłączono z MQTT", e.Exception?.Message, null);
                await Task.Delay(TimeSpan.FromSeconds(5));
                await ConnectToMqtt();
            };

            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                    Task.Run(() => ForceReconnect());
            };

            SetTrayStatus("Łączenie...");
            await ConnectToMqtt();
        }

        static async Task ForceReconnect()
        {
            SetTrayStatus("Łączenie...");
            try { await mqttClient.DisconnectAsync(); } catch { }
            await Task.Delay(TimeSpan.FromSeconds(3));
            await ConnectToMqtt();
        }

        static async Task ConnectToMqtt()
        {
            if (!await connectLock.WaitAsync(0))
                return; // inna próba połączenia już w toku

            try
            {
                if (mqttClient.IsConnected)
                    return;

                await mqttClient.ConnectAsync(mqttOptions);
            }
            catch (Exception ex)
            {
                SetTrayStatus($"Błąd: {ex.Message}");
                ShowNotification("Błąd połączenia", ex.Message, null);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    await ConnectToMqtt();
                });
            }
            finally
            {
                connectLock.Release();
            }
        }

        static void SetTrayStatus(string status)
        {
            var text = $"MQTT Notifier — {status}";
            trayIcon.Text = text.Length > 63 ? text[..63] : text;
        }

        static void ShowNotification(string title, string message, string imageUrl)
        {
            uiContext.Post(_ =>
            {
                try
                {
                    ToastContentBuilder builder = new ToastContentBuilder()
                        .AddText(title ?? "")
                        .AddText(message ?? "");

                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        try
                        {
                            var filePath = Path.Combine(Path.GetTempPath(), "ha_notification_image.jpg");
                            var bytes = httpClient.GetByteArrayAsync(imageUrl).GetAwaiter().GetResult();
                            File.WriteAllBytes(filePath, bytes);
                            builder.AddInlineImage(new Uri(filePath));
                        }
                        catch
                        {
                            builder.AddText("[Błąd ładowania obrazka]");
                        }
                    }

                    builder.Show();
                }
                catch (Exception ex)
                {
                    trayIcon.ShowBalloonTip(5000, title ?? "", $"{message}\n({ex.Message})", ToolTipIcon.Info);
                }
            }, null);
        }
    }
}
