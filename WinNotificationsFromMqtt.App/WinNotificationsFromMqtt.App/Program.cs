using Microsoft.Toolkit.Uwp.Notifications;

using MQTTnet;
using MQTTnet.Client;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Geolocation;
using WinNotificationsFromMqtt.App;

namespace MqttTrayNotifier
{
    static class Program
    {
        static NotifyIcon trayIcon;
        static IMqttClient mqttClient;
        static AppConfiguration configuration;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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

            Application.Run(); // Keeps the app alive in tray
        }

        static async Task ConnectMqtt()
        {
            var factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
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

                    ShowNotification(title, message, imageUrl);
                }
                catch
                {
                    ShowNotification("MQTT Wiadomość", payload, null);
                }

                return Task.CompletedTask;
            };

            mqttClient.ConnectedAsync += async e =>
            {
                ShowNotification("Połączono z MQTT", e.ConnectResult.ToString(), null);

                var s = await mqttClient.SubscribeAsync("notifications/global");
            };

            mqttClient.DisconnectedAsync += async e =>
            {
                ShowNotification("Rozłącozno z MQTT", e.Exception?.ToString(), null);
            };

            try
            {
                await mqttClient.ConnectAsync(options);
            }
            catch (Exception ex)
            {
                ShowNotification("Błąd połączenia", ex.Message, null);
            }
        }


        static void ShowNotification(string title, string message, string imageUrl)
        {
            ToastContentBuilder builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                try
                {
                    builder.AddInlineImage(new Uri(imageUrl));
                }
                catch
                {
                    builder.AddText("[Błąd ładowania obrazka]");
                }
            }

            // Konieczna rejestracja AUMID — dowolna unikalna nazwa
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                // Można tu dodać akcję po kliknięciu
            };

            ToastNotificationManagerCompat.History.Clear(); // (opcjonalnie: czyści poprzednie)
            builder.Show();
        }

    }
}
